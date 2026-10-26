using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GaameTranslate.Core;
using GaameTranslate.Infrastructure;
using GaameTranslate.Windows;
using Microsoft.Extensions.Logging;

namespace GaameTranslate.App;

public sealed partial class MainViewModel(IScreenCapture capture, IOcrProvider ocr, LocalDatabase database, ISettingsStore settingsStore,
    ICredentialStore credentials, HttpClient http, TranslationCache cache, ILogger<MainViewModel> logger,
    DictionaryService dictionary, ISpeechService speech, LocalMediaStore media) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceLanguage), nameof(TargetLanguage), nameof(LanguagePairLabel), nameof(NeedsProviderSetup))]
    private AppSettings settings = new();
    [ObservableProperty] private int pageIndex;
    [ObservableProperty] private string status = "Ready when you are. Select an area to get started.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isMonitoring;
    [ObservableProperty] private string inputText = "";
    [ObservableProperty] private string outputText = "";
    [ObservableProperty] private bool splitSentences;
    [ObservableProperty] private string wordSearch = "";
    [ObservableProperty] private string historySearch = "";
    [ObservableProperty] private VocabularyEntry? selectedWord;
    [ObservableProperty] private TranslationSession? selectedHistory;
    [ObservableProperty] private VocabularyEntry? reviewCard;
    [ObservableProperty] private bool answerRevealed;
    [ObservableProperty] private int wordCount;
    [ObservableProperty] private int dueCount;
    [ObservableProperty] private string pipelineInfo = "No capture yet";
    public ObservableCollection<VocabularyEntry> Words { get; } = [];
    public ObservableCollection<VocabularyEntry> RecentWords { get; } = [];
    public ObservableCollection<TranslationSession> History { get; } = [];
    public ObservableCollection<TranslationSession> RecentHistory { get; } = [];
    public IReadOnlyList<LanguageChoice> SourceLanguages { get; } = [new("auto", "Detect automatically"), .. Languages.Translation];
    public IReadOnlyList<LanguageChoice> TargetLanguages => Languages.Translation;
    public IReadOnlyList<LanguageChoice> OcrLanguages => [new("auto", "Windows profile language"), .. ocr.AvailableLanguages];
    public string OcrSummary => ocr.AvailableLanguages.Count == 0 ? "No OCR packs installed" : string.Join(", ", ocr.AvailableLanguages.Select(l => l.Name));
    public TranslationSession? LastSession { get; private set; }
    public CapturedFrame? LastFrame { get; private set; }
    private AppSettings? lastProfileSettings;
    public AppSettings SettingsForSession(TranslationSession session) => LastSession?.Id == session.Id && lastProfileSettings is { } profile
        ? profile with { LightTheme = Settings.LightTheme, Hotkeys = Settings.Hotkeys, SaveWordScreenshots = Settings.SaveWordScreenshots, SaveWordAudio = Settings.SaveWordAudio }
        : Settings;
    public DictionaryService Dictionary => dictionary;
    public ISpeechService Speech => speech;
    public LocalMediaStore Media => media;
    public GameProfileStore Profiles { get; } = new(Path.GetDirectoryName(media.DirectoryPath)!);
    public bool NeedsProviderSetup => ProviderCatalog.RequiresKey(Settings) && !HasKey(Settings.Provider);
    public string LanguagePairLabel => $"{(Settings.SourceLanguage == "auto" ? "Auto" : Languages.Translation.FirstOrDefault(l => l.Code == Settings.SourceLanguage)?.Name ?? Settings.SourceLanguage)} → {Languages.Translation.FirstOrDefault(l => l.Code == Settings.TargetLanguage)?.Name ?? Settings.TargetLanguage}";
    public string SourceLanguage
    {
        get => Settings.SourceLanguage;
        set { if (string.IsNullOrEmpty(value) || value == Settings.SourceLanguage) return; Settings = Settings with { SourceLanguage = value }; _ = GuardAsync(() => settingsStore.SaveAsync(Settings)); }
    }
    public string TargetLanguage
    {
        get => Settings.TargetLanguage;
        set { if (string.IsNullOrEmpty(value) || value == Settings.TargetLanguage) return; Settings = Settings with { TargetLanguage = value }; _ = GuardAsync(() => settingsStore.SaveAsync(Settings)); }
    }
    public Func<CancellationToken, Task<PixelRect?>>? SelectRegion { get; set; }
    public event Action<bool>? CaptureVisibilityChanged;
    public event Action<TranslationSession>? SessionReady;
    public event Action<string, TranslationSession, TranslatedBlock>? WordLookupRequested;
    public event Action? SettingsChanged;
    public event Action<bool>? ShortcutRecordingChanged;
    public bool IsRecordingShortcut { get; private set; }
    public void SetShortcutRecording(bool recording)
    {
        if (recording == IsRecordingShortcut) return;
        IsRecordingShortcut = recording; ShortcutRecordingChanged?.Invoke(recording);
    }
    public event Action? ToggleOverlayRequested;
    public event Action? InteractionRequested;
    public event Action<string>? OperationFailed;
    private CancellationTokenSource? operation;
    private readonly SemaphoreSlim pipelineGate = new(1, 1);
    private long generation;
    private CancellationTokenSource? monitoring;
    private string? previousScreenText;
    public Func<bool>? IsScreenOverlayVisible { get; set; }
    public event Action? CloseOverlayRequested;
    public async Task InitializeAsync()
    {
        await Task.Run(() => database.InitializeAsync()); Settings = await settingsStore.LoadAsync(); await RefreshAsync();
        if (NeedsProviderSetup) Status = "Set up your translation provider in Settings. Windows OCR runs entirely on your PC.";
    }
    public async Task RefreshAsync()
    {
        var words = await Task.Run(() => database.GetWordsAsync(WordSearch)); Replace(Words, words);
        var all = await Task.Run(() => database.GetWordsAsync()); WordCount = all.Count; DueCount = all.Count(w => w.DueAt <= DateTimeOffset.UtcNow);
        Replace(RecentWords, all.Take(4));
        var history = await Task.Run(() => database.GetHistoryAsync(HistorySearch)); Replace(History, history); Replace(RecentHistory, (await Task.Run(() => database.GetHistoryAsync())).Take(3));
    }
    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> values) { collection.Clear(); foreach (var item in values) collection.Add(item); }
    public async Task GuardAsync(Func<Task> work)
    {
        try { await work(); }
        catch (OperationCanceledException) { if (!IsBusy) Status = "Cancelled. Ready for your next capture."; }
        catch (Exception e)
        {
            logger.LogWarning("Operation failed: {ExceptionType}", e.GetType().Name);
            Status = e is UserFacingException ? e.Message : "The operation could not be completed. Check your settings, connection and available disk space, then try again.";
            OperationFailed?.Invoke(Status);
        }
    }
    [RelayCommand] private Task RegionAsync() => GuardAsync(() => RunCaptureAsync("Region"));
    [RelayCommand] private Task FullscreenAsync() => GuardAsync(() => RunCaptureAsync("Fullscreen"));
    [RelayCommand] private Task AllScreensAsync() => GuardAsync(() => RunCaptureAsync("All monitors"));
    [RelayCommand] private Task RepeatAsync() => GuardAsync(() => RunCaptureAsync("Repeat"));
    [RelayCommand] private void Cancel() { StopMonitoring(); operation?.Cancel(); }
    [RelayCommand]
    public async Task ScreenTranslationAsync()
    {
        if (IsScreenOverlayVisible?.Invoke() == true) { CloseOverlayRequested?.Invoke(); return; }
        await GuardAsync(() => RunCaptureAsync("Screen translation"));
    }
    [RelayCommand] public Task WordAtCursorAsync() => GuardAsync(() => RunCaptureAsync("Word at cursor"));
    [RelayCommand]
    private async Task ToggleMonitoringAsync()
    {
        if (IsMonitoring) { StopMonitoring(); return; }
        if (Settings.LastRegion is null) { await GuardAsync(() => RunCaptureAsync("Region")); if (Settings.LastRegion is null) return; }
        var local = new CancellationTokenSource(); monitoring = local; var token = local.Token; IsMonitoring = true; previousScreenText = null;
        try
        {
            while (!token.IsCancellationRequested) { await RunCaptureAsync("Watch region"); await Task.Delay(Settings.MonitoringIntervalMs, token); }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { await GuardAsync(() => Task.FromException(e)); }
        finally { if (ReferenceEquals(monitoring, local)) { IsMonitoring = false; monitoring = null; } local.Dispose(); }
    }
    public void StopMonitoring() { monitoring?.Cancel(); if (IsMonitoring) operation?.Cancel(); IsMonitoring = false; }
    [RelayCommand] private void ToggleOverlay() => ToggleOverlayRequested?.Invoke();
    [RelayCommand] private void ToggleInteraction() => InteractionRequested?.Invoke();
    [RelayCommand] private void OpenSettings() => PageIndex = 5;
    [RelayCommand] private void OpenVocabulary() => PageIndex = 2;
    [RelayCommand]
    private async Task SwapLanguagesAsync() => await GuardAsync(async () =>
    {
        if (Settings.SourceLanguage == "auto") throw new UserFacingException("Choose an explicit source language before swapping.");
        Settings = Settings with { SourceLanguage = Settings.TargetLanguage, TargetLanguage = Settings.SourceLanguage }; await settingsStore.SaveAsync(Settings);
    });
    [RelayCommand] private Task SearchWordsAsync() => GuardAsync(RefreshAsync);
    [RelayCommand] private Task SearchHistoryAsync() => GuardAsync(RefreshAsync);
    [RelayCommand] private void CopyOutput() { if (OutputText.Length > 0) Copy(OutputText); }
    public void Copy(string text) { try { System.Windows.Clipboard.SetText(text); Status = "Copied to clipboard."; } catch (System.Runtime.InteropServices.COMException) { Status = "The clipboard is busy. Try again."; } }
    [RelayCommand]
    private async Task TranslateTextAsync() => await GuardAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(InputText)) throw new UserFacingException("Type or paste some text to translate.");
        await ExecutePipelineAsync(async token =>
        {
            var configuration = Settings;
            Status = "Translating text…";
            var texts = SplitSentences ? TextProcessing.SplitSentences(InputText) : new[] { TextProcessing.Normalize(InputText) };
            var provider = Provider(configuration);
            var result = await cache.TranslateAsync(provider, new(texts, configuration.SourceLanguage, configuration.TargetLanguage, configuration.ContextHint), CacheIdentity(configuration), token);
            token.ThrowIfCancellationRequested(); OutputText = string.Join(Environment.NewLine, result.Select(r => r.Text)); Status = "Translation ready.";
            var blocks = texts.Select((t, i) => new TranslatedBlock(new(i, t, new(0, 0, 0, 0), []), result[i].Text)).ToArray();
            var session = new TranslationSession(Guid.NewGuid(), DateTimeOffset.UtcNow, new(), result[0].SourceLanguage, configuration.TargetLanguage, configuration.Provider, "Text", blocks, new(0, 0, 0, 0));
            LastSession = session; LastFrame = null; lastProfileSettings = null; if (Settings.SaveHistory) await Task.Run(() => database.AddHistoryAsync(session, Settings.HistoryLimit, token), token); await RefreshAsync();
        });
    });
    private ITranslationProvider Provider(AppSettings? configuration = null) => new TranslationProvider(http, configuration ?? Settings, credentials);
    private string CacheIdentity(AppSettings? configuration = null) => ProviderCatalog.Identity(configuration ?? Settings);
    private async Task ExecutePipelineAsync(Func<CancellationToken, Task> work)
    {
        operation?.Cancel(); var local = new CancellationTokenSource(); operation = local; var id = Interlocked.Increment(ref generation);
        IsBusy = true;
        try { await pipelineGate.WaitAsync(local.Token); try { await work(local.Token); } finally { pipelineGate.Release(); } }
        finally { if (id == generation) { IsBusy = false; operation = null; } local.Dispose(); }
    }
    public async Task StopAsync() { StopMonitoring(); speech.Stop(); operation?.Cancel(); await pipelineGate.WaitAsync(); pipelineGate.Release(); }
    public async Task RunCaptureAsync(string kind)
    {
        await ExecutePipelineAsync(async token =>
        {
            var configuration = Settings;
            var appliedProfile = false;
            if (configuration.AutoApplyGameProfiles)
            {
                var profile = await Profiles.FindAsync(NativeMethods.ForegroundProcessName(), token);
                if (profile is not null) { appliedProfile = true; configuration = profile.Settings with { AutoApplyGameProfiles = Settings.AutoApplyGameProfiles, SaveHistory = Settings.SaveHistory, SaveWordScreenshots = Settings.SaveWordScreenshots, SaveWordAudio = Settings.SaveWordAudio }; }
            }
            var total = Stopwatch.StartNew(); var clock = Stopwatch.StartNew(); CapturedFrame frame; PixelRect? bounds = null;
            var prior = NativeMethods.GetForegroundWindow();
            NativeMethods.GetCursorPos(out var cursor);
            try
            {
                Status = kind == "Region" ? "Drag over text. Press Esc to cancel." : "Capturing screen…";
                CaptureVisibilityChanged?.Invoke(true); await Task.Delay(100, token);
                if (kind == "Region") bounds = SelectRegion is null ? null : await SelectRegion(token);
                else if (kind is "Repeat" or "Watch region") bounds = configuration.LastRegion ?? throw new UserFacingException("Select a region first. The repeat shortcut will reuse it.");
                else if (kind == "Word at cursor") bounds = new PixelRect(cursor.X - 500, cursor.Y - 220, 1000, 440).Intersect(DesktopCapture.CursorDisplay().Bounds);
                else if (kind == "All monitors") bounds = DesktopCapture.DesktopBounds;
                else
                {
                    var displays = DesktopCapture.Displays;
                    bounds = configuration.MonitorIndex >= 0 && configuration.MonitorIndex < displays.Count ? displays[configuration.MonitorIndex].Bounds : DesktopCapture.CursorDisplay().Bounds;
                }
                if (bounds is null) { Status = "Selection cancelled."; return; }
                token.ThrowIfCancellationRequested(); NativeMethods.SetForegroundWindow(prior); NativeMethods.DwmFlush(); await Task.Delay(80, token);
                clock.Restart(); frame = await capture.CaptureAsync(bounds.Value, token);
            }
            finally { CaptureVisibilityChanged?.Invoke(false); }
            var captureMs = clock.Elapsed.TotalMilliseconds;
            if (kind == "Region") { Settings = Settings with { LastRegion = bounds }; await settingsStore.SaveAsync(Settings, token); }
            Status = $"Распознавание: {configuration.OcrEngine}…"; clock.Restart();
            if (ocr is AdaptiveOcrProvider adaptive) { adaptive.Engine = configuration.OcrEngine; adaptive.UseGpu = configuration.OcrUseGpu; }
            var document = await ocr.RecognizeAsync(frame, configuration.OcrLanguage, configuration.Preprocessing, token);
            if (configuration.GroupParagraphs) document = ScreenAnalysis.GroupParagraphs(document);
            var selectedWord = kind == "Word at cursor" ? ScreenAnalysis.WordAt(document, cursor.X, cursor.Y) : null;
            if (kind == "Word at cursor")
            {
                if (selectedWord is null) throw new UserFacingException("Наведите курсор на слово и нажмите сочетание ещё раз.");
                document = document with { Blocks = document.Blocks.Where(b => b.Words.Contains(selectedWord)).ToArray() };
            }
            var ocrMs = clock.Elapsed.TotalMilliseconds;
            if (document.Blocks.Count == 0) throw new UserFacingException("No text was found. Select a tighter area, choose the matching OCR language or enable small-text scaling in Settings.");
            if (kind == "Watch region" && document.Text == previousScreenText) { Status = "Автоперевод включён · текст не изменился"; return; }
            Status = $"Перевод {document.Blocks.Count} реплик…"; clock.Restart();
            var source = configuration.SourceLanguage == "auto" ? Languages.Base(document.Language) : configuration.SourceLanguage;
            var result = await cache.TranslateAsync(Provider(configuration), new(document.Blocks.Select(b => b.Text).ToArray(), source, configuration.TargetLanguage,
                string.Join("\n", new[] { configuration.ContextHint, document.Text[..Math.Min(document.Text.Length, 10000)] }).Trim()), CacheIdentity(configuration), token);
            var translationMs = clock.Elapsed.TotalMilliseconds; token.ThrowIfCancellationRequested();
            var session = new TranslationSession(Guid.NewGuid(), DateTimeOffset.UtcNow, frame.Bounds, result.FirstOrDefault()?.SourceLanguage ?? document.Language,
                configuration.TargetLanguage, configuration.Provider, kind, document.Blocks.Select((b, i) => new TranslatedBlock(b, result[i].Text) { BackgroundColor = configuration.SampleOverlayBackground ? ScreenAnalysis.SampleBackground(frame, b.Bounds) : null }).ToArray(), new(captureMs, ocrMs, translationMs, total.Elapsed.TotalMilliseconds));
            LastSession = session; LastFrame = frame; lastProfileSettings = appliedProfile ? configuration : null; InputText = session.OriginalText; OutputText = session.TranslatedText;
            PipelineInfo = $"Capture {captureMs:0} ms  ·  OCR {ocrMs:0} ms  ·  Translate {translationMs:0} ms  ·  Total {total.Elapsed.TotalMilliseconds:0} ms";
            if (selectedWord is not null) WordLookupRequested?.Invoke(selectedWord.Text, session, session.Blocks[0]); else SessionReady?.Invoke(session);
            previousScreenText = document.Text;
            Status = IsMonitoring ? "Автоперевод области включён · нажмите Стоп для завершения" : $"Переведено реплик: {session.Blocks.Count}. {configuration.OcrEngine} · {configuration.Provider}";
            logger.LogInformation("Pipeline finished: {BlockCount} blocks, capture {CaptureMs:0} ms, OCR {OcrMs:0} ms, translation {TranslationMs:0} ms", session.Blocks.Count, captureMs, ocrMs, translationMs);
            if (Settings.SaveHistory)
            {
                try { await Task.Run(() => database.AddHistoryAsync(session, Settings.HistoryLimit, token), token); }
                catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning("History write failed: {ExceptionType}", e.GetType().Name); Status = "Translation ready, but history could not be saved. Check available disk space."; }
            }
            await RefreshAsync();
        });
    }
    public async Task SaveSettingsAsync(AppSettings value, string? newKey, bool removeKey)
    {
        value.Validate(); GlobalHotkeys.Validate(value.Hotkeys); await settingsStore.SaveAsync(value);
        if (removeKey) credentials.Delete(value.Provider); else if (!string.IsNullOrWhiteSpace(newKey)) credentials.Write(value.Provider, newKey.Trim());
        Settings = value; lastProfileSettings = null; OnPropertyChanged(nameof(NeedsProviderSetup)); cache.Clear(); Status = "Настройки сохранены."; SettingsChanged?.Invoke();
    }
    public bool HasKey(string provider) { try { return !string.IsNullOrWhiteSpace(credentials.Read(provider)); } catch (UserFacingException) { return false; } }
    public void SaveAssistantKey(string provider, string key) { if (!string.IsNullOrWhiteSpace(key) && provider != "None") credentials.Write(provider, key.Trim()); }
    public void UpdateProviderCredential(string provider, string key, bool remove)
    {
        if (!ProviderCatalog.All.Any(p => p.Name == provider)) throw new UserFacingException("Выберите сервис перевода.");
        if (remove) credentials.Delete(provider); else if (!string.IsNullOrWhiteSpace(key)) credentials.Write(provider, key.Trim());
        OnPropertyChanged(nameof(NeedsProviderSetup));
    }
    public async Task<string> TestConnectionAsync(AppSettings configuration, string? draftKey, bool removeSavedKey, CancellationToken token)
    {
        configuration.Validate();
        var keys = new PreviewCredentials(credentials, configuration.Provider, draftKey, removeSavedKey);
        var result = await new TranslationProvider(http, configuration, keys).TranslateAsync(new(["Hello, traveler."], "en", configuration.TargetLanguage, configuration.ContextHint), token);
        return result[0].Text;
    }
    private sealed class PreviewCredentials(ICredentialStore saved, string provider, string? draft, bool remove) : ICredentialStore
    {
        public string? Read(string name) => name == provider ? remove ? null : string.IsNullOrWhiteSpace(draft) ? saved.Read(name) : draft.Trim() : saved.Read(name);
        public void Write(string name, string value) => throw new NotSupportedException();
        public void Delete(string name) => throw new NotSupportedException();
    }
    public async Task TestProviderAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Settings.TranslationTimeoutSeconds));
        var result = await Provider().TranslateAsync(new(["Hello, traveler."], "en", Settings.TargetLanguage, Settings.ContextHint), timeout.Token);
        Status = $"{Settings.Provider}: {result[0].Text}";
    }
    public async Task SyncAnkiAsync()
    {
        await ExecutePipelineAsync(async token =>
        {
            Status = "Синхронизация Anki…";
            var words = await Task.Run(() => database.GetWordsAsync(token: token), token); var result = await new AnkiConnectClient(http, media).SyncAsync(Settings, words, token);
            Status = $"Anki: добавлено {result.Added}, обновлено {result.Updated}, помечено удалёнными {result.MarkedRemoved}. Расписание повторений сохранено.";
        });
    }
    public async Task<TranslationItem> LookupAsync(string word, string source, string target, string context, CancellationToken token, AppSettings? configuration = null)
    {
        var result = await cache.TranslateAsync(Provider(configuration), new([word], source, target, context), CacheIdentity(configuration), token);
        return result[0];
    }
    public async Task<VocabularyEntry> SaveWordAsync(VocabularyEntry word) { var saved = await Task.Run(() => database.SaveWordAsync(word)); await RefreshAsync(); Status = saved.Encounters > 1 ? "Already saved. Encounter count updated." : "Added to your vocabulary."; return saved; }
    public async Task UpdateWordAsync(VocabularyEntry word) { await Task.Run(() => database.UpdateWordAsync(word)); await RefreshAsync(); Status = "Word updated."; }
    [RelayCommand] private async Task DeleteWordAsync() => await GuardAsync(async () => { if (SelectedWord is not { } word) return; await Task.Run(() => database.DeleteWordAsync(word.Id)); SelectedWord = null; await RefreshAsync(); Status = "Word deleted."; });
    [RelayCommand] private async Task DeleteHistoryAsync() => await GuardAsync(async () => { if (SelectedHistory is not { } entry) return; await Task.Run(() => database.DeleteHistoryAsync(entry.Id)); SelectedHistory = null; await RefreshAsync(); Status = "History entry deleted."; });
    [RelayCommand]
    private async Task StartReviewAsync() => await GuardAsync(async () =>
    {
        ReviewCard = (await Task.Run(() => database.GetWordsAsync(dueOnly: true))).OrderBy(e => e.DueAt).FirstOrDefault(); AnswerRevealed = false;
        if (ReviewCard is null) Status = "You’re all caught up. Save more words or come back when cards are due.";
    });
    [RelayCommand] private void RevealAnswer() => AnswerRevealed = true;
    [RelayCommand]
    private async Task GradeAsync(string grade) => await GuardAsync(async () =>
    {
        if (ReviewCard is null || !AnswerRevealed) return;
        var updated = ReviewScheduler.Apply(ReviewCard, Enum.Parse<ReviewGrade>(grade), DateTimeOffset.UtcNow);
        await Task.Run(() => database.UpdateWordAsync(updated)); await RefreshAsync(); await StartReviewAsync();
    });
    public async Task ClearHistoryAsync() { await Task.Run(() => database.ClearHistoryAsync()); await RefreshAsync(); }
    public async Task ImportWordsAsync(IReadOnlyList<VocabularyEntry> entries) => await ExecutePipelineAsync(async token =>
    {
        await Task.Run(async () => { foreach (var entry in entries) { token.ThrowIfCancellationRequested(); await database.SaveWordAsync(entry, token); } }, token);
        await RefreshAsync(); Status = $"Imported {entries.Count} words. Existing words update their encounter count.";
    });
    public async Task ClearWordsAsync() { await Task.Run(() => database.ClearVocabularyAsync()); await RefreshAsync(); ReviewCard = null; }
    public void ReopenHistory() { if (SelectedHistory is not { } session) return; InputText = session.OriginalText; OutputText = session.TranslatedText; LastSession = session; LastFrame = null; lastProfileSettings = null; if (session.CaptureType != "Text") SessionReady?.Invoke(session); PageIndex = 1; }
}

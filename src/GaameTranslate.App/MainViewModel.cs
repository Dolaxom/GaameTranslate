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
    ICredentialStore credentials, HttpClient http, TranslationCache cache, ILogger<MainViewModel> logger) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceLanguage), nameof(TargetLanguage), nameof(LanguagePairLabel), nameof(NeedsProviderSetup))]
    private AppSettings settings = new();
    [ObservableProperty] private int pageIndex;
    [ObservableProperty] private string status = "Ready when you are. Select an area to get started.";
    [ObservableProperty] private bool isBusy;
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
    public bool NeedsProviderSetup => Settings.Provider != "LibreTranslate" && !HasKey(Settings.Provider);
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
    public event Action? SettingsChanged;
    public event Action? ToggleOverlayRequested;
    public event Action? InteractionRequested;
    public event Action<string>? OperationFailed;
    private CancellationTokenSource? operation;
    private readonly SemaphoreSlim pipelineGate = new(1, 1);
    private long generation;
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
    [RelayCommand] private void Cancel() => operation?.Cancel();
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
            LastSession = session; if (Settings.SaveHistory) await Task.Run(() => database.AddHistoryAsync(session, Settings.HistoryLimit, token), token); await RefreshAsync();
        });
    });
    private ITranslationProvider Provider(AppSettings? configuration = null) => new TranslationProvider(http, configuration ?? Settings, credentials);
    private string CacheIdentity(AppSettings? configuration = null) { var s = configuration ?? Settings; return $"{s.Provider}|{s.LibreEndpoint}|{s.AzureRegion}"; }
    private async Task ExecutePipelineAsync(Func<CancellationToken, Task> work)
    {
        operation?.Cancel(); var local = new CancellationTokenSource(); operation = local; var id = Interlocked.Increment(ref generation);
        IsBusy = true;
        try { await pipelineGate.WaitAsync(local.Token); try { await work(local.Token); } finally { pipelineGate.Release(); } }
        finally { if (id == generation) { IsBusy = false; operation = null; } local.Dispose(); }
    }
    public async Task StopAsync() { operation?.Cancel(); await pipelineGate.WaitAsync(); pipelineGate.Release(); }
    public async Task RunCaptureAsync(string kind)
    {
        await ExecutePipelineAsync(async token =>
        {
            var configuration = Settings;
            var total = Stopwatch.StartNew(); var clock = Stopwatch.StartNew(); CapturedFrame frame; PixelRect? bounds = null;
            var prior = NativeMethods.GetForegroundWindow();
            try
            {
                Status = kind == "Region" ? "Drag over text. Press Esc to cancel." : "Capturing screen…";
                CaptureVisibilityChanged?.Invoke(true); await Task.Delay(100, token);
                if (kind == "Region") bounds = SelectRegion is null ? null : await SelectRegion(token);
                else if (kind == "Repeat") bounds = configuration.LastRegion ?? throw new UserFacingException("Select a region first. The repeat shortcut will reuse it.");
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
            Status = "Reading text with Windows OCR…"; clock.Restart();
            var document = await ocr.RecognizeAsync(frame, configuration.OcrLanguage, configuration.Preprocessing, token); var ocrMs = clock.Elapsed.TotalMilliseconds;
            if (document.Blocks.Count == 0) throw new UserFacingException("No text was found. Select a tighter area, choose the matching OCR language or enable small-text scaling in Settings.");
            Status = $"Translating {document.Blocks.Count} text lines…"; clock.Restart();
            var result = await cache.TranslateAsync(Provider(configuration), new(document.Blocks.Select(b => b.Text).ToArray(), configuration.SourceLanguage, configuration.TargetLanguage,
                string.Join("\n", new[] { configuration.ContextHint, document.Text[..Math.Min(document.Text.Length, 10000)] }).Trim()), CacheIdentity(configuration), token);
            var translationMs = clock.Elapsed.TotalMilliseconds; token.ThrowIfCancellationRequested();
            var session = new TranslationSession(Guid.NewGuid(), DateTimeOffset.UtcNow, frame.Bounds, result.FirstOrDefault()?.SourceLanguage ?? document.Language,
                configuration.TargetLanguage, configuration.Provider, kind, document.Blocks.Select((b, i) => new TranslatedBlock(b, result[i].Text)).ToArray(), new(captureMs, ocrMs, translationMs, total.Elapsed.TotalMilliseconds));
            LastSession = session; InputText = session.OriginalText; OutputText = session.TranslatedText;
            PipelineInfo = $"Capture {captureMs:0} ms  ·  OCR {ocrMs:0} ms  ·  Translate {translationMs:0} ms  ·  Total {total.Elapsed.TotalMilliseconds:0} ms";
            SessionReady?.Invoke(session); Status = $"Translated {session.Blocks.Count} lines. Click an OCR word to look it up and save it.";
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
        Settings = value; OnPropertyChanged(nameof(NeedsProviderSetup)); cache.Clear(); SettingsChanged?.Invoke(); Status = "Settings saved. Shortcuts and overlay preferences are active.";
    }
    public bool HasKey(string provider) { try { return !string.IsNullOrWhiteSpace(credentials.Read(provider)); } catch (UserFacingException) { return false; } }
    public async Task<TranslationItem> LookupAsync(string word, string source, string target, string context, CancellationToken token)
    {
        var result = await cache.TranslateAsync(Provider(), new([word], source, target, Settings.Provider.StartsWith("DeepL", StringComparison.Ordinal) ? context : ""), CacheIdentity(), token);
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
    public void ReopenHistory() { if (SelectedHistory is not { } session) return; InputText = session.OriginalText; OutputText = session.TranslatedText; LastSession = session; if (session.CaptureType != "Text") SessionReady?.Invoke(session); PageIndex = 1; }
}

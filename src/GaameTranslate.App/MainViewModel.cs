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
    [NotifyPropertyChangedFor(nameof(SourceLanguage), nameof(TargetLanguage), nameof(LanguagePairLabel), nameof(NeedsProviderSetup), nameof(ShowDictionaryNotice), nameof(DictionaryNotice), nameof(DictionaryNoticeTitle))]
    private AppSettings settings = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDictionaryNotice), nameof(DictionaryNotice), nameof(DictionaryNoticeTitle))]
    private bool usedDictionaryFallback;
    private readonly TranslationFailover failover = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDictionaryNotice))]
    private bool isStartupTipVisible;
    public event Action? QuickStartRequested;
    public event Action<string>? SettingsTabRequested;
    public void NotifyStartup() => IsStartupTipVisible = Settings.ShowStartupTips && NeedsProviderSetup;
    [RelayCommand] private void DismissStartupTip() => IsStartupTipVisible = false;
    [RelayCommand] private void OpenQuickStart() { IsStartupTipVisible = false; QuickStartRequested?.Invoke(); }
    [RelayCommand] private void TrySample()
    {
        IsStartupTipVisible = false;
        var useSample = InputText.Length == 0;
        if (useSample) InputText = "Hello, traveler! Your journey begins here.";
        PageIndex = 1; Status = useSample ? "Sample ready. Press Translate text to try your current translator. No screen capture is needed." : "Your text has been kept. Press Translate text when you are ready.";
        if (System.Windows.Application.Current is App app) app.ShowMain();
    }
    public void OpenSettingsTab(string tab) { OpenSettings(); SettingsTabRequested?.Invoke(tab); }
    [ObservableProperty] private int pageIndex;
    [ObservableProperty] private UiMessage status = "Ready when you are. Select an area to get started.";
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
    [ObservableProperty] private UiMessage pipelineInfo = "No capture yet";
    public ObservableCollection<VocabularyEntry> Words { get; } = [];
    public ObservableCollection<VocabularyEntry> RecentWords { get; } = [];
    public ObservableCollection<TranslationSession> History { get; } = [];
    public ObservableCollection<TranslationSession> RecentHistory { get; } = [];
    public IReadOnlyList<LanguageChoice> SourceLanguages { get; } = [new("auto", "Detect automatically"), .. Languages.Translation];
    public IReadOnlyList<LanguageChoice> TargetLanguages => Languages.Translation;
    public string ApplicationVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "";
    public IReadOnlyList<LanguageChoice> OcrLanguages => [new("auto", "Windows profile language"), .. ocr.AvailableLanguages];
    public string OcrSummary => ocr.AvailableLanguages.Count == 0 ? "No OCR packs installed" : string.Join(", ", ocr.AvailableLanguages.Select(l => l.Name));
    public TranslationSession? LastSession { get; private set; }
    public CapturedFrame? LastFrame { get; private set; }
    private AppSettings? lastProfileSettings;
    public AppSettings SettingsForSession(TranslationSession session) => LastSession?.Id == session.Id && lastProfileSettings is { } profile
        ? profile with { InterfaceLanguage = Settings.InterfaceLanguage, LightTheme = Settings.LightTheme, Hotkeys = Settings.Hotkeys, SaveWordScreenshots = Settings.SaveWordScreenshots, SaveWordAudio = Settings.SaveWordAudio }
        : Settings;
    public DictionaryService Dictionary => dictionary;
    public ISpeechService Speech => speech;
    public LocalMediaStore Media => media;
    public GameProfileStore Profiles { get; } = new(Path.GetDirectoryName(media.DirectoryPath)!);
    public bool UsesBuiltInDictionary(AppSettings configuration) => configuration.Provider == ProviderCatalog.BuiltInDictionary || ProviderCatalog.MissingSetupFields(configuration) || ProviderCatalog.RequiresKey(configuration) && !HasKey(configuration.Provider);
    public bool NeedsProviderSetup => UsesBuiltInDictionary(Settings);
    public bool ShowDictionaryNotice => (NeedsProviderSetup || UsedDictionaryFallback) && !IsStartupTipVisible;
    public UiMessage DictionaryNotice => UsedDictionaryFallback ? ProviderCatalog.DictionaryFallbackWarning : ProviderCatalog.DictionaryWarning;
    public UiMessage DictionaryNoticeTitle => UsedDictionaryFallback ? "Backup translation · English → Russian" : "Built-in dictionary · English → Russian";
    public string EffectiveProviderName(AppSettings configuration) => UsesBuiltInDictionary(configuration) ? ProviderCatalog.BuiltInDictionary : configuration.Provider;
    public UiMessage LanguagePairLabel => L.M("{0} → {1}", new UiMessage(Settings.SourceLanguage == "auto" ? "Auto" : Languages.Translation.FirstOrDefault(l => l.Code == Settings.SourceLanguage)?.Name ?? Settings.SourceLanguage), new UiMessage(Languages.Translation.FirstOrDefault(l => l.Code == Settings.TargetLanguage)?.Name ?? Settings.TargetLanguage));
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
    public bool InterfaceOnlySettingsChange { get; private set; }
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
        if (NeedsProviderSetup) Status = ProviderCatalog.DictionaryShortWarning;
    }
    public async Task RefreshAsync()
    {
        var selectedId = SelectedWord?.Id;
        var all = await Task.Run(() => database.GetWordsAsync()); Replace(Words, all); SelectedWord = all.FirstOrDefault(w => w.Id == selectedId);
        WordCount = all.Count; DueCount = all.Count(w => w.DueAt <= DateTimeOffset.UtcNow);
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
            if (e is UserFacingException { IsDictionaryFallback: true }) UsedDictionaryFallback = true;
            logger.LogWarning("Operation failed: {ExceptionType}", e.GetType().Name);
            Status = e is UserFacingException known ? known.LocalizedMessage : "The operation could not be completed. Check your settings, connection and available disk space, then try again.";
            OperationFailed?.Invoke(Status.Text);
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
    [RelayCommand] private void OpenSettings() { IsStartupTipVisible = false; PageIndex = 5; SettingsTabRequested?.Invoke("translation"); if (System.Windows.Application.Current is App app) app.ShowMain(); }
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
            var result = await cache.TranslateAsync(provider, new(texts, configuration.SourceLanguage, configuration.TargetLanguage, configuration.ContextHint) { PreserveUnknownText = true }, CacheIdentity(configuration), token);
            token.ThrowIfCancellationRequested(); UsedDictionaryFallback = result.Any(r => r.IsDictionaryFallback); OutputText = string.Join(Environment.NewLine, result.Select(r => r.Text)); Status = UsedDictionaryFallback ? ProviderCatalog.DictionaryFallbackShortWarning : result.Any(r => r.IsDictionaryTranslation) ? ProviderCatalog.DictionaryShortWarning : "Translation ready.";
            var blocks = texts.Select((t, i) => new TranslatedBlock(new(i, t, new(0, 0, 0, 0), []), result[i].Text)).ToArray();
            var session = new TranslationSession(Guid.NewGuid(), DateTimeOffset.UtcNow, new(), result[0].SourceLanguage, configuration.TargetLanguage, result.Any(r => r.IsDictionaryTranslation) ? ProviderCatalog.BuiltInDictionary : EffectiveProviderName(configuration), "Text", blocks, new(0, 0, 0, 0)) { IsDictionaryTranslation = result.Any(r => r.IsDictionaryTranslation), IsDictionaryFallback = UsedDictionaryFallback };
            LastSession = session; LastFrame = null; lastProfileSettings = null; if (Settings.SaveHistory) await Task.Run(() => database.AddHistoryAsync(session, Settings.HistoryLimit, token), token); await RefreshAsync();
        });
    });
    private ITranslationProvider Provider(AppSettings? configuration = null) => UsesBuiltInDictionary(configuration ?? Settings) ? new BuiltInDictionaryProvider() : failover.Wrap(new TranslationProvider(http, configuration ?? Settings, credentials), CacheIdentity(configuration));
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
                if (profile is not null) { appliedProfile = true; configuration = profile.Settings with { InterfaceLanguage = Settings.InterfaceLanguage, AutoApplyGameProfiles = Settings.AutoApplyGameProfiles, SaveHistory = Settings.SaveHistory, SaveWordScreenshots = Settings.SaveWordScreenshots, SaveWordAudio = Settings.SaveWordAudio }; }
            }
            var total = Stopwatch.StartNew(); var clock = Stopwatch.StartNew(); CapturedFrame frame; PixelRect? bounds = null;
            var prior = NativeMethods.GetForegroundWindow();
            NativeMethods.GetCursorPos(out var cursor);
            try
            {
                Status = kind == "Region" ? "Drag over text. Press Esc to cancel." : "Capturing screen…";
                CaptureVisibilityChanged?.Invoke(true); await Task.Delay(kind == "Word at cursor" ? 16 : 100, token);
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
                token.ThrowIfCancellationRequested(); NativeMethods.SetForegroundWindow(prior); NativeMethods.DwmFlush(); await Task.Delay(kind == "Word at cursor" ? 16 : 80, token);
                clock.Restart(); frame = await capture.CaptureAsync(bounds.Value, token);
            }
            finally { CaptureVisibilityChanged?.Invoke(false); }
            var captureMs = clock.Elapsed.TotalMilliseconds;
            if (kind == "Region") { Settings = Settings with { LastRegion = bounds }; await settingsStore.SaveAsync(Settings, token); }
            Status = L.M("Распознавание: {0}…", configuration.OcrEngine); clock.Restart();
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
            var source = configuration.SourceLanguage == "auto" ? Languages.Base(document.Language) : configuration.SourceLanguage;
            if (selectedWord is not null)
            {
                token.ThrowIfCancellationRequested();
                // Open as soon as the word is recognized. The popup owns its cancellable, progressive requests.
                var lookupSession = new TranslationSession(Guid.NewGuid(), DateTimeOffset.UtcNow, frame.Bounds, source, configuration.TargetLanguage, EffectiveProviderName(configuration), kind,
                    document.Blocks.Select(b => new TranslatedBlock(b, "")).ToArray(), new(captureMs, ocrMs, 0, total.Elapsed.TotalMilliseconds)) { IsDictionaryTranslation = UsesBuiltInDictionary(configuration) };
                LastSession = lookupSession; LastFrame = frame; lastProfileSettings = appliedProfile ? configuration : null; InputText = document.Text; OutputText = "";
                PipelineInfo = L.M("Capture {0:0} ms  ·  OCR {1:0} ms  ·  Словарь открыт до перевода контекста", captureMs, ocrMs);
                WordLookupRequested?.Invoke(selectedWord.Text, lookupSession, lookupSession.Blocks[0]);
                Status = lookupSession.IsDictionaryTranslation ? ProviderCatalog.DictionaryShortWarning : "Слово распознано · сначала быстрый перевод, затем контекст";
                logger.LogInformation("Word lookup opened after capture {CaptureMs:0} ms and OCR {OcrMs:0} ms", captureMs, ocrMs);
                return;
            }
            Status = L.M("Перевод {0} реплик…", document.Blocks.Count); clock.Restart();
            var result = await cache.TranslateAsync(Provider(configuration), new(document.Blocks.Select(b => b.Text).ToArray(), source, configuration.TargetLanguage,
                string.Join("\n", new[] { configuration.ContextHint, document.Text[..Math.Min(document.Text.Length, 10000)] }).Trim()) { PreserveUnknownText = true }, CacheIdentity(configuration), token);
            var translationMs = clock.Elapsed.TotalMilliseconds; token.ThrowIfCancellationRequested();
            UsedDictionaryFallback = result.Any(r => r.IsDictionaryFallback);
            var session = new TranslationSession(Guid.NewGuid(), DateTimeOffset.UtcNow, frame.Bounds, result.FirstOrDefault()?.SourceLanguage ?? document.Language,
                configuration.TargetLanguage, result.Any(r => r.IsDictionaryTranslation) ? ProviderCatalog.BuiltInDictionary : EffectiveProviderName(configuration), kind, document.Blocks.Select((b, i) => new TranslatedBlock(b, result[i].Text) { BackgroundColor = configuration.SampleOverlayBackground ? ScreenAnalysis.SampleBackground(frame, b.Bounds) : null }).ToArray(), new(captureMs, ocrMs, translationMs, total.Elapsed.TotalMilliseconds)) { IsDictionaryTranslation = result.Any(r => r.IsDictionaryTranslation), IsDictionaryFallback = UsedDictionaryFallback };
            LastSession = session; LastFrame = frame; lastProfileSettings = appliedProfile ? configuration : null; InputText = session.OriginalText; OutputText = session.TranslatedText;
            PipelineInfo = L.M("Capture {0:0} ms  ·  OCR {1:0} ms  ·  Translate {2:0} ms  ·  Total {3:0} ms", captureMs, ocrMs, translationMs, total.Elapsed.TotalMilliseconds);
            SessionReady?.Invoke(session);
            previousScreenText = document.Text;
            Status = IsMonitoring ? new UiMessage("Автоперевод области включён · нажмите Стоп для завершения") : L.M("Переведено реплик: {0}. {1} · {2}", session.Blocks.Count, configuration.OcrEngine, configuration.Provider);
            if (session.IsDictionaryTranslation) Status = ProviderCatalog.DictionaryShortWarning;
            if (session.IsDictionaryFallback) Status = ProviderCatalog.DictionaryFallbackShortWarning;
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
        InterfaceOnlySettingsChange = (value.InterfaceLanguage != Settings.InterfaceLanguage || value.ShowStartupTips != Settings.ShowStartupTips) && (value with { InterfaceLanguage = Settings.InterfaceLanguage, ShowStartupTips = Settings.ShowStartupTips }) == Settings && string.IsNullOrWhiteSpace(newKey) && !removeKey;
        value.Validate(); GlobalHotkeys.Validate(value.Hotkeys); await settingsStore.SaveAsync(value);
        if (removeKey) credentials.Delete(value.Provider); else if (!string.IsNullOrWhiteSpace(newKey)) credentials.Write(value.Provider, newKey.Trim());
        Settings = value; L.SetLanguage(value.InterfaceLanguage);
        if (!NeedsProviderSetup || !value.ShowStartupTips) IsStartupTipVisible = false;
        if (!InterfaceOnlySettingsChange) { lastProfileSettings = null; cache.Clear(); failover.Clear(); UsedDictionaryFallback = false; }
        OnPropertyChanged(nameof(NeedsProviderSetup)); Status = "Настройки сохранены."; SettingsChanged?.Invoke();
    }
    public bool HasKey(string provider) { try { return !string.IsNullOrWhiteSpace(credentials.Read(provider)); } catch (UserFacingException) { return false; } }
    public void SaveAssistantKey(string provider, string key) { if (!string.IsNullOrWhiteSpace(key) && provider != "None") credentials.Write(provider, key.Trim()); }
    public void UpdateProviderCredential(string provider, string key, bool remove)
    {
        if (!ProviderCatalog.All.Any(p => p.Name == provider)) throw new UserFacingException("Выберите сервис перевода.");
        if (remove) credentials.Delete(provider); else if (!string.IsNullOrWhiteSpace(key)) credentials.Write(provider, key.Trim());
        if (remove || !string.IsNullOrWhiteSpace(key)) { cache.Clear(); failover.Clear(); UsedDictionaryFallback = false; }
        OnPropertyChanged(nameof(NeedsProviderSetup));
        OnPropertyChanged(nameof(ShowDictionaryNotice));
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
        var result = await new TranslationProvider(http, Settings, credentials).TranslateAsync(new(["Hello, traveler."], "en", Settings.TargetLanguage, Settings.ContextHint), timeout.Token);
        Status = L.M("{0}: {1}", Settings.Provider, result[0].Text);
    }
    public async Task SyncAnkiAsync()
    {
        await ExecutePipelineAsync(async token =>
        {
            Status = "Синхронизация Anki…";
            var words = await Task.Run(() => database.GetWordsAsync(token: token), token); var result = await new AnkiConnectClient(http, media).SyncAsync(Settings, words, token);
            Status = L.M("Anki: добавлено {0}, обновлено {1}, помечено удалёнными {2}. Расписание повторений сохранено.", result.Added, result.Updated, result.MarkedRemoved);
        });
    }
    public async Task<TranslationItem> LookupAsync(string word, string source, string target, string context, CancellationToken token, AppSettings? configuration = null, bool preserveUnknownText = false)
    {
        var result = await cache.TranslateAsync(Provider(configuration), new([word], source, target, context) { PreserveUnknownText = preserveUnknownText }, CacheIdentity(configuration), token);
        token.ThrowIfCancellationRequested(); UsedDictionaryFallback = result.Any(r => r.IsDictionaryFallback);
        return result[0];
    }
    public async Task<(TranslationItem Item, bool Saved)> LookupWordAsync(string word, string source, string target, AppSettings configuration, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var saved = Words.FirstOrDefault(w => TextProcessing.WordKey(w.Word) == TextProcessing.WordKey(word) && Languages.Base(w.SourceLanguage) == Languages.Base(source) && w.TargetLanguage == target);
        if (saved is not null) return (new(saved.Translation, saved.SourceLanguage), true);
        // Context-free keys let a repeated word hit the cache even in a different game sentence.
        return (await LookupAsync(word, source, target, "", token, configuration), false);
    }
    public void RecordWordLookupReady(TranslationSession session, double elapsedMs)
    {
        if (LastSession?.Id != session.Id || session.CaptureType != "Word at cursor") return;
        var totalMs = session.Timings.TotalMs + elapsedMs;
        PipelineInfo = L.M("Capture {0:0} ms  ·  OCR {1:0} ms  ·  Слово {2:0} ms  ·  Первый результат {3:0} ms", session.Timings.CaptureMs, session.Timings.OcrMs, elapsedMs, totalMs);
        logger.LogInformation("Word translation ready in {WordMs:0} ms after OCR, total {TotalMs:0} ms", elapsedMs, totalMs);
    }
    public async Task CompleteWordLookupContextAsync(TranslationSession session, string translation, double elapsedMs, CancellationToken token, bool dictionaryTranslation = false, bool dictionaryFallback = false)
    {
        token.ThrowIfCancellationRequested();
        if (session.CaptureType != "Word at cursor" || LastSession?.Id != session.Id || string.IsNullOrWhiteSpace(translation)) return;
        var complete = session with { Blocks = session.Blocks.Select(b => b with { Translation = translation }).ToArray(), Timings = session.Timings with { TranslationMs = elapsedMs, TotalMs = session.Timings.TotalMs + elapsedMs }, IsDictionaryTranslation = dictionaryTranslation, IsDictionaryFallback = dictionaryFallback, Provider = dictionaryTranslation ? ProviderCatalog.BuiltInDictionary : session.Provider == ProviderCatalog.BuiltInDictionary ? EffectiveProviderName(SettingsForSession(session)) : session.Provider };
        LastSession = complete; OutputText = translation;
        if (Settings.SaveHistory)
        {
            try { await Task.Run(() => database.AddHistoryAsync(complete, Settings.HistoryLimit, token), token); }
            catch (Exception error) when (error is not OperationCanceledException) { logger.LogWarning("Word lookup history write failed: {ExceptionType}", error.GetType().Name); }
        }
        token.ThrowIfCancellationRequested();
        if (LastSession?.Id == session.Id) await RefreshAsync();
    }
    public async Task<VocabularyEntry> SaveWordAsync(VocabularyEntry word) { var saved = await Task.Run(() => database.SaveWordAsync(word)); await RefreshAsync(); Status = saved.Encounters > 1 ? "Слово уже есть в словаре. Перевод и счётчик встреч обновлены." : "Слово добавлено в словарь."; return saved; }
    public async Task UpdateWordAsync(VocabularyEntry word) { await Task.Run(() => database.UpdateWordAsync(word)); await RefreshAsync(); Status = "Карточка обновлена."; }
    [RelayCommand] private async Task DeleteWordAsync() => await GuardAsync(async () => { if (SelectedWord is not { } word) return; await Task.Run(() => database.DeleteWordAsync(word.Id)); SelectedWord = null; await RefreshAsync(); Status = "Карточка удалена из словаря."; });
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
        await RefreshAsync(); Status = L.M("Imported {0} words. Existing words update their encounter count.", entries.Count);
    });
    public async Task ClearWordsAsync() { await Task.Run(() => database.ClearVocabularyAsync()); await RefreshAsync(); ReviewCard = null; }
    public void ReopenHistory() { if (SelectedHistory is not { } session) return; InputText = session.OriginalText; OutputText = session.TranslatedText; LastSession = session; LastFrame = null; lastProfileSettings = null; if (session.CaptureType != "Text") SessionReady?.Invoke(session); PageIndex = 1; }
}

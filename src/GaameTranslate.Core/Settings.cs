namespace GaameTranslate.Core;

public sealed record PreprocessingOptions
{
    public double Scale { get; init; } = 1.5;
    public bool Grayscale { get; init; }
    public bool EnhanceContrast { get; init; }
    public bool Threshold { get; init; }
    public bool Denoise { get; init; }
}
public sealed record HotkeySettings
{
    public string Region { get; init; } = "Ctrl+Shift+T";
    public string Fullscreen { get; init; } = "Ctrl+Shift+F";
    public string Repeat { get; init; } = "Ctrl+Shift+R";
    public string ToggleOverlay { get; init; } = "Ctrl+Shift+O";
    public string Vocabulary { get; init; } = "Ctrl+Shift+V";
    public string Interaction { get; init; } = "Ctrl+Shift+I";
    public string ScreenTranslation { get; init; } = "Alt+Q";
    public string WordLookup { get; init; } = "Alt+W";
}
public sealed record AppSettings
{
    public string InterfaceLanguage { get; init; } = "ru";
    public string SourceLanguage { get; init; } = "auto";
    public string OcrLanguage { get; init; } = "auto";
    public string OcrEngine { get; init; } = "PP-OCRv6 Small";
    public bool OcrUseGpu { get; init; } = true;
    public string TargetLanguage { get; init; } = "ru";
    public string Provider { get; init; } = "LibreTranslate";
    public string LibreEndpoint { get; init; } = "http://127.0.0.1:5000";
    public string AzureRegion { get; init; } = "";
    public string ContextHint { get; init; } = "";
    public string YandexFolderId { get; init; } = "";
    public bool YandexIamToken { get; init; }
    public string YandexModel { get; init; } = "";
    public string GigaChatModel { get; init; } = "GigaChat";
    public string GigaChatScope { get; init; } = "GIGACHAT_API_PERS";
    public string CompatibleEndpoint { get; init; } = "http://127.0.0.1:1234/v1";
    public string CompatibleModel { get; init; } = "";
    public string OllamaEndpoint { get; init; } = "http://127.0.0.1:11434";
    public string OllamaModel { get; init; } = "translategemma:4b";
    public string AssistantProvider { get; init; } = "None";
    public string AssistantModel { get; init; } = "qwen3:4b-instruct-2507-q4_K_M";
    public int TranslationTimeoutSeconds { get; init; } = 120;
    public bool GroupParagraphs { get; init; } = true;
    public bool MatchSourceTypography { get; init; } = true;
    public bool SampleOverlayBackground { get; init; } = true;
    public int MonitoringIntervalMs { get; init; } = 1500;
    public bool SaveWordScreenshots { get; init; }
    public bool SaveWordAudio { get; init; }
    public bool EnableOnlineDictionary { get; init; }
    public bool AutoExplainWords { get; init; } = true;
    public string DictionaryDirectory { get; init; } = "";
    public string SpeechVoice { get; init; } = "";
    public bool FreezeImage { get; init; }
    public bool AutoApplyGameProfiles { get; init; }
    public bool ControllerEnabled { get; init; }
    public string AnkiEndpoint { get; init; } = "http://127.0.0.1:8765";
    public string AnkiDeck { get; init; } = "GaameTranslate";
    public OverlayMode OverlayMode { get; init; }
    public double OverlayOpacity { get; init; } = .94;
    public double OverlayFontSize { get; init; } = 18;
    public bool ClickThrough { get; init; }
    public bool SaveHistory { get; init; } = true;
    public int HistoryLimit { get; init; } = 500;
    public bool MinimizeToTray { get; init; } = true;
    public bool StartMinimized { get; init; }
    public bool LightTheme { get; init; }
    public int MonitorIndex { get; init; } = -1;
    public PixelRect? LastRegion { get; init; }
    public PreprocessingOptions Preprocessing { get; init; } = new();
    public HotkeySettings Hotkeys { get; init; } = new();
    public AppSettings Validate()
    {
        if (InterfaceLanguage is not ("ru" or "en")) throw new UserFacingException("Choose Russian or English for the interface.");
        if (Preprocessing is null || Hotkeys is null || string.IsNullOrWhiteSpace(OcrLanguage)) throw new UserFacingException("Settings are incomplete. Restore the OCR and shortcut settings.");
        if (OcrEngine is not ("Windows" or "PP-OCRv6 Small" or "PP-OCRv6 Medium")) throw new UserFacingException("Выберите доступный движок OCR.");
        if (ContextHint is null || ContextHint.Length > 1000) throw new UserFacingException("Keep the translation context hint under 1,000 characters.");
        if (!Languages.Translation.Any(l => l.Code == TargetLanguage)) throw new UserFacingException("Choose a supported target language.");
        if (SourceLanguage != "auto" && !Languages.Translation.Any(l => l.Code == SourceLanguage)) throw new UserFacingException("Choose a supported source language.");
        if (!ProviderCatalog.All.Any(p => p.Name == Provider)) throw new UserFacingException("Choose a translation provider.");
        if (Provider == "LibreTranslate") ValidateEndpoint(LibreEndpoint);
        if (Provider == "Ollama" || AssistantProvider == "Ollama") ValidateEndpoint(OllamaEndpoint);
        if (Provider == "OpenAI compatible" || AssistantProvider == "OpenAI compatible") ValidateEndpoint(CompatibleEndpoint);
        if (!ValidateEndpoint(AnkiEndpoint).IsLoopback) throw new UserFacingException("AnkiConnect должен работать на локальном компьютере.");
        if (string.IsNullOrWhiteSpace(AnkiDeck) || AnkiDeck.Length > 120) throw new UserFacingException("Укажите имя колоды Anki до 120 символов.");
        if (TranslationTimeoutSeconds is < 10 or > 600) throw new UserFacingException("Request timeout must be between 10 and 600 seconds.");
        if (MonitoringIntervalMs is < 500 or > 10000) throw new UserFacingException("Monitoring interval must be between 500 and 10,000 ms.");
        if (AssistantProvider != "None" && !ProviderCatalog.IsLanguageModel(AssistantProvider)) throw new UserFacingException("Choose a language model for dictionary explanations.");
        if (OverlayOpacity is < .3 or > 1 || !double.IsFinite(OverlayOpacity)) throw new UserFacingException("Overlay opacity must be between 30% and 100%.");
        if (OverlayFontSize is < 12 or > 40 || !double.IsFinite(OverlayFontSize)) throw new UserFacingException("Overlay text size must be between 12 and 40.");
        if (Preprocessing.Scale is < .5 or > 3 || !double.IsFinite(Preprocessing.Scale)) throw new UserFacingException("OCR scale must be between 0.5 and 3.");
        if (HistoryLimit is < 0 or > 10000) throw new UserFacingException("History limit must be between 0 and 10,000.");
        if (!Enum.IsDefined(OverlayMode)) throw new UserFacingException("Choose a valid overlay mode.");
        return this;
    }
    public static Uri ValidateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new UserFacingException("Use an HTTPS server address, or an HTTP loopback address such as http://127.0.0.1:5000. Credentials belong in the key field.");
        return uri;
    }
}

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
}
public sealed record AppSettings
{
    public string SourceLanguage { get; init; } = "auto";
    public string OcrLanguage { get; init; } = "auto";
    public string TargetLanguage { get; init; } = "ru";
    public string Provider { get; init; } = "DeepL Free";
    public string LibreEndpoint { get; init; } = "http://127.0.0.1:5000";
    public string AzureRegion { get; init; } = "";
    public string ContextHint { get; init; } = "";
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
        if (Preprocessing is null || Hotkeys is null || string.IsNullOrWhiteSpace(OcrLanguage)) throw new UserFacingException("Settings are incomplete. Restore the OCR and shortcut settings.");
        if (ContextHint is null || ContextHint.Length > 1000) throw new UserFacingException("Keep the translation context hint under 1,000 characters.");
        if (!Languages.Translation.Any(l => l.Code == TargetLanguage)) throw new UserFacingException("Choose a supported target language.");
        if (SourceLanguage != "auto" && !Languages.Translation.Any(l => l.Code == SourceLanguage)) throw new UserFacingException("Choose a supported source language.");
        if (Provider is not ("DeepL Free" or "DeepL Pro" or "Azure" or "LibreTranslate")) throw new UserFacingException("Choose a translation provider.");
        if (Provider == "LibreTranslate") ValidateEndpoint(LibreEndpoint);
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
            throw new UserFacingException("LibreTranslate needs an HTTPS address, or an HTTP loopback address such as http://127.0.0.1:5000.");
        return uri;
    }
}

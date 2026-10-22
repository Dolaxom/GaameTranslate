namespace GaameTranslate.Core;

public interface IScreenCapture
{
    Task<CapturedFrame> CaptureAsync(PixelRect bounds, CancellationToken cancellationToken);
}
public interface IOcrProvider
{
    IReadOnlyList<LanguageChoice> AvailableLanguages { get; }
    Task<OcrDocument> RecognizeAsync(CapturedFrame frame, string language, PreprocessingOptions options, CancellationToken cancellationToken);
}
public interface ITranslationProvider
{
    string Name { get; }
    Task<IReadOnlyList<TranslationItem>> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
}
public interface ICredentialStore
{
    string? Read(string name);
    void Write(string name, string value);
    void Delete(string name);
}
public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

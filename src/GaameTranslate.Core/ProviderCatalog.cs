namespace GaameTranslate.Core;

public sealed record ProviderInfo(string Name, string Description, bool LanguageModel = false, bool KeyRequired = true);
public static class ProviderCatalog
{
    public const string BuiltInDictionary = "Built-in dictionary";
    public const string DictionaryShortWarning = "No provider configured · built-in English–Russian dictionary. Basic word-by-word translation; context and grammar may be wrong.";
    public const string DictionaryWarning = "No translation provider is set up. Using the built-in English–Russian dictionary: word-by-word hints, without sentence context or grammar. Quality is limited; unknown words stay unchanged. Set up a provider in Settings for a full translation.";
    public static readonly IReadOnlyList<ProviderInfo> All = [
        new(BuiltInDictionary, "Offline English–Russian word dictionary · no setup needed", false, false),
        new("Yandex Translate", "Яндекс Cloud Translate · официальный API; API-ключ или IAM-токен"),
        new("YandexGPT", "Контекстный перевод и словарь · Yandex AI Studio",true),
        new("GigaChat", "Контекстный перевод и объяснения · Сбер GigaChat",true),
        new("Ollama", "Локальные модели: TranslateGemma, Qwen и другие",true,false),
        new("OpenAI compatible", "LM Studio, DeepSeek и другие совместимые серверы",true,false),
        new("LibreTranslate", "Локальный Argos / LibreTranslate · без API-ключа",false,false),
        new("Azure", "Microsoft Azure Translator"),
        new("DeepL Free", "DeepL API Free · доступность зависит от региона"),
        new("DeepL Pro", "DeepL API Pro · доступность зависит от региона")];
    public static bool IsLanguageModel(string name) => All.FirstOrDefault(p => p.Name == name)?.LanguageModel == true;
    public static bool RequiresKey(AppSettings settings)
    {
        if (settings.Provider == "OpenAI compatible") return !AppSettings.ValidateEndpoint(settings.CompatibleEndpoint).IsLoopback;
        if (settings.Provider == "Ollama") return !AppSettings.ValidateEndpoint(settings.OllamaEndpoint).IsLoopback;
        return All.FirstOrDefault(p => p.Name == settings.Provider)?.KeyRequired == true;
    }
    public static bool RequiresKey(string name) => All.FirstOrDefault(p => p.Name == name)?.KeyRequired == true;
    public static string Identity(AppSettings s) => System.Text.Json.JsonSerializer.Serialize(new { s.Provider, s.LibreEndpoint, s.AzureRegion, s.YandexFolderId, s.YandexIamToken, s.YandexModel, s.GigaChatModel, s.GigaChatScope, s.CompatibleEndpoint, s.CompatibleModel, s.OllamaEndpoint, s.OllamaModel });
}

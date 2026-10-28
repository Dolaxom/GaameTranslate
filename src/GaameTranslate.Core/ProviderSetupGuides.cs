namespace GaameTranslate.Core;

public sealed record ProviderSetupGuide(string Category, string Steps, string Note, string? DocumentationUrl);

/// <summary>Short, offline help. Opening a guide never contacts a provider.</summary>
public static class ProviderSetupGuides
{
    public static ProviderSetupGuide Get(string provider) => provider switch
    {
        ProviderCatalog.BuiltInDictionary => new("No setup · offline", "1. Keep English → Russian selected.\n2. Try a word or a short sentence.\n3. Connect a service later for full translations.", "Dictionary hints do not understand sentence context or grammar. Unknown words stay unchanged.", null),
        "None" => new("Optional assistant", "1. Keep the assistant off to start.\n2. Word translation and saved cards still work.\n3. Choose a language model here when you want explanations.", "The translator handles the text. The assistant explains a word in its sentence.", null),
        "LibreTranslate" => new("Local or self-hosted translator", "1. Start your LibreTranslate server with English and Russian models.\n2. Enter its address, usually http://127.0.0.1:5000.\n3. Check the connection, then save.", "Installing GaameTranslate does not start a LibreTranslate server. A local server usually needs no API key.", "https://docs.libretranslate.com/guides/installation/"),
        "Ollama" => new("Local AI · hardware-dependent speed", "1. Install and start Ollama; download a model.\n2. Enter its model name and server address, usually http://127.0.0.1:11434.\n3. Check the connection, then save.", "Translation: translategemma:4b or a general model. Explanations: use a general model such as Qwen. Models need a separate download.", "https://docs.ollama.com/quickstart"),
        "OpenAI compatible" => new("Local server or compatible cloud API", "1. Start LM Studio or get access to a compatible API.\n2. Enter the base URL, model ID, and a key if required.\n3. Check the connection, then save.", "For local LM Studio, the usual URL is http://127.0.0.1:1234/v1. Cloud APIs need their own credentials and may charge for usage.", "https://lmstudio.ai/docs/developer/openai-compat"),
        "Yandex Translate" => new("Cloud translator · API account", "1. Enable Yandex Cloud Translate access and create an API key.\n2. Enter the key and your folder ID; choose IAM only for an IAM token.\n3. Check the connection, then save.", "Use the Translate API credentials, not a Yandex account password. Billing and the ai.translate.user role must allow requests.", "https://aistudio.yandex.ru/en/docs/translate/quickstart"),
        "YandexGPT" => new("Cloud AI · API account", "1. Get Yandex AI Studio API access and model permissions.\n2. Enter the API key and folder ID; select a model if needed.\n3. Check the connection, then save.", "A consumer chat subscription does not replace API access. Translation and dictionary explanations are configured separately.", "https://aistudio.yandex.ru/ru/docs/ai-studio/quickstart/"),
        "GigaChat" => new("Cloud AI · API account", "1. Get an authorization key from the GigaChat API account.\n2. Paste that key and select the matching scope and model.\n3. Check the connection, then save.", "Use the authorization key, not your password or a temporary access token. GIGACHAT_API_PERS is for personal API accounts; Windows may need the provider's trusted certificate.", "https://developers.sber.ru/docs/ru/gigachat/api/reference/rest/gigachat-api"),
        "Azure" => new("Cloud translator · Azure resource", "1. Create an Azure Translator resource.\n2. Copy its API key and region into these fields.\n3. Check the connection, then save.", "Use the Translator resource credentials. Regional or multi-service resources need the matching region.", "https://learn.microsoft.com/en-us/azure/ai-services/translator/text-translation/quickstart/rest-api"),
        "DeepL Free" or "DeepL Pro" => new("Cloud translator · DeepL API plan", "1. Get a key for a DeepL API plan.\n2. Match Free or Pro to that API key.\n3. Check the connection, then save.", "A DeepL website subscription is separate from API access. Availability depends on your region and account.", "https://developers.deepl.com/docs/getting-started/quickstart"),
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
}

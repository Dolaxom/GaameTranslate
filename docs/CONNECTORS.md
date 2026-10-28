# Connecting a translator

Open **Settings → Translation**, choose a service, follow its three-step card, check the connection, then save. The assistant in **Settings → AI assistant** is optional and configured separately. Checking a connection sends a sample phrase to that selected service; startup and help do not contact it.

| Connector | Setup guide |
| --- | --- |
| Built-in English–Russian dictionary | No account or server. Basic word hints; no sentence grammar. |
| LibreTranslate | [Install and start the server](https://docs.libretranslate.com/guides/installation/), then enter its URL. |
| Ollama | [Start Ollama and download a model](https://docs.ollama.com/quickstart), then enter its name and server URL. |
| OpenAI-compatible API | [LM Studio's server connection](https://lmstudio.ai/docs/developer/openai-compat), or your cloud provider's base URL, model ID, and key. |
| Yandex Translate | [Translate API setup](https://aistudio.yandex.ru/en/docs/translate/quickstart): API access, key, and folder ID. |
| YandexGPT | [AI Studio setup](https://aistudio.yandex.ru/ru/docs/ai-studio/quickstart/): API credentials, folder, and model access. |
| GigaChat | [API authorization](https://developers.sber.ru/docs/ru/gigachat/api/reference/rest/gigachat-api): authorization key, account scope, and model. |
| Azure Translator | [Create a Translator resource](https://learn.microsoft.com/en-us/azure/ai-services/translator/text-translation/quickstart/rest-api), then copy its key and region. |
| DeepL Free / Pro | [DeepL API setup](https://developers.deepl.com/docs/getting-started/quickstart): match the selected API plan to the key. |

Cloud API access and quotas are separate from consumer chat/translation subscriptions. Local services need to be running before their connection can succeed. The built-in dictionary remains the English–Russian fallback if a configured service fails.

Yandex folder IDs supplied in settings are now forwarded with either API-key or IAM authentication. IAM still requires a folder ID. Existing API-key configurations without a folder are preserved.

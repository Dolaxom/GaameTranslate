# GaameTranslate

A Windows app for translating games and learning words as you play. Translate text on screen, look up a word under your cursor, and save it with context for later review.

Built with C#, .NET 10, and WPF. The interface supports **English and Russian**.

## What it does

- **Screen translation** — select an area or translate the screen with an overlay. Automatically translate changes in a selected area.
- **Word lookup** — get a word translation first, then load the surrounding sentence and optional AI explanations in the background.
- **Personal vocabulary** — save words with context, notes, tags, and pronunciation. Review them with spaced repetition.
- **Local dictionaries** — import Yomitan, DSL, StarDict, Kaikki, and CC-CEDICT dictionaries.
- **Import and export** — use CSV, export to Anki, or sync through AnkiConnect.
- **Game profiles** — keep different settings for different games.

![Vocabulary view](assets/screenshots/vocabulary.png)

## Get started

Requires **Windows 11 x64**. Windowed or borderless games work best.

1. Extract the app ZIP and run `GaameTranslate.exe`. Keep the extracted files together; the .NET runtime is included.
2. Open **Settings → Translation** and choose a translation provider.
3. Enter its connection details, select your languages, and click **Test connection**, then **Save**.
4. Open a game and press **Alt+Q** to translate the screen or **Alt+W** to look up a word under the cursor.

To change the interface language, open **Settings → Application → Interface language**, choose **English** or **Русский**, and save. No restart is needed.

The default provider is LibreTranslate at `http://127.0.0.1:5000`. It must be running separately, or you can select another provider.

## Default shortcuts

| Shortcut | Action |
| --- | --- |
| **Alt+Q** | Translate the screen; press again to close the overlay |
| **Alt+W** | Look up the word under the cursor |
| **Ctrl+Shift+T** | Select an area to translate |
| **Ctrl+Shift+R** | Translate the last selected area again |
| **Ctrl+Shift+O** | Show or hide the translation overlay |
| **Ctrl+Shift+I** | Toggle clicking through the overlay |
| **Escape** | Close the active translation window |

Customize shortcuts in Settings. To fully quit the app, choose **Exit** from its system tray menu.

## Translation providers

Choose a provider that works in your region:

| Provider | What you need |
| --- | --- |
| LibreTranslate | Server URL and an optional API key |
| Ollama | Local server and a downloaded model |
| OpenAI-compatible API | Server URL, model name, and API key when required; supports LM Studio |
| Yandex Translate | Cloud API key or IAM token; folder ID when required |
| YandexGPT | AI Studio credentials, folder ID, and model access |
| GigaChat | Authorization key, OAuth scope, and model |
| Azure Translator | API key and resource region |
| DeepL Free / Pro | API key for the selected plan |

Cloud providers may require a separate API account and quota. A consumer subscription does not necessarily include API access.

An optional assistant can explain words in context. Configure it separately in **Settings → Dictionary**.

### Run local models with Ollama

From the project folder or extracted app folder, run in PowerShell:

```powershell
./scripts/Start-LocalModels.ps1 -Assistant
```

This downloads portable Ollama, TranslateGemma 4B, and Qwen3 4B. Allow several gigabytes of disk space and download traffic. Docker is not required.

In the app, select **Ollama** with URL `http://127.0.0.1:11434`. Use `translategemma:4b` for translation, or `qwen3:4b-instruct-2507-q4_K_M` for translation and dictionary explanations. Run the script again after restarting Windows to start the server.

## OCR and privacy

Screen text recognition runs locally. Built-in PP-OCRv6 models support English, Japanese, and Chinese. Other explicitly selected languages use Windows OCR and may need the matching Windows language pack.

Settings, vocabulary, and history are stored in `%LOCALAPPDATA%\GaameTranslate`. API keys are protected with Windows DPAPI. Vocabulary and media files are not encrypted. There is no telemetry.

When you request a cloud translation or explanation, the selected provider receives the recognized text and relevant context. Screenshots are not sent to translation APIs. Saving screenshots and audio with vocabulary cards is optional and off by default.

Protected content and exclusive fullscreen games may prevent capture or overlays. The app does not inject code into games or read their memory.

## Build from source

Install the **.NET 10 SDK** on Windows, then run:

```powershell
./scripts/Build.ps1 -Configuration Release -NativeVerification
./scripts/Publish.ps1
```

The runnable app is written to `artifacts/publish/win-x64`; the ZIP is created in `artifacts`.

See [testing notes](docs/TESTING.md), [release notes](docs/RELEASE-0.3.0.md), and the [localization guide](docs/LOCALIZATION.md) for more detail.

## License

See [LICENSE](LICENSE). Downloaded models and imported dictionaries have their own licenses.

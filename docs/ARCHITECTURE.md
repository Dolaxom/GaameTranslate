# Architecture

## Boundaries and composition

The solution has four production assemblies and two verification projects. Capture/OCR dependencies are independent of application state; WPF presentation uses records supplied by those providers.

| Project | Responsibilities |
|---|---|
| `GaameTranslate.Core` | Provider contracts, immutable settings/models, physical geometry, Unicode normalization, sentence splitting, language resolution, label placement, spaced repetition |
| `GaameTranslate.Windows` | GDI screen capture, monitor topology, native selection windows, PP-OCRv6 / WinRT OCR, speech, XInput, image preprocessing, Win32 hotkey registration, account-bound DPAPI storage |
| `GaameTranslate.Infrastructure` | Official HTTP translation adapters, bounded in-memory cache, SQLite persistence, local dictionaries, CSV/APKG/AnkiConnect, game profiles, atomic JSON settings |
| `GaameTranslate.App` | .NET host/DI, CommunityToolkit MVVM state and commands, WPF resources/views, overlay presentation, dictionary popup, tray and single-instance lifecycle |
| `GaameTranslate.Tests` | xUnit domain, provider contract and persistence tests, including temporary SQLite databases |
| `GaameTranslate.Windows.Verify` | STA desktop integration runner: real capture/OCR/windows/hotkeys/DPAPI; HTTP test double exists only in this test assembly |

The app uses Microsoft.Extensions.Hosting and Logging, with a small bounded local log provider. Services are constructed explicitly through dependency injection. There is no web service, game plugin, injection module or hosted infrastructure.

## Screen pipeline

1. `RegisterHotKey` delivers a registered command through an `HwndSource` message window. The same view-model pipeline serves tray and main-window actions.
2. A new request cancels an obsolete request, waits for the shared pipeline gate, and snapshots translation/OCR configuration. Cancellation unwinds selection and releases the gate.
3. The main window and prior overlays temporarily hide. Region selection creates one transparent WPF window for each current display. Win32 pointer coordinates are shared between these windows. Escape/right click/token cancellation cancels; display topology changes also cancel selection.
4. `DesktopCapture` checks the requested rectangle and obtains BGRA pixels with GDI `CopyFromScreen`. Its bitmap, drawing context and locked native image data have explicit lifetimes. A 40-megapixel input cap prevents unbounded captures.
5. `WindowsOcrProvider` chooses an installed recognizer and preprocesses on a worker thread. Bilinear resizing respects `OcrEngine.MaxImageDimension` and an additional 20-megapixel working cap. Contrast, grayscale, box smoothing and fixed thresholding are opt-in.
6. WinRT returns actual OCR lines and words. Each line is a block in engine reading order; no confidence score is invented. Separate X/Y ratios undo rounded resizing. All stored bounds become absolute physical desktop pixels.
7. Same-row fragments are merged before paragraph grouping. Translation submits complete paragraphs as batched text, preserving result order and context. It never uploads image bytes. DeepL receives the context hint and up to 5,000 characters of total context; other providers receive only the text arrays and their supported options.
8. Cancellation is checked before publishing. A `TranslationSession` associates each OCR block with its translation, capture rectangle, language pair, provider and stage timings.
9. Overlay windows render the session. History writes afterward; a history-write failure does not discard a successful visible translation.

Text translation uses the same HTTP/cache layer and supports a whole text block or explicit sentence splitting. No OCR is needed for pasted text.

## Coordinate model

`PixelRect` represents physical pixels, including negative desktop origins. The manifest requests PerMonitorV2. Screen rectangles and pointer positions never use WPF device-independent units. Each selection/overlay window converts its monitor-local physical coordinates using that window's current composition transform. OCR maps scaled image coordinates back to the captured rectangle before UI processing.

Spatial placement tries nearby non-overlapping candidates within the monitor. If no readable arrangement fits, the whole session falls back to one floating panel. Replacement intentionally follows original bounds and allows translated wrapping; it does not reconstruct an image. Mixed-DPI physical hardware validation is still outstanding.

## Translation HTTP behavior

The provider selects official DeepL/Azure/Yandex/GigaChat hosts or a user-configured LibreTranslate/Ollama/OpenAI-compatible base address. Remote LibreTranslate requires HTTPS; loopback can use HTTP. Redirect following is disabled on the app's HttpClient to prevent forwarding credentials to a redirect destination.

Batches contain at most 40 strings and approximately 16,000 input characters. A single block above 15,000 characters fails with an actionable message. The linked deadline is configurable from 10 to 600 seconds (120 by default). Yandex batches stay below 9,000 input characters. LLM batches contain up to 12 paragraphs with explicit response IDs; TranslateGemma uses its required single-message translation prompt. Rate-limit/server errors get at most two retries; `Retry-After` above eight seconds produces a wait-and-retry-later message. Authentication, quota, invalid response, unsupported language, timeout and network errors have distinct user-facing feedback. Responses are bounded to 4 MB and must preserve item count.

The translation cache holds at most approximately 2,000 items, resets when full, and uses SHA-256 keys over provider/configuration identity, source, target, context and text. It stores only successful results, remains in process memory, and is cleared on saving provider settings. Multiple concurrent identical requests are not coalesced; no persistent translation or OCR cache exists.

## Persistence and credentials

`SettingsStore` validates and serializes a complete settings record, writes a sibling temporary file and replaces the old JSON after completion. A semaphore serializes access. Invalid settings are preserved and reported; they are never silently overwritten with defaults.

SQLite uses parameterized commands, short pooled connections, WAL and a five-second busy timeout. Schema version 1 stores vocabulary and history with JSON payloads plus searchable/indexed metadata. A unique normalized word/language-pair key prevents duplicates. Insertion and encounter updates are transactional. New nonempty dictionary fields and translations refresh on duplicate encounters; notes, tags and review scheduling survive. History has bounded retention; search uses .NET ordinal case-insensitive comparison so Cyrillic is treated correctly.

CSV import validates quoting, columns and required text before row insertion. Each row is committed incrementally and duplicates count as encounters. Exports quote all fields and escape potential spreadsheet formulas. Anki export supports schema-11 APKG with optional media and reverse cards, as well as UTF-8 TSV. AnkiConnect updates a separate note model using stable word/language IDs and marks missing words instead of deleting notes.

API keys live in separate DPAPI-encrypted files bound to the current Windows user. Names are hashed, keys are not included in settings, and plaintext byte buffers are zeroed after encryption. SQLite and CSV remain unencrypted personal text; the app explains this in Settings and documentation.

## Windows lifecycle

A named per-login-session mutex prevents duplicate processes; a named event asks the existing instance to open its main window. The tray owns open/capture/vocabulary/pause/overlay/exit actions. Closing normally hides the main window. Exit cancels the pipeline, unregisters hotkeys, closes secondary windows, removes the tray icon and stops/disposes the host. Hotkey registration uses `MOD_NOREPEAT` and no keyboard hook.

Floating/side windows show without activation and can be pinned, moved and resized. All result windows opt out of capture with `WDA_EXCLUDEFROMCAPTURE` where Windows supports it, and are also hidden explicitly during requested captures. Interactive lookup deliberately takes focus. Native `WS_EX_TRANSPARENT`/`WS_EX_NOACTIVATE` implement whole-overlay click-through, with a global recovery shortcut.

## Tradeoffs

- GDI is a small, working backend for desktop/borderless content. It cannot guarantee protected/exclusive-fullscreen capture. Windows Graphics Capture can be added behind `IScreenCapture`.
- PP-OCRv6 adds bundled en/ja/zh models and CTC word alignment. Windows OCR supplies other explicit languages. Vertical Japanese, furigana and game-font segmentation still need broader evaluation. The user can edit any substring for lookup.
- WPF resources and vector paths provide an original UI without a large theme framework. Code-behind handles dialogs/view filtering; orchestration and persistent state live in the view model and services.
- The first release is a portable unsigned folder/ZIP. There is no installer, update service or bundled offline translator.

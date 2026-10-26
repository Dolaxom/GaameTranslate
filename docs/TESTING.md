# Testing and verification

## Automated domain and infrastructure tests

```powershell
dotnet test tests/GaameTranslate.Tests -c Release
```

Tests cover Unicode normalization and sentence boundaries, installed-language selection, scaling/negative-coordinate math, spatial placement/overflow fallback, HTTP endpoint validation, DeepL/Azure/LibreTranslate response mapping, authentication and malformed-response errors, transient retry bounds, batched request/context serialization, cache isolation, SQLite reopen/CRUD/concurrent duplicate behavior, review scheduling, JSON settings roundtrip/corruption preservation, history retention/deletion/Unicode search, and CSV quoting/formula protection.

External HTTP services use explicit test doubles; no API keys or network calls are needed. Tests use their own temporary database directories and dispose SQLite pools before cleanup.

## Windows integration verification

The settings redesign has a separate native UI suite:

```powershell
dotnet run --project tests/GaameTranslate.Windows.Verify -c Release -- artifacts/settings-ux --settings
```

It checks draft preservation across application navigation, service-specific fields, independent credential drafts, preview connection requests without persistence, cancellation, save/discard, validation without partial writes, shortcut entry, light appearance, and all six tabs at 960×650. HTTP replies are explicitly marked test doubles. Rendered PNGs and `results.json` are saved to the supplied output directory.

On an unlocked interactive Windows desktop with the English OCR pack:

```powershell
dotnet run --project tests/GaameTranslate.Windows.Verify -c Release -- artifacts/verification
```

This is an STA executable, not an xUnit suite. It renders all application pages, creates a visible white text fixture, captures that fixture with real GDI, recognizes it with real Windows OCR, passes text through the real LibreTranslate HTTP adapter using a test-only HttpMessageHandler, and displays/persists the mapped result. Every canned translated string is prefixed **[API TEST DOUBLE]**. This verifies plumbing and native behavior; it does not verify live translation quality.

It also exercises native region-selector cancellation, all four overlay modes, native click-through style changes, hotkey registration/conflicts/unregistration, vocabulary reopening, persistent review due dates and real current-user DPAPI roundtrip. It writes `results.json`, PNG renders and isolated test data under the supplied repository output directory. On failure it returns a nonzero exit code and writes `failure.txt`.

The production app also supports:

```powershell
GaameTranslate.exe --data-dir "C:\path\inside\repo\artifacts\smoke" --smoke-test
```

This smaller diagnostic reports installed OCR languages, a real rendered-image OCR result, a real 320×120 desktop capture, coordinate bounds and DPAPI roundtrip in `native-smoke.json`. It never calls a translation API. Exit any existing application first because the single-instance mechanism activates the existing instance instead of running diagnostics in a second process.

## Local verification record

Implementation was exercised on Windows with .NET SDK 10.0.103, an x64 desktop and one 2560×1440 monitor. Installed OCR languages were `en-US` and `ru`.

- Debug and Release builds are checked with build output retained in `artifacts` where applicable.
- The real English OCR result for the native fixture was **Hello traveler. Your journey begins here.**
- Final Release run: **67/67 xUnit tests passed**, and **31/31 Windows integration checks passed**, with zero build warnings or errors. Machine-readable artifacts are `artifacts/test-results/unit-tests.trx` and `artifacts/verification-release-v02/results.json`. These describe the implementation run, not a guarantee about future builds.
- The main page and overlay renders were visually inspected. Source/target selector display, inherited window themes, compact floating size, dark scrollbars and primary-action contrast were corrected during this pass.
- Local Ollama / TranslateGemma and Qwen are exercised separately in artifacts/quality*. Cloud services have contract tests; no live Yandex/GigaChat/DeepL/Azure credentials were supplied. No actual game or mixed-DPI hardware compatibility claim is made.
- The self-contained Windows x64 distribution was also launched successfully with bundled **Microsoft.NETCore.App 10.0.13** and **Microsoft.WindowsDesktop.App 10.0.13**. Its separate native smoke run passed real English OCR, desktop capture and DPAPI checks.

## Manual acceptance matrix

Use an isolated data directory. Prepare an actual configured provider before evaluating translation success. Keep private screen content outside the selected rectangle.

| Scenario | Expected result |
|---|---|
| Fresh launch without a key | Setup card and actionable provider message; no fabricated translation |
| Configure DeepL Free/Pro | Correct official API host, real translated dialogue, source detection, context hint |
| Configure Azure including region | Real translation; missing/incorrect key or region gives safe actionable feedback |
| Local LibreTranslate | Works only while service/models are available; no internet needed for loopback translation |
| Ctrl+Shift+T drag / reverse drag | Border/dimmed surround track pointer; release produces text result |
| Escape / right click while selecting | All selection windows close; underlying app resumes normal input |
| Rapid capture/cancel/new capture | Obsolete work cancels; latest request owns the displayed result |
| Fullscreen with monitor under cursor / selected monitor | Only intended physical monitor is captured |
| Repeat before/after region selection | Clear instruction before selection; same region reused afterward and after restart |
| Dual monitors left/above primary | Negative origins remain correct in OCR/labels |
| 100% + 150% + 200% mixed scaling | Selection/labels align on each monitor, including crossing a seam |
| Disconnect or rotate display during selection | Selection cancels; repeat either stays valid or requests a new area |
| Borderless game, minimized game, exclusive fullscreen | Borderless works when capturable; unavailable content produces a limitation/error, never a compatibility promise |
| Blank area / wrong or missing OCR pack | No-text or language installation guidance |
| Japanese horizontal/vertical/furigana and Chinese/Korean | Test actual installed pack quality; manually select substrings where segmentation is poor |
| Network offline / API 429 / 456 / timeout | Safe message; bounded retries; cancellation remains responsive |
| Floating window | Drag, resize, pin/unpin, copy, close and Escape function |
| Spatial/replacement on screen edges | Labels stay on monitor; spatial collisions resolve or fall back to a panel |
| Ctrl+Shift+I click-through | Mouse reaches game; same shortcut restores lookup controls |
| Word click / arbitrary substring | Real word translation; save preserves correct language pair and context |
| Repeated saved word | One entry, incremented encounter count, existing notes preserved |
| Vocabulary search/filter/sort/edit/delete | Matching view updates, edits survive restart, duplicate rename rejected |
| CSV punctuation/multiline/import/export | Unicode and quotes roundtrip; duplicate imports count as encounters |
| Anki export | APKG with media imports and reimports without duplicate notes; optional TSV remains available |
| Review Again/Hard/Good/Easy | Answer reveals, grading changes due time, future cards leave due queue |
| History disabled / clear / reopen | No new retained entries when disabled; existing data can be cleared; reopen shows source/translation/word lookup |
| Tray / shortcut conflict / second launch | Hotkeys remain active while hidden; conflicts reported; second launch activates existing window |
| Light appearance / narrow window / 200% text | Readable, keyboard-focus visible, scrollable settings and pages |
| Exit during selection/OCR/network call | Pipeline cancels, no stranded overlay/tray icon or registered shortcut |

For soak testing, repeat 100 captures and track process working set, private bytes, USER/GDI handles and idle CPU using Task Manager/Process Explorer. The app performs no capture at idle unless explicit region monitoring is enabled. Opt-in XInput polling checks button state without capture. Stage timings are displayed on Translate and in bounded diagnostics without recognized text or credentials.

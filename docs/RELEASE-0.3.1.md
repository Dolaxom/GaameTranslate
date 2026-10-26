# 0.3.1 — first translation without provider setup

New installations start with the built-in English–Russian dictionary. Alt+Q, Alt+W, region translation, text translation, and saving vocabulary work without a translation server, API key, or model download. Screen recognition still uses the selected local OCR engine.

The bundled FreeDict/WikDict adaptation contains 57,063 English lookup keys and adds approximately 1 MB of compressed data. It is embedded in the application, so it also works when the application is offline. See [source, attribution, license, and conversion details](dictionaries/README.md).

This is deliberately a basic dictionary mode: longest available dictionary phrases, then individual words, with simple English spelling/inflection hints. It does not generate sentence grammar or choose meanings from context. Unknown words in sentences remain unchanged. Unknown single-word lookups explain that the word is missing rather than offering to save a fake translation. Other language pairs require a configured provider.

An always-visible, localized notice explains the quality limit and links to translation settings. Word popups and all four overlay modes also show the notice. Cached results and saved translation history retain dictionary provenance. Existing provider choices are preserved. Selecting a provider without its required key uses the dictionary immediately; configuring a provider returns to that provider. Connection tests still test the selected service and never disguise failure as a successful connection.

Tests cover offline translation, language restrictions, cancellation, dictionary data integrity, preserved Russian letters, no HTTP or credential access in built-in mode, and cache/history provenance. Native verification covers the first-run UI, word-first flow, screen pipeline with controlled capture/OCR, saved cards, RU/EN warnings, all overlay modes, and switching to a configured HTTP adapter. This verification does not measure live service quality or real-game OCR accuracy.

Run the focused Windows verification with:

```powershell
dotnet run --project tests/GaameTranslate.Windows.Verify -c Release -- artifacts/built-in-dictionary --built-in
```

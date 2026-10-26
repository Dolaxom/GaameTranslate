# Bundled English–Russian dictionary

`src/GaameTranslate.Infrastructure/Data/eng-rus.json.gz` is a compact adaptation of the **English–Русский FreeDict+WikDict dictionary**, edition **2025.11.23**, maintained and published by **Karl Bartel**.

- Source: https://download.freedict.org/generated/eng-rus/eng-rus.tei
- Upstream project: https://www.wikdict.com/ and https://freedict.org/
- Base data: Wiktionary contributors, https://www.wiktionary.org/, via DBnary, http://kaiko.getalp.org/about-dbnary/
- License: **Creative Commons Attribution-ShareAlike 3.0 Unported**, https://creativecommons.org/licenses/by-sa/3.0/legalcode
- Full upstream license: [CC-BY-SA-3.0.txt](CC-BY-SA-3.0.txt)
- Source SHA-256: `7c48ef3981903361ea90f8ef9a64e58cc7899341d0fb873c2f61198198cba7af`

The adapted dictionary data remains under CC BY-SA 3.0. The application's MIT license does not replace the dictionary license.

Changes: extracted English headwords and Russian translations from TEI, removed stress accents (preserving ё and й), normalized case/apostrophes, merged duplicate translations, omitted entries without Russian translations, and compressed the resulting JSON. The upstream file lists 62,181 headwords; the bundled adaptation contains 57,063 distinct keys with Russian translations. Definitions and pronunciations are not included in this adaptation.

To reproduce the bundled data, run `scripts/Build-BundledDictionary.ps1` with PowerShell 7. The script downloads the upstream source and verifies its pinned hash before conversion. To use an existing source file, pass `-SourcePath <path>`.

This is a bilingual word dictionary, not a machine translation model. Sentence output uses lexical substitutions, preserving punctuation and unknown words. It does not infer sentence meaning or generate correct Russian inflections. Longer dictionary phrases are preferred over isolated words where available.

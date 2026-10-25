# Third-party components

GaameTranslate includes unmodified Sdcb.SimdPaddleOCR 2.0.0,
Sdcb.SimdPaddleOCR.ModelProvider 1.0.0 and the ChineseV6Small / ChineseV6Medium
1.0.0 model packages. Copyright their respective contributors; Apache License
2.0. See `SimdPaddleOCR-Apache-2.0.txt` and
https://github.com/sdcb/SimdPaddleOCR . The OCR model weights are distributed
through those model packages. No translation model is included in the ZIP.

Other dependencies include .NET / WPF / System.Speech, CommunityToolkit.Mvvm,
Microsoft.Extensions, Microsoft.Data.Sqlite and SQLitePCLRaw. Their original
licenses and notices apply. The self-contained .NET distribution includes its
own license and third-party notices. Dependency versions are recorded in the
project files and application `.deps.json`.

The optional local-model setup script downloads Ollama and separately selected
models into `artifacts/`; these are separate from the application distribution.
See https://github.com/ollama/ollama/blob/main/LICENSE ,
https://ollama.com/library/translategemma and https://ollama.com/library/qwen3 .
Imported dictionaries retain their publishers' licenses; importing a dictionary
does not grant redistribution rights.

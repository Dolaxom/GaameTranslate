# Delivery status and roadmap

## Implemented and verified locally

- Modular .NET 10/WPF solution, DI/host, shared dark/light resources, six functional screens.
- Requested GDI capture, per-monitor region selector/cancellation, native Windows OCR and physical-coordinate mapping.
- Region/fullscreen/all-monitor/repeat operations; persistent last region.
- DeepL Free/Pro, Azure and LibreTranslate HTTP adapters, batching, process cache, bounded retry, cancellation and safe errors. HTTP contract tests use doubles; live-account verification remains below.
- Floating/spatial/replacement/side-panel windows, compact word lookup, click-through and global interaction toggle.
- SQLite vocabulary/context/notes/tags, unique word/language pairs, encounter counts, editing/deleting/search/filter/sort, CSV import/export and Anki TSV.
- Local bounded text history, search/reopen/copy/delete/clear, and persisted simple spaced-repetition scheduling.
- Shortcut recorder and registration conflict reporting; tray actions; second-instance activation; current-user DPAPI; safe timing diagnostics.
- xUnit tests and an interactive Windows integration verifier with actual native capture/OCR/overlays/hotkeys/DPAPI.
- Build/test/publish scripts and self-contained x64 distribution support.

## Required before a public production release

1. Supply real DeepL and Azure credentials and a separately running LibreTranslate service. Exercise live pairs, language detection, invalid credentials, quotas, billing limits and cancellation. Check Japanese translation quality on actual dialogue.
2. Install Japanese/Chinese/Korean and other priority OCR packs; test a representative game corpus, stylized text, vertical text, furigana and mixed-script lines. English/Russian were the only installed OCR packs on the implementation machine.
3. Complete the mixed-DPI/multi-monitor/disconnect hardware matrix and game compatibility tests. The implementation environment had one 2560×1440 monitor.
4. Run a longer foreground/tray session and monitor idle memory/CPU, handle counts, repeated captures and sleep/resume behavior. No benchmark or game compatibility certification is claimed.
5. Accessibility review with Narrator/high contrast, high text scaling and keyboard-only overlay operation. Refine spatial label focus order.
6. Sign binaries, add installer/uninstaller and clean-machine deployment verification. The portable development build is unsigned.

## Next functional improvements, in priority order

1. Add Windows Graphics Capture as an alternate backend for capture compatibility and efficiency.
2. Optional, visibly indicated subtitle-region monitoring: frame-change hashing, debouncing, duplicate suppression, pause controls and cancellation. **No periodic/background capture is currently implemented.**
3. Add an offline OCR provider with Japanese vertical layout and better segmentation, plus a corpus-based quality evaluator and in-app image/bounding-box diagnostics.
4. Group adjacent OCR lines into paragraphs/dialogue boxes with tested reading-order heuristics. Currently a recognized OCR line is the translation unit, with DeepL surrounding-text context.
5. Add first-class automatic startup registration, remembered window/overlay positions, review statistics and accessible toast/error surfaces.
6. Model-management guidance for local translation; optional dynamic provider language lists, alternate meanings and richer dictionary metadata where the official service supports them.
7. More review/learning options, transactional bulk imports, database migration/backup UX, an optional encryption story for vocabulary/history, and `.apkg` export if useful.

No integration is represented by a nonfunctional button. Unimplemented integrations and background modes are excluded from the UI rather than simulated.

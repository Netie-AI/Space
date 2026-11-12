# Active map

What exists in this repo and where. Update when structure changes, not when state changes
- state lives in STATUS.md.

| Path | Role |
|---|---|
| `src/NetieSpace/` | WPF host (Space Quick Look + Edit-in-app document chrome, video NLE) |
| `src/NetieSpace/Apps/Shell/` | Netie PDF / Image / Video app processes: pre-mutex `AppEntry`, `AppIdentity` (AUMID table), `AppInstance` (per-app mutex + activation), `AppWindow` hosting `AppShell` (`HomeView`, `ToolPageView`, `AppTheme.xaml`), pure models, `AppSnapshot` (S50-E1) |
| `scripts/check-app-shell.ps1` | Exe-level check on Windows: app snapshots, `--list-tools`, one real tool run (CI job `build (windows)`) |
| `src/NetieSpace/Apps/Tools/` | Tool contract: 91-entry catalog, `EngineProbes`, licence gate, `ToolRegistry` (reflection), `ToolRunner`, headless `ToolCli` (S50-E2) |
| `src/NetieSpace/Apps/Pdf/`, `Apps/Image/`, `Apps/Video/` | Tool providers found by reflection: 16 PDF, 10 image, 27 video runners. Code needing WPF / WIC / WinRT goes under a `Windows/` subfolder (`Apps/Pdf/Windows/`: PDF to JPG) |
| `src/NetieSpace/Views/PreviewWindow.xaml` | Peek glass vs light Edit chips vs `_pdfSpaceMode` docked document chrome (header/nav/ribbon/rail) |
| `src/NetieSpace/Assets/pdfjs/netie-viewer.html` | pdf.js page canvas + Fill overlays (HTML toolbar hidden in workbench) |
| `src/NetieSpace/Assets/markdown/md-editor.html` | Local markdown workbench (marked+KaTeX, outline, annotate). Edit click until auto-open (MD-1) |
| `src/NetieSpace/Assets/code-viewer/json-viewer.html` | JSON / jsonl peek tree (row 51). Default peek from ContentParserService; Edit stays pre code |
| `src/NetieSpace/Services/FillProfileStore.cs` | Local DPAPI Autofill profile (never uploaded) |
| `src/NetieSpace/Services/FillProfileHarvest.cs` | Save-time harvest held as a proposal until the user confirms |
| `src/NetieSpace/Views/FillProfileWindow.xaml` | My details (Settings + Fill rail) |
| `src/NetieSpace/Services/PdfEditService.cs` | PdfSharpCore bake (flatten text/stamps/highlights) |
| `native/netie-embed/` | Optional Rust sparse embeddings |
| `native/netie-pdf-detect/` | Rust snap-cell sidecar (`cargo build --release`) |
| `native/netie-pdf-form/` | Go AcroForm list/fill sidecar (`go build`) |
| `tools/convert_engine.py` | Share dest + watermark/protect/OCR |
| `license-api/` | Cloudflare Worker license + Stripe |
| `scripts/register-open-with.ps1` | Additive HKCU Open with + Start Menu (no UserChoice steal) |
| `scripts/register-startup.ps1` | HKCU Run auto-start |

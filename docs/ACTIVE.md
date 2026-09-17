# Active map

What exists in this repo and where. Update when structure changes, not when state changes
- state lives in STATUS.md.

| Path | Role |
|---|---|
| `src/NetieSpace/` | WPF host (Space Quick Look + Edit-in-app document chrome, video NLE) |
| `src/NetieSpace/Views/PreviewWindow.xaml` | Peek glass vs light Edit chips vs `_pdfSpaceMode` docked document chrome (header/nav/ribbon/rail) |
| `src/NetieSpace/Assets/pdfjs/netie-viewer.html` | pdf.js page canvas + Fill overlays (HTML toolbar hidden in workbench) |
| `src/NetieSpace/Assets/markdown/md-editor.html` | Local markdown workbench (marked+KaTeX, outline, annotate). Edit click until auto-open (MD-1) |
| `src/NetieSpace/Assets/markdown/md-editor.html` | Markdown workbench canvas (own product PRD; dual-pane hide-chrome) |
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

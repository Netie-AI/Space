# Active map

What exists in this repo and where. Update when structure changes, not when state changes
- state lives in STATUS.md.

| Path | Role |
|---|---|
| `src/NetieSpace/` | WPF host (Space preview, PDFSpace Edit-in-app rail, video NLE) |
| `src/NetieSpace/Assets/pdfjs/netie-viewer.html` | pdf.js Fill & Sign overlays, AcroForm widgets, snap, stamps |
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

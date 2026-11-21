```yaml
keywords:
  - pdfspace
  - fill-and-sign
  - edit-pdf
  - pdfcpu
  - pdfer_forms
  - pdfium-render
  - lopdf
  - pdf.js
  - acroform
  - snap-to-box
  - live-annotation
  - netie-embed
main_idea: Keep WPF + pdf.js live-annotation overlays. Add one Rust sidecar (snap-cell on PNG) and one Go sidecar (pdfcpu form-list + form-fill). Do not rewrite the app. Do not use iText or PyMuPDF AGPL for Fill & Sign. Flatten via PdfSharpCore stays the default for flat forms.
kind: lens
date: 2026-09-04
reuse: docs/research/findings/OPENEDIT_ACROBAT_AUDIT.md, docs/research/findings/FILLSIGN_TEST.md, docs/research/findings/PDF_OPS_NEXT.md, native/netie-embed
cite: https://www.adobe.com/acrobat/resources/how-to-fill-out-a-pdf-form.html
```

# Acrobat-class Fill & Sign + Edit PDF for Space (PDFSpace)

PREFLIGHT: PARTIAL. No `docs/subagents_findings` existed. Reused `docs/research/findings/OPENEDIT_ACROBAT_AUDIT.md` (2026-07-22; some rows are stale vs code), `FILLSIGN_TEST.md`, `PDF_OPS_NEXT.md`, `PARKING_LOT.md` P7 (whole-app Rust/Go rewrite parked), `native/netie-embed`.

This is a build plan, not an implementation. Route any product slice through the PRD Agent before tickets.

## Hard rails

- Keep WPF host + WebView2 + pdf.js (`4.10.38` in `src/NetieSpace/Assets/pdfjs/build/pdf.mjs`).
- Live-annotation: HTML overlays are visual truth; bake on save from a clean base. Do not reload the viewer while typing.
- Y-origin: viewer and C# records use PDF points origin BOTTOM-LEFT. PdfSharpCore `XGraphics` is TOP-LEFT. Convert once: `yTop = page.Height - h - yFromBottom` (`PdfEditService.DrawFillTexts`).
- MIT / Apache / BSD only. BAN iText (AGPL) and leaked sources.
- BAN a whole-app Rust/Go rewrite (`PARKING_LOT.md` P7; PRD ledger row 1).
- Native = sidecar stdin/stdout JSON like `native/netie-embed`. One small Go binary AND one small Rust binary max. Named jobs, not frameworks.
- Do not scrape Adobe. Do not upload the PDF for autofill.

---

## 1. Acrobat UI map vs what D:\Space already has

Acrobat Fill & Sign (desktop / Acrobat Sign self-fill) is: click anywhere on a flat page, type a text box, place check/X/dot, place a typed/drawn/image signature, resize, A/A font, Autofill profile (name, email, phone, address). Interactive AcroForms are a different path: click the widget, tab between fields, values live in `/V`. XFA is legacy Adobe-only; PDF 2.0 deprecated it.

Sources:

- https://www.adobe.com/acrobat/resources/how-to-fill-out-a-pdf-form.html
- https://helpx.adobe.com/sign/how-to/use-fill-and-sign.html
- https://www.adobe.com/acrobat/hub/how-to-autofill-pdf-forms.html
- https://www.nutrient.io/blog/pdfjs-native-annotation-layer-forms/
- https://www.nutrient.io/blog/pdfjs-annotation-editor-layer/

| Acrobat control | Space today | File |
|---|---|---|
| Fill & Sign mode | Ribbon **Fill** -> `enterFillMode`; click cell/AcroForm/line/blank | `PreviewWindow.xaml` PdfModeFillBtn; `netie-viewer.html` `enterFillMode` |
| Snap to table cell | Raster ruling scan `detectCellAt` (~170pt window `getImageData`) | `netie-viewer.html` ~1176 |
| Snap to AcroForm widget | PdfPig rects posted as `setFormFields`; `fillFieldRectAt` hit-test | `PdfEditService.GetFormFieldRects`; `PreviewWindow.PostFormFieldsToViewer` |
| Snap to underline | Bottom rule only -> 20pt-high box on the line (`kind: line`) | `detectCellAt` |
| Free text box | 150x20pt at click | `pagesEl` click handler ~1387 |
| Type in overlay | `contenteditable` `.fill-text`; Enter/Esc/blur commit | `createFillEl` |
| A / A / trash | `#fillFloatBar`; font *1.15 / *0.87 | `scaleActiveFill` |
| Autosave bake | Debounced 1600ms fill / 250ms stamp; overlays stay | `FlushPendingPdfStampsAsync` `keepOverlaysEditable: true` |
| Flatten typed text | `PdfEditService.DrawFillTexts` vector + GDI PNG fallback | `PdfEditService.cs` ~449 |
| Signature place/drag | Image or typed PNG; Shift snaps stamp to field | `createOverlayEl` mousedown; `snapPlacement` |
| Signature library | New signature / My signatures / Stamp image | `PreviewWindow.xaml` PdfFillPanel sibling Sign panel |
| Autofill profile | Missing | -- |
| Check / X / dot tools | Missing | -- |
| Comb (IAb) | Called out as v1 limit | audit Packet 1 known limits |
| Interactive `/V` write-back | Missing; visual flatten only | audit Fable B.2 |
| Edit PDF native text | Bbox overlay; click promotes fill with `cover: true` (white-out) | `setTextBoxes` ~2062; `CoverBackground` |
| Edit PDF current page | Host often seeds **page 1 only** | `PdfEditTextMode_Click` `PostTextBoxesToViewer(..., 1)` |
| OCR overlay | Local Tesseract then Windows.Media.Ocr; `OCR this page` seeds fills | `ImageOcrService`; `SeedOcrFillTextsAsync` |
| Comment highlight | Drag overlay; bake `DrawHighlights` | `PdfCommentHighlight_Click` |
| Organize / Merge / Sign stamp | Shipped | `PdfOrganizeWindow`; `PdfEditService` |
| Protect / Watermark / Scan OCR | Python `convert_engine.py` (pymupdf AGPL debt) | `ConversionService`; `PDF_OPS_NEXT.md` |
| OpenEdit Tools home grid | Not built | audit Packet 2 |
| pdf.js AnnotationLayer / ENABLE_FORMS | Not wired. `getDocument(fileUrl)` only | `netie-viewer.html` `load()` |
| pdf.js AnnotationEditorLayer + `saveDocument()` | Bundled in pdf.mjs but unused | pdfjs 4.10.38 exports |

Audit stale vs 2026-09-04 code: Comment highlights ARE persisted; Protect/Watermark/Scan&OCR exist under More; Edit PDF click is more than status text (promotes a cover fill). Fill & Sign v1 is still the right description.

**pdf.js layer decision:** keep custom HTML overlays. AnnotationLayer is for widgets already in the PDF; AnnotationEditorLayer + `saveDocument()` fights the clean-base re-bake model and does not snap to scanned table rulings. Optional later: `page.getAnnotations()` as a second field source (JS) without switching visual truth.

**AcroForm vs XFA vs flattened:**

- AcroForm widgets: snap + later Go `/V` fill.
- Flattened / print-to-PDF: raster snap + `DrawFillTexts`. This is the common SME form.
- XFA: detect and refuse honestly ("static / print this form first"). Do not ship `enableXfa: true` as a write path.

---

## 2. Recommended Rust job

**Binary:** `netie-pdf-detect.exe`  
**Home:** `native/netie-pdf-detect/` (same layout as `native/netie-embed`)  
**Jobs:** `snap-cell` (required). Optional later in the SAME binary: `form-rects` via `pdfer_forms` if PdfPig is slow on a file. Do not add a second Rust exe.

**Why Rust:** `detectCellAt` is the laggy hot path (sync `getImageData` on click; comment in viewer already cut the window from 320pt to 170pt). Pixel walk + line scoring belongs off the UI thread in a small native job. Not a PDF parser.

**Crates (MIT/Apache):**

- `image` 0.25 -- decode PNG, luma
- `serde` + `serde_json` -- stdin/stdout JSON (netie-embed skipped serde; this job is bigger, JSON crate is fine)

Do **not** take `pdfium-render` in this binary (needs `pdfium.dll`, 3-13MB, XFA/V8 variants). Evaluate only if a later job must raster a PDF page without WebView2. Host already has `exportPagePng`.

Do **not** take raw `lopdf` here. If AcroForm inspect ever moves to Rust, use `pdfer_forms` (MIT/Apache, built on lopdf): https://crates.io/crates/pdfer_forms -- but Go pdfcpu already covers list+fill.

**CLI (match `RustEmbedBridge`):**

```
netie-pdf-detect snap-cell
```

stdin JSON (one object, then EOF):

```json
{
  "job": "snap-cell",
  "pngPath": "C:\\Temp\\netie_ocr_xxx.png",
  "clickXPx": 412,
  "clickYPx": 880,
  "pageWidthPt": 612,
  "pageHeightPt": 792
}
```

stdout JSON (one line):

```json
{
  "ok": true,
  "kind": "cell",
  "xPt": 72.1,
  "yFromBottom": 640.0,
  "wPt": 180.4,
  "hPt": 18.2
}
```

`kind` is `cell` | `line` | `none`. On `none` or `ok: false`, JS falls back to the free 150x20 box. Exit 0 even on `none`; exit 2 on usage; exit 1 on IO.

Port the existing JS algorithm (dark threshold 130, 88% run, 11pt span, underline fallback). Do not invent ML.

C# bridge: clone `RustEmbedBridge` (`ManagedSparseEmbed.cs`) -- FindExe under `tools/netie-pdf-detect/` then `native/netie-pdf-detect/target/release/`. Timeout ~50ms target, 250ms cap. Missing exe = keep JS `detectCellAt`.

---

## 3. Recommended Go job

**Binary:** `netie-pdf-form.exe`  
**Home:** `native/netie-pdf-form/`  
**Module:** `github.com/pdfcpu/pdfcpu` v0.13.0 (2026-06-09, Apache-2.0)  
**Docs:** https://pdfcpu.io/form/form and https://pdfcpu.io/form/form_fill  
**API:** `api.FillFormFile` / `ExportForm` + `FillForm` (not `CreateFile`). CLI equivalent: `pdfcpu form list --json`, `pdfcpu form export`, `pdfcpu form fill`. Piping: https://pdfcpu.io/getting_started/piping/

**Jobs:** `form-list`, `form-fill`. Same binary. Do not shell out to a full pdfcpu CLI product name; link the library.

**Why Go:** PdfSharpCore cannot write real AcroForm `/V`. PdfPig is read-only. pdfcpu generates appearance streams; `needAppearances: true` if a viewer still blanks until click (https://pdfcpu.io/form/form_fill).

**CLI:**

```
netie-pdf-form form-list
netie-pdf-form form-fill
```

`form-list` stdin:

```json
{ "job": "form-list", "in": "D:\\forms\\w9.pdf" }
```

stdout:

```json
{
  "ok": true,
  "xfa": false,
  "fields": [
    {
      "name": "topmostSubform[0].Page1[0].f1_1[0]",
      "type": "text",
      "page": 1,
      "x": 36.0,
      "y": 720.0,
      "w": 200.0,
      "h": 16.0
    }
  ]
}
```

Rect origin: BOTTOM-LEFT PDF points (same as `PdfFormFieldRect`). Convert inside Go if pdfcpu's JSON uses a different origin; do not leak a third convention into JS.

`form-fill` stdin:

```json
{
  "job": "form-fill",
  "in": "C:\\Temp\\netie_sigbase_xxx.pdf",
  "out": "C:\\Temp\\netie_form_xxx.pdf",
  "needAppearances": true,
  "fields": [ { "name": "FirstName", "value": "Ada" } ]
}
```

stdout: `{ "ok": true, "out": "...", "filled": 1, "missing": [] }`

XFA or no AcroForm: `{ "ok": false, "error": "no_acroform" }` or `"xfa_unsupported"`. Host then keeps `DrawFillTexts`.

Do not flatten in this job. Flatten remains C# `DrawFillTexts` / `CoverBackground` for scans and print-PDFs.

---

## 4. What stays in JS / C#

**JS (`netie-viewer.html`):**

- pdf.js render, zoom, thumbs, continuous scroll
- contenteditable overlays, float bars, live-annotation
- click routing: AcroForm hit -> sidecar/JS snap-cell -> free box
- stamp/highlight overlays
- pointer capture drag (section 5)
- `exportPagePng` for OCR and snap-cell

**C#:**

- WPF ribbon (Fill / Sign / Comment / Organize / Save / More)
- WebView2 JSON bridge (`PreviewWindow_WebMessageReceived`)
- Clean-base lock + `FlushPendingPdfStampsAsync`
- `DrawFillTexts` / `StampSignatureImage` / `DrawHighlights` (PdfSharpCore MIT)
- `GetTextBoxes` PdfPig for Edit PDF bboxes
- `ImageOcrService` (Tesseract then Windows.Media.Ocr; cloud OCR stays opt-in `NETIE_SPACE_OCR_CLOUD`)
- DPAPI fill profile (section 6)
- Sidecar process spawn (clone `RustEmbedBridge`)

**Python `convert_engine.py`:** watermark / protect / searchable OCR only. Do **not** add Fill & Sign or AcroForm write-back there.

**PyMuPDF license trap:** AGPL-3.0 or Artifex commercial (https://pypi.org/project/PyMuPDF/ 1.28.2). Already used for watermark/protect. Do not grow it into form fill. Treat as existing license debt, not a Fill engine.

---

## 5. Snap / drag implementation notes

Lag and lost-pointer come from the WebView2 HWND + document mousemove, not from needing CSS transforms on the WPF window. Do not apply CSS `transform` to the WebView HWND or the page canvas during drag.

Today (`netie-viewer.html`):

- Stamp drag: `mousedown` on overlay sets `boxDrag`; `viewer mousemove` writes `style.left/top`; `window mouseup` commits. **No `setPointerCapture`.** Leaving the WebView or a button-up outside the view drops the drag.
- Fill boxes: not draggable (click-to-create only).
- Snap stamp: Shift + `snapPlacement` nearest AcroForm, radius `SNAP_RADIUS_PT`.
- Cell snap: sync canvas walk on click.

Do this:

1. Use Pointer Events on the overlay (`pointerdown` -> `el.setPointerCapture(ev.pointerId)`). Release on `pointerup` / `pointercancel`. This stays inside the WebView2 DOM.
2. During move, only write overlay CSS left/top (already PDF-space on layout). Convert to `xPt` / `yFromBottom` on **pointerup** via existing `placementFromOverlayCss`.
3. Coalesce with `requestAnimationFrame` (scroll already does this ~1677).
4. Hit-test in PDF points against `formFields[]` first (cheap). Raster snap-cell only if miss.
5. Fill click: if sidecar present, `exportPagePng` + `snap-cell`; else JS `detectCellAt`.
6. Fill-box drag: same capture as stamps; do not CSS-scale the canvas.
7. Ignore WPF `Mouse.Capture` over the WebView -- it fights Chromium.

Y math stays `PdfJsViewerService.CanvasToPdfBottomLeft` / `PdfBottomLeftToXGraphicsTop`.

---

## 6. AI fill + memory without sending PDFs off-box

Acrobat Autofill is a local (or Adobe-account) profile of name, email, phone, address -- not a cloud vision pass on the file. https://www.adobe.com/acrobat/resources/how-to-fill-out-a-pdf-form.html

Space plan:

1. **Profile store:** DPAPI JSON beside the vault (`PasswordVaultService` pattern, `%LocalAppData%\NetieSpace\`). Keys: `givenName`, `familyName`, `fullName`, `email`, `phone`, `address1`, `city`, `state`, `postalCode`, `country`, `dateISO`. No PDF bytes in this file.
2. **AcroForm match:** lowercase / strip punctuation on `field.name`. Map `first_name|firstname|givenname|fname` -> `givenName`, `ssn` never auto-filled, dates -> today local if the field type is date or name contains `date`.
3. **Flat / scanned:** OCR line **above or left** of the empty snap box (already have `ImageOcrService` boxes). Same key map on the label string. Never send the PNG or PDF to a cloud model unless the user already opted into `NETIE_SPACE_OCR_CLOUD` (image OCR only, not a new PDF upload).
4. **Optional local LLM later:** send **field names + nearby labels + profile keys** only (a few hundred tokens). Chat `ConversationMemoryCache` is the wrong store (it is prompt-priming, not a PII profile).
5. **UI:** after Fill click on a named field, show a chip "Fill from profile" rather than silent overwrite. Learn: when the user types a value into `fullName`, offer "Save to profile".

Do not scrape Adobe security-folder autofill files.

---

## 7. Ordered 5 implementation slices

1. **Pointer capture + fill-box drag (JS only).** `setPointerCapture` on stamp and fill overlays. Convert geometry on pointerup. Regression: `scripts/pdf-stress` 26/26 plus a drag-off-webview case. No sidecar.
2. **Rust `netie-pdf-detect` job `snap-cell`.** Port `detectCellAt`. C# fallback to JS. Host reuses `RequestViewerPagePngAsync`. Target click-to-box < 50ms when the exe is present.
3. **Go `netie-pdf-form` jobs `form-list` + `form-fill`.** Replace PdfPig as the primary field list (keep PdfPig fallback). If the overlay `snapped === 'acroform'` and `name` is set, Save calls `form-fill` on the clean base **then** still flattens stamps/highlights in C#. Untouched widgets stay interactive. Missing name -> `DrawFillTexts`.
4. **Edit PDF white-out on the visible page.** `GetTextBoxes` for `pageNum` not hardcoded 1. Click bbox -> cover fill (already `cover: true`). FORMAT = existing A/A plus font family on the float bar. OCR seed already works; do not duplicate. Honest limit: ligatures / overlapping spans.
5. **Local Autofill profile.** DPAPI store + name/label matcher. Chip UI. No PDF upload. Wire names from slice 3.

Parked (unlock later): comb IAb; check/X/dot tools; OpenEdit Tools grid; pdfium-render page raster; XFA; PyMuPDF form fill; whole-app rewrite.

---

## Library scorecard (2026)

| Lib | License | Use |
|---|---|---|
| PdfSharpCore 1.3.65 | MIT | Flatten text/stamps/highlights. Keep. |
| PdfPig 0.1.9 | Apache-2.0 | Fallback text + form rects. Keep. |
| pdf.js 4.10.38 | Apache-2.0 | Viewer only. Custom overlays. |
| pdfcpu v0.13.0 | Apache-2.0 | **Go sidecar** form-list / form-fill. |
| pdfer_forms | MIT OR Apache-2.0 | Parked Rust form crate if Go is blocked. https://github.com/clark-labs-inc/pdfer-forms-rs |
| lopdf | MIT | Too low-level alone. |
| pdfium-render 0.9.0 | MIT OR Apache-2.0 | Parked. Needs pdfium.dll (Apache/BSD + third-party). https://crates.io/crates/pdfium-render https://github.com/bblanchon/pdfium-binaries |
| image 0.25 | MIT OR Apache-2.0 | **Rust sidecar** PNG snap-cell. |
| PyMuPDF | AGPL or commercial | Watermark/protect only. Not Fill. |
| iText | AGPL | Banned. |
| pypdf | BSD | Python fallback if convert_engine must drop pymupdf later. |

---

## Sidecar spawn pattern (copy)

`native/netie-embed/src/main.rs` + `RustEmbedBridge.TryEncode`: args = job name, stdin = payload, stdout = one JSON object, missing exe = silent fallback.

Install: `scripts/build-netie-embed.ps1` twin scripts -> `tools/netie-pdf-detect/netie-pdf-detect.exe` and `tools/netie-pdf-form/netie-pdf-form.exe`. Gitignore the exes like ffmpeg.

---

## Verify (when a slice lands)

```
cd scripts/pdf-stress && npm install
python -m http.server 8517 --directory ../../src/NetieSpace/Assets/pdfjs
node stress.mjs all
```

In-app: `docs/research/findings/FILLSIGN_TEST.md` A+B. AcroForm: snap + Save + reopen in Acrobat Reader (human) for `/V` vs flatten. Y-mirror probe still applies to any new XGraphics draw.

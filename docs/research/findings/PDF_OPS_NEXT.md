# PDF ops — next Python-engine slice (design only)

**Scope:** watermark · protect/unlock · OCR → searchable PDF  
**Pattern:** `tools/convert_engine.py` subcommands + JSON stdout (same contract as `export-as`)  
**Status:** SHIPPED — engine + tests + C# More menu (OCR needs optional Tesseract)

---

## What Netie has today

| Layer | Location | Capabilities |
|---|---|---|
| **C# mutate (PdfSharpCore)** | `src/NetieSpace/Services/PdfEditService.cs` | merge, split-all, extract pages, rotate, reverse, organize/compose, insert image page, stamp signature (image/typed), fill-text bake, create blank (if wired) |
| **C# read (PdfPig)** | same file | `GetTextBoxes` (native text layer), `GetFormFieldRects` |
| **UI** | `PreviewWindow.xaml.cs`, `PdfOrganizeWindow` | PDF ▾ merge/split/extract/rotate/reverse/organize/sign; OCR probe shows “scanned — Windows OCR next” |
| **Python convert** | `ConversionService` → `tools/convert_engine.py` | `to-md`, `to-pdf`, `export-as`, previews; last stdout line = JSON `{success, output_path, tool, bytes, error}` |
| **Stress tests** | `scripts/convert-stress/test_matrix.py` | subprocess engine, temp fixtures (PIL + **pymupdf**), PASS/FAIL/SKIP |

**Gaps (explicit in `OPENEDIT_ACROBAT_AUDIT.md`):** Protect, Watermark toolbar, Scan&OCR / searchable PDF — all **missing**. PdfSharpCore cannot encrypt or run OCR; keep these in Python.

`tools/convert_engine.py` is referenced by C# but may be absent on a fresh clone — new actions belong in the same file/module layout when landed.

---

## Proposed CLI actions (add to `convert_engine.py`)

All actions: write sibling or explicit `<out>`, print one JSON line, never throw to stderr-only (match `export-as` guard rails).

| Action | Invocation | Notes |
|---|---|---|
| **pdf-watermark** | `pdf-watermark <in> <out> --text "DRAFT" [--opacity 0.25] [--angle 45] [--pages 1-3\|all]` | Text overlay every selected page |
| **pdf-watermark-image** | `pdf-watermark-image <in> <out> <png> [--scale 0.3] [--tile]` | Optional phase-2; image stamp across pages |
| **pdf-protect** | `pdf-protect <in> <out> --password <user> [--owner <owner>] [--perm print,copy,modify,annotate]` | AES-256 user lock |
| **pdf-unlock** | `pdf-unlock <in> <out> --password <pw>` | Decrypt to new file; wrong password → `{success:false, error:"bad password"}` |
| **pdf-ocr** | `pdf-ocr <in> <out> [--lang eng] [--force]` | Scanned → searchable PDF; skip if text layer already present unless `--force` |

Shared flags: `--pages` spec parser can mirror C# `ParsePageSpec("1-3,5")` for watermark only.

---

## Libraries

| Job | Library | Why |
|---|---|---|
| Watermark, encrypt/decrypt | **pymupdf** (`import pymupdf as fitz`) | Already in stress fixtures; `insert_text`/`insert_textbox` + rotation; `doc.save(..., encryption=..., user_pw=...)`; `authenticate()` for unlock |
| OCR → searchable | **ocrmypdf** (pip) | Industry default; wraps Tesseract; `--skip-text` for born-digital PDFs |
| Tesseract binary | **system / optional bundle** | Same SKIP pattern as ffmpeg in `test_matrix.py` |
| Fallback OCR (no Tesseract) | **none in v1** | Windows.Media.Ocr stays C#-only per audit; don’t split OCR backends in slice 1 |

**requirements.txt adds:** `pymupdf>=1.24` (direct dep; today only `pymupdf4llm` pulls it transitively), `ocrmypdf>=16` (optional group or core with graceful skip).

**Do not use:** iText (license), PdfSharpCore for encrypt (unsupported), pure pytesseract without PDF output stage.

---

## Implementation sketch (Python only)

```text
convert_engine.py
  main(argv) → dispatch
  _emit(result: dict) → print(json); sys.exit(0 if result["success"] else 1)

  pdf_watermark(in, out, text, opacity, angle, pages)
    doc = pymupdf.open(in)
    for page in selected:
      page.insert_textbox(rect, text, rotate=angle, fill_opacity=opacity, ...)
    doc.save(out)

  pdf_protect(in, out, user_pw, owner_pw=None, permissions=...)
    doc = pymupdf.open(in)
    doc.save(out, encryption=pymupdf.PDF_ENCRYPT_AES_256, user_pw=..., owner_pw=..., permissions=...)

  pdf_unlock(in, out, password)
    doc = pymupdf.open(in)
    if not doc.authenticate(password): return error
    doc.save(out, encryption=pymupdf.PDF_ENCRYPT_NONE)

  pdf_ocr(in, out, lang, force)
    import ocrmypdf
    ocrmypdf.ocr(in, out, language=lang, skip_text=not force, deskew=True)
```

C# later (thin): `PdfOpsService` clones `ConversionService.RunAsync` — no .NET SDK needed for engine work.

---

## Acceptance tests — `scripts/pdf-stress/test_pdf_ops.py`

Mirror `scripts/convert-stress/test_matrix.py`:

```text
ROOT, ENGINE, PY, PASS/FAIL/SKIP
run(action_args) → parse last JSON line from stdout
build_fixtures(tmp):
  - native.pdf   — pymupdf text layer ("Page one")
  - scan.pdf     — raster page only (render PNG → insert_image, no text)
  - protected.pdf — produced in-test via pdf-protect

Cases:
  pdf-watermark native→out     PASS if out exists, size>200, pymupdf get_text contains watermark string
  pdf-protect native→locked     PASS if pymupdf.open without password fails / needs auth
  pdf-unlock locked→clear       PASS if opens without password; text preserved
  pdf-unlock wrong-password     PASS if success is false (no throw)
  pdf-ocr scan→searchable       PASS if get_text length > 10 on page 1 (SKIP if tesseract missing)
  pdf-ocr native→out            PASS if skip path: out exists, get_text unchanged (or SKIP+force path)
  bad target / missing in       PASS if success false + error key

Run: .venv\Scripts\python.exe scripts\pdf-stress\test_pdf_ops.py
Exit 0 = green; document SKIP count like ffmpeg/heic in test_matrix.
```

---

## Slice order (one PR)

1. **pymupdf deps + pdf-watermark + pdf-protect/unlock** — no external binaries; fully CI-green.
2. **pdf-ocr + test skips** — ship with `scripts/install-tesseract.ps1` note in README (optional).
3. **C# wire** — Protect / Watermark / Scan&OCR cards call new actions via `ConversionService` or sibling `PdfOpsService`.

---

## Out of scope (this slice)

- Redaction, cert digital signatures, permission UI parity with Acrobat Pro
- Moving merge/split/organize from PdfEditService to Python (C# path is fine)
- Groq Vision OCR for PDF pages (keep for image→MD; OCR PDF uses local Tesseract)

---

## Key references

- `src/NetieSpace/Services/PdfEditService.cs` — existing PDF mutate surface
- `src/NetieSpace/Services/ConversionService.cs` — JSON subprocess contract
- `scripts/convert-stress/test_matrix.py` — acceptance test shape
- `docs/research/findings/OPENEDIT_ACROBAT_AUDIT.md` — Protect / OCR backlog

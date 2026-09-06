```yaml
keywords:
  - acrobat
  - fill-and-sign
  - edit-pdf
  - pdfspace
  - open-in-app
  - 5d-plan
main_idea: Ranked 5d Acrobat gaps for D:\Space. Ship Fill&Sign + Edit PDF + Open-in-Netie on WPF/pdf.js. Park Protect/Share/Compare/OCR-full and whole-app Rust/Go.
date: 2026-09-05
reuse: docs/research/findings/OPENEDIT_ACROBAT_AUDIT.md, docs/subagents_findings/2026-09-04_acrobat-pdfspace-engines.md
```

# Acrobat DC distill — Space 5d ship table

Repo root = **`D:\Space` only**. Do not edit `C:\Users\...\Netie Space` for Netie work.

## Already shipped (do not rebuild)

| Capability | Where |
|---|---|
| Fill click-cell / AcroForm / line / free box | `netie-viewer.html` enterFillMode |
| A/A/delete float bars | fillFloatBar + stamp bar |
| Bake fill + stamp + highlight | `PdfEditService.DrawFillTexts` / Stamp / DrawHighlights |
| Save-then-close flush | `RequestCloseAsync` + `_pdfFlushTask` |
| Edit PDF bbox -> cover fill | `enterTextEditMode` + cover whiteout |
| Edit in app workbench rail | `_pdfSpaceMode` + PdfSpaceRail |
| Organize / Merge / Protect / Watermark | ribbon More + Organize window |
| Compress honest (disabled) | MenuItem IsEnabled=False |

## Ranked 5d gaps (this wave)

| # | Gap | Ship bar | Out |
|---|---|---|---|
| 1 | Fill & Sign split buttons | One **Fill & Sign** mode shows Fill + Sign panels | Comb IAb, check/X tools |
| 2 | Edit PDF OCR honesty | Status: native text vs OCR seed; OCR not fake Edit | Full OCR pipeline / searchable PDF rewrite |
| 3 | Open externally = Acrobat | PDF failure / open -> **Edit in app** (Netie), not OS default | Steal system PDF association |
| 4 | Create blank PDF | `CreateBlankPdf` + rail button | From-scanner Create |
| 5 | Combine | Rail -> existing Merge dialog | Fancy Acrobat Combine UI |

## Parked (STATUS / PARKING_LOT)

- Whole-app Rust/Go rewrite (P7)
- True `/V` AcroForm write-back
- Share / Compare / full Protect UX parity
- Pointer HUD + AirGPT under low RAM (mesh note, not Space)

## Demo proof (human, ~10 min)

1. Space on a PDF -> **Edit in app** -> Fill & Sign -> type in a cell -> Sign place -> **Save** -> close waits for flush.
2. Edit PDF -> click a word -> change text -> Save (whiteout + new text).
3. OCR this page on a scan -> seeds boxes; status never claims full Acrobat OCR.
4. Create blank / Combine from rail; Compress stays disabled or honest.
5. Failed PDF preview: **Open in Netie Space** enters workbench, does not launch Acrobat.

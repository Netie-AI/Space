```yaml
keywords:
  - multieditor-goal
  - flying-boxes
  - annotation-layer
  - markdown-oss
  - themarkdowneditor
  - csv-editor
main_idea: Goal armed. Flying PDF boxes fixed via pageCssBox + no scroll re-layout + annot CSS. MD dual-pane + CSV grid shipped offline. Milkdown/TipTap still NEEDS-YOU. Whole-app Rust PARK.
date: 2026-09-07
```

# Multi-editor goal — PDF bbox + MD inventory

## Goal

Smooth multi-format editors; PDF + Markdown first; OSS downloads when MIT/Apache; Rust sidecars only; no flying PDF boxes.

## PDF flying boxes (executed)

Cause: overlay layout used `getBoundingClientRect()` during scroll (viewport drift) + incomplete AnnotationLayer CSS for AcroForm widgets.

Fix in `netie-viewer.html`:
- fill/stamp/highlight layout → `pageCssBox` (`clientWidth` + `offsetLeft/Top`)
- annotDiv pinned `left/top: 0` with pdf.js-style field CSS vars
- scroll handler no longer re-layouts fills/stamps/highlights (lag + fly source)
- DPR capped at 1.5; fill-draw clamp uses bitmap pixels; stamp ghost size via `pageCssBox`
- Self-check: `scripts/check-pdf-bbox-glue.ps1`, `check-widget-align.ps1`

Human visual still required on EP FORM once RAM >= ~2 GB free.

Live pdf.js (Playwright, 2026-09-07): `__stress/acroform.pdf` widget Rect [72 690 252 714]. Wrap-relative x/y/w/h matched 72/612 and (792-714)/792 within 1e-5 at Fit and at setZoom 2.5 (`--scale-factor` tracked cssScale). Scroll did not change wrap-relative x/y. Fill overlay on `__stress/test.pdf` held bx~0.0817 through scroll/zoom/Fit.

## Markdown + CSV (shipped v1)

- `Assets/markdown/md-editor.html` + `marked.min.js` (MIT) dual-pane Split/Source/Preview
- `Assets/markdown/csv-editor.html` offline grid
- Wired via `https://netie.markdown/...` in `PreviewWindow.TextEditButton_Click`
- Self-check: `scripts/check-multieditor-assets.ps1`

https://themarkdowneditor.app/ is a paid macOS app, not OSS. Milkdown/TipTap MIT still NEEDS-YOU for richer WYSIWYG.
BAN OnlyOffice AGPL. LibreOffice = installed soffice, not repo dump. P7 whole-app Rust PARK.

PRD: `2026-09-07_multieditor-goal-prd.md` row 33.

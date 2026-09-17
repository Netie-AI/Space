# Fill bounding-box blocked by AcroForm layer

keywords: fill, bounding-box, annotationLayer, pointer-events, EP FORM, autofill
main_idea: Fill-mode set pointer-events:auto on the whole pdf.js annotationLayer, so every drag hit the overlay and never started a fill box. Layer stays none; only widget sections are auto. EP FORM was never filled (no Gemini, no Cortex run). Demo only: `__stress/acroform.pdf`.

## Cause

`#viewer.fill-mode .annotationLayer` was `pointer-events: auto` on the full-page overlay. Combined with `closest('.annotationLayer') return` on mousedown, drag-draw never started on any AcroForm PDF.

## Fix

- Layer: `pointer-events: none` in fill-mode
- Widget `section`: `pointer-events: auto` (native type)
- JS skip only `section` / `input|textarea|select`, not the whole layer

## Verify

```
powershell -File D:\Space\scripts\check-pdf-bbox-glue.ps1
```

Playwright on `netie-viewer.html?file=__stress/acroform.pdf`: Fill on, annot PE none, section PE auto, Name typed `DEMO (not EP FORM)`, drag created `.fill-text`. Screenshot `filled-demo-acroform.png`.

Does not prove: human EP FORM 158 widgets in WPF. RAM was 0.37 GB; no rebuild.

## Simple path (no Cortex, no Gemini fill)

Fill & Sign -> type blue widgets or Autofill from My details (DPAPI) -> Save.

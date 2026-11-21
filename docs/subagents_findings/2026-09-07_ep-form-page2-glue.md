# EP FORM AcroForm glue (pdf.js viewer)

keywords: EP FORM, acroform, flying-boxes, page-2, zoom, scroll, fit
main_idea: Real EP FORM in netie-viewer.html. Widgets are on page 2 (page 1 has none). 37 sections on page 2 stayed wrap-relative through Fit, scroll, and zoom 2. Not WPF, not all 158 widgets.

## Evidence 2026-09-07 Playwright

File: local copy `__stress/ep-form.pdf` (gitignored, from Downloads). Viewer `netie-viewer.html`.

- Page 1 annotationLayer hidden / 0 sections (cover).
- Page 2: 37 widget sections after Fit (scale ~1.27).
- First widget wrap-rel ~ x 0.05865 y 0.20421 w 0.0178 h 0.0123 at Fit and after scroll (delta ~1e-9).
- After setZoom 2: x 0.05865 y 0.20421 w 0.01783 (delta < 0.001).

Trap: snapping `.page-wrap` (page 1) reports 0 widgets. Glue checks must use `data-page="2"`.

Does not prove: remaining pages/widgets, WPF WebView2 host, human visual on this PC (RAM). P7 PARK.

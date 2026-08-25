# Netie PDF viewer stress harness

Drives the real `netie-viewer.html` in headless Edge/Chrome and asserts the whole
Sign + Fill behaviour surface: place/drag/zoom/scroll, autosave reload storms
(ghost regression), float bars, table-cell detection, typing, deletion,
position-keeping. 26 assertions, ~60s.

The viewer exposes a dev hook (`window.__netie`) **only when not hosted in
WebView2**, so the harness tests the exact shipping file with zero behaviour
change inside the app.

## Run

```bash
cd scripts/pdf-stress
npm install

# 1. test asset (only needed once, or after changing the generator)
python make_test_pdf.py "../../src/NetieSpace/Assets/pdfjs/__stress/test.pdf"

# 2. serve the real viewer folder
python -m http.server 8517 --directory ../../src/NetieSpace/Assets/pdfjs

# 3. in another terminal
node stress.mjs all        # or phase1 / fill
```

Outputs: console PASS/FAIL, `stress_results.json`, `shot_*.png` next to the script.
Set `NETIE_BROWSER` to a chrome/edge exe path if autodetect fails.

## Known geometry of the test PDF

Page 1 mimics the employment form's REFEREES grid:
column X edges `50,175,285,375,465,530,562`; row Y edges `580,620,660,700`
(origin bottom-left). The empty second data row's Name cell is
`x 50..175, y 580..620` — F1 asserts cell detection against it.
Page 2 has Yes/No boxes, page 3 is mostly blank (free-text fallback + stamp
area). Regenerate any time with `make_test_pdf.py`.

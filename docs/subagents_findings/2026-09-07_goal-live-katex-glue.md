# Goal evidence - live KaTeX + PDF glue (Debug exe exists)

keywords: katex, glue, acroform, markdown, offline, P7
main_idea: Offline KaTeX renderToString OK. Live md-editor produced 2 .katex nodes. AcroForm widget x~0.1176 held Fit/scroll/zoom 2.5; fill overlay did not fly. EP FORM in WPF still unproven. P7 PARK.

## Markdown

`node scripts/check-katex-offline.mjs` OK.
Playwright `md-editor.html`: `$x^2$` + `$$frac$$` -> 2 `.katex`, GFM table. No CDN.

## PDF

Playwright `__stress/acroform.pdf`: widget wrap-rel x 0.11764 at Fit and 2.5. annot PE none in fill-mode. Seeded fill overlay stable on scroll.

Does not prove: EP FORM 158 widgets in NetieSpace.exe. Image Rotate90Cw (PowerShell could not load the WPF type). PPTX (no soffice on this PC).

# Goal progress - PDF glue live + MD DISTILL UX

keywords: flying-boxes, glue-live, markdown, themarkdowneditor, lag, thumb-batch, P7
main_idea: Live AcroForm widget+fill wrap-relative coords held through scroll and zoom 2.5. MD dual-pane gained hide-chrome, type size, light/dark, Ctrl+B/I. Not goal-complete: EP FORM human, no KaTeX/GitHub, office still inventory.

## PDF (execute-now)

Playwright on `netie-viewer.html?file=__stress/acroform.pdf` 2026-09-07:
- widget wrap-rel x~0.11765 y~0.09848 at Fit and setZoom 2.5 (72/612, (792-714)/792)
- scroll did not change widget or seeded fill overlay
- fill-mode annot PE none / section auto
- Repeat: `scripts/pdf-stress/glue-live.mjs` (needs puppeteer-core + server)

Lag: THUMB_BATCH 48 -> 12; visible-page prefetch 800px -> 400px; DPR still capped 1.5.

Does not prove: EP FORM 158 widgets in WPF (RAM < 2 GB).

## Markdown (DISTILL, not a clone)

`md-editor.html`: auto-hide chrome, Dark/Light, A-/A+, word count, Ctrl+B/I wrapSel, GFM tables/tasks/strike, image max-width. Offline marked MIT.
Smoke: table+del+checkbox+light+15.5px type.
Skipped: KaTeX, GitHub sync, Milkdown/TipTap (NEEDS-YOU), paid macOS clone.

## Other formats

Still inventory (OnlyOffice AGPL BAN). P7 PARK. CSV grid unchanged.

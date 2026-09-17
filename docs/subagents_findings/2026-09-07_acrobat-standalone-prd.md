---
keywords: acrobat-standalone, replace-surface, edit-in-app, wpf-chrome, canvas-only, previewwindow, netie-viewer, hide-toolbar, pageHud, pdfspace, p7
main_idea: Founder signed PRD replace-surface. Edit-in-app must be a standalone WPF document window, not HTML-in-Chrome. Same NetieSpace.exe. WebView2 is page canvas only. Supercedes row 44 HTML chrome polish. Execute-now one lane on PreviewWindow + netie-viewer.html.
date: 2026-09-07
kind: prd_intake
product: Space
reuse: docs/subagents_findings/2026-09-07_acrobat-chrome-prd.md, docs/subagents_findings/2026-09-07_acrobat-chrome-spec.md, PRD-001 ledger 4/8/12/44
cite: PRD-001 row 45
---

# PRD intake - Edit-in-app is a standalone WPF app (Mode B)

PREFLIGHT: PARTIAL. Reused acrobat-chrome-prd, acrobat-chrome-spec, STATUS.md, PRD-001 ledger 4/8/12/44.

Analog: TAS-SPACE live lane (`D:\Space`). No frozen Acrobat clone. Prefer extend-live. DISTILL founder Acrobat screenshots into original Netie WPF. Do not vendor Adobe. Do not scrape Adobe binaries. BAN n8n / grok-bot / AGPL / GPL / leaked trees. Palantir P1 stays parked.

## Nearness (artifacts checked, no %)

Customer-facing artifact: `src/NetieSpace/Views/PreviewWindow.xaml` (`WindowStyle=None`, `AllowsTransparency=True`, dark glass Quick Look) hosting WebView2 `src/NetieSpace/Assets/pdfjs/netie-viewer.html`.

What a person can open today after row 44:

- Same glass shell. Brand `NETIE SPACE` + filename + `Edit in app`.
- HTML `#toolbar` still in the page (Prev/Next/zoom/Fill/Highlight/Search) plus `#pageHud` plus `#thumbs`. Light `html.doc-app` tokens. Host `setDocChrome`.
- WPF `PdfEditorRibbon` light chips (Fill & Sign / Edit PDF / Organize / Comment / Save / More). `PdfSpaceRail` label Tools.
- Glue/http test still opens the HTML in Chrome at `127.0.0.1:8522` -- that is a webpage.

What they still cannot open:

- A standalone document window that does not look like a browser page.
- WPF-owned title/menu/tabs + two toolbars with WebView showing only the page.

Landing: not fetched (Marketing). PRD draft, not sliced. Open GitHub: `Netie-AI/Space#3` SEC-01 only.

## Routing (Mode B)

Founder words this turn: "it shouldnt be a chrome, it should be a standalone app, with own UI, need prettier, rewrite different totally new ui it is super important, amend prd start building and improve the ui based on all the pictures i sent you"

Maps to **NO epic** (PRD unsliced). That is a PRD amendment. Founder signed in the same message (`amend prd start building`).

**SIGNED replace-surface** for Edit-in-app ONLY. Supercedes row 44 (HTML toolbar restyle was still a webpage). Not a second PDF product. Not P7.

**Execute-now** (inspectable): one seated writer, row 12 contention set.

1. When `_pdfSpaceMode`: WPF is the app chrome. Light document window: title, file tab, page/zoom toolbar, mode toolbar Fill & Sign / Edit PDF / Organize / Comment, Save, left thumbs, right Tools.
2. WebView2 is the PAGE CANVAS only. Hide HTML `#toolbar` / `#pageHud`. Hide HTML `#thumbs` once a WPF thumb pane exists (do not leave dual thumbs).
3. Keep every viewer JS id and host message (`gotoPage`, `setZoom`, `fitPage`, `enterFillMode`, ...). WPF buttons click those; do not rewrite fill glue.
4. Space-key Quick Look glass DNA stays when `_pdfSpaceMode` is false.
5. DISTILL screenshots (row 44 spec inventory) into original Netie WPF. No Adobe logo, Home cloud, AI Assistant, Sign In, Share-to-Adobe, Tools card home, dual-window page drag.
6. Organize between PDFs stays `PdfOrganizeWindow` + Combine.

**PARK:** P7, second exe, Adobe brand/Home/Tools grid, iLove cloud, compress/page-numbers/crop this wave (row 41 FILL_ONLY ops). Nested File/Edit/View clone.

## Hard rails (writer)

- Same `NetieSpace.exe`. Visual state on `PreviewWindow`, not a new Window class unless the glass HWND cannot go light.
- Do not retoken all of `App.xaml`. Workbench local brushes already exist (`DocChip`, `#F5F5F5`, `#0B5CAB`).
- Contention files (row 12, one lane): `PreviewWindow.xaml`, `PreviewWindow.xaml.cs`, `netie-viewer.html`. Update `scripts/check-acrobat-chrome.ps1` in the same lane (it currently requires visible `#pageHud` / toolbar markup).
- Do not delete `#toolbar` nodes. Hide them (`display:none` / `html.canvas-only`). Glue server in Chrome may keep HTML chrome for debug.
- Do not steal Fill smoothness. Overlay bake, annotationLayer `--scale-factor`, and fill/stamp float bars stay on the canvas.
- EPIC Agent must not file a second epic on those three files.

## Verified files

- `PreviewWindow.xaml` -- `WindowStyle=None` ~10; glass title ~137; `EditInAppButton` ~195; `PdfEditorRibbon` ~512; `PdfSpaceRail` ~1081.
- `PreviewWindow.xaml.cs` -- `_pdfSpaceMode` ~77; `ApplyPdfSpaceRail` `setDocChrome` ~654-671; existing `gotoPage` / `setZoom` / `fitPage`.
- `netie-viewer.html` -- `html.doc-app` ~2; `#toolbar` ~263; `#thumbs` ~328; `#pageHud` ~334; `setDocChrome` ~2643.
- `scripts/check-acrobat-chrome.ps1` -- asserts `#pageHud` present; must flip to canvas-only when workbench is on.
- GitHub -- `Netie-AI/Space#3` OPEN only.

## Ledger

Appended PRD-001 row 45. No tickets. No implementation this intake.

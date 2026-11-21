---
keywords: acrobat-chrome, edit-in-app, pdfspace, light-chrome, icon-toolbar, pdf-editor-ribbon, pdfspace-rail, netie-viewer, p7, packet-2-lite, fill-only
main_idea: Founder Acrobat-look ask is chrome polish of the signed Edit-in-app workbench (ledger 8/12/44), execute-now. Not a new product. Light Netie chrome on live viewer+ribbon+rail. Park Home, Tools grid, nested menus, dual-window page drag, Adobe brand, P7, second exe.
date: 2026-09-07
kind: prd_intake
product: Space
reuse: docs/subagents_findings/2026-09-04_pdfspace-prd-intake.md, docs/subagents_findings/2026-09-04_acrobat-pdfspace-engines.md, docs/subagents_findings/2026-09-05_acrobat-5d-implement.md, docs/research/findings/2026-09-05_acrobat_dc_distill.md, docs/research/findings/OPENEDIT_ACROBAT_AUDIT.md
cite: https://helpx.adobe.com/acrobat/desktop/edit-documents/organize-pages/move-between-pdfs.html
---

# PRD intake - Acrobat-class chrome for Edit in app (Mode B)

PREFLIGHT: PARTIAL. Reused pdfspace-prd-intake, acrobat-pdfspace-engines, acrobat-5d-implement, acrobat_dc_distill, OPENEDIT_ACROBAT_AUDIT, PRD-001 ledger rows 4/5/8/12/36/41.

Analog: TAS-SPACE live lane (`D:\Space`). No frozen Acrobat clone. Prefer extend-live. DISTILL Acrobat UX into original Netie chrome on live files. Do not vendor Adobe. Do not scrape Adobe binaries. BAN n8n / grok-bot / AGPL / GPL / leaked trees. Palantir P1 stays parked. Do not redesign Space Quick Look glass DNA. Workbench-mode chrome only.

## Nearness (artifacts checked, no %)

Customer-facing artifact: `src/NetieSpace/Assets/pdfjs/netie-viewer.html` hosted in `PreviewWindow.xaml` WebView2. Same HTML also opened in Chrome at `127.0.0.1:8522` for glue tests (no WPF chrome; looks even more like a dark web page).

What a person can open today:

- Top-right **Edit in app** (`EditInAppButton`) sets `_pdfSpaceMode`. Brand text switches to PDFSpace workbench mode. Identity of the product is still Netie Space (row 8).
- Viewer: `color-scheme: dark`, liquid-glass `#toolbar`, text chips `Continuous` / `Fit` / `Fill text` / `Highlight` / `Mark` / `Select area` / `Search`, page `‹` `›` + number, zoom `-` `%` `+`, left `#thumbs`. Overlay `#banner` + fill/stamp float bars. No icon toolbar. No light chrome.
- WPF `PdfEditorRibbon`: dark teal (`#1A5EC4B8`) text chips Organize / Sign / Fill / Comment / Save / More. Mode panels are more chips (`Exit Fill`, Autofill, OCR).
- WPF `PdfSpaceRail` (188px): label `PDFSPACE`, text buttons Fill & Sign / Edit PDF / Sign / Organize / Comment / Autofill / My details / OCR / Blank / Combine / Export / Watermark / Protect.
- Packet 2 lite = this right rail, not a second exe. Fill/Sign/Edit/Organize/Comment already live (rows 8/12/13/14/28, acrobat 5d).

What they cannot open:

- Acrobat-class professional light chrome (this defect).
- Adobe Home, Tools card grid, nested File/Edit/View, cloud, AI Assistant, Share-to-Adobe, Sign In (PARK; do not clone).
- Dual live PDF windows with drag-pages-between (Adobe helpx organize/move-between-pdfs). Space already has Organize + Combine.
- A second PDF exe / OpenEdit Home shell (row 4 unsigned remainder; row 8 signed rail-only).

Landing: not fetched (Marketing owns building it). PRD still draft, not sliced. Open GitHub: `Netie-AI/Space#3` SEC-01 only. No PDF epic exists.

## Routing (Mode B)

Founder words ("rebuild the whole app") look like a product rewrite. Hard rails + signed row 8 already bound the workbench. Call:

**Execute-now** under signed rows 8/12 (chrome polish). **Not** a PRD amendment for a new Acrobat product. **Not** a second epic. PRD is unsliced; do not manufacture a parent. Seated writer restyles the live workbench files. EPIC Agent must not file a second epic on the row-12 contention set.

Split (same pattern as rows 5/15/28):

1. **Execute-now (inspectable)** -- when `_pdfSpaceMode`, make Edit-in-app look and behave like a professional desktop PDF app, Netie-branded: light chrome, icon toolbar (page nav, zoom, fit, search), left thumbs restyle, right tools rail restyle, mode-specific Edit toolbar (Fill / Edit PDF / Organize / Comment show the tools for that mode, not every chip at once), Save in the toolbar not as a floating overlay chip. Keep WPF + WebView2 + pdf.js + custom overlays. Keep Fill smoothness lane (row 41 FILL_ONLY) -- this is chrome of existing tools, not new PDF ops.
2. **PARK** -- Adobe Home, Tools card grid, cloud, nested File/Edit/View clone, drag pages between two live PDF windows, Adobe brand/AI/Share/Sign-In, second PDF exe, P7 whole-app Rust/Go rewrite, iLove cloud, compress/page-numbers/crop this wave (row 41).

Helpx `move-between-pdfs`: DISTILL as page nav + organize. Route to existing `PdfOrganizeWindow` + Combine. Do not invent a dual-window Acrobat clone.

## Hard rails (writer)

- Keep Netie Space identity. Rail may say PDFSpace as the workbench mode label (row 8). No Adobe logo, purple, Home cloud, or Sign In.
- Keep P7 parked. No rewrite of `NetieSpace.exe` in Rust/Go.
- Do not restyle the Space-key Quick Look glass shell (`App.xaml` tokens, `GlassHelper`) as a global DNA rewrite. Light chrome is local to viewer CSS + workbench ribbon/rail while `_pdfSpaceMode`.
- Contention files (row 12, one lane): `PreviewWindow.xaml`, `PreviewWindow.xaml.cs`, `netie-viewer.html`. Do not open a second epic on them. Coordinate with any in-flight Fill glue on the same HTML.
- Do not slice OpenEdit Home/Tools as a product shell (row 4 remainder stays unsigned).
- Do not unpark iLove / compress / page-numbers / crop (rows 29/36/41).
- Do not scrape Adobe binaries. Research = public helpx + founder screenshots + existing distill findings, then original Netie CSS/XAML.

## Verified files

- `src/NetieSpace/Assets/pdfjs/netie-viewer.html` -- `:root` dark glass; `#toolbar` text chips (lines ~8-75, ~224-248); `#thumbs` dark rail; no icon set.
- `src/NetieSpace/Views/PreviewWindow.xaml` -- `EditInAppButton` ~118; `PdfEditorRibbon` ~435 (EditorChip row + Save chip ~459); `PdfSpaceRail` ~997.
- `src/NetieSpace/Views/PreviewWindow.xaml.cs` -- `_pdfSpaceMode` ~77; `EditInAppButton_Click` ~614; `ApplyPdfSpaceRail` ~654.
- `STATUS.md` Now 1 -- PDFSpace workbench already live; P7 PARK; FILL_ONLY this wave.
- GitHub -- `Netie-AI/Space#3` OPEN only.

## Ledger

Appended PRD-001 row 44. No tickets. No implementation this intake.

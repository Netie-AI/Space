---
keywords: acrobat-ui-rebuild, edit-in-app, docked-chrome, row-45, row-48, overlay-supercede, chrome-glue, 8522, doc-app, PreviewWindow, netie-viewer, fill-only
main_idea: SIGNED execute-now. Supercede row 48 workbench floating overlay. Restore and improve row 45 docked Acrobat-like WPF chrome. Keep row 48 peek/light-edit density and Space swallow. Chrome glue at 127.0.0.1:8522 must be a light doc-app even without WPF. Same exe, one lane, FILL_ONLY.
date: 2026-09-09
kind: prd_intake
product: Space
reuse: docs/subagents_findings/2026-09-07_acrobat-chrome-prd.md, docs/subagents_findings/2026-09-07_acrobat-chrome-spec.md, docs/subagents_findings/2026-09-07_acrobat-standalone-prd.md, docs/subagents_findings/2026-09-08_chrome-density-prd.md, PRD-001 ledger 8/12/44/45/47/48
cite: https://helpx.adobe.com/acrobat/desktop/edit-documents/organize-pages/move-between-pdfs.html
---

# PRD intake - Acrobat UI rebuild (Mode B)

PREFLIGHT: HIT. Reused acrobat-chrome-prd (row 44), acrobat-chrome-spec, acrobat-standalone-prd (row 45), chrome-density-prd (row 48), STATUS.md, PRD-001 ledger 8/12/44/45/47/48. Helpx organize/move-between-pdfs fetched as public docs (not Adobe binaries). Founder screenshot pack described in this turn; images not stored in repo.

Analog: TAS-SPACE live lane (`D:\Space`). No frozen Acrobat clone. Prefer extend-live. DISTILL founder screenshots + public helpx into original Netie WPF/CSS. Do not vendor Adobe. Do not scrape Adobe binaries. BAN n8n / grok-bot / AGPL / GPL / leaked trees. Palantir P1 parked.

## Nearness (artifacts checked, no %)

Customer-facing artifact the founder photographed this turn: Chrome glue `127.0.0.1:8522/netie-viewer.html`, not the WPF workbench. That page is still a webpage.

What a person can open today:

- Chrome glue: `netie-viewer.html` has `html.doc-app` in source, but `:root` / `body` / `#viewer` still default `#10141c`. Left `#thumbs`, HTML `#toolbar`, `#pageHud`. No right Tools rail (that is WPF). Page can sit left of a dark void. AcroForm Save/Clear widgets on the form are document content, not app chrome. This matches screenshot 1.
- WPF Edit-in-app (`ApplyWorkbenchShell`): `DocAppHeader` + `DocNavBar` forced `Collapsed`. `SetRibbonFloated(true)` reparents `PdfEditorRibbon` into `DocToolFloat`. `ApplyPdfSpaceRail` always `Collapsed` the rail (column width 0) even when `_pdfSpaceMode`. HTML `canvas-only` hides `#toolbar` / `#pageHud`. This is the row-48 overlay, not the Acrobat stacked toolbars in screenshots 3/6/7.
- Peek density from row 48 is live (`PeekActionStrip` collapsed). Space swallow is live (`App.xaml.cs`). Light Edit teaching collapsed.

What they still cannot open:

- A standalone document window that looks like Acrobat Pro/Reader document chrome (two docked toolbars, file tab, left thumbs, right Tools list, light gray, page centered).
- A Chrome glue screenshot that does not look like a dark website.

Landing: not fetched (Marketing). PRD draft, not sliced. Open GitHub: `Netie-AI/Space#3` SEC-01 only (does not own this).

## Routing (Mode B)

Maps to **NO epic** (PRD unsliced). Not Space#3. Not a closed epic. Not a second PDF product.

Founder words this turn ("UI sucks / look like Acrobat desktop / rebuild when Edit in app / standalone smooth app / redesign / build based on the UI") plus Acrobat stacked-toolbar screenshots **resolve the 45 vs 48 conflict**.

**SIGNED execute-now.** Same `NetieSpace.exe`. One seated writer. One lane. No tickets. EPIC Agent must not file a second epic.

| Surface | Call |
|---|---|
| Edit-in-app WPF chrome | **Execute-now.** Supercede row 48 workbench overlay. Restore/improve row 45 docked chrome. |
| Peek + light Edit density | **Keep** row 48 (not contradicted). |
| Space swallow in workbench | **Keep** row 48 (supercedes row 47 leave-to-peek). |
| Chrome glue `8522` | **Execute-now.** Light `doc-app` even with no WPF host. Not the product shell; still inspectable because the founder photographs it. |
| Nested File/Edit/View clone, Home cloud, Tools grid, dual-window, FORMAT pane, Hand, Add Image/Link/Crop/Header&Footer, Share-to-Adobe | **PARK** (already signed). |
| P7 / second exe / iLove cloud | **PARK**. |

Not NEEDS-YOU. Founder screenshots are the sign.

## Conflict call (45 vs 48)

Row 45 signed: WPF owns chrome (title, file tab, page/zoom toolbar, mode toolbar, left thumbs, right Tools). WebView is canvas-only.

Row 48 then hid that stack for a floating overlay because the live stacked chrome was ugly glass + Look Up strip + teaching.

This turn the founder shows Acrobat **stacked** toolbars again (screenshots 3, 6, 7) and photographs the **Chrome glue** as the suck (screenshot 1). The overlay is not the target.

**Workbench overlay is superceded.** Docked row-45 chrome is the workbench target. Row-48 peek/light-edit/Space-swallow stay.

## Screenshot map (DISTILL, do not clone)

| # | Seen | Route |
|---|---|---|
| 1 | Chrome glue: dark toolbar, left thumbs, black void, left-aligned page, form Save/Clear widgets | Execute-now HTML light canvas. Do not add Save/Clear buttons. |
| 2 | Menu > View > Page navigation nested | PARK nested clone. Keep existing File/Edit/View items already on `DocAppHeader`. Do not add First/Last/Shift+Ctrl+N. |
| 3 | Compact two-row Edit PDF; green box on Add Image + Link | Row 1 = `DocNavBar`. Row 2 = `PdfEditorRibbon` Fill / Edit PDF / Organize / Comment. PARK Hand, Add Image, Link, Crop, Header & Footer (FILL_ONLY). |
| 4 | Acrobat Home (Andy): cloud, AI, Sign In | PARK. `DocHomeTab` stays "leave workbench", not a cloud recents page. |
| 5 | Tools card grid | PARK. `PdfSpaceRail` stays icon+label list. |
| 6 | Pro Edit PDF full: light gray, menus, Home/Tools/file tab, two toolbars, dashed boxes, FORMAT + colorful rail | DISTILL docked layout. Dashed boxes = existing fill overlay. PARK FORMAT pane and colorful switcher. |
| 7 | Reader document: Home/Tools, save/print/email, page 3/5, thumbs, right tools, Share | Save on `DocNavBar` (`PdfSaveStampsButton`). Hide print/email/`DocShareBtn`. Left thumbs + right Tools list. |

Helpx move-between-pdfs (Windows: Tile Vertically, drag thumbs across two PDFs): DISTILL to existing `PdfOrganizeWindow` + Combine. Do not clone dual-window.

## Execute-now checklist (inspectable)

Product surface = `NetieSpace.exe` Edit-in-app. Glue surface = same `netie-viewer.html` with no host.

### A. WPF workbench (`_pdfSpaceMode`)

1. Show docked chrome: `DocAppHeader` Visible, `DocNavBar` Visible, `PdfEditorRibbon` docked in `RibbonBand` (not in `DocToolFloat`).
2. `SetRibbonFloated(false)` when workbench is on. `DocToolFloat.IsOpen = false`. `DocZoomFloat` closed (zoom lives on `DocNavBar`).
3. `ApplyPdfSpaceRail`: when `on`, `PdfSpaceRail` Visible and column ~188px. Do not leave Width 0.
4. `DocLeftNav` Visible, column ~120px (not 44px icon strip). WPF thumbs own the pane; HTML `#thumbs` stay hidden via `canvas-only` + `no-thumbs` when WPF thumbs are on.
5. Hide `DocShareBtn`. Hide peek glass: `TitleBarHost` / `PeekTitleCluster` / `PeekActionStrip` / Look Up strip stay collapsed.
6. Mode row stays Fill & Sign / Edit PDF / Organize / Comment + Save + More. Active mode panel only. Autofill / My details / OCR / A- / A+ stay on `PdfFillPanel` (docked), not a floating overlay.
7. WebView2 = page canvas only. Keep `canvas-only` so HTML `#toolbar` / `#pageHud` are hidden. Keep every JS id and host message. Fill/stamp float bars stay on the canvas.
8. Tokens local to workbench (do not retoken `App.xaml`): ribbon/rail `#F5F5F5`, canvas `#E8E8E8`, text `#2C2C2C`, muted `#6E6E6E`, line `#D6D6D6`, accent `#0B5CAB`. Not teal. Not Adobe red.
9. Page centered in the remaining well. WebView default background `#E8E8E8` (already in `ApplyPdfSpaceRail` when `on`).

### B. Chrome glue (`127.0.0.1:8522`, no host)

10. Light is the default, not a class the host can forget. `html.doc-app` stays on in markup. `body` / `#viewer` / `#thumbs` must not paint `#10141c` when the class is on. Prefer making light the file default so a missing class cannot flash a black void.
11. `#toolbar` visible. `#thumbs` visible. `#pageHud` hidden (node kept). No `canvas-only` / `slim-chrome` without a host message.
12. Center the page in `#viewer` (flex/center or `margin: 0 auto` on `.page-wrap`). Canvas `#E8E8E8`. No black void on the right.
13. Do not add `#save` or Clear page. Form widgets on the PDF stay document content.
14. Do not invent WPF in Chrome. Glue is a light document page, not Home/Tools/Share.

### C. Keep from row 48 (do not reopen)

15. Peek: no Look Up / Add to Chat strip. Add to Chat = existing context menu. Summary hide/show only.
16. Light Edit (`_editMode`, not workbench): no teaching copy; equal Organize / Sign / Fill / Comment.
17. Space in workbench swallows (does not leave, does not close). Esc closes. Done leaves.

### D. Verify

18. Update `scripts/check-acrobat-chrome.ps1` in this lane: workbench asserts docked `DocAppHeader` / `DocNavBar` / `PdfSpaceRail` shown in `ApplyWorkbenchShell` / `ApplyPdfSpaceRail` when `on`; `SetRibbonFloated` not required as the workbench path; `DocToolFloat` node may remain but must not be the workbench chrome. Viewer asserts `html.doc-app` default, no `#save` / Clear page, `canvas-only` still hides `#toolbar` for the host. Keep Space swallow / `--edit` / `GetDoubleClickTime` asserts.

## PARK this wave

- Adobe brand/logo/red, Home cloud, AI Assistant, Sign In, Share-to-Adobe, print/email as chrome
- Tools card grid as a product shell
- Dual-window drag pages between PDFs (use Organize + Combine)
- Nested View > Page navigation submenu (First/Last/Go to page Shift+Ctrl+N)
- FORMAT properties pane; colorful tool switcher rail
- Hand tool; Add Image / Link / Crop / Header & Footer as new ops (FILL_ONLY)
- P7 rewrite; second exe; iLove cloud; compress / page-numbers / crop this wave
- Draw-app / add-picture (row 41 / 48)

## Hard rails (writer)

- Same `NetieSpace.exe`. PDFSpace is a mode label. Netie Space identity stays.
- Contention (row 12/45/48, one lane): `PreviewWindow.xaml`, `PreviewWindow.xaml.cs`, `netie-viewer.html`, `scripts/check-acrobat-chrome.ps1`. Do not touch `App.xaml.cs` unless Space swallow regresses (already signed).
- Keep every Click handler and viewer message id (`gotoPage`, `setZoom`, `fitPage`, `enterFillMode`, `autofillProfile`, `setDocChrome`, ...). Do not steal Fill glue / annotationLayer `--scale-factor`.
- Do not delete `#toolbar` / `#pageHud` / `#thumbs` nodes. Hide in WPF; show in Chrome glue.
- Do not retoken all of `App.xaml`. Peek glass DNA stays when `_pdfSpaceMode` is false.
- Do not scrape Adobe binaries.

## Signed brief (writer, no guessing)

**Layout when `_pdfSpaceMode`:**

```
+------------------------------------------------------------------+
| File  Edit  View  Help                         [Done] [x]        |
| Home  Tools     [filename.pdf x]                                |
+------------------------------------------------------------------+
| Save  Find | 1 / N | - 100% + Fit                                  |
| Fill & Sign | Edit PDF | Organize | Comment | Save | More         |
| (active mode panel only)                                          |
+--------+------------------------------------------+--------------+
| thumbs | WebView2 canvas only                     | Tools        |
| ~120px | #E8E8E8, page centered, white page          | Fill & Sign  |
|        | fill/stamp HUDs stay on page             | Edit PDF     |
|        |                                          | Organize ... |
+--------+------------------------------------------+--------------+
```

**Visible in `_pdfSpaceMode`:** `DocAppHeader`, `DocNavBar` (no Share), docked `PdfEditorRibbon` + active mode panel, `DocLeftNav`, `PdfSpaceRail`, Done, file tab. Tokens `#F5F5F5` / `#E8E8E8` / `#0B5CAB`.

**Hidden in `_pdfSpaceMode`:** `DocToolFloat`, `DocZoomFloat`, `DocShareBtn`, peek title/Look Up/Add to Chat, teaching (`PdfRibbonTitle`, `PdfRibbonHint`, `#banner`), HTML `#toolbar` / `#pageHud` / HTML thumbs when WPF thumbs on, print/email/Share, Hand, FORMAT, Home cloud, Tools cards.

**Must not add:** new ops, second exe, Adobe brand, nested page-nav menu, dual-window, Save/Clear on the HTML page.

**Chrome glue:** light `#toolbar` + left thumbs + centered page on `#E8E8E8`. That is how the 8522 screenshot stops looking like a dark website.

## Verified files this intake

- `PreviewWindow.xaml` -- `DocAppHeader` ~561 Collapsed; `DocNavBar` ~641; `PdfEditorRibbon` ~749; `DocToolFloat` ~1354; `PdfSpaceRail` ~1391.
- `PreviewWindow.xaml.cs` -- `ApplyPdfSpaceRail` always collapses rail ~719-722; `ApplyWorkbenchShell` collapses header/nav ~741-744; `SetRibbonFloated(on)` ~772; `PostDocChrome` `on = workbench` ~891.
- `netie-viewer.html` -- `html.doc-app` ~2; `:root` dark + `body`/`#viewer` `#10141c` ~8-40, ~116; `canvas-only` ~136; `setDocChrome` ~2651.
- `scripts/check-acrobat-chrome.ps1` -- still asserts `DocToolFloat` / `SetRibbonFloated` as workbench chrome (must flip).
- GitHub -- `Netie-AI/Space#3` OPEN only.

## Ledger

Appended PRD-001 row 49. Replace-surface + success assertion updated. No tickets. No implementation this intake.

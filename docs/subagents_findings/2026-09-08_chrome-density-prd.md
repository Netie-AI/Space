---
keywords: chrome-density, peek-strip, floating-overlay, space-swallow, summary-dock, look-up, add-to-chat, edit-in-app, _pdfSpaceMode, _editMode, liquid-glass
main_idea: SIGNED execute-now chrome density. Peek collapses Look Up/Add to Chat strip. Light Edit hides teaching, equal chips. Workbench hides stacked upper WPF chrome; floating overlay over the page. Space in workbench does not leave. Same exe, one lane. Draw-app PARK.
date: 2026-09-08
kind: prd_intake
product: Space
reuse: docs/subagents_findings/2026-09-07_acrobat-standalone-prd.md, docs/subagents_findings/2026-09-07_acrobat-chrome-spec.md, docs/subagents_findings/2026-09-07_pdf-double-space-prd.md, PRD-001 ledger 8/12/44/45/47
cite: PRD-001 row 48
---

# PRD intake - Chrome density (Mode B)

PREFLIGHT: PARTIAL. Reused acrobat-standalone-prd (row 45 WPF workbench, canvas-only), acrobat-chrome-spec, pdf-double-space-prd (row 47 item 3 Space-in-workbench leaves to peek), STATUS.md Now item 1, PRD-001 ledger 8/12/44/45/47. Founder screenshots this turn: dark glass stacked bars (PDF Editor light Edit + NETIE SPACE peek with Look Up / Add to Chat / summary / HTML Fill text toolbar). Founder is still seeing the OLD stacked chrome, not only the light Acrobat shell.

Analog: TAS-SPACE live lane (`D:\Space`). Prefer extend-live. No frozen Acrobat clone. DISTILL into original Netie glass overlay. BAN n8n / grok-bot / AGPL / GPL / leaked trees. Palantir P1 parked. Do not clone Adobe brand. Do not steal Explorer PDF UserChoice (row 13).

## Nearness (artifacts checked, no %)

Customer-facing artifact: live `src/NetieSpace/Views/PreviewWindow.xaml` plus founder screenshots this turn of the running PreviewWindow.

What a person can open today:

- Peek: dark glass `WindowStyle=None` shell. Brand + filename + Edit/Edit in app + Summary + Chat. `PeekActionStrip` still shows Look Up / Add to Chat / Select area. Summary band has dock chip (`SummaryDockButton`), float chip (`SummaryFloatChip`), collapse. HTML `#toolbar` still shows Fill text / Highlight / Search / zoom unless `html.canvas-only`.
- Light Edit (`_editMode`, not workbench): `PdfEditorRibbon` with `PdfRibbonTitle` "PDF tools", `PdfRibbonHint` "Pick a mode", Fill panel paragraph teaching. Peek strip can still stack under it.
- Edit in app (`_pdfSpaceMode`): WPF `DocAppHeader` (File menu + Home/Tools + file tab) + `DocNavBar` (Save/Find/page/zoom) + ribbon still exist as a stacked upper chrome. HTML `#toolbar` / `#pageHud` already have `html.canvas-only { display:none }`. Left `DocLeftNav` thumbs exist. Autofill / My details / OCR / A- / A+ already live on `PdfFillPanel`.
- Space in workbench: `App.xaml.cs` HandleSpacePressed `if (win.IsPdfWorkbench) await win.SetPdfWorkbenchAsync(false)` -- leaves to peek (row 47 item 3, live).
- Add to Chat on highlight: already on WebView2 `ContextMenuRequested` (`lookup` + `addChat` items). Do not invent a new chat API.

What they still cannot open:

- Peek without the Look Up / Add to Chat strip.
- Light Edit without teaching copy and with equal-size Organize / Sign / Fill / Comment only.
- Edit in app with the whole upper WPF stack gone and a floating overlay that does not cover the form.
- Space-in-workbench that stays in the app (Esc closes, Done leaves).
- Bottom-left floating zoom HUD + page-orientation chip above thumbs.

Landing: not fetched (Marketing). PRD draft, not sliced. Open GitHub: `Netie-AI/Space#3` SEC-01 only (does not own this).

## Routing (Mode B)

Maps to **NO epic** (PRD unsliced). Not Space#3. Not a closed epic.

This is a **PRD amendment** to the signed Edit-in-app replace-surface (row 45 stacked header) and the Space-in-workbench gesture (row 47 item 3). Peek / light-Edit density is chrome of the already-signed preview (rows 8/12), same contention files. Founder signed BUILD IMPROVE in this message.

**SIGNED execute-now.** Same exe. One seated writer. One lane. No second epic.

Not a second PDF product. Not P7. Not a UserChoice steal. Not Adobe brand.

## Execute-now (inspectable)

1. PEEK (not `_editMode`, not `_pdfSpaceMode`): collapse `PeekActionStrip` (Look Up, Add to Chat, Select area). Move Look Up to a small icon beside `ContinueChatButton` (Chat). Add to Chat = existing highlight / right-click clip path. Summary stays top; remove `SummaryDockButton` / `SummaryFloatChip` / "Summary back on top"; only hide/show via `SummaryToggleButton` (title) + `SummaryCollapseButton`.
2. LIGHT EDIT (`_editMode`, not workbench): hide teaching copy (`PdfRibbonTitle`, `PdfRibbonHint`, Fill panel paragraph, viewer `#banner`). Primary row = Organize / Sign / Fill / Comment same-size chips only. Save stays named `PdfSaveStampsButton` and may sit as an equal chip. Hide `PeekActionStrip`. Hide HTML `#toolbar` the same canvas-only way OR hide fill/highlight/search chips -- zoom/page move to a floating HUD.
3. EDIT IN APP (`_pdfSpaceMode`): hide entire WPF upper chrome that was `DocAppHeader` + `DocNavBar` + `PdfEditorRibbon` stacked (collapse to zero height; keep nodes). Tools + Autofill / My details / OCR / A- / A+ live on a FLOATING WPF overlay over the content well (center-ish, must not cover the form). Space key does NOT close and does NOT leave workbench (supercedes row 47 item 3). Esc still closes. Done still leaves. Bottom-left floating zoom `-` N% `+`. Left thumbs stay; optional page-orientation chip above thumbs (reuse `PdfRotate90_Click`). HTML `#toolbar` / `#pageHud` stay hidden (`canvas-only`).
4. Every top panel different by mode. Glass: restyle remaining overlay/HUD as half-transparent liquid glass locally. Do not retoken all of `App.xaml`. Peek glass DNA stays for non-workbench.

## PARK this wave

- New draw-app / stroke smoothing / add-image (row 41 FILL_ONLY). Mark / highlight already exist. Treat as maybe; do not slice.
- Do not steal PDF UserChoice.
- P7 PARK.
- Do not clone Adobe brand / Home / Tools grid / dual-window.

## Hard rails (writer)

- Same `NetieSpace.exe`. Reuse existing Click handlers and viewer message IDs (`gotoPage`, `setZoom`, `enterFillMode`, `autofillProfile`, ...).
- Contention files (row 12/45/47, one lane): `PreviewWindow.xaml`, `PreviewWindow.xaml.cs`, `netie-viewer.html`, `App.xaml.cs` (Space swallow only).
- Do not delete `#toolbar` nodes; hide them. Keep `DocAppHeader` / `DocNavBar` names; height zero in workbench.
- Do not invent a new ChatPanel API for Add to Chat.
- `SendSlideChatButton` rides `PeekActionStrip` collapse; no replacement this wave.
- Update `scripts/check-acrobat-chrome.ps1` in the same lane (today it asserts `DocNavBar` / `DocAppHeader` / `DocFileTab` presence as the workbench chrome).
- EPIC Agent must not file a second epic on those four files.

## Verified files

- `PreviewWindow.xaml` -- `SummaryToggleButton` ~300; `ContinueChatButton` ~308; `DocAppHeader` ~533; `DocNavBar` ~613; `PeekActionStrip` ~659; `LookupClipButton` ~662; `AddChatClipButton` ~664; `SummaryFloatChip` ~687; `SummaryDockButton` ~703; `PdfEditorRibbon` ~726; `PdfRibbonTitle` "PDF tools" ~731; `PdfRibbonHint` ~740; Fill teaching paragraph ~870; Autofill / My details ~876-881; `DocLeftNav` ~991.
- `PreviewWindow.xaml.cs` -- `_editMode` ~58; `_pdfSpaceMode` ~77; `SetPdfWorkbenchAsync` ~643; `ApplyWorkbenchShell` DocAppHeader/DocNavBar/PeekActionStrip ~727-745; `ContextMenuRequested` Look Up + Add to Chat ~1309; `PdfRotate90_Click` ~6556.
- `netie-viewer.html` -- `html.canvas-only #toolbar` / `#pageHud` ~136-137; `#toolbar` Fill/Highlight ~268-306; `#banner` teaching ~335; `setDocChrome` `canvasOnly` ~2648.
- `App.xaml.cs` -- HandleSpacePressed workbench leave ~267-274 (`SetPdfWorkbenchAsync(false)`). Esc still `HandleEscapePressed`.
- `scripts/check-acrobat-chrome.ps1` -- asserts DocNavBar / DocAppHeader / DocFileTab / canvas-only.
- GitHub -- `Netie-AI/Space#3` OPEN only.

## Ledger

Appended PRD-001 row 48. Success assertion + replace-surface clause updated. No tickets. No implementation this intake.

---
keywords: double-space, pdf-workbench, preview-mode, _pdfSpaceMode, HandleSpacePressed, --edit, open-with, UserChoice, GetDoubleClickTime, ql-toggle
main_idea: Founder signed Space-once peek / double-tap-Space PDF workbench in the same NetieSpace.exe. Fast double-Space today open-then-closes peek. Execute-now one writer on App.xaml.cs + PreviewWindow.xaml.cs. Open with stays --preview. Do not steal PDF defaults. P7 PARK.
date: 2026-09-07
kind: prd_intake
product: Space
reuse: docs/subagents_findings/2026-09-07_acrobat-standalone-prd.md, docs/subagents_findings/2026-09-05_open-with-prd-intake.md, PRD-001 ledger 8/12/13/44/45
cite: PRD-001 row 47
---

# PRD intake - Double-tap Space enters PDF workbench (Mode B)

PREFLIGHT: PARTIAL. Reused acrobat-standalone-prd (row 45), open-with-prd-intake (row 11/13), STATUS.md, PRD-001 ledger 8/12/44/45. Live: App.xaml.cs HandleSpacePressed / OpenPreviewAsync / TryHandleCliPreview; PreviewWindow.xaml.cs EditInAppButton_Click / _pdfSpaceMode; scripts/register-open-with.ps1 `--preview "%1"`.

Analog: TAS-SPACE live lane (`D:\Space`). Prefer extend-live. No frozen Acrobat clone. BAN n8n / grok-bot / AGPL / GPL / leaked trees. Palantir P1 parked. Do not steal Explorer PDF UserChoice (row 13).

## Nearness (artifacts checked, no %)

Customer-facing artifact: Space-key peek in Explorer (`App.xaml.cs` HandleSpacePressed) plus Edit-in-app WPF workbench (`PreviewWindow` `_pdfSpaceMode`).

What a person can open today:

- Space on a selected file opens Quick Look glass peek (`OpenPreviewAsync`).
- Second Space while peek is open closes it (`RequestCloseAsync`). Fast double-Space is open-then-close.
- `NetieSpace.exe --preview PATH` and Open with Netie Space both open peek (`TryExtractPreviewPath` has `--preview` and a bare file; no `--edit`). `register-open-with.ps1` CALL is `--preview "%1"` additive HKCU. No UserChoice write.
- PDF workbench (`_pdfSpaceMode`) only after peek, via Edit in app / Done toggle (`EditInAppButton_Click`). Space-key QL glass stays when `_pdfSpaceMode` is false (row 45).

What they still cannot open:

- Double-tap Space on a PDF that lands in the Acrobat-style workbench.
- CLI `--edit PATH` workbench.
- Space-in-workbench that leaves to peek without closing the window.

Landing: not fetched (Marketing). PRD draft, not sliced. Open GitHub: `Netie-AI/Space#3` SEC-01 only (does not own this).

## Routing (Mode B)

Maps to **NO epic** (PRD unsliced). Not Space#3. Not a closed epic.

This is a **PRD amendment** to the Space-key success assertion, on the already-signed Edit-in-app workbench (rows 8/12/45). Founder words are first-person product spec after screenshot chrome shipped, not the row-45 phrase "start building". Still **SIGNED execute-now**: same exe, same `_pdfSpaceMode` surface, entry path only. One seated writer.

Not a second PDF product. Not P7. Not a UserChoice steal.

## Execute-now (inspectable)

1. Double-tap Space (`GetDoubleClickTime` window) on a selected PDF: after load, enter `_pdfSpaceMode` (same path as Edit in app). Non-PDF: stay peek.
2. Space after peek has settled (longer than double-click time): still CLOSE peek (QL DNA).
3. Space while already in workbench: leave workbench to peek (Space = preview mode). Do not close the window.
4. CLI `--edit PATH` and `--preview --edit`: open PDF workbench. Open with registration stays additive `--preview` for all types this wave. Do not rewrite UserChoice. Optional later: PDF Open-with can pass `--edit`. Default CALL: keep `--preview` so right-click Open with stays peek.
5. Contention files (row 12/45, one lane): `App.xaml.cs` + `PreviewWindow.xaml.cs`. `netie-viewer.html` unchanged. No second epic. Do not retoken `App.xaml`.
6. P7 PARK. Do not steal PDF defaults.

## Hard rails (writer)

- Same `NetieSpace.exe`. Do not add a second Window class for this gesture.
- Do not change `scripts/register-open-with.ps1` this wave (stays `--preview`).
- Expose a public enter/leave workbench on `PreviewWindow` if App needs it; reuse `EditInAppButton_Click` logic, do not duplicate chrome.
- Typing context / Explorer F2 rename still ignore Space (`IsTypingContext`, `IsExplorerLabelEdit`).
- Esc still closes (HandleEscapePressed). Do not retarget Esc to leave-workbench unless a later row says so.
- EPIC Agent must not file a second epic on `App.xaml.cs` / `PreviewWindow.xaml.cs` / `netie-viewer.html`.

## Verified files

- `App.xaml.cs` -- HandleSpacePressed ~220: if preview open -> TryPopPreviewStack else RequestCloseAsync ~252-259. Open path ~288-307 Delay 40 then OpenPreviewAsync. TryExtractPreviewPath ~574: `--preview` + bare file. TryHandleCliPreview ~592 always OpenPreviewAsync. No `--edit`. No GetDoubleClickTime.
- `PreviewWindow.xaml.cs` -- `_pdfSpaceMode` ~77; EditInAppButton_Click ~628-654 enter/leave; ApplyPdfSpaceRail / ApplyWorkbenchShell ~668+. RequestCloseAsync ~8283 sets `_pdfSpaceMode = false` then closes.
- `scripts/register-open-with.ps1` -- `$command` `--preview "%1"` ~22; comment "does not change UserChoice" ~31.
- GitHub -- `Netie-AI/Space#3` OPEN only.

## Ledger

Appended PRD-001 row 47. Success assertion updated. No tickets. No implementation this intake.

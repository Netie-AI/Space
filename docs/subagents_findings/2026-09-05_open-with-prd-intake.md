---
keywords: open-with, file-association, rebuild, publish, ep-form, pdfspace, default-programs, binaries, prd-amendment
main_idea: Rebuild/publish is operational. Additive Explorer Open with vs set-as-default is a founder PRD amendment. All-binaries editors is a new product (P8 preview already covers csv/excel). EP FORM fill/sign/write is already signed row 8 / STATUS Now.
date: 2026-09-05
kind: prd_intake
product: Space
---

# PRD intake - Open with + rebuild + EP FORM (Mode B)

PREFLIGHT: PARTIAL. Reused `2026-09-04_pdfspace-prd-intake.md` and `2026-09-04_acrobat-pdfspace-engines.md`. No prior Open-with / Default Programs finding.

Analog: TAS-SPACE live lane (`D:\Space`). Prefer extend-live (`scripts/register-startup.ps1` pattern). BAN/SKIP/PARK not triggered by Open-with. Do not redesign layout DNA. Do not hijack every Windows default association.

## Nearness (artifacts checked, no %)

Customer-facing artifact: `D:\Space\dist\NetieSpace\NetieSpace.exe` (194301769 bytes, mtime 2026-08-28 18:28). Stale vs 2026-09-04 tree (Edit in app, FillProfileStore, pdf sidecars). A person opening that exe cannot yet judge the PDFSpace rail.

Explorer right-click Open with: argv parser exists; Windows registration does not.

EP FORM.pdf: `C:\Users\OoiJianHong\Downloads\Jh Document\EP FORM.pdf` exists (4038542 bytes, mtime 2026-09-04). Fill/Sign/Edit-in-app exist in source, not in the 2026-08-28 dist.

Landing: not fetched (Marketing owns building it). PRD still draft, not sliced. No open epics.

## Confirm / correct (live tree)

- PRD status draft, not sliced. Success assertion is Space-key preview + OpenVault keys, not Acrobat Pro, not Default Programs. Confirmed.
- Ledger row 2 already: open every code/text type. Confirmed.
- Ledger row 7 is missing in the PRD file (jumps 6 -> 8). 2026-09-04 intake claimed row 7 = AI fill / personal KB amendment. This run did not insert it (ledger is append-only).
- Ledger row 8 already signed. Confirmed. Source now has `EditInAppButton`, `FillProfileStore`, `PdfDetectBridge`, `PdfFormBridge`.
- STATUS Now: PDFSpace workbench. Human next matches EP FORM. Confirmed.
- `App.xaml.cs` TryExtractPreviewPath: `--preview PATH` and bare existing file at args[0]. Confirmed.
- No file-association / Default Programs / OpenWithProgids / context-menu registration. Confirmed (grep empty). `PreviewHandlerService` reads other apps' IPreviewHandler CLSID; it does not register Space.
- `install.bat` + `scripts/publish.ps1` publish + `RegisterStartup` only (`scripts/register-startup.ps1` HKCU Run). Confirmed.
- Preview routes pdf/docx/xlsx/csv/pptx/images/text/video via PreviewWindow + ContentParserService + OfficePreviewService. Confirmed. `.exe` / `.dll` / `.msi` -> ShellInfo metadata, not an editor.

## Ledger rows added (PRD-001)

Appended rows 9-12. Rows 1-8 unchanged (row 7 still absent).

| # | Maps to | Call |
|---|---|---|
| 9 | no epic (operational) | Rebuild/publish now. Dist is 2026-08-28. |
| 10 | split P8/row2 vs amendment | csv/excel already preview. All-binaries editors = new product. |
| 11 | no epic | PRD amendment: Open with. Additive vs default is founder. |
| 12 | execute row 8 | EP FORM fill/sign/write. Do not second-own PreviewWindow files. |

## Execute now vs NEEDS-YOU

May run NOW (no new epic, no GitHub tickets):

1. Rebuild: `install.bat` or `scripts\publish.ps1` (stops running exe). Do not edit those files this pass.
2. Sidecars for fill: `cargo build --release` in `native/netie-pdf-detect`; `go build` in `native/netie-pdf-form`. Not wired in install.bat. Do not patch install.bat until row 11 (contention).
3. EP FORM: continue signed row 8 on live tree. Test `NetieSpace.exe --preview "C:\Users\OoiJianHong\Downloads\Jh Document\EP FORM.pdf"` then Edit in app -> Fill / write in spaces / Sign / Save / reopen. Files if a gap remains: `PreviewWindow.xaml`, `PreviewWindow.xaml.cs`, `netie-viewer.html`, `FillProfileStore.cs`, `PdfFormBridge.cs`, `PdfDetectBridge.cs`. One lane only.

Needs founder (do not implement, do not slice):

1. Additive HKCU Open with / `HKCU\Software\Classes\Applications\NetieSpace.exe\SupportedTypes` vs set-as-default UserChoice for all extensions. Additive matches the words "Open with". Default-for-* would steal Word/Excel/PDF.
2. Whether "all binaries" becomes first-class editors (hex/PE) vs keep ShellInfo + P8.
3. Slice PRD-001 (still draft). Open-with has no authorizing clause until (1) is signed.

If (1) is signed additive-only, smallest later files: sibling of `register-startup.ps1` plus a call from `publish.ps1` / `install.bat`. Keep `App.xaml.cs` argv as-is. Do not pair that packaging edit with a second PreviewWindow epic.

## Refuse-and-why (Mode A)

Not slicing. PRD draft. Success assertion is not Open-with. Two future epics must not both own `App.xaml.cs` / `scripts/publish.ps1` / `install.bat`, and must not both own PreviewWindow / netie-viewer.html.

---
keywords: widget-align, scale-factor, annotation-layer, fill-profile, memory, sec-01, freeide, run-button, prd-amendment
main_idea: Widget misalignment is an execute-now defect on signed AcroForm fill. Unbounded AI memory harvest is a PRD amendment plus SEC-01. Code Run/npm/python is the wrong product (FreeIDE). Do not implement.
date: 2026-09-05
kind: prd_intake
product: Space
---

# PRD intake - widget align / memory / Run (Mode B)

PREFLIGHT: HIT. Reused `2026-09-04_pdfspace-prd-intake.md` and `2026-09-05_open-with-prd-intake.md`.

Analog: TAS-SPACE live lane (`D:\Space`). Prefer extend-live. Do not unpark P7. Do not vendor analog IDEs (lane 19 SKIP). SKIP second memory product (lanes 12/17/18, Cortex `/api/memory`). Do not unpark Palantir P1.

## Nearness (artifacts checked, no %)

Customer-facing artifact: PDF Fill on `src/NetieSpace/Assets/pdfjs/netie-viewer.html` AnnotationLayer (EP FORM.pdf pages 2-3).

What a person can do today:

- Edit in app, type in native AcroForm widgets, Autofill from local DPAPI (FullName/Email/Phone/Address), Sign, Save.
- Open with Netie Space (row 13).
- Preview code/text via `ContentParserService.WrapCodeHtml` (highlight.js). No Run.

What they cannot do:

- Widgets that sit on the printed labels at zoom (~177-190%). `--scale-factor` is hardcoded to 1.
- Type-aware fill (number vs June vs Jun) from field type. Autofill is substring match; unmatched boxes get FullName.
- A Settings page of "me". My details is four PromptDialogs. Gear opens `ApiSetupWindow` (keys).
- AI memory that replicates the next form. Cortex `/api/memory` is not wired. SEC-01 (`Netie-AI/Space#3`) still owns leave-machine.
- Press Run / npm/python stack runner.

Landing: not fetched (Marketing owns building it). PRD still draft, not sliced. No open epics. Ledger row 7 still missing (append-only; not inserted).

## Confirm / correct (live tree)

- `--scale-factor: 1` on `.annotationLayer` in `netie-viewer.html` line ~99. `renderPageAt` sets canvas CSS from `cssScale` (~0.25-4). `renderAnnotLayer` passes `cssVp` but does not set `--scale-factor` on the div.
- `FillProfileStore.Profile` = FullName, Email, Phone, Address, DateToday (`dd/MM/yyyy` computed). Save writes four strings. DPAPI CurrentUser. No upload.
- `PdfFillProfile_Click` = PromptDialog "My details". `SetupHelpButton_Click` = `ApiSetupWindow`.
- `profileValueFor` matches email/phone/address/date substrings; else name.
- No Run button. No npm/python project runner. `BackgroundRemovalService` python is rembg, not a code runner.
- Contention files if PDF work continues: `netie-viewer.html`, `PreviewWindow.xaml.cs`, `FillProfileStore.cs`.

## Ledger rows added (PRD-001)

Appended rows 14-17. Rows 1-13 unchanged (row 7 still absent).

| # | Maps to | Call |
|---|---|---|
| 14 | signed 8/12/13 | Execute now: widget align in netie-viewer.html |
| 15 | split | Field ID + type-from-PDF = execute. AI-learn format = row 16. |
| 16 | split row 8 vs amendment | Bounded FillProfileStore + local view = execute. Unbounded AI harvest = amendment + Space#3. |
| 17 | no epic | PRD amendment / wrong product: FreeIDE. Do not clone an IDE. |

## Execute now (no new epic, no GitHub tickets)

One lane. PDF usability first.

1. `src/NetieSpace/Assets/pdfjs/netie-viewer.html` -- set `--scale-factor` to live cssScale; size annot div to canvas CSS; type-aware Autofill from AcroForm FT/maxLen/date format; stop dumping FullName into unmatched fields.
2. `src/NetieSpace/Services/FillProfileStore.cs` -- bounded extra local fields only (DOB, nationality, optional date-format pref). Still DPAPI, no upload.
3. `src/NetieSpace/Views/PreviewWindow.xaml.cs` (+ `.xaml` if the chip must open one window) -- replace the four PromptDialogs with one local "My details" view of that store. Do not put PII into `ApiSetupWindow`.

Do not touch Cortex `/api/memory`. Do not add a Run button. Do not edit `install.bat` / Open-with scripts this pass. Keep P7 parked.

## NEEDS-YOU (do not implement)

1. Unbounded harvest + "AI has memory of me / replicate next form": consume Cortex `/api/memory` vs stay local FillProfileStore only vs refuse a third KB. Any AI fill is also SEC-01 (`Netie-AI/Space#3`) before it can leave the machine.
2. Code Run / npm-then-build / python-press-Run: keep in FreeIDE vs rewrite the Space press release into an IDE. TAS lane 19 SKIP. Default: wrong product.

## Refuse-and-why (Mode A)

Not slicing. PRD draft. Success assertion is Space-key preview + OpenVault keys, not an IDE, not a personal-KB product. Two future epics must not both own `netie-viewer.html` / `PreviewWindow.xaml.cs` / `FillProfileStore.cs`.

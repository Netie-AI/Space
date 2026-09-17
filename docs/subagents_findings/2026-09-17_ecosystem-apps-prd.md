---
keywords: ecosystem, netie-pdf, netie-image, netie-video, --app, aumid, start-menu, taskbar-pin, single-instance-mutex, tool-catalog, ilovepdf, iloveimg, video-toolkit, agpl, pymupdf, license-gate, availability-probe, teaching-copy, dead-controls, open-in-app, fill-only-lifted, row-50
main_idea: SIGNED ecosystem (ledger 50). Netie PDF / Netie Image / Netie Video are independent app identities (own AUMID, Start Menu entry, taskbar icon, tool home) on one runtime `NetieSpace.exe --app`, one process each, no mutex / hook / tray. Local-only parity with the 91-row catalog (iLovePDF 37, iLoveIMG 16, 123apps + Clideo video 38) in `docs/research/findings/2026-09-17_ecosystem_tool_catalog.md`. No how-to copy or dead controls anywhere. Premise fix - Watermark / Protect / Unlock / PDF-to-image run on AGPL PyMuPDF and default publish ships no Python - so a tool contract + license / availability gate comes first. Slice S50-E1..E7; E1 + E2 in flight.
date: 2026-09-17
kind: prd_intake
product: Space (Netie app family - Space, PDF, Image, Video)
reuse: docs/research/findings/2026-09-17_ecosystem_tool_catalog.md, docs/research/findings/2026-09-06_ilovepdf_distill.md, docs/research/findings/2026-09-06_iloveimg_distill.md, docs/subagents_findings/2026-09-06_ilove-parity-prd.md, docs/subagents_findings/2026-09-07_chatfill-split-apps-prd.md, docs/subagents_findings/2026-09-08_chrome-density-prd.md, docs/subagents_findings/2026-09-09_acrobat-ui-rebuild-prd.md, PRD-001 ledger 29/30/35/36/41/45/48/49
cite: https://netie.ai/space/
---

# PRD intake - Netie app ecosystem, local tool catalog, no how-to copy (Mode B + Mode A)

PREFLIGHT: PARTIAL. HIT on Space findings (ilove-parity-prd, chatfill-split-apps-prd, chrome-density-prd, acrobat-ui-rebuild-prd), both 2026-09-06 iLove distill tables, and the parallel lane's `2026-09-17_ecosystem_tool_catalog.md`, which landed in INDEX during this intake and is now the catalog source for the slice. KB `kb.py search`: R-0011, R-0001, R-0007, R-0005 apply. MISS on app identity / AUMID / engine license gate - new ground, verified from code this intake.

Analog: TAS-SPACE live lane (`D:\Space`). No frozen analog clone in the TAS map owns PDF / image / video tool homes. Prefer extend-live (`PdfEditService`, `ConversionService`, `PdfMergeWindow`, `PdfOrganizeWindow`, `ImageEditService`, `BackgroundRemovalService`, `FfmpegService`, the workbench, the NLE). DISTILL the public iLovePDF / iLoveIMG tool lists into original Netie WPF; do not vendor, do not scrape binaries. BAN n8n / grok-bot / AGPL / GPL copy / leaked trees. Palantir P1 parked. No UI token or layout DNA redesign: app homes reuse the row 49 light workbench tokens.

## Founder words (verbatim, relayed by orchestrator)

> Scale and continue I want it to be strong ecosystem like got indenpendent app all adobe then can edit vidoe edit pdf edit image. like ilove pdf go scrap all then confttinue build improve ui, non make sense part can remove, words also fdont random instructiosn eveyrwhere

Translation check: (1) ecosystem of independent apps for PDF, image, video - correct. (2) scrape every iLovePDF tool and keep building - correct for PDF; iLoveIMG is the same vendor's image catalog, already distilled (row 30); no video site is founder-named, so the parallel lane's 123apps + Clideo union (38 rows) is the working source, not a founder call. (3) improve UI, remove nonsense parts and instruction text - correct; refined so R-0011 failure / unavailable / cloud-disclosure lines are not treated as instructions.

## Nearness (artifacts checked, no %)

| Surface | Artifact checked | What is true |
|---|---|---|
| Landing | https://netie.ai/space/ and https://netie.ai | Waitlist; "No installer yet" links /space/download/; home says there is no .exe on the site. Names no Netie PDF / Image / Video app. Claims "Ask Cortex only if you want to" while `AiService` calls providers directly (TAS-SPACE section 4, Space#3 OPEN) - Marketing flag under NETIE rule 1. Building the landing is Marketing. |
| App identity | `scripts/register-open-with.ps1` 73-83; `installer/NetieSpace.iss` [Icons]; grep AppUserModel over `src`, `scripts`, `installer` | One shortcut `Netie Space.lnk`. Zero AppUserModelID. No `src/NetieSpace/Apps/`. |
| Launch | `App.xaml.cs` 50-83, 634-641 | Mutex `Global\NetieSpace.SingleInstance.v1` is the first statement of OnStartup; a second process forwards `--preview` / `--edit` to the live instance and exits. Argv knows `--preview`, `--edit`, `--stamp-test`; no `--app`. A second app process cannot exist today. |
| PDF tools | `PreviewWindow.xaml` 589-616 and 824-840; `PdfEditService.cs`; `ConversionService.cs`; `tools/convert_engine.py` registered actions 3359-3601 | Tools are reachable only inside the peek / workbench window (File: Combine, Export, Protect; More: Edit text / OCR, Compress (IsEnabled=False), Export, Watermark, Protect, Unlock, Scan and OCR, Cert sign) plus the Merge and Organize dialogs. Catalog (2026-09-17) PDF 37 rows, code exists yes 12 / partial 12 / no 13; verdicts LOCAL 22, LOCAL-PARTIAL 9, NEEDS-ENGINE 2, CLOUD-ONLY 1, AI-EGRESS 3. "Exists" is service code, not a reachable tool: no app home exists. |
| PDF engines | `convert_engine.py` 2108 `require_pymupdf`; 3125 watermark; 3174 protect; 3208 unlock; 3037 pdf_to_images; 1346 PDF-to-Word; 552 PDF-to-Markdown; 2713 HTML/MD-to-PDF Story | Watermark, Protect, Unlock and PDF-to-image run on PyMuPDF (AGPL). PDF-to-Word tries PyMuPDF first. Repo `.venv` holds pymupdf 1.28.0 + pymupdf4llm; `publish.ps1 -IncludeVenv` copies `.venv` into dist. OCR is ocrmypdf + Tesseract. |
| Customer package | `scripts/publish.ps1` 165-172; `ConversionService.cs` 176-225 | Default publish ships no Python. ResolvePython looks for `.venv` at the repo root or beside the exe. From `dist\NetieSpace` on a customer PC no `convert_engine.py` tool runs (to-PDF, export-as, watermark, protect, unlock, OCR). "Runs on this machine" holds on the founder laptop only. |
| Image tools | `ImageEditService.cs` (Compress, Rotate90Cw, CompressJpeg); `BackgroundRemovalService.cs` 55-80; `PreviewWindow.xaml.cs` 5372 | Compress and Remove BG buttons live in peek. rembg is not installed here; the click runs `pip install rembg` with a status line and no confirm. Rotate90Cw exists in the service (uncommitted +40) with no image rotate button found in XAML. Catalog Image 16 rows, code exists yes 1 / partial 7 / no 8; verdicts LOCAL 10, LOCAL-PARTIAL 4, NEEDS-ENGINE 2. |
| Video tools | `FfmpegService.cs` (Trim, DeleteMiddle, Merge, ExportKeptSegments, ExportTimeline, BurnSubtitlesIfNeeded, ApplyExportPreset); `ffmpeg -version` | Reachable only through peek Edit (NLE). System ffmpeg 8.1.2 full_build and bundled `tools/ffmpeg` 8.1.2 essentials_build are both `--enable-gpl --enable-version3` (GPLv3, invoked out-of-process). The bundled essentials build has the filters the catalog relies on (vidstabdetect / vidstabtransform, delogo, subtitles, ass, rubberband, reverse, loop). Catalog Video 38 rows, code exists yes 7 / partial 11 / no 20; verdicts LOCAL 36, NEEDS-ENGINE 1, AI-EGRESS 1. |
| UI copy | `PreviewWindow.xaml`, `PreviewWindow.xaml.cs`, `netie-viewer.html`, `md-editor.html`, `Views/*.xaml` | Candidates, not all teaching: 100 ToolTips and 9 sentence-length TextBlocks in PreviewWindow.xaml (for example "Pick a mode - tools appear below", "Move playhead -> Split -> select clip -> Delete"); 108 status strings of 6+ words in PreviewWindow.xaml.cs (for example "PDF preview - press Edit for tools.", "Select a clip on the timeline first (click it), then Delete."); viewer `#banner` "Drag signature onto the page - release to place, then drag the box"; md-editor `#hint` "Offline Marked (MIT) - chrome hides idle"; ApiSetupWindow 6; one each in Merge, Organize, VideoOrder, Signature, FillProfile, Login, PasswordVault, StartupHud, ToastHud. Quotes transliterated to ASCII. |
| Dead / duplicate controls | `md-editor.html` 105 and 215-219; grep `mdAsk` in `PreviewWindow.xaml.cs` (0 hits); `netie-viewer.html` 307; `PreviewWindow.xaml` 827 | md-editor Ask posts `mdAsk`, the host has no handler, and the hint then says the wire waits. Viewer `stampConfirmBtn` is a hidden "Legacy - auto-kept on drop" button. The Compress menu item is permanently disabled. |

A customer today can: nothing from the landing (no installer). On this laptop: Space peek, double-tap PDF workbench, about ten PDF operations inside that one window, image compress, video cuts in the peek NLE.

A customer today cannot: open Netie PDF / Image / Video from Start or the taskbar; see a tool home; run any Python-engine tool from a published package; compress a PDF, add page numbers, crop, repair, compare, HTML-to-PDF; resize / crop / watermark / upscale / blur an image; convert or compress a video as a tool.

## Routing (Mode B)

Maps to NO epic (PRD unsliced; the only open issue is `Netie-AI/Space#3` SEC-01, which does not own this). Not a closed epic. Result: PRD amendment. The founder typed the words in session; rows 45 / 47 / 48 / 49 were signed on the same kind of words. **SIGNED.**

One PRD, not a new one: the document law is one PRD per buyer-visible slice; the three apps share one front door, one tool contract and one success assertion; rows 8-49 (the PDF workbench history) already live in Space PRD-001. Markdown keeps its own PRD (different buyer, own ledger).

Supersedes / unparks: rows 29 + 30 PARK (unlock met - founder named the catalog, Solution now reads as a local toolkit; cloud upload stays refused). Row 35 press-release NEEDS-YOU closed; row 35 no-suite-packing kept (separate app windows, nothing packed into peek). Rows 36 / 41 FILL_ONLY lifted for the three app tool homes only. Out-of-scope "second PDF product or second exe" replaced. Out-of-scope "Tools card home" narrowed to inside the workbench.

## Calls on the orchestrator recommendation

| # | Call | Why |
|---|---|---|
| A | ACCEPTED, refined | `--app` branches before the mutex (`App.xaml.cs` 52). App processes run no mutex, keyboard hook, tray, or key-sync / login flow. AUMIDs are exe-agnostic (for example `Netie.Pdf`) so the parked exe split keeps user pins. Display names are working names (surface tier). Pin-relaunch is in the acceptance; trap: WScript.Shell shortcuts carry no AppUserModelID, so pins would relaunch without `--app`. Quick Look gets ONE Open in Netie PDF / Image / Video entry that replaces Edit in app / Edit, not a second button. The workbench and NLE stay the apps' editors; S50-E6 regroups them under the app taskbar icon. |
| B | ACCEPTED, premise corrected | "No AGPL dependency" contradicts shipped Watermark / Protect / Unlock / PDF-to-image (PyMuPDF). "Every tool runs on this machine" is laptop-only (no Python in the default dist). So the tool contract + license / availability gate (S50-E2) comes before the tool lanes. Engine installs become explicit confirmed downloads (Remove BG auto-pips today). PDF/A via Ghostscript shows an unavailable reason until a non-AGPL engine is named. A Redact that only draws a box over extractable text is refused. FILL_ONLY is lifted for the three app tool homes only; the row 49 workbench ribbon PARK list stays. Catalog source is `2026-09-17_ecosystem_tool_catalog.md` (91 rows); its NEEDS-ENGINE five (PDF/A, PDF/A validate, upscale, remove background, auto-subtitles) show unavailable reasons until an engine is installed by explicit confirm. Fix PyMuPDF once at `require_pymupdf()` (R-0004), not per action. |
| C | ACCEPTED, refined | Remove how-to sentences, engine credits and dead or duplicate controls on every surface. Tooltips name the control (plus shortcut). R-0011 result / failure / unavailable-reason lines and the row 40 cloud-disclosure chip STAY. No retoken, no layout DNA change; row 48 density and row 49 docked chrome stay. |
| D | ACCEPTED, refined | Exclusive owner per file (below). `App.xaml.cs` carries its own tray / toast copy cleanup inside S50-E1 so S50-E6 never touches it. `scripts/publish.ps1` belongs to S50-E2; S50-E1 edits only `register-open-with.ps1`, which publish already calls (`publish.ps1` 206). `PdfMergeWindow` / `PdfOrganizeWindow` copy goes to S50-E3, `VideoOrderWindow` to S50-E5. `md-editor.html` is S50-E6 only while no Markdown lane runs. |
| E | REFUSED for this slice | Rows 45-49 were one lane on one file set. This ask needs parallel writers, a contract and a license gate: filed epics, tickets and R-0003 verification. That is also what lets the Ticket Runner seat lanes in parallel ("scale"). Execute-now without tickets would bypass WIP and verification. |

## Epic bodies (for the EPIC Agent to file; not filed here)

Appetite 2 weeks each. Repo Netie-AI/Space for all. `EPIC-###` numbers are assigned on filing. Stable keys S50-E1..E7 match PRD-001 section 5.

### S50-E1 - Netie PDF, Netie Image and Netie Video open from Start as their own apps

- Tier: FOUNDATION (visible). The irreversible part is the AUMID strings, ProgID names, shortcut names and the `--app` argv, which get written into HKCU, the Start Menu and user pins.
- Repo: Netie-AI/Space. Contract impact: additive (`--app pdf`, `--app image`, `--app video`, optional FILE; `--preview` / `--edit` unchanged for the AirGPT plugin manifest). Depends on: none. Precondition: Sept 9 WIP checkpoint commit.
- Acceptance: WHEN Start is opened THE SYSTEM SHALL list Netie PDF, Netie Image and Netie Video. WHEN one is launched, with or without Netie Space in the tray, THE SYSTEM SHALL open it as a separate process with its own taskbar button and icon, and Space-key peek SHALL keep working with no second hook. WHEN its taskbar button is pinned, the app closed and the pin clicked THE SYSTEM SHALL relaunch the same app. WHEN `NetieSpace.exe --app pdf FILE` runs THE SYSTEM SHALL open Netie PDF with FILE selected. WHEN the apps are unregistered THE SYSTEM SHALL remove the three shortcuts and its ProgID entries and SHALL NOT have changed any UserChoice.
- Owns: `src/NetieSpace/App.xaml.cs` (branch before the mutex; app processes run no mutex, GlobalKeyboardHook, tray or key-sync / login flow; its own tray and toast copy), `src/NetieSpace/NetieSpace.csproj` (icon Content items), `scripts/register-open-with.ps1`, `installer/NetieSpace.iss`, `src/NetieSpace/Assets/apps/**`, `src/NetieSpace/Apps/Shell/**` (home window; the tool grid binds to the S50-E2 registry as the last ticket, after that registry lands).
- Trap: WScript.Shell CreateShortcut cannot write System.AppUserModel.ID.

### S50-E2 - Every tool reports honestly whether it can run on this PC, and no AGPL engine ships

- Tier: FOUNDATION (the tool contract every runner binds to) + BOUNDARY (license and install refusal). Invisible; paired with S50-E1.
- Repo: Netie-AI/Space. Contract impact: none (in-process library contract, no wire). Depends on: none.
- Acceptance: WHEN the catalog loads from the published package on a clean profile THE SYSTEM SHALL mark each tool available or unavailable with a named reason (engine missing, license not cleared for shipping, needs Space#3 disclose). WHEN an engine is missing THE SYSTEM SHALL NOT install or download it unless the person confirms a prompt naming source and size. WHEN a shipping package would contain PyMuPDF, pymupdf4llm, Ghostscript or OnlyOffice THE SYSTEM SHALL fail the build naming the package. The check SHALL fail against a planted AGPL wheel (R-0007) and pass on a clean dist (R-0005).
- Owns: `src/NetieSpace/Apps/Tools/**` (entry: id, app, engine, license, probe, runner), `scripts/publish.ps1` (shipping-build gate), new `scripts/check-tool-engines.ps1`, `docs/decisions/DR-0002` (proposed: engine runtime and license policy - C# first; Python only as a shipped runtime with non-AGPL wheels, otherwise unavailable).
- Seeds: the 91 rows (ids, typed options, verdicts, engine candidates) in `docs/research/findings/2026-09-17_ecosystem_tool_catalog.md`. Records FFmpeg as GPLv3 out-of-process (license notice ships with the package).

### S50-E3 - Netie PDF runs the local iLovePDF-class tools

- Tier: CAPABILITY (visible). Contract impact: none. Depends on: S50-E1, S50-E2.
- Acceptance: WHEN a person picks a Netie PDF home tool (merge, split, compress, organize, rotate, page numbers, crop, watermark, protect, unlock, repair, PDF to JPG, JPG to PDF, Office to PDF, PDF to Word / Excel / PowerPoint, HTML to PDF, OCR, sign, compare, PDF to Markdown; edit and fill open the existing workbench) on a real file THE SYSTEM SHALL write a local result with no upload, and that result SHALL open in peek. Watermark, Protect, Unlock and PDF to JPG SHALL run without PyMuPDF. Tools that cannot run SHALL show the reason. THE SYSTEM SHALL NOT offer a Redact that leaves text extractable.
- Owns: `src/NetieSpace/Apps/Pdf/**`, `tools/convert_engine.py`, `Services/ConversionService.cs`, `Services/PdfEditService.cs`, `Services/PdfMergeService.cs`, `Views/PdfMergeWindow.*`, `Views/PdfOrganizeWindow.*` (including their copy).
- Scope: the catalog's 37 PDF rows with verdict LOCAL or LOCAL-PARTIAL. The PyMuPDF fix lands once at `require_pymupdf()` in `convert_engine.py` (eight write paths share it), not per action.
- Extend-live: PdfEditService Merge, MergeOrdered, SplitAllPages, ExtractPages, RotatePages, ReversePages, ComposePages, InsertImageAsPage, CreateBlankPdf; PdfCmsSigner; `--edit PATH` for the workbench. Candidate non-AGPL engines already present: PdfSharpCore (MIT), PdfPig, pikepdf (MPL-2.0), pdf.js in WebView2, WebView2 print-to-PDF, Office COM where installed (probed).
- Out: cloud, remote e-Sign, AI summarize / translate (Space#3), PDF/A via Ghostscript, create form fields (row 28 PARK), workbench add image / shape / freehand.

### S50-E4 - Netie Image runs the local iLoveIMG-class tools

- Tier: CAPABILITY (visible). Contract impact: none. Depends on: S50-E1, S50-E2. Scope: the catalog's 16 image rows.
- Acceptance: WHEN a person picks compress, resize, crop, convert to or from JPG (PNG, GIF, BMP, TIFF; WebP where a codec exists), rotate, watermark, photo editor, remove background, upscale, blur face or HTML to image on a real image THE SYSTEM SHALL write a local result. Engines not present (rembg, upscale or face models) SHALL show unavailable with the reason and an explicit confirm-to-download naming source and size; no pip install on click.
- Owns: `src/NetieSpace/Apps/Image/**`, `Services/ImageEditService.cs`, `Services/BackgroundRemovalService.cs`, `tools/remove_bg.py`. Must not edit `tools/convert_engine.py` (S50-E3 owns it; serialize a ticket if needed).

### S50-E5 - Netie Video runs local video tools and opens the timeline editor

- Tier: CAPABILITY (visible). Contract impact: none. Depends on: S50-E1, S50-E2. Scope: the catalog's 38 video rows (123apps + Clideo union; no video site is founder-named).
- Acceptance: WHEN a person picks trim, cut middle, merge, compress, convert, resize (Match / 1080p / 4K), extract audio, video to GIF or burn subtitles on a real video THE SYSTEM SHALL export locally with visible progress. WHEN Edit video is picked THE SYSTEM SHALL open that file in the existing timeline editor.
- Owns: `src/NetieSpace/Apps/Video/**`, `Services/FfmpegService.cs`, `Services/FfmpegExportGraph.cs`, `Views/VideoOrderWindow.*`.

### S50-E6 - No how-to text or dead controls anywhere; peek opens the file in the right app

- Tier: SURFACE (visible). Contract impact: none. Depends on: S50-E1 for the Open-in-app and taskbar-grouping tickets only; copy tickets are unblocked.
- Acceptance: WHEN any Netie Space surface shows (peek, light Edit, workbench, NLE, Chrome glue 8522, md-editor, settings dialogs) THE SYSTEM SHALL NOT show how-to sentences, engine credits or controls that do nothing (md-editor Ask hidden until the host handles `mdAsk`; the viewer legacy Keep stamp removed; the disabled Compress item gone or live through Netie PDF). Tooltips name the control. Result, failure, unavailable-reason and cloud-disclosure lines stay. WHEN peek shows a PDF, image or video THE SYSTEM SHALL offer one Open in Netie PDF / Image / Video entry in place of Edit in app / Edit. WHEN the PDF workbench or the video timeline editor is showing THE SYSTEM SHALL show it under the Netie PDF / Netie Video taskbar icon.
- Owns (one lane): `Views/PreviewWindow.xaml`, `Views/PreviewWindow.xaml.cs`, `Assets/pdfjs/netie-viewer.html`, `Assets/markdown/md-editor.html` (Serves: Markdown PRD-001; no concurrent Markdown lane), `scripts/check-acrobat-chrome.ps1`, `Views/ApiSetupWindow.*`, `FillProfileWindow.*`, `LoginWindow.*`, `PasswordVaultWindow.*`, `SignatureDialog.*`, `StartupHud.*`, `ToastHud.*`, `GlassChoiceDialog.*`, `PromptDialog.*`.
- Keep: row 48 density, row 49 docked chrome, Space swallow. No retoken.

### S50-E7 - Founder walks all three apps on real files

- Tier: DEMO (visible). Contract impact: none. Depends on: S50-E3, S50-E4, S50-E5, S50-E6.
- HUMAN_TEST_GATES (founder performs; agent ticks in the same turn): HT1 Start, each app, pin, close, relaunch. HT2 three PDF tools on real files plus one unavailable reason. HT3 image compress and crop, video trim and convert. HT4 peek Open in Netie PDF on `EP FORM.pdf`. Output: a claim list traceable to passing gates, handed to Marketing (the landing still says no installer).

## WIP

In flight: S50-E1 (visible) + S50-E2. Queued in order: S50-E3 + S50-E4, then S50-E5 + S50-E6, then S50-E7. Pulling S50-E6 earlier is a founder reprioritization of the queue, not a new slice.

## Contended files (resolved by exclusive owner; no DR needed)

`App.xaml.cs` S50-E1 only. `NetieSpace.csproj` S50-E1 only (later NuGet adds are serialized tickets). `register-open-with.ps1`, `NetieSpace.iss` S50-E1. `publish.ps1` S50-E2. `convert_engine.py`, `ConversionService.cs`, `PdfEditService.cs` S50-E3. `ImageEditService.cs`, `BackgroundRemovalService.cs` S50-E4. `FfmpegService.cs` S50-E5. `PreviewWindow.xaml`, `PreviewWindow.xaml.cs`, `netie-viewer.html`, `md-editor.html`, `check-acrobat-chrome.ps1` S50-E6.

Precondition: `git diff --stat` shows uncommitted Sept 9 WIP on `App.xaml.cs` (+154), `PreviewWindow.xaml` (+724), `PreviewWindow.xaml.cs` (+883), `netie-viewer.html` (+824), `ImageEditService.cs` (+40). Checkpoint-commit it before S50-E1 or S50-E6 seats a writer.

## Stays PARK / BAN / gated

Cloud upload and processing; remote e-Sign; workflows, batch cloud API, mobile scan. P7 rewrite. Separate exes (unlock: shared class-library split signed, AUMIDs kept). Adobe brand, Home cloud, AI Assistant, Sign In, Share-to-Adobe, dual-window. AGPL engines in the shipping build (PyMuPDF, pymupdf4llm, Ghostscript, OnlyOffice) - default is re-engine; buying a commercial PyMuPDF license would be a founder spend call and is not assumed. AI summarize / translate / chat egress behind Space#3 disclose-first. PPTX / Word / Excel / CSV editor apps. Create AcroForm fields (row 28). Workbench add image / shape / freehand and the row 49 ribbon PARK list (unlock: founder names workbench edit ops after S50-E6). Palantir P1. n8n, grok-bot, GPL copy, leaked trees.

## Flags for the orchestrator (not done here)

- `D:\Space\STATUS.md` Now 1 ("FILL_ONLY") and the Next "Parked" line (suite packing 35, iLove cloud 29-30, compress / page-numbers / crop 41) are stale after row 50. `PARKING_LOT.md` P7 entries "Full iLovePDF / iLoveIMG cloud parity" and "Full image / PPTX / Word / Excel clone editors" need the row 50 unlock text. Not edited here, to avoid a second writer during the checkpoint commit.
- `D:\Netie\Software Blueprint\Markdown\` is untracked in Netie-AI/Netie (R-0008). Its ledger needs the reciprocal Serves line once it is committed.
- TAS-SPACE needs PLANNED rows for app identities and the tool contract when S50-E1 / S50-E2 land.
- The landing claim "Ask Cortex only if you want to" is not traceable to a passing gate while Space#3 is OPEN.

## Ledger

Appended Space PRD-001 row 50. Rows 1-49 unchanged; row 7 still absent. Amended the header, Solution, Out of scope and Success assertion; added section 5 (slice). No tickets. No product code.

# STATUS

**Hard cap: 60 lines.** Older narrative moves to CHANGELOG.md; it does not accumulate here.

## Now

1. **PDFSpace workbench (Edit in app)** -- same `NetieSpace.exe`. Space once = Quick Look
   peek. Double-tap Space on a PDF = workbench. Space in the editor does **not** leave
   or close (Esc closes, Done leaves). Peek: no Look Up/Add-to-Chat strip. Light Edit:
   equal chips, no teaching. Workbench: docked Acrobat-like WPF chrome, 120px WPF thumbs
   (HTML thumbs hidden), min 1100x720 (row 45 restored; row 48 overlay superceded).
   Chrome glue 8522 = light doc-app. `--edit PATH` opens
   the editor. Open with stays `--preview`. P7 PARK. FILL_ONLY.
2. **Open with Netie Space** -- additive HKCU right-click + Start Menu. Does not
   steal Word/Excel/PDF defaults. `scripts/register-open-with.ps1` from publish.
3. **Native hot paths (not a rewrite)** -- Rust `native/netie-pdf-detect` snap-cell
   (JS `detectCellAt` fallback). Go `native/netie-pdf-form` pdfcpu form-list/fill
   (`tools/netie-pdf-form/` when built). P7 whole-app rewrite stays parked.
4. **Stripe pay-first** -- Product `netie_space` / Price `price_1U8ckCFV5wcFod2faXiUCsaV`.
   Register `pending` until pay. Worker miss is `worker_unreachable`. You still
   put secrets.
5. **Cert sign** -- xunit `PdfClassicXref`. UI `LoadPfx` EphemeralKeySet then
   PersistKeySet. Adobe Reader check still human.
6. **S50 integration branch (#311, wave 4 consolidated)** -- 12 lanes merged, every lane
   branch proven an ancestor. Windows CI green at 762860f: xunit 1365 passed 0 skipped,
   exe checks PASS, desktop (real windows, UI Automation) PASS, installer PASS. Linux 1275.
7. **Tools** -- 91 catalog rows, **68 available on the Windows runner** (floor enforced in
   `scripts/check-app-shell.ps1` and `check-installer.ps1`). New: pdf-repair, pdf-sign-self,
   pdf-redact, pdf-from-html, img-html-to-image, vid-meme, vid-stop-motion, vid-screen-record.
   `--doctor [--json]` gives every engine a definite state (13 of 31 present on the runner).
8. **Apps** -- Netie PDF / Image / Video / MD (`--app md`, local pictures only, print to PDF).
   Four Start Menu shortcuts; every icon checked before any shortcut is written.
9. **Install** -- per-user Inno Setup (`installer/NetieSpace.iss`, package.yml): silent
   install, tools, shortcuts, uninstall green on CI. Clean machine and upgrade unverified.
10. **Send to phone** -- `--send-to-phone FILE [--yes]`: no --yes = disclosure, nothing sent
   (Space#3). Proven on CI with AirGPT absent; never run against a live AirGPT.
11. **No AGPL** -- PyMuPDF/pymupdf4llm removed (pypdfium2, pikepdf, reportlab, svglib LGPL).
   CI python job installs them and fails on any SKIP line. Dev stress scripts still import it.
12. **Peek chrome (S50 wave 5, PR into the integration branch)** -- header is one row, secondary
   actions in a More sheet, Fit never enlarges, tooltips guarded, summary hidden for pictures and
   clips. Verified only by the desktop job screenshots; video pane cause is read from `[video]` log lines.

## Next

- EP FORM in pdf.js: widgets are on **page 2** (page 1 empty). 37 widgets glued Fit/scroll/zoom 2 (wrap-rel x~0.05865). Copy gitignored. Still open: other pages, WPF host, PPTX (no soffice), Rotate90 not process-tested. P7 PARK.
- Space#3 OPEN: disclose-first shipped (tray Cloud AI notice + Cloud: chip). Do not silent-ungate; do not close by deleting the four paths. ChatPanel widget API still waits #3. FILL_ONLY this wave.
- Space stays PDF-local. Markdown PRD-001: outline/annotate/system-theme/`mdAsk` live in `md-editor.html`. Click Edit on a `.md` (auto-open waits row 45). Host is AirGPT. JSON peek tree (row 51) is `Assets/code-viewer/json-viewer.html` via `ContentParserService` - PreviewWindow frozen. Self-checks: `scripts/check-pdf-bbox-glue.ps1`, `check-widget-align.ps1`, `check-multieditor-assets.ps1`, `check-acrobat-chrome.ps1`, `check-json-viewer.ps1`.
- Human Windows Export (`docs/HUMAN_PILLAR1_TEST.md`). Stripe `4242`. Adobe on cert PDF.
- Parked: Comfy GPU (code-ready local client; live GPU still human), P7 rewrite (`docs/HUMAN_PARKED.md`). Hex editors. Object memory (22-24). Chatspace widget fill (25/31/34/38/39) waits Space#3. Suite packing (35) PARK. iLove cloud (29-30) PARK. Compress/page-numbers/crop PARK this wave (41). OnlyOffice AGPL BAN. DeepSeek Harness BAN as orchestrator.

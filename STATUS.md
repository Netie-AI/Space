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
6. **Netie PDF / Image / Video app processes (S50-E1-a #306)** -- code landed, Linux gate
   green (xunit + WinExe compile + Windows-shape test compile). **Not verified on Windows**:
   the `--app` walk, AUMID read-back, second-launch activate and `WinRtSmokeTests` need a
   Windows run by a different session before #306 closes. Window is a skeleton until #310.
7. **Tools (#308, E3 #301, E4 #302, E5 #303)** -- 91 catalog rows, **60 have a runner**
   (21 pdf, 11 image, 28 video), 31 honestly unavailable. 912 xunit
   on Linux. Windows CI runs the exe over 22 tools (`scripts/check-app-shell.ps1`).
   OCR and text to speech need a language pack / voice on the PC and fail naming it.
8. **App shell (S50-E1-b #310)** -- home, tool grid, tool page, `--snapshot`. Models under
   xunit; **no window has been seen by a person**. `scripts/check-app-shell.ps1` in the
   Windows CI job is the first exe-level evidence; the PNGs are its artifact.
9. **Install (S50-E1-c #309, S50-E2-b #307)** -- `--register-apps` Start Menu shortcuts with
   AUMID, pin relaunch, app icons, engine licence gate in `publish.ps1`, notices, DR-0002
   proposed. COM and PowerShell proven only by Windows CI. No `installer/*.iss` exists.

## Next

- EP FORM in pdf.js: widgets are on **page 2** (page 1 empty). 37 widgets glued Fit/scroll/zoom 2 (wrap-rel x~0.05865). Copy gitignored. Still open: other pages, WPF host, PPTX (no soffice), Rotate90 not process-tested. P7 PARK.
- Space#3 OPEN: disclose-first shipped (tray Cloud AI notice + Cloud: chip). Do not silent-ungate; do not close by deleting the four paths. ChatPanel widget API still waits #3. FILL_ONLY this wave.
- Space stays PDF-local. Markdown PRD-001: outline/annotate/system-theme/`mdAsk` live in `md-editor.html`. Click Edit on a `.md` (auto-open waits row 45). Host is AirGPT. JSON peek tree (row 51) is `Assets/code-viewer/json-viewer.html` via `ContentParserService` - PreviewWindow frozen. Self-checks: `scripts/check-pdf-bbox-glue.ps1`, `check-widget-align.ps1`, `check-multieditor-assets.ps1`, `check-acrobat-chrome.ps1`, `check-json-viewer.ps1`.
- Human Windows Export (`docs/HUMAN_PILLAR1_TEST.md`). Stripe `4242`. Adobe on cert PDF.
- Parked: Comfy GPU, P7 rewrite (`docs/HUMAN_PARKED.md`). Hex editors. Object memory (22-24). Chatspace widget fill (25/31/34/38/39) waits Space#3. Suite packing (35) PARK. iLove cloud (29-30) PARK. Compress/page-numbers/crop PARK this wave (41). OnlyOffice AGPL BAN. DeepSeek Harness BAN as orchestrator.

# STATUS

**Hard cap: 60 lines.** Older narrative moves to CHANGELOG.md; it does not accumulate here.

## Now

1. **Cursor-origin CI** -- `scripts/ci-cursor.sh` (python + license-api + net8 xunit + WinExe `-p:EnableWindowsTargeting=true`). Ignore GHA billing.
   Live wipe 2.8..3.4s. `ci-cursor.sh` runs wipe + `ExportTimelineAsync`
   (Leave-black / caption / Mom's clips / Export range / LUT). Windows CI runs encode-pillar1-wipe.ps1.
2. **Ready PRs #4-#234 landed**. Draft #1 stays draft.
   Marks are packed seconds. + Clip Video filter lists mpeg / 3gp / flv / ts.
   WebP/HEIC/ICO Space as Image (WebView2). WebP/TIF/HEIC drop and WebP/HEIC OCR/Copy/Compress/Comfy rasterize via Pillow, not XImage.
   Play after + Clip advances the packed slider. Clip/fade/caption drag
   skips the 200ms canvas rebuild (Clear used to abort the drag).
   Speed 2x shrinks packed width. Order... and Leave-black use that
   packed length. OV lane and the preview picture show the title card.
   Leave black seeks, pauses, keeps a V1 hole, and Export pads past V1.
   Captions after + Clip / Speed / drag / Order use packed time. Wipe
   Export shifts those times (UI ruler stays pre-wipe). Ctrl+Z
   restores the sidecar (remap used to stick). Export range slices cues. CI
   encodes clip-2 (~2.0s) and the full packed window (wipe 2.8..3.4s). Concat
   lists use file names; LUT `lut3d` copies the cube to %TEMP% so
   Mom's clips does not open as Moms.
   Cert sign gates in-app MuPDF compact `/Kids[` / `/Type/Page` (sample.pdf).
   `install.bat` / `publish.ps1` download gitignored `tools/ffmpeg`.
   Pillar clip/check scripts use that bundle, not PATH-only.
   Fade / Wipe / drag / Auto captions / LUT / CC cue push undo so extra Ctrl+Z keeps + Clip.
   Failed Split / + Clip / cancel Delete / no-op fade-handle / fade-handle
   or clip-drag capture-loss / second Wipe discard that undo.
   Ctrl+Z after Leave black restores the title card and fill-gaps flag.
   A2 uses packed start (preview-only, no amix).
   Share Export as Word / Excel writes real .docx / .xlsx. Opening those
   plus .xlsm / SVG / ODT / .ppsx / scan-PDF Word+HTML+MD / photo office PDF+Space HTML (ODF+XLSX) / photo ODS Excel + photo ODP PPTX pictures / HTML Story PDF + MD dest 1 + photo HTML MD + SVG HTML Word+MD + CSS bg/@import+image-set+amp-img+PDF / MD title+ref / wrap dest / pct href / src not data-src / &#46; href / HEIC sibling / data-URI charset / HTML title strip / base href / UTF-8 BOM / UTF-16 HTML / curly src / MD curly dest / UTF-16 MD / UTF-16 CSV / UTF-16 CSS / meta charset / xml encoding / CSS @charset / CJK charset / IANA charset / svg href / svg use / svg prefix / svg close / svg upper / OLE no-blank / silent MP3.
3. **Stripe pay-first** -- Product `netie_space` / Price `price_1U8ckCFV5wcFod2faXiUCsaV`.
   Register is `pending` until pay. Sign-in, Setup, and vault activate all
   persist Worker `license_key` via `LicenseActivation`. Gate exception exits
   (no unlicensed continue). WMI board-UUID is capped at 3s and saved on
   the entitlement (later `unknown-board` cannot unlicense). Download URLs
   use the request host. Dead Worker DNS is `worker_unreachable` (Buy still
   opens netie.space). Sign-in on that miss still shows Buy so
   `SkipGate: false` is not a deadlock. After the Worker is up, Buy
   without a session is `register_first` even if the email box matches D1.
   You still put secrets.
4. **ComfyUI / Windows AI** -- code-ready local client; sidecar only on fail
   (WinAI sidecar only when WinML is down). Probe `/api/` then unprefixed.
   Poll `/api/jobs` then `/history`; SVG/ICO Copy/OCR/Compress/Comfy rasterize. Live GPU still human.
5. **Cert sign** -- rewrite+sign proven in xunit (`PdfClassicXref`). Object id
   match is not a substring (`12` != `2`). Indirect `/Kids` and `/Annots`
   plus compact MuPDF `/Kids[4 0 R]` resolve so the first-page box lands.
   `make-test-pfx.ps1` uses Windows PKI (no openssl). UI `LoadPfx` tries
   EphemeralKeySet then PersistKeySet (Windows CNG PFX). Adobe still human.

## Next

- Human Windows Export (`docs/HUMAN_PILLAR1_TEST.md`). Wipe on either side of the join.
  Export clones the timeline (Space-close cannot mutate the encode). Caption
  status matches that snapshot. Ribbon / context-menu / handle Fade all write
  the session. Toast uses bundled ffprobe. Fresh clone: install downloads ffmpeg.
- Human: Stripe restricted key + webhook + test card `4242`; Adobe Reader on a cert-signed PDF (hybrid rewrite + Sign refuse leftover hybrid).
- Parked: Comfy GPU, P7 (`docs/HUMAN_PARKED.md`). Product identity later.

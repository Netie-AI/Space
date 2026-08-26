# STATUS

**Hard cap: 60 lines.** Older narrative moves to CHANGELOG.md; it does not accumulate here.

## Now

1. **Cursor-origin CI** -- `scripts/ci-cursor.sh` (python + license-api + net8 xunit). Ignore GHA billing.
   Live wipe encode gate is 2.8..3.4s (hard-cut ~3.6s fails).
   `ci-cursor.sh` runs `encode-pillar1-wipe.sh` + `ExportTimelineAsync`.
   Windows CI runs `encode-pillar1-wipe.ps1`.
2. **Ready PRs #4-#117 landed**. Draft #1 stays draft.
   Marks are packed seconds. + Clip expands In/Out and slider max.
   Play after + Clip advances the packed slider. Clip/fade/caption drag
   skips the 200ms canvas rebuild (Clear used to abort the drag).
   Speed 2x shrinks packed width. Order... and Leave-black use that
   packed length. OV lane and the preview picture show the title card.
   Leave black seeks, pauses, keeps a V1 hole, and Export pads past V1.
   Export range slices (`SlicePacked`). Concat lists use file names so
   a folder like Mom's clips does not open as Moms.
   Cert sign gates in-app MuPDF compact `/Kids[` / `/Type/Page` (sample.pdf).
   `install.bat` / `publish.ps1` download gitignored `tools/ffmpeg`.
   Pillar clip/check scripts use that bundle, not PATH-only.
   Fade / Wipe / drag push undo so extra Ctrl+Z keeps + Clip.
   Failed Split / + Clip / cancel Delete / no-op fade-handle / fade-handle
   or clip-drag capture-loss / second Wipe discard that undo.
   Ctrl+Z after Leave black restores the title card and fill-gaps flag.
   A2 uses packed start (preview-only, no amix).
   Delete key runs the same ribbon Delete dialog.
3. **Stripe pay-first** -- Product `netie_space` / Price `price_1U8ckCFV5wcFod2faXiUCsaV`.
   Register is `pending` until pay. Sign-in, Setup, and vault activate all
   persist Worker `license_key` via `LicenseActivation`. Gate exception exits
   (no unlicensed continue). WMI board-UUID is capped at 3s and saved on
   the entitlement (later `unknown-board` cannot unlicense). Download URLs
   use the request host. Dead Worker DNS is `worker_unreachable` (Buy still
   opens netie.space). Sign-in on that miss still shows Buy so
   `SkipGate: false` is not a deadlock. After the Worker is up, Buy
   without a session is `register_first` (does not open netie.space).
   You still put secrets.
4. **ComfyUI / Windows AI** -- code-ready local client; sidecar only on fail
   (WinAI sidecar only when WinML is down). History waits for image outputs
   (not `completed` alone). Live GPU still human.
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
- Parked: Comfy GPU, P7 (`docs/HUMAN_PARKED.md`).

## Later

- Product identity in Netie constitution (document workspace vs warehouse Spaces).
- Optional marketing after Pillar 1 user Export stress.

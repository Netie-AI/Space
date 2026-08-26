# CHANGELOG

Append-only. Never edited, only added to. Newest first.

## 2026-08-26

- License: WMI `Win32_ComputerSystemProduct` UUID is cached and capped at 3s. `SkipGate: false` launch and Activate used to hang the UI on a stuck CIMOM.
- Image: **Windows AI...** writes `{stem}.winai-input.json` only when WinML is down. A present runtime no longer leaves a leftover sidecar.
- PDF: `TryFirstPageRef` follows indirect `/Kids N 0 R` (Chrome/Word array object) so cert-sign stamps page 1 instead of a zero-size `/Rect`.
- PDF: `TryReadObject` / xref offsets match `id 0 obj` only when the previous byte is not a digit. `12 0 obj` used to resolve as object 2, so the signature widget could land on the wrong page.
- Export: **Export all** clones `TimelineProject` before the background job. Space-close + open another file used to `ResetFromSource` the live project mid-encode (same object as `timelineSnapshot`).
- License: `EnsureLicenseGate` exits on exception (`Shutdown`), same as cancel. A thrown login dialog used to leave `SkipGate: false` running unlicensed. `/v1/download` file URLs use `requestPublicBase` (local `:8787` no longer gets a committed `workers.dev` token URL).
- License: Sign-in auto-activate and OpenVault auto-login go through `LicenseActivation` (Worker `license_key` via `ReadString`). Login used to save `auth.LicenseKey` from the login payload, so a post-pay Sign in could persist an empty key. Setup Activate already used this path.
- Video: session Split matches `TimelineProject` blade -- wipe/fade-out stay on the outgoing half only. A `with` copy used to put wipe on both halves and burn a second xfade. Playbook Undo is Ctrl+Z.
- Comfy: `/history` poll waits for an image in outputs. `status.completed` with empty outputs used to stop the poll, so `DownloadFirstOutput` failed and Space wrote a sidecar instead of `{stem}.comfy.png`. Pillar 1 playbook: Undo after Split/Delete so Wipe+Export still uses full 1.6s+2.0s clips (~3.1s).
- License: device fingerprint volume token is `Environment.MachineName` via `LicenseOffline.StableVolumeToken`. The old `string.GetHashCode` is randomized per process on .NET 8, so Activate + `SkipGate: false` treated a paid PC as unlicensed on the next launch.
- Export: toast duration uses `FfmpegService.ResolveFfprobe` (`tools/ffmpeg` next to the repo or exe, then PATH). PATH-only lookup hid `~3.1s` on Windows after `install.bat`.
- License: **Activate this laptop** persists `license_key` from the Worker entitlement. The offline gate used to require a key and treat a paid PC as unlicensed on the next `SkipGate: false` launch. Token-only entitlements still count as licensed.
- Comfy: sidecar `.comfy-input.json` only after upload/queue/download fail (not on a good `{stem}.comfy.png`). Upload part Content-Type follows the image extension. First-gen xref-stream (Chrome Save as PDF) rewrite+sign is in xunit.
- License: Checkout success/cancel and portal return use the Worker host that handled the request (`requestPublicBase`). Local `:8787` and `netie-space-license.<you>.workers.dev` no longer bounce to the committed `PUBLIC_BASE_URL` (`netie-space-license.workers.dev`).
- PDF: `PdfClassicXref` strips Chrome/Word incremental xref-stream tails (last classic `%%EOF`) before PdfSharp. PdfSharp Open fails on those hybrids (`no trailer found`). Leftover `/Type /XRef` after rewrite still throws. xunit rewrite+signs both shapes.
- PDF: `Sign` refuses leftover hybrid/xref-stream (`NeedsClassicXrefRewrite`). The old `xref` substring matches `startxref`, so a failed rewrite used to write a PKCS#7 Adobe will not show. `/Pages` is fail-closed (no default object 2). Stripe playbook: do not `wrangler deploy` while D1 id is `REPLACE_AFTER_D1_CREATE`.
- PDF: cert sign rewrites hybrid files (classic trailer plus `/Type /XRef` stream). A trailer alone is not enough -- Adobe incremental PKCS#7 still fails on those.
- License: Customer Portal return is `/v1/billing/return` ("Billing updated", not a new charge). Checkout success only says "Payment received" when the session is actually paid.
- License: `POST /v1/checkout` returns 409 `already_paid` when the license is active and already has a Stripe session. Buy buttons tell you to Activate instead of opening a second Checkout. Re-subscribe after expire reuses `stripe_customer_id`.
- License: fulfill accepts Stripe customer/subscription as string or `{ id }`. A second fulfill for the same session backfills null portal ids (`COALESCE`) so Manage billing is not stuck after a thin webhook.
- License: Login, Setup Buy, and Activate share `LicenseActivation.ResolveApiBase` (`NETIE_LICENSE_API` wins, then appsettings `ApiBaseUrl`). Local Worker pay cannot land on production `workers.dev`.
- License: pay-first when Stripe secrets are live. Register creates `pending` (not `active`). Activate returns `payment_required` until fulfill stamps `stripe_checkout_session_id`. Login gate shows **Get Netie subscription** + **Activate this laptop** so `SkipGate: false` is not a deadlock. Local Worker without secrets still grants `active`.
- Image: **Windows AI...** fail-closed WinML probe (`WindowsAiClient`). No WinAppSDK. Down -> toast + `.winai-input.json`. rembg / Copy stay. Live enhance still human.
- License: Setup **Activate this laptop** binds the saved session after Stripe pay (`LicenseActivation`). Same path as tray Account Activate. No WPF types in the helper.
- Video: **Wipe** works from either clip at a join (playbook "Wipe on the join"). Fade/wipe live on `KeptClip` so Export `ApplyTo` cannot drop them.
- PDF: Cert sign rewrites xref-stream to classic xref via PdfSharp before PKCS#7. `NeedsClassicXrefRewrite` has no PdfSharp types. Failed rewrite leaves original bytes.
- Export: toast includes ffprobe duration; 2.8..3.4s adds `(wipe expect ~3.1s)` for the pillar1 human check.
- License: `GET /v1/checkout/success?session_id=` retrieves the Stripe session and fulfills if paid (webhook backup). `GET /v1/stripe/status` is a no-secret probe; Setup shows configured vs `stripe_not_configured`.
- Export: `scripts/check-pillar1-export.ps1` / `.sh` ffprobes a Windows Export (2.8..3.4s). Not a substitute for the click path.
- CI: `scripts/ci-cursor.sh` now runs `dotnet test` on `tests/NetieSpace.Tests` (net8, no WPF). `ci-cursor-install.sh` installs the 8.0 SDK. Same suite as Windows CI; live ffmpeg wipe encodes here too.
- PDF: `PdfCmsSigner.TryVerify` checks the detached CMS against `/ByteRange`. `Sign` fail-closes if self-check fails. Adobe Reader panel is still human.
- License: `setup-license-api.ps1 -Deploy` prompts for `STRIPE_SECRET_KEY` + `STRIPE_WEBHOOK_SECRET` (Enter skips).
- Comfy: after queue, GET `/view` writes `{stem}.comfy.png` and reloads it so Copy / drag-out work like rembg. Workflow is LoadImage + SaveImage. Live GPU still human.
- Comfy: **Send to Comfy...** POSTs `/upload/image` before `/prompt` so the open file is in Comfy's input folder. Upload fail -> sidecar + filename-only LoadImage. rembg / Copy stay the offline path.
- PDF: cert sign now incremental-overrides the first page `/Annots` and writes a Form XObject appearance (`Digitally signed`). Adobe Signature panel is still a human check.
- License: test-mode Stripe Product `netie_space` + Price `price_1U8ckCFV5wcFod2faXiUCsaV` ($9/mo) on account NETIE; Worker `[vars]` has the Price id. Secret key + webhook still human.
- Export: `scripts/encode-pillar1-wipe.sh` burns Space's wipeleft graph on the pillar1 clips (3.12s reference here).
- Setup: **Manage billing** calls `POST /v1/billing-portal` after a paid customer id exists.
- Human gates: `scripts/make-pillar1-clips.ps1` (1.6s + 2.0s lavfi clips, wipe ~3.1s) and `scripts/make-test-pfx.ps1` (password `test`) so Export + Adobe checks have fixtures.
- PDF: **Cert sign (PFX)...** embeds `adbe.pkcs7.detached` via in-box `SignedCms` incremental xref (`PdfCmsSigner`). Visual Sign stamp stays. No iText. Adobe Reader check is still human.
- Image: **Send to Comfy...** on the image ribbon. `ComfyUiClient` probes `http://127.0.0.1:8188` (~1.2s, `NETIE_COMFY_URL` override). Down -> toast/status only; rembg / Copy still work. Up -> POST /prompt LoadImage or sidecar `.comfy-input.json`. Live GPU still human (`docs/HUMAN_PARKED.md`).
- Convert: track `tools/convert_engine.py` (`tools/` stays ignored except the engine). JSON last-line `{success, output_path, tool, bytes, error}` for to-md / to-pdf / preview / preview-html / preview-pptx-html / export-as / pdf-watermark / pdf-protect / pdf-unlock / pdf-ocr. `tests/test_convert_engine.py` skips pymupdf smoke if the dep is missing.
- License: Setup **Get Netie subscription** calls `POST /v1/checkout`. Worker creates a Stripe subscription Checkout Session (no `payment_method_types`, no `automatic_tax`). Missing secrets return 503 `stripe_not_configured`. Webhook `/v1/webhooks/stripe` verifies `Stripe-Signature` and writes D1 Stripe columns. `POST /v1/billing-portal` opens Customer Portal after a customer id exists. HMAC vectors in `tests/test_stripe_checkout.py`. Human secrets + test pay: `docs/HUMAN_STRIPE_CHECKOUT.md`.
- Docs: Windows Pillar 1 click playbook (`docs/HUMAN_PILLAR1_TEST.md`). Playwright cannot open the WPF app. Parked-ticket process: `docs/HUMAN_PARKED.md`.
- CI: Cursor-origin gate is `scripts/ci-cursor.sh` (python + license-api + ffmpeg). GitHub hosted runners hit a spending limit; do not wait on them.
- CI: drop the 4th aggregator `ci` job. GitHub already fails the workflow if Windows, python, or license-api fails. After #25 that extra runner never started (billing/spending limit).
- CI: Windows job installs ffmpeg (`setup-ffmpeg` / GyanD, not choco — community feed 504) and encodes Space's wipeleft graph via `FfmpegExportLiveTests`. Still not the human Export gate.
- Export: `FfmpegExportGraph` owns fade/wipe filter math; Windows xunit + python ffmpeg smoke use the same wipeleft offset (`left − 0.5s`). Not a substitute for the human Export gate.

## 2026-08-25

- CI: `license-api` job runs `tsc --noEmit` (schema-only Worker; Stripe Checkout still parked). Aggregator `ci` now requires Windows + python + license-api.
- CI: bump `actions/checkout@v5`, `setup-dotnet@v5`, `setup-python@v6` (Node 24 majors). Node 20 on runners is deprecated.
- CI: Windows job runs `dotnet test` (xunit Select/Split/Drag/Delete/Fade + WASM policy). Tests compile-link NLE services (no WPF WinExe ProjectReference — testhost hangs on App.xaml). `--blame-hang-timeout 2m`. Aggregator job `ci` fails unless both Windows and python succeed. `merge_group` trigger ready if merge queue is enabled. Not a substitute for the human Export gate.
- CI: Windows `setup-dotnet` caches NuGet (`cache: true` on `*.csproj` / `*.sln`).
- Marketing: `scripts/demo_video/` renders a 16.5s 1080p promo (brand / Space key / preview / CTA). Output under `scripts/demo_video/out/` (not `/opt/cursor/artifacts`). `NETIE_DEMO_MAX_FRAMES=1` skips encode.
- Housekeeping: STATUS.md records green main CI and merge-only-if-green (no `--auto`).
- CI: ubuntu job installs ffmpeg and smokes Space's fade + wipeleft xfade graph (not a substitute for user Export stress).
- ShellInfo files with an Explorer preview handler open in an **opaque overlay** (IPreviewHandler). Glass PreviewWindow stays layered; `NETIE_SPACE_PREVIEW_HANDLER=0` disables. Folders and missing CLSIDs keep the HTML metadata view.
- CI: python job runs every `tests/test_*.py` contract script after compileall (not syntax-only).
- PDF/Video editor ribbons: primary chips only; **More...** menus hold range export, Match/1080/4K presets, LUT, watermark/protect/OCR. No ComboBox (that template crashed XAML load).
- Video: **Order...** dialog + multi-select **+ Clip** packs several files onto V1 in chosen order (drag / Up / Down). Export still uses packed sources so Fade/Wipe join mixed files.
- Cortex: Space sends WASM pack policy (deny WASI FS/net, 50k fuel, 4MiB, 2s kill) on sidecar calls; `CORTEX_WASM_DISABLED` skips isolate. Python stub + ditch-tier sidecar in `scripts/` with tests (fuel/OOM/missing export/timeout/disabled).
- Video: **+ Clip** packs a second/third source onto V1 (no overlap; drag to reorder). Export uses per-clip sources so Fade/Wipe xfade still join mixed files.
- Word/HTML: contenteditable preview editor with autosave. `.html`/`.htm` writes HTML; existing local `.docx` is rewritten via OpenXML (no new NuGet / no iText).
- PPTX: Send slide chip (and Add to Chat on a deck) stages the visible slide PNG into ChatPanel — not a whole-deck screenshot.
- Video: clip-to-clip **Wipe** via ffmpeg `xfade=transition=wipeleft` (offset = outgoing clip duration − 0.5s). Overlay **Text** chip burns on export (`drawtext` + system font, SRT `subtitles=` fallback).
- PDF: Comment highlight/mark overlay now bakes into the opened file via PdfSharpCore appearance streams (no iText). Save/autosave writes marks; a failed persist keeps the session overlay and leaves the original bytes untouched.
- Chat: Cite / First Principles / Mindmap mode chips in the Notes & chat panel. Default stays normal chat. Selected mode persists per document via DocumentChatStore. Cite quotes + short citations; First Principles strips to axioms; Mindmap returns an indented / mermaid-friendly tree. Empty Send in a mode maps the open document.
- Fix: qualify `Markdig.Syntax.Block` in ChatMarkdownRenderer (CS0104 vs WPF Documents.Block).
- CI: GitHub Actions Windows Debug `dotnet build` + python `compileall` on PRs.
- Image: Copy button, drag-out from preview, rembg dialog Copy (bitmap + PNG + file drop).


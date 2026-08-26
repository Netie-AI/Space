# CHANGELOG

Append-only. Never edited, only added to. Newest first.

## 2026-08-26

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


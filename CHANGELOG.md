# CHANGELOG

Append-only. Never edited, only added to. Newest first.

## 2026-08-25

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


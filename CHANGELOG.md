# CHANGELOG

Append-only. Never edited, only added to. Newest first.

## 2026-08-25

- Video: **+ Clip** packs a second/third source onto V1 (no overlap; drag to reorder). Export uses per-clip sources so Fade/Wipe xfade still join mixed files.
- Word/HTML: contenteditable preview editor with autosave. `.html`/`.htm` writes HTML; existing local `.docx` is rewritten via OpenXML (no new NuGet / no iText).
- PPTX: Send slide chip (and Add to Chat on a deck) stages the visible slide PNG into ChatPanel — not a whole-deck screenshot.
- Video: clip-to-clip **Wipe** via ffmpeg `xfade=transition=wipeleft` (offset = outgoing clip duration − 0.5s). Overlay **Text** chip burns on export (`drawtext` + system font, SRT `subtitles=` fallback).
- PDF: Comment highlight/mark overlay now bakes into the opened file via PdfSharpCore appearance streams (no iText). Save/autosave writes marks; a failed persist keeps the session overlay and leaves the original bytes untouched.
- Chat: Cite / First Principles / Mindmap mode chips in the Notes & chat panel. Default stays normal chat. Selected mode persists per document via DocumentChatStore. Cite quotes + short citations; First Principles strips to axioms; Mindmap returns an indented / mermaid-friendly tree. Empty Send in a mode maps the open document.
- Fix: qualify `Markdig.Syntax.Block` in ChatMarkdownRenderer (CS0104 vs WPF Documents.Block).
- CI: GitHub Actions Windows Debug `dotnet build` + python `compileall` on PRs.
- Image: Copy button, drag-out from preview, rembg dialog Copy (bitmap + PNG + file drop).


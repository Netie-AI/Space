---
keywords: markdown-editor, distill, outline, annotate, mdSelection, mdAsk, marked
main_idea: MD-0 shipped in md-editor.html. Outline + in-file ==/<mark> annotate + system theme + mdSelection/mdAsk hooks. Auto-open and ChatPanel still wait Acrobat PreviewWindow.
date: 2026-09-07
kind: execute
product: Markdown
---

# Markdown MD-0 DISTILL execute

PREFLIGHT: HIT. PRD `2026-09-07_markdown-product-prd.md`. Files: `Assets/markdown/md-editor.html` only.

Verify:
- `powershell -File D:\Space\scripts\check-multieditor-assets.ps1` -> OK (KaTeX + eqToMark + hooks)
- Browser `md-editor.html?demo=1`: Split render, outline H1-H3, `==keep this dump==` as mark, Annotate wrote `<mark title="keep for AI">`, task toggle, theme Light/System, Preview hides source.

Does not prove: auto-open `.md` in NetieSpace.exe, ChatPanel consume of mdAsk, save/reopen on a real file (HT1/HT3 human).

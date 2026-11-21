---
keywords: markdown-product, themarkdowneditor, md-editor, auto-open, annotate, chat-connected, row-42, acrobat-contention, marked, space#3
main_idea: Founder BUILD unparks Markdown PRD-001 (same NetieSpace.exe, not a second exe). Execute-now is md-editor.html only. Auto-open and ChatPanel wire wait Acrobat row 45 (PreviewWindow). Keep marked. PARK GitHub, Codex-in-editor, insights-as-KB, UserChoice, Space#3 upsert.
date: 2026-09-07
kind: prd_intake
product: Markdown
---

# PRD intake - Markdown product (Mode B then signed draft)

PREFLIGHT: HIT. Reused `2026-09-07_chatfill-split-apps-prd.md`, `2026-09-07_multieditor-goal-prd.md`, `2026-09-07_goal-pdf-md-progress.md`, `docs/research/findings/2026-09-07_multiformat_editor_inventory.md`, `2026-09-07_acrobat-standalone-prd.md`.

Analog: TAS-SPACE live PreviewWindow + `md-editor.html` + ChatPanel. No frozen markdown-editor clone in TAS. themarkdowneditor.app is paid macOS Tahoe 26 -- DISTILL UX only. Insights = Cortex `/api/memory` (lanes 12/17/18 SKIP second KB). AI coding tools = FreeIDE (lane 19). Prefer extend-live. BAN OnlyOffice AGPL / n8n / grok-bot / GPL / leaked trees. P7 PARK. Palantir P1 PARK.

## Nearness (artifacts checked, no %)

Customer-facing artifact: `NetieSpace.exe` preview. Markdown canvas: `src/NetieSpace/Assets/markdown/md-editor.html`. Host: `PreviewWindow.xaml.cs` `TextEditButton_Click`. First open: `ContentParserService.ParseMarkdownAsync` (Markdig HTML). Marketing DISTILL source: https://themarkdowneditor.app/ (fetched 2026-09-07). Landing for Markdown: none (Marketing owns; themarkdowneditor.app is not ours).

What a person can do today:

- Space-key or additive Open with on `.md` (`scripts/register-open-with.ps1` lists `.md`).
- See Markdig HTML preview first. Click Edit to load dual-pane Split/Source/Preview.
- Live GFM via marked MIT: headings, lists, tables, strike, task checkboxes (toggle writes source), links, images.
- KaTeX 0.16.11 MIT (`$` / `$$`).
- Hide-idle chrome, type size A-/A+, line spacing, font mono/sans/serif, image width, narrow/wide, light/dark, Ctrl+B/I, word/char count.
- Autosave via `postMessage` `textEdit`.
- Chat about the open file (`OnChatSendRequested` -> `AiService.ChatAsync`, text only). No caret/selection bridge from `md-editor.html`.
- Convert/share MD to PDF/HTML/DOCX already.

What they cannot do (marketing + founder extras):

| Ask | Gap |
|---|---|
| Auto-open into editor | First paint is Markdig HTML; Edit is required. |
| Annotate | No highlight/note layer in `md-editor.html`. |
| Chat connected to edit | ChatPanel exists; no `mdSelection` / apply-with-confirm. |
| Easy manage | No heading outline / sidebar. No file library (good -- marketing is in-place). |
| Insights | Word count only. No outline. Ontology is Cortex, not this canvas. |
| When-syntax-appears | Source is always raw textarea; no hide-markup-in-preview-caret mode. |
| System theme / accent | Light/Dark toggle only. |
| GitHub sync | None. PARK. |
| Codex / Claude Code / OpenCode in editor | None. PARK / FreeIDE. |
| macOS Tahoe chrome | Wrong OS. Do not clone. |

Space#3 OPEN: disclose-first shipped; ChatPanel Cortex memory still waits.

## Per-ask CALL

| # | Ask | Maps to | CALL |
|---|---|---|---|
| a | DISTILL editor UX (document-first, hide chrome, GFM+LaTeX, width/type/spacing/font/image, light/dark) | Space 37/42; live `md-editor.html` | **execute-now** in `md-editor.html` only. Close remaining DISTILL: heading outline sidebar, optional system theme, "syntax when" as a Source vs Preview discipline (do not vendor Milkdown). Do not clone macOS chrome. |
| b | Auto-open `.md` into the editor | new; contends PreviewWindow | **ticket-later.** Needs `PreviewWindow.xaml.cs` (`RenderContentAsync` markdown branch / extract of `TextEditButton_Click`). Ledger 45 owns that file for PDF. Same PreviewWindow writer may append auto-open after Acrobat chrome lands. Until then: click Edit. Do not steal UserChoice. |
| c | Annotate (AI-era) | new (not on marketing page) | **execute-now** in `md-editor.html` (selection highlight + note that round-trips in the `.md` source, e.g. `==mark==` or HTML comment spans). Not PDF annotator. Not `netie-viewer.html`. |
| d | Chat communicate + edit, connected | ChatPanel live; Space 25/3 | **split.** `postMessage` `mdSelection` / `mdAsk` hooks = **execute-now** in HTML. ChatPanel consume + apply-with-confirm = **ticket-later** on `PreviewWindow.xaml.cs` after Acrobat. Cortex upsert = **BLOCKED Space#3**. Do not silent-rewrite the buffer. |
| e | Easy manage | marketing sidebar + in-place files | **execute-now** outline/sidebar in HTML. In-place save already. No library. No folder vault. |
| f | Understand insights | founder extra; TAS 12 | **PARK** as ontology / second KB. Local outline + word count is UX (a/e), not Cortex. Consume `/api/memory` waits Space#3. |
| g | GitHub sync | Space 37; P8 | **PARK.** Wrong product. |
| h | Codex / Claude Code / OpenCode in editor | Space 37; NETIE.md FreeIDE | **PARK.** Wrong product. |
| i | New exe vs same `NetieSpace.exe` | Space 8/35/45 analog | **same exe**, markdown workbench mode. Own PRD. Not `Markdown.exe`. |
| j | Unpark row 42 now vs wait Acrobat | Space 42 queued; founder BUILD | **UNPARK the Markdown PRD now.** Do not wait to write it. Do not open a PreviewWindow markdown WIP while 45 is in flight. HTML execute-now is the second inspectable lane (does not share Acrobat files). |

## WIP / contention

Acrobat row 45 owns `PreviewWindow.xaml`, `PreviewWindow.xaml.cs`, `netie-viewer.html`.

Execute-now **can** stay inside `md-editor.html` only. Auto-open **cannot**.

WIP limit: Acrobat is the inspectable PreviewWindow lane. Markdown HTML is allowed as a second visible lane because it does not share those three files.

```
Repo:            Netie-AI/Space
Contract impact: none
Depends on:      ledger 45 for PreviewWindow.cs; Netie-AI/Space#3 for Cortex memory
```

No tickets this intake (EPIC Agent later). PRD unsliced.

## Engine

Keep Markdig + marked + KaTeX. Founder has not signed Milkdown/TipTap. Do not download.

## Markdown PRD

Written: `D:\Netie\Software Blueprint\Markdown\PRD-001-local-markdown-editor.md`

Space PRD stays PDF-local. Ledger row 46 points at that file.

## Files parent MAY edit (execute-now)

- `D:\Space\src\NetieSpace\Assets\markdown\md-editor.html`
- `D:\Space\src\NetieSpace\Assets\markdown\NOTICE.txt` (only if new MIT notes)
- `D:\Space\scripts\check-multieditor-assets.ps1` (assert new hooks)

Same PreviewWindow owner, **after** Acrobat chrome, MAY then touch:

- `PreviewWindow.xaml.cs` markdown auto-open + later `mdSelection` consume

## Files parent must NOT touch for Markdown now

- `PreviewWindow.xaml` / `PreviewWindow.xaml.cs` / `netie-viewer.html` (Acrobat)
- ChatPanel Cortex `/api/memory`
- `csv-editor.html` / Office / image suite
- New exe, P7, OnlyOffice, UserChoice

## PARK

- GitHub sync
- Codex / Claude Code / OpenCode in editor
- New exe
- `.md` UserChoice steal
- ChatPanel Cortex upsert before Space#3
- Second memory KB / Palantir P1
- CSV / Office / image / video further packing
- OnlyOffice AGPL
- P7 rewrite
- Clone paid macOS app
- Milkdown / TipTap until signed
- Markdown WPF replace-surface (row 45 is PDF-only)

## NEEDS-YOU

1. NETIE.md does not name Markdown as a product (constitution). Add a sentence later by PR, or keep "workbench of Space" as this PRD does.
2. Space press-release rewrite (PDF-local vs still Quick Look + video/image) remains from row 35 -- not this product.
3. If auto-open must land this hour: pause Acrobat or keep one PreviewWindow owner. Default CALL is wait.

## Refuse

Clone paid app. Vendor AGPL. Reopen P7. Steal `.md` UserChoice. ChatPanel Cortex upsert before Space#3. Second memory KB. Pack CSV/Office further. Second-own Acrobat files for PDF chrome.

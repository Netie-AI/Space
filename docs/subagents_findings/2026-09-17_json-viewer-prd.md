---
keywords: json-viewer, peek-canvas, collapsible-tree, content-parser, code-viewer, jsonl, ndjson, row-51, execute-now, no-fourth-app, no-previewwindow, wrapcodehtml, highlight-js
main_idea: SIGNED row 51 execute-now. Space-key on .json is a collapsible tree peek (types, search, copy path, pretty/raw) emitted by ContentParserService, not a Netie JSON Start-menu app and not a new epic (WIP already S50-E1+E2). Put json-viewer.html under Assets/code-viewer so csproj stays untouched. Kind stays Text. Edit stays contenteditable on pre code. PreviewWindow frozen.
date: 2026-09-17
kind: prd_intake
product: Space
reuse: docs/subagents_findings/2026-09-17_ecosystem-apps-prd.md, docs/subagents_findings/2026-09-07_markdown-product-prd.md, docs/subagents_findings/2026-09-17_ui-copy-audit.md
cite: D:\Netie\Software Blueprint\Space\PRD-001-document-workspace-governed.md ledger 51
---

# PRD intake - JSON viewer peek canvas (Mode B)

PREFLIGHT: PARTIAL. HIT: ecosystem-apps-prd (ledger 50, PDF/Image/Video only), markdown-product-prd (same-exe peek canvas, HTML execute-now while PreviewWindow frozen), ui-copy-audit (no how-to copy). MISS: JSON tree as a first-class peek surface.

Analog: TAS-SPACE live (`D:\Space`). No frozen JSON-viewer clone in the TAS map. Prefer extend-live `ContentParserService.ParseTextAsync` + `WrapCodeHtml` + `Assets/code-viewer/`. DISTILL public tree UX (VS Code outline / Firefox JSON view) into original Netie HTML. Do not vendor jsoneditor / monaco. BAN n8n, grok-bot, AGPL, GPL copy, leaked trees. Palantir P1 parked. No UI token or layout DNA redesign.

## 1. Translation check

Founder (verbatim): `then help me build json viewer all can view easily`

| # | Reading | Call |
|---|---|---|
| 1 | Build a JSON viewer in Space | CORRECT |
| 2 | "all can view easily" = non-dev tree (expand/collapse, types, search), not raw highlighted source, not a Start-menu app | CORRECT |
| 3 | Not a JSON editor suite unless Edit for text already exists | CORRECT. Edit already exists: `TextEditButton_Click` contenteditable on `pre code` for `PreviewKind.Text`. |
| 4 | Not Netie JSON as a fourth ecosystem app | CORRECT. Ledger 50 named PDF / Image / Video only. |

Refined: analog is **markdown first-paint** (`ParseMarkdownAsync` emits `RenderedHtml`, peek shows it with no PreviewWindow special case), **not** csv-editor (grid only after Edit click) and **not** S50 `--app`. Default peek IS the tree. `.jsonl` / `.ndjson` in this slice as a line list (same parser file; today they are ShellInfo). `.jsonc` PARK (comments fail `JSON.parse`).

## 2. Nearness (artifacts checked, no %)

Customer-facing artifact: `NetieSpace.exe` Space-key peek. Landing https://netie.ai/space/ does not mention JSON (Marketing). TAS lane: TAS-SPACE live.

| Surface | Artifact checked | What is true |
|---|---|---|
| Default peek | `ContentParserService.cs` 11-16, 85-86, 482-502, 520, 548-620 | `.json` is in `TextExtensions`. `ParseTextAsync` caps 400_000 chars, `GetHighlightLanguage` maps `.json` -> highlight.js `json`, `WrapCodeHtml` emits a `<pre><code class="language-json">` dump with `highlight.min.js` + `github-dark.min.css`. `Kind = PreviewKind.Text`. |
| Tree / search / path | grep `json-viewer` / `jsoneditor` / `monaco` over `src` | Zero hits. No fold, no key search, no copy path, no type chips. |
| Kind | `AppModels.cs` 3-15 | `PreviewKind` has no Json. |
| Edit | `PreviewWindow.xaml.cs` 5974-6118 | Text/Markdown only. CSV/MD load `csv-editor.html` / `md-editor.html`. JSON falls through to contenteditable on `pre code` (6074-6100). If peek HTML has no `pre code`, Edit would contenteditable `document.body` and save display text -- data loss. |
| Drop / open | `PreviewWindow.xaml.cs` 7360; `ExplorerFileService.cs` 1017; `PdfMergeWindow.xaml.cs` 24; `register-open-with.ps1` 42 | `.json` already a convertible / Open-with text ext. |
| jsonl | `ContentParserService.cs` 11-16 and 131; `UsageFootprintStore.cs` 17 | `.jsonl` / `.ndjson` are **not** in `TextExtensions`. Unknown ext -> `ShellInfoPreview.Build` (line 131). Internal `usage.jsonl` / `file-graph.jsonl` are app logs, not a customer viewer. |
| Asset glob | `NetieSpace.csproj` 56-58 | `Assets\code-viewer\**\*` already CopyToOutput. `Assets\json\` does **not** exist and would need a new Include -- S50-E1 owns that csproj. |
| WIP | PRD-001 section 5 | S50-E1 + S50-E2 in flight (not filed on GitHub). S50-E6 owns PreviewWindow, queued. |

A customer today: Space-key on a `.json` file shows highlighted source. Cannot fold objects, search keys, copy a path, or see types without reading braces. `.jsonl` is a ShellInfo metadata peek, not even source.

## 3. Routing

Maps to **NO epic**. S50-E1 is AUMID / `--app` / Start. S50-E2 is tool contract / license. S50-E6 (queued) owns PreviewWindow copy + Open-in-app, not parser HTML. `Netie-AI/Space#3` is SEC-01. Markdown PRD-001 is `.md`. Ledger 10 already routes "suitable format" through this parser; this is that capability, not a new product.

Not a closed epic. Result: PRD amendment. Founder typed the words in session (same class as rows 45-50). **SIGNED.**

Decision vs new epic vs PARK: **SIGNED execute-now**. A new epic this wave would be WIP 3. PARK the product-widenings in section 6.

```
Repo:            Netie-AI/Space
Contract impact: none
Depends on:      none (must not wait on S50-E1/E2; must not share their files)
```

## 4. Calls on the orchestrator recommendation

| # | Call | Why |
|---|---|---|
| A | ACCEPTED, refined | Peek canvas, same exe, original HTML. File path refined: `src/NetieSpace/Assets/code-viewer/json-viewer.html` (existing glob), **not** `Assets/json/` this wave. `.jsonl` / `.ndjson` IN (line list). Kind stays `Text`. Default peek is the tree (markdown first-paint), not Edit-click (csv-editor). Invalid JSON: parse error + existing code dump. No AUMID, no `--app json`, no fourth app. |
| B | ACCEPTED | `ContentParserService.cs` owns default peek HTML. `ParseTextAsync` branches `.json` / `.jsonl` / `.ndjson` to `WrapJsonHtml`. S50-E1/E6 keep `PreviewWindow`. No csproj edit. |
| C | ACCEPTED | No how-to banner, no "press X to Y". Tooltips name Search / Copy path / Pretty / Raw / Expand. Truncate is a result line (`Showing first 400000 characters`), not "open in your editor". R-0011 parse-error lines stay. |
| D | ACCEPTED | PARK list below. |
| E | ACCEPTED | Refuse a new epic this wave. Execute-now on the peek canvas is the cheaper slice. |

## 5. SIGNED execute-now

### Exclusive file owners

| File | Role |
|---|---|
| `src/NetieSpace/Services/ContentParserService.cs` | Add `.jsonl` `.ndjson` to `TextExtensions`. Branch those + `.json` to `WrapJsonHtml`. Keep cap 400_000. Kind stays `PreviewKind.Text`. `WrapJsonHtml` reads `FindCodeViewerAsset("json-viewer.html")`, injects payload, returns `RenderedHtml`. Invalid parse: error line + existing `WrapCodeHtml`. |
| `src/NetieSpace/Assets/code-viewer/json-viewer.html` | Original Netie tree: collapse, type chips, search, copy path, pretty/raw. **MUST** keep `<pre><code class="language-json">` with the original source so `TextEditButton_Click` 6074 still finds it. Optional `file://` to existing `highlight.min.js` on Raw. No vendor. |

Optional new (no contention): `scripts/check-json-viewer.ps1` asserting the HTML ids and that `ContentParserService.cs` calls `json-viewer.html`. Do not extend `check-multieditor-assets.ps1` with PreviewWindow strings.

### PreviewWindow must NOT change

Do not edit: `PreviewWindow.xaml`, `PreviewWindow.xaml.cs`, `App.xaml.cs`, `NetieSpace.csproj`, `AppModels.cs` (no `PreviewKind.Json`), `register-open-with.ps1`, `md-editor.html`, `csv-editor.html`, `netie-viewer.html`, `installer/NetieSpace.iss`.

Trap: Edit JSON as a dedicated canvas (virtual host + postMessage like csv-editor) **is** a PreviewWindow change. Out of this wave. If the tree HTML omits `pre code`, Edit will save innerText of the tree and corrupt the file.

### Acceptance

- WHEN a person selects a `.json` file in Explorer or on the Desktop and presses Space, THE SYSTEM SHALL show a collapsible tree of that file's objects and arrays, with a type mark on each node, a search box that filters keys and values, a control that copies the JSON path of the selected node, and a pretty/raw toggle. THE SYSTEM SHALL NOT show only a highlighted source dump as the default peek.
- WHEN the file text is not valid JSON, THE SYSTEM SHALL show the parse error and the existing highlighted source dump, and SHALL NOT invent a tree.
- WHEN the person clicks Edit on that peek, THE SYSTEM SHALL use the existing source `contenteditable` (the tree page still has `pre code` with the original text) and SHALL autosave that source. THE SYSTEM SHALL NOT contenteditable the tree labels.
- WHEN the file is `.jsonl` or `.ndjson`, THE SYSTEM SHALL show a list of per-line trees (blank lines skipped; a bad line shows that line's error and does not fail the rest).
- WHEN the text is longer than 400_000 characters, THE SYSTEM SHALL truncate as today and show a result line naming the cap, with no how-to banner.
- THE SYSTEM SHALL NOT add a Netie JSON Start-menu app, `--app json`, a new AUMID, a `PreviewKind.Json`, or a how-to sentence on this canvas. Tooltips name the control.

### How to wire (for the writer, not this agent)

`WrapJsonHtml` follows `WrapCodeHtml`: emit one HTML document. Inject JSON into `<script type="application/json" id="netie-json">` (escaped). Tree script is original Netie in `json-viewer.html` (inline or linked via `FindCodeViewerAsset`). Do not `Navigate` to a virtual host (that is PreviewWindow). Do not download monaco / jsoneditor.

## 6. PARK / BAN

- JSON editor suite (form editor, JSON Schema forms, diff)
- Schema validation product
- JSON-to-CSV converter product
- `.jsonc` (JSON with comments)
- UserChoice steal for `.json`
- Second exe / `--app json` / Netie JSON AUMID / fourth ecosystem app
- Monaco, jsoneditor, AGPL/GPL vendor trees
- Cloud pretty-print
- `PreviewKind.Json` this wave
- Open-with / drop-list jsonl widening (`register-open-with.ps1` is S50-E1; PreviewWindow drop list is S50-E6)
- Palantir P1; n8n; grok-bot; leaked Claude Code

## 7. Ledger

Appended Space PRD-001 row 51. Added out-of-scope "fourth Start-menu app (Netie JSON)" and a JSON WHEN/THE SYSTEM SHALL on the success assertion. Rows 1-50 unchanged in meaning; row 50 text not rewritten. No tickets. No product code.

## Flags for the parent (not done here)

- `STATUS.md` Next does not yet name row 51. Optional one-liner: JSON peek tree execute-now; PreviewWindow frozen.
- TAS-SPACE needs a SHIPPED row for JSON tree peek when the writer lands it.
- S50-E1 still owns `NetieSpace.csproj`; do not add `Assets\json\`.

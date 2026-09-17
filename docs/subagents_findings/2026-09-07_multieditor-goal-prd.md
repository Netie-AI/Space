---
keywords: markdown, milkdown, tiptap, onlyoffice, libreoffice, p7, rust, flying-boxes, fill-sign, pptx, word, csv, themarkdowneditor
main_idea: Flying PDF boxes and local smoothness are execute-now under signed Fill. Whole-app Rust rewrite stays PARK P7. Embed Milkdown/TipTap is NEEDS-YOU (default keep Markdig). Full image/office clones are amendment. OnlyOffice AGPL BAN; do not vendor LibreOffice.
date: 2026-09-07
kind: prd_intake
product: Space
---

# PRD intake - multi-editor goal (Mode B)

PREFLIGHT: HIT. Reused `2026-09-04_pdfspace-prd-intake.md`, `2026-09-07_space25-signed-lag.md`, `2026-09-04_acrobat-pdfspace-engines.md`, `2026-09-06_ilove-parity-prd.md`, `2026-09-05_open-with-prd-intake.md`. P7 PARK already law.

Analog: TAS-SPACE live PreviewWindow + pdf.js Fill + Markdig `.md` + P8 Office HTML + P5 video NLE + LibreOffice `soffice` fallback. No frozen markdown-editor clone in TAS. themarkdowneditor.app is a paid macOS app -- DISTILL UX only, do not clone. Prefer extend-live. BAN n8n / grok-bot / AGPL / GPL / leaked trees. OnlyOffice is AGPL -- BAN copy. Palantir P1 stays parked. Do not redesign layout DNA.

## Nearness (artifacts checked, no %)

Customer-facing artifact: `NetieSpace.exe` preview in `src/NetieSpace/Views/PreviewWindow.xaml`.

What a person can do today:

- PDF Fill & Sign overlays + AcroForm widgets (`netie-viewer.html`). Snap sidecar `PdfDetectBridge`. Alignment/lag already signed execute-now (ledger 14/28/32). Parent is on flying boxes -- this agent does not touch that file.
- Markdown: Markdig HTML preview (`ContentParserService.ParseMarkdownAsync`). Edit = source `<pre contenteditable>` from `RawText` (`TextEditButton_Click`). Convert/share to PDF/HTML/DOCX already.
- Word/HTML: mammoth/OpenXML HTML + `ToggleHtmlEditModeAsync` contenteditable.
- CSV: plaintext text path (`TextExtensions` includes `.csv`), not a grid editor.
- XLSX: HTML table / digest (`OfficePreviewService.XlsxToHtml`), not Excel.
- PPTX: slide HTML / COM / LibreOffice headless (`LibreOfficePreviewService`).
- Image: rembg + compress (`ImageEditService`). Not a photo suite.
- Video: CapCut-light NLE shipped (PARKING_LOT P5).

What they cannot do:

- WYSIWYG split markdown like themarkdowneditor.app (live preview while typing source; chrome that hides; GitHub sync).
- Milkdown / TipTap / OnlyOffice inside the preview (grep empty).
- Full PPTX/Word/CSV/Excel/image products.
- Whole-app Rust `NetieSpace.exe`.

Landing: not fetched (Marketing owns building it). PRD still draft, not sliced. Open GitHub: `Netie-AI/Space#3` SEC-01 only.

## Per-ask CALL

| # | Ask | Maps to | CALL |
|---|---|---|---|
| 1 | Flying PDF boxes / smoothness | signed Fill (rows 8/14/28/32) | **execute-now**. Parent lane on `netie-viewer.html`. Rust snap already `native/netie-pdf-detect` with JS fallback. Not ChatPanel. Not a new epic. Do not second-own the viewer. |
| 2 | Embed OSS markdown editor in Space preview | row 2 text-edit vs no epic | **NEEDS-YOU** engine, then **ticket-later**. Not execute-now (contends `PreviewWindow.xaml.cs` with flying boxes). Default: keep Markdig + source edit. themarkdowneditor.app is paid macOS, not a license to copy. Split source+preview in WebView2 can ride row 2 after the PDF lane. |
| 3 | Whole-app Rust rewrite | P7 / rows 1/6/8/32 | **PARK P7**. Targeted sidecars only. |
| 4 | Full image/video/pptx/word/csv editors | row 10 / P4/P5/P8 / rows 29-30 | **amendment / PARK**. Video NLE already shipped -- do not rebuild. Light in-preview edit stays P8. OnlyOffice-class suite needs a press-release rewrite. Not an epic wave this session. |
| 5 | Download OSS into repo (Milkdown, TipTap, OnlyOffice, LibreOffice, ...) | none | **NEEDS-YOU** + license. MIT/Apache/BSD assets only if signed. **BAN** OnlyOffice AGPL. Do not vendor LibreOffice; keep installed `soffice.exe`. Do not download until signed. |

## License card (CALL 5)

| Name | License | Space call |
|---|---|---|
| Markdig | BSD-2 | ALREADY. Keep. |
| pdf.js 4.10.38 | Apache-2.0 | ALREADY. Keep. |
| Milkdown | MIT | OK if founder signs vendor. Default skip. |
| TipTap core | MIT | OK if founder signs. TipTap Pro = commercial -- BAN unless paid. |
| CodeMirror 6 | MIT | OK if founder signs (source-first analog). |
| OnlyOffice Docs / Desktop | AGPL-3.0 | **BAN**. Same class as iText. |
| LibreOffice | MPL-2.0 | Installed soffice fallback only. Do not dump the tree into `D:\Space`. |
| themarkdowneditor.app | paid macOS (Tahoe 26+) | DISTILL UX. Do not clone. GitHub sync / Codex-in-editor is not Space. |

## Contention / WIP

Flying-box parent already owns `netie-viewer.html` (and likely `PreviewWindow.xaml.cs`). A markdown WebView would also edit `PreviewWindow.xaml.cs`. Two lanes must not share that file. Markdown waits. WIP: PDF smoothness is the inspectable in-flight lane. Do not open a second PreviewWindow epic.

```
Repo:            Netie-AI/Space
Contract impact: none
Depends on:      flying-box execute-now done; then founder markdown-engine sign
```

No engine+consumer split. No Cortex PDF writer. No tickets this session (EPIC Agent later).

## Execute now

None in this PRD-agent run (no product code). Parent continues ledger 28/32 flying boxes + lag on existing Fill paths.

## Refuse

- Vendor OnlyOffice / AGPL into the repo.
- Vendor LibreOffice source.
- Clone themarkdowneditor.app (GitHub sync, macOS chrome, paid product).
- Reopen P7.
- Slice a multi-editor Office suite.
- Start Milkdown/TipTap while flying boxes are in flight.

## Ledger

Appended Space PRD-001 row 33. Rows 1-32 unchanged. Row 7 still absent.

## NEEDS-YOU

Sign one markdown engine, or refuse OSS embed:

1. Keep Markdig + source `contenteditable` (recommended). Optional later: split pane, still Markdig.
2. Vendor Milkdown MIT into `src/NetieSpace/Assets/` with LICENSE.
3. Vendor TipTap MIT core only (no Pro).

Until that sign: no download, no markdown tickets.

Row 32 online-host NEEDS-YOU is unchanged.

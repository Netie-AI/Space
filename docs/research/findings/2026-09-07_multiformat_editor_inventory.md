```yaml
keywords:
  - oss-inventory
  - markdown
  - marked
  - onlyoffice-ban
  - libreoffice
  - csv
  - pptx
main_idea: Offline dual-pane MD editor via marked MIT. Format editor inventory for goal. OnlyOffice AGPL BAN. Whole-app Rust PARK.
date: 2026-09-07
```

# Multi-format editor inventory (goal)

## Shipped this wave

| Surface | Path | License |
|---|---|---|
| PDF fill/sign glue | `Assets/pdfjs/netie-viewer.html` pageCssBox | Apache-2 (pdf.js) |
| Markdown dual-pane | `Assets/markdown/md-editor.html` + marked + KaTeX 0.16.11 MIT | MIT |
| CSV grid editor | `Assets/markdown/csv-editor.html` (+row/col) | Netie (stdlib parse) |
| Markdown preview | Markdig (existing) | BSD-2 |

## Priority backlog (not whole-app rewrite)

| Format | Space today | OSS option | Call |
|---|---|---|---|
| PDF | PDFSpace workbench | keep pdf.js + PdfSharpCore | execute (ongoing) |
| Markdown | dual-pane + KaTeX + hide-chrome | marked + KaTeX MIT; Milkdown NEEDS-YOU | shipped v2 |
| Image | rembg + compress; Rotate90 in source (needs rebuild) | keep local; no iLove clone | partial |
| Video | trim/export NLE | keep ffmpeg | shipped |
| CSV | grid + add/remove row-col | keep hand-rolled | shipped v2 |
| Word/DOCX | OpenXML HTML preview | keep; LibreOffice soffice convert if installed | inventory |
| PPTX | OpenXML HTML preview; Edit launches installed soffice | LibreOffice MPL (not vendored). OnlyOffice AGPL BAN | integrate-if-installed |
| Full Office suite | no | OnlyOffice **BAN AGPL**; LibreOffice install not repo dump | PARK |

## Rails

- P7 whole-app Rust rewrite PARK
- Rust/Go = sidecars only (snap-cell, pdfcpu)

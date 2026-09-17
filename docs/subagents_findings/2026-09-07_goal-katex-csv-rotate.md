# Goal progress - KaTeX CSV image rotate

keywords: katex, markdown, csv, image-rotate, themarkdowneditor, P7, pptx
main_idea: Offline KaTeX in md-editor (MIT 0.16.11). CSV add/remove row-col. Image Rotate90 in source (needs rebuild; RAM 0.16 GB). Goal not complete: EP FORM human, PPTX no round-trip, GitHub sync skipped, image rotate not in running exe yet.

## Markdown

`Assets/markdown/katex/` + `renderMathInElement` after marked. Line-spacing chip. DISTILL of themarkdowneditor.app math + type size, not a clone. No GitHub sync.

## CSV / image / office

CSV `addRow`/`addCol`. Image `Rotate90Cw` + toolbar. Word HTML edit already existed. PPTX still preview (OnlyOffice AGPL BAN). P7 PARK.

## Verify

```
powershell -File D:\Space\scripts\check-multieditor-assets.ps1
powershell -File D:\Space\scripts\check-pdf-bbox-glue.ps1
```

Does not prove: KaTeX in live WebView2 (RAM blocked browser). Rotate in NetieSpace.exe until rebuild.

---
keywords: [ecosystem, ilovepdf, iloveimg, video-tools, 123apps, clideo, tool-catalog, local-engine, distill]
main_idea: iLovePDF (33 web tools + 3 API-only), iLoveIMG (13 + 1 API-only) and the 123apps+Clideo video union (38) enumerated with typed options and mapped to engines on this laptop - 66 of 89 tools are fully LOCAL on FFmpeg / pikepdf / PdfSharpCore / WinRT / Office COM, 13 LOCAL-PARTIAL, 5 need an engine (PDF/A, PDF/A validate, AI upscale, background removal, speech-to-text), 1 is cloud-only (request e-sign), 4 are AI-egress; the existing convert_engine.py PDF write ops sit on banned AGPL pymupdf and must be re-based on pikepdf before any of them count as shipped.
date: 2026-09-17
cite: [https://www.ilovepdf.com/, https://www.iloveimg.com/, https://123apps.com/, https://clideo.com/tools, https://clideo.com/, https://www.iloveapi.com/docs/api-reference, https://developer.ilovepdf.com/docs/api-reference, https://www.ilovepdf.com/merge_pdf, https://www.ilovepdf.com/split_pdf, https://www.ilovepdf.com/compress_pdf, https://www.ilovepdf.com/pdf_to_word, https://www.ilovepdf.com/pdf_to_powerpoint, https://www.ilovepdf.com/pdf_to_excel, https://www.ilovepdf.com/word_to_pdf, https://www.ilovepdf.com/powerpoint_to_pdf, https://www.ilovepdf.com/excel_to_pdf, https://www.ilovepdf.com/edit-pdf, https://www.ilovepdf.com/pdf_to_jpg, https://www.ilovepdf.com/jpg_to_pdf, https://www.ilovepdf.com/sign-pdf, https://www.ilovepdf.com/pdf_add_watermark, https://www.ilovepdf.com/rotate_pdf, https://www.ilovepdf.com/html-to-pdf, https://www.ilovepdf.com/unlock_pdf, https://www.ilovepdf.com/protect-pdf, https://www.ilovepdf.com/organize-pdf, https://www.ilovepdf.com/convert-pdf-to-pdfa, https://www.ilovepdf.com/repair-pdf, https://www.ilovepdf.com/add_pdf_page_number, https://www.ilovepdf.com/scan-pdf, https://www.ilovepdf.com/ocr-pdf, https://www.ilovepdf.com/compare-pdf, https://www.ilovepdf.com/redact-pdf, https://www.ilovepdf.com/crop-pdf, https://www.ilovepdf.com/pdf-forms, https://www.ilovepdf.com/pdf-summarize, https://www.ilovepdf.com/translate-pdf, https://www.ilovepdf.com/pdf-to-markdown, https://www.ilovepdf.com/remove-pages, https://www.iloveimg.com/compress-image, https://www.iloveimg.com/resize-image, https://www.iloveimg.com/crop-image, https://www.iloveimg.com/convert-to-jpg, https://www.iloveimg.com/photo-editor, https://www.iloveimg.com/upscale-image, https://www.iloveimg.com/remove-background, https://www.iloveimg.com/meme-generator, https://www.iloveimg.com/rotate-image, https://www.iloveimg.com/jpg-to-image, https://www.iloveimg.com/html-to-image, https://www.iloveimg.com/watermark-image, https://www.iloveimg.com/blur-face, https://online-video-cutter.com/, https://online-video-cutter.com/video-editor, https://online-video-cutter.com/merge-videos, https://online-video-cutter.com/add-music-to-video, https://online-video-cutter.com/add-image-to-video, https://online-video-cutter.com/add-text-to-video, https://online-video-cutter.com/remove-logo, https://online-video-cutter.com/crop-video, https://online-video-cutter.com/rotate-video, https://online-video-cutter.com/resize-video, https://online-video-cutter.com/loop-video, https://online-video-cutter.com/volume, https://online-video-cutter.com/change-video-speed, https://online-video-cutter.com/stabilize-video, https://video-converter.com/, https://online-audio-converter.com/, https://clideo.com/cut-video, https://clideo.com/crop-video, https://clideo.com/merge-video, https://clideo.com/change-video-speed, https://clideo.com/rotate-video, https://clideo.com/flip-video, https://clideo.com/reverse-video, https://clideo.com/resize-video, https://clideo.com/add-subtitles-to-video, https://clideo.com/add-audio-to-video, https://clideo.com/mute-video, https://clideo.com/compress-video, https://clideo.com/video-converter, https://clideo.com/gif-maker, https://clideo.com/loop-video, https://clideo.com/split-video, https://clideo.com/filter-video, https://clideo.com/adjust-video, https://clideo.com/slideshow-maker, https://clideo.com/add-image-to-video, https://clideo.com/stop-motion, https://clideo.com/dpi-converter, https://clideo.com/add-text-to-video, https://clideo.com/auto-subtitle-generator, https://clideo.com/image-converter, https://clideo.com/gif-editor, https://clideo.com/video-maker, https://clideo.com/video-to-text, https://clideo.com/meme-maker, https://www.adobe.com/products/catalog.html, https://www.adobe.com/express/feature/quick-actions]
---

# Ecosystem tool catalog - Netie PDF / Netie Image / Netie Video

Snapshot 2026-09-17 of what iLovePDF, iLoveIMG, 123apps and Clideo actually list, with
the user-facing options each tool exposes, mapped to what can run on this laptop.
Research only. No code changed.

## Conventions

- `choice[a/b/c]` = one of the listed values (slash used instead of pipe so the tables
  survive markdown). `int` = number, `%` = percent, `page-range` = "1,3,5-9,-2-end" style
  spec, `grid` = position grid, `file` = secondary upload, `text` = free text.
- Options marked `(API)` come from the public iLoveAPI reference and are the exact enum
  the web tool drives; `(web)` = visible on the tool page; `not public` = only shown after
  upload and not in the API reference.
- Verdicts: `LOCAL`, `LOCAL-PARTIAL` (named limitation), `NEEDS-ENGINE` (missing engine
  + license), `CLOUD-ONLY`, `AI-EGRESS` (cloud LLM, gated behind Space#3 disclose-first).
- `exists`: `yes (File.Method)` / `partial (...)` / `no`, from a grep of
  `src/NetieSpace/Services/*.cs` and `tools/convert_engine.py`.
- Engines assumed present: .NET 8 + PdfSharpCore 1.3.65 (MIT), PdfPig 0.1.9 (Apache-2.0),
  DocumentFormat.OpenXml, WPF/WIC, System.Drawing, WinRT (Windows.Data.Pdf,
  Windows.Media.Ocr, Windows.Media.FaceAnalysis, Windows.Graphics.Imaging,
  Windows.Graphics.Capture, Windows.Media.Capture, Windows.Media.SpeechSynthesis),
  WebView2 (PrintToPdfAsync, CapturePreviewAsync), FFmpeg 8.1.2 full build, Tesseract 5,
  Office 16 COM, Python 3.14 with pikepdf (MPL-2.0), img2pdf (LGPL-3.0), ocrmypdf
  (MPL-2.0), Pillow, openpyxl, python-pptx. Banned: PyMuPDF/fitz, Ghostscript, OnlyOffice.

## Repo fact that changes the `exists` column

`tools/convert_engine.py` implements `pdf-watermark` (line 3126), `pdf-protect` (3175),
`pdf-unlock` (3209, 3234), `pdf_to_images` (3038) and four more PDF write helpers
(901, 1279, 2713, 2759, 2808, 2835) on `require_pymupdf()` (2108), and `to-md` falls back
to `pymupdf4llm` / `pymupdf` text (252, 266). PyMuPDF is AGPL and banned by the product
brief. Every `exists` row that routes through these is marked `partial (AGPL path)`:
the UI and the ConversionService signature exist, the engine underneath must be
re-based on pikepdf (MPL-2.0, installed) or PdfSharpCore before the tool counts as
shipped. Space-side .NET code (PdfEditService on PdfSharpCore + PdfPig, PdfCmsSigner,
FfmpegService, ImageEditService on WIC) is clean.

## Netie PDF

Source catalog: iLovePDF homepage grid, 33 tools, grouped the way the site groups them
(Organize / Optimize / Convert to PDF / Convert from PDF / Edit / Security / Intelligence),
plus 3 API-only tools at the end. Every web tool accepts upload from computer, Google
Drive and Dropbox; that cloud intake is not repeated per row.

### Organize PDF

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| pdf-merge | Merge PDF | Combine PDFs in a chosen order | PDF, multiple | drag order; "order by name" button (web); show/hide covers toggle (web); no API params | single PDF | PdfSharpCore page copy | LOCAL | yes (PdfEditService.MergeOrdered, PdfEditService.Merge, PdfMergeWindow) |
| pdf-split | Split PDF | Cut into ranges, single pages, size-bounded chunks | PDF, single per task | split_mode choice[ranges/fixed_range/remove_pages/filesize] (API); ranges page-range text; fixed_range int pages per file; merge_after checkbox; web adds "Size" mode "files no larger than N MB" with optional compression (Premium) and "Smart ranges" driven by a prompt text (API Smart Split, AI) | zip of PDFs, or one PDF when merge_after | PdfSharpCore ExtractPages loop; filesize mode = iterative page-count bisection on saved size; smart mode = LLM | LOCAL (smart-ranges sub-mode is AI-EGRESS) | yes (PdfEditService.SplitAllPages, PdfEditService.ExtractPages); fixed_range and filesize modes not built |
| pdf-remove-pages | Remove pages | Delete selected pages | PDF, single | click pages; shift for ranges (web); page-range | PDF | PdfSharpCore ApplyPagePlan | LOCAL | yes (PdfEditService.ApplyPagePlan, PdfOrganizeWindow) |
| pdf-extract-pages | Extract pages | Pull chosen pages out | PDF, single | page selection page-range; "merge extracted pages into one PDF" checkbox (web); else one PDF per page | PDF or zip | PdfSharpCore ExtractPages | LOCAL | yes (PdfEditService.ExtractPages) |
| pdf-organize | Organize PDF | Reorder, rotate, delete, mix pages from several PDFs, insert blank | PDF, multiple | drag thumbnails; order files by name; sort pages by number; per-page rotate/delete; add blank page; reset (web) | single PDF | PdfSharpCore ComposePages | LOCAL | yes (PdfEditService.ComposePages, PdfEditService.CreateBlankPdf, PdfOrganizeWindow) |
| pdf-scan | Scan to PDF | Phone camera shots relayed to the browser via QR, imaged into PDF | camera JPGs from phone, multiple | orientation choice[portrait/landscape]; page size choice[fit/A4/letter]; margin choice[none/small/big]; merge all into one PDF checkbox (web) | PDF | Windows.Media.Capture (webcam) or WIA scanner + img2pdf / PdfSharpCore; the phone QR relay is a hosted service | LOCAL-PARTIAL (webcam/WIA scanner capture is local; the phone-to-browser relay is CLOUD-ONLY) | no |

### Optimize PDF

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| pdf-compress | Compress PDF | Reduce file size | PDF, multiple | compression_level choice[extreme/recommended/low] (API; web labels "Extreme / Recommended / Less compression") | PDF | pikepdf: object streams, stream recompression, drop unreferenced objects, de-duplicate; image XObjects re-encoded with Pillow (downsample + JPEG quality per level); no Ghostscript | LOCAL-PARTIAL (no full re-distill; ratio on vector-heavy files is small - show expected size before/after honestly) | no (menu disabled honest per 2026-09-06 distill) |
| pdf-repair | Repair PDF | Recover a damaged PDF | PDF, multiple | none (web); "may download as a different format if detected" | PDF | pikepdf open (qpdf xref reconstruction) + save; PdfClassicXref rewrite for Space-written files | LOCAL-PARTIAL (xref/trailer/stream-length repair only; no content-stream healing) | partial (PdfEditService.RewriteClassicXref, PdfClassicXref - xref rewrite only) |
| pdf-ocr | OCR PDF | Make scanned PDF selectable and searchable | PDF, multiple | ocr_languages multi-choice, 60+ languages (API list eng/fra/deu/spa/chi_sim/chi_tra/jpn/kor/ara/rus/...) | PDF with text layer | ocrmypdf + Tesseract 5 (installed tessdata only); Windows.Media.Ocr for plain text | LOCAL (language coverage = installed tessdata; document that) | yes (ConversionService.PdfOcrAsync -> convert_engine pdf-ocr -> ocrmypdf) |

### Convert to PDF

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| pdf-from-jpg | JPG to PDF | Images into PDF pages | JPG (web), multiple | orientation choice[portrait/landscape]; pagesize choice[fit/A4/letter]; margin choice[none/small/big] (web) or int px (API); merge_after checkbox (default on) | PDF or zip | img2pdf (lossless embed) or PdfSharpCore DrawImage | LOCAL | partial (PdfEditService.InsertImageAsPage single image; convert_engine file_to_pdf) |
| pdf-from-word | Word to PDF | DOC/DOCX to PDF | DOC, DOCX, multiple | none | PDF | Word COM ExportAsFixedFormat (Office 16 present); no LibreOffice fallback on this laptop | LOCAL (requires Office; absence must be visible, R-0011) | partial (ConversionService.ConvertToPdfAsync -> convert_engine file_to_pdf; verify it routes to Office COM not soffice) |
| pdf-from-powerpoint | PowerPoint to PDF | PPT/PPTX to PDF | PPT, PPTX, multiple | none | PDF | PowerPoint COM ExportAsFixedFormat | LOCAL (requires Office) | partial (same path; LibreOfficePreviewService exists but LibreOffice not installed) |
| pdf-from-excel | Excel to PDF | XLS/XLSX to PDF | XLS, XLSX, multiple | none | PDF | Excel COM ExportAsFixedFormat | LOCAL (requires Office) | partial (same path) |
| pdf-from-html | HTML to PDF | Web page or HTML bundle to PDF | URL text; HTML/CSS/JS files or ZIP; pasted HTML | viewport choice[1200/1920/1440/768/320 px]; page size choice[A3/A4/A5/Letter]; "one long page" checkbox; orientation choice[portrait/landscape]; margin choice[none/small/big]; "try to block ads" checkbox; "remove overlay popups" checkbox; preview before convert (web) | PDF | WebView2 CoreWebView2.PrintToPdfAsync with CoreWebView2PrintSettings (page size, orientation, margins, scale) ; one-long-page = custom page height from document scrollHeight; popup removal = injected script; ad blocking = not provided | LOCAL (ad-block option omitted or best-effort only) | no (HtmlDocxWriter covers HTML->DOCX, not PDF) |

### Convert from PDF

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| pdf-to-jpg | PDF to JPG | Render each page, or extract embedded images | PDF, multiple | pdfjpg_mode choice[pages/extract] (API); quality choice[normal/high] (web; no DPI field) | zip of JPGs (one JPG when one page) | Windows.Data.Pdf PdfPage.RenderToStreamAsync at chosen DestinationWidth -> WIC JPEG encoder; extract mode = PdfPig page.GetImages() raw bytes | LOCAL | partial (convert_engine pdf_to_images - AGPL pymupdf path, re-base on Windows.Data.Pdf) |
| pdf-to-word | PDF to Word | PDF to editable DOC/DOCX | PDF, multiple | OCR choice[no OCR/OCR (Premium)] with 14 OCR languages (web); output DOCX | DOCX | Word COM: open PDF (Word PDF Reflow) then SaveAs2 wdFormatXMLDocument; scanned input = ocrmypdf first | LOCAL-PARTIAL (needs Office; fidelity is Word reflow, not Solid Documents) | partial (convert_engine file_to_docx; check it uses Word COM) |
| pdf-to-powerpoint | PDF to PowerPoint | PDF to editable PPT/PPTX | PDF, multiple | OCR choice[no OCR/OCR], 14 languages (web) | PPTX | python-pptx: one slide per page as picture (Windows.Data.Pdf render) plus PdfPig word boxes as text shapes; no reflow engine exists locally | LOCAL-PARTIAL (picture slides + approximate text boxes, not true reflow) | no |
| pdf-to-excel | PDF to Excel | Tables into spreadsheets | PDF, multiple | OCR choice[no OCR/OCR]; layout choice[one sheet/multiple sheets] (web) | XLSX | PdfPig word coordinates -> row/column clustering -> openpyxl or DocumentFormat.OpenXml; Word COM reflow tables as second route | LOCAL-PARTIAL (heuristic table detection; merged cells and borderless tables degrade) | partial (convert_engine file_to_xlsx) |
| pdf-to-pdfa | PDF to PDF/A | ISO archival conformance | PDF, multiple | conformance choice[pdfa-1b/1a/2b/2u/2a/3b/3u/3a] (default 2b); allow_downgrade checkbox (API + web) | PDF/A | pikepdf can write XMP + OutputIntent but cannot embed missing fonts or convert colour spaces; the standard converter is Ghostscript; Word COM ExportAsFixedFormat(UseISO19005_1) gives PDF/A-1b only after Word reflow | NEEDS-ENGINE (Ghostscript is AGPL and banned; LibreOffice MPL-2.0 exports PDF/A from Office sources only and is not installed; Word COM PDF/A-1b re-export is a lossy fallback, not a conversion) | no |

### Edit PDF

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| pdf-rotate | Rotate PDF | Rotate whole files, many at once | PDF, multiple | direction choice[left/right] per file, 90-degree steps; filter choice[all/portrait/landscape]; reset all (web) | PDF(s) | PdfSharpCore page.Rotate | LOCAL | yes (PdfEditService.RotatePages, PdfEditService.GetPageRotations) |
| pdf-page-numbers | Page numbers | Stamp page numbers | PDF, multiple | page mode choice[single/facing pages]; first_cover checkbox; position grid 3x2 (vertical choice[top/bottom], horizontal choice[left/center/right]); margin choice[small/recommended/big] (API: vertical/horizontal_position_adjustment int); pages page-range (web presets: all, from page N, skip first/last up to 5); starting_number int; text choice[{n} only/Page {n}/Page {n} of {p}/custom] with variables {n} {p} {r}; font_family choice[Arial/Arial Unicode MS/Verdana/Courier/Times New Roman/Comic Sans MS/Impact/WenQuanYi Zen Hei/Lohit Marathi]; font_size int (default 14); font_color color; background color; shadow checkbox; opacity int 0-100 (web) | PDF(s) | PdfSharpCore XGraphics.DrawString per page with installed system fonts | LOCAL | no |
| pdf-watermark | Watermark | Stamp text or image | PDF, multiple | mode choice[text/image]; text; image file PNG/JPG; pages page-range; position grid 3x3 (vertical choice[top/middle/bottom], horizontal choice[left/center/right]) + int adjustments; mosaic (tile) checkbox; rotation choice[0/45/90/180/270] (web) or int 0-360 (API); font_family list as above; font_style choice[normal/bold/italic]; font_size int; font_color color; background color + shadow (web); transparency choice[100/75/50/25 %] (web) or int 1-100 (API); layer choice[above/below] | PDF(s) | PdfSharpCore XGraphics (XGraphicsPdfPageOptions.Append for above, Prepend for below), alpha via XColor, rotation via transform, tile loop | LOCAL | partial (ConversionService.PdfWatermarkAsync: text only, opacity + angle + pages; AGPL pymupdf path, re-base on PdfSharpCore) |
| pdf-crop | Crop PDF | Crop margins or a chosen area | PDF, single | drag rectangle; apply choice[all pages/current page]; reset all (web); auto-margin crop not shown | PDF | PdfSharpCore set /CropBox (optionally /MediaBox) per page, non-destructive | LOCAL | no |
| pdf-edit | Edit PDF | Add text, images, shapes, freehand; move or delete existing content | PDF, single | add text (font, size, color); image file; shapes; freehand draw; highlight/annotate; z-order front/back; select existing text to move/edit/delete (web) | PDF | PdfSharpCore overlays for additions; existing text edit = whiteout + retype (as Space does today); true in-place text edit needs a content-stream rewriter on PdfPig + PdfSharpCore | LOCAL-PARTIAL (existing-text edit is whiteout + retype, not reflow) | partial (PdfEditService.DrawFillTexts, DrawHighlights, StampSignatureImage, InsertImageAsPage; PreviewWindow "Edit text / OCR" mode) |
| pdf-forms | PDF Forms | Detect fields, create fillable fields, fill forms | PDF, single | auto-detect fields; field types choice[text/checkbox/radio/dropdown list] (web); fill values; flatten/export not shown | PDF | PdfSharpCore AcroForm field creation; fill via PdfFormBridge; detection via PdfDetectBridge + PdfEditService.GetFormFieldRects | LOCAL | partial (PdfFormBridge.TryList/TryFill, PdfEditService.GetFormFieldRects, PdfDetectBridge.TrySnapCell; widget creation "/FT insert" parked) |

### PDF Security

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| pdf-unlock | Unlock PDF | Remove password protection | PDF, multiple | password text when a user password is set; owner-only restrictions removed without it (API: no params) | PDF | pikepdf open(password=...) save(encryption=False) | LOCAL | yes (ConversionService.PdfUnlockAsync; AGPL pymupdf path, re-base on pikepdf) |
| pdf-protect | Protect PDF | Password + permission restrictions | PDF, multiple | open password text (min 8 chars, mixed classes hinted); separate permissions password text; permission checkboxes[printing/modification/copy-extract/comments/form filling/accessibility extraction/assembly] (web); encryption strength not public | PDF | pikepdf Encryption(R=6 AES-256, owner/user, Permissions flags) | LOCAL | yes (ConversionService.PdfProtectAsync with --owner and --perm; AGPL pymupdf path, re-base on pikepdf; permission UI not built) |
| pdf-sign-self | Sign PDF - sign yourself | Place your own signature and fields | PDF, single | signature source choice[draw/type/upload PNG,JPG,SVG/draw on phone]; elements list[signature/initials/name/date/text/company stamp]; signature type choice[simple/digital certificate] (web) | PDF (+ PDF/A option, audit trail on web) | PdfEditService stamps + PdfCmsSigner PKCS#7 detached with a PFX | LOCAL | yes (PdfEditService.StampSignatureImage, StampTypedSignature, RenderTypedSignaturePng; PdfCmsSigner.SignFile, LoadPfx; SignatureDialog, SignatureLibrary) |
| pdf-sign-request | Sign PDF - request signatures | Send to others, collect signatures | PDF + signer list | signers (name, email, phone); role choice[signer/validator/viewer]; lock_order checkbox; expiration int 1-130 days (web default 15); reminders checkbox + cycle days; access code text; force_signature_type choice[all/text/sign/image]; language choice (20+); custom subject/message; branding logo; save as template; eIDAS timestamp, SMS verification, QR verification, audit trail (API + web) | signed PDF + audit PDF | hosted signer portal, email/SMS delivery, timestamp authority | CLOUD-ONLY | no |
| pdf-redact | Redact PDF | Permanently remove text and graphics | PDF, single | selection choice[manual area/search text]; preset patterns choice[text/credit card/phone number/email]; clear all; review before download; runs text recognition (web) | PDF | pattern search via PdfPig words + regex; removal by rasterising affected pages (Windows.Data.Pdf render -> paint boxes -> re-embed as image page via PdfSharpCore), optional re-OCR with ocrmypdf; a true content-stream scrubber is a later own-code step | LOCAL-PARTIAL (redacted pages are rasterised; their text layer is lost unless re-OCR'd - say so in the UI) | no |
| pdf-compare | Compare PDF | Show differences between two versions | 2 PDFs | mode choice[semantic text/content overlay]; scroll sync checkbox; change report with count (web) | on-screen view (+ optional report) | text mode: PdfPig words per page + LCS/diff (DiffPlex MIT or own); overlay mode: Windows.Data.Pdf render both -> per-pixel diff via WIC -> coloured composite | LOCAL | no |

### PDF Intelligence

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| pdf-summarize | AI Summarizer | Summarise a PDF | PDF, single | length choice[short/medium/long]; model choice[standard/advanced (credits)]; language choice (25 codes, API); output_format choice[pdf/md] (API); copy/download (web); requires login (web) | text / MD / PDF | local extractive summary (LocalSummaryService) or on-device Phi Silica via WindowsAiClient where present; otherwise cloud LLM | AI-EGRESS (local extractive fallback exists; cloud gated by Space#3) | partial (LocalSummaryService.Build, AiService chat summary) |
| pdf-translate | Translate PDF | AI translation keeping layout | PDF, single | source language choice (80+, auto); target language choice (23); layout choice[keep layout/only text]; model choice[standard/advanced] (web) | PDF | no local MT engine on this laptop; re-typesetting can reuse the whiteout + retype path once text is translated | AI-EGRESS | no |
| pdf-to-markdown | PDF to Markdown | PDF to MD keeping headings, tables, lists, links | PDF, multiple | none (API "no params") ; images preserved (web) | MD | markitdown (MIT) + PdfPig font-size heading heuristics + own table detector; ocrmypdf for scans | LOCAL-PARTIAL (heading/table detection is heuristic; iLovePDF's is AI-assisted) | yes (ConversionService.ConvertToMarkdownAsync -> markitdown; drop the pymupdf4llm fallback, AGPL) |

### API-only tools (no homepage tile)

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| pdf-smart-split | Smart Split | Split by a natural-language prompt | PDF | prompt text (API) | zip | LLM over PdfPig page text | AI-EGRESS | no |
| pdf-validate-pdfa | Validate PDF/A | Check conformance | PDF | conformance choice[pdfa-1b/.../3a]; allow_downgrade checkbox (API) | report | veraPDF (Java) | NEEDS-ENGINE (veraPDF, dual GPLv3 / MPL-2.0; not installed) | no |
| pdf-extract-text | Extract Text | Plain or detailed text with coordinates | PDF | detailed checkbox -> page, x, y, width, font name, font size per run (API) | TXT / JSON | PdfPig words + letters | LOCAL | yes (PdfEditService.GetTextBoxes, PdfPageMaterializer.ExtractPages) |

## Netie Image

Source catalog: iLoveIMG homepage grid, 13 tools, grouped Optimize / Modify / Convert /
Create / Security, plus the API-only Repair Image. Clideo's Image Converter and DPI
Converter (image-adjacent tools on a video site) are folded in at the end. Batch upload
is offered by every iLoveIMG tool.

| id | name | category | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|---|
| img-compress | Compress IMAGE | Optimize | Shrink file size keeping the format | JPG, PNG, SVG, GIF; multiple | compression_level choice[extreme/recommended/low] (API); web runs automatic (level not public) | same format, zip for many | WIC JpegBitmapEncoder QualityLevel; PNG palette quantise + optimise (Pillow); GIF per-frame optimise (Pillow); SVG whitespace/metadata minify (own) | LOCAL | yes (ImageEditService.Compress, CompressJpeg, PromptCompress) - GIF flattens to a still and SVG is not handled, so partial for those two |
| img-resize | Resize IMAGE | Modify | Resize by pixels or percent | JPG, PNG, SVG, GIF; multiple | resize_mode choice[pixels/percentage]; pixels_width int; pixels_height int; percentage choice[25/50/75 % smaller] (web) or int (API); maintain_ratio checkbox; no_enlarge_if_smaller checkbox; entered size acts as maximum across a mixed batch (web) | same format | WIC TransformedBitmap / Pillow LANCZOS; SVG = rewrite width/height/viewBox; GIF per frame | LOCAL | no |
| img-crop | Crop IMAGE | Modify | Crop a rectangle | JPG, PNG, GIF; multiple | width int; height int; x int; y int (API + web fields); visual drag rectangle; aspect presets not public | same format | WIC CroppedBitmap / Pillow crop; GIF per frame | LOCAL | partial (PreviewWindow region select feeds chat clip, no save-as-crop) |
| img-to-jpg | Convert to JPG | Convert | Many formats to JPG | PNG, GIF, TIF, PSD, SVG, WEBP, HEIC, RAW (CR2, RW2, NEF, ARW, SR2, ORF, PEF, RAF, RAW); multiple | quality choice[recommended/high (Premium, keeps pixel size)] (web) | JPG, zip | WIC decoders (PNG, GIF, TIFF native; WEBP, HEIC/HEIF, RAW via the Microsoft Store codec packs when installed); SVG rasterised through WebView2 CapturePreview; PSD flattened composite via Pillow | LOCAL-PARTIAL (WEBP/HEIC/RAW depend on optional Windows codec packs; SVG via WebView2; PSD composite only) | partial (convert_engine convert_image via Pillow) |
| img-from-jpg | Convert from JPG | Convert | JPG to PNG, GIF, or animated GIF from several JPGs | JPG; multiple | to choice[png/gif/gif_animation] (web) or [jpg/png/gif/gif_animation/heic] (API); gif_time int seconds per image (API hundredths, default 50); gif_loop checkbox | PNG / GIF / animated GIF | WIC PNG/GIF encoders; animated GIF via Pillow save_all + duration + loop; HEIC encode only when the HEIF codec pack is present | LOCAL | partial (convert_engine convert_image) |
| img-upscale | Upscale Image | Optimize | Enlarge with detail preservation | images under 6 MP; multiple | multiplier choice[2/4] (API + web) | same format | Real-ESRGAN via ONNX Runtime, or ComfyUI opt-in; Lanczos/bicubic in WIC is a resize, not an upscale | NEEDS-ENGINE (ONNX Runtime MIT + Real-ESRGAN weights BSD-3-Clause; ComfyUI GPL-3.0 opt-in over HTTP is already the pattern) | partial (ComfyUiClient exists, no upscale workflow shipped) |
| img-remove-bg | Remove background | Optimize | Cut the subject out | JPG, PNG; multiple | none (web); transparent PNG implied | PNG | rembg + U2-Net, or ONNX Runtime + ISNet/BiRefNet; Windows App SDK ImageObjectExtractor only on Copilot+ NPU hardware | NEEDS-ENGINE (rembg MIT + U2-Net weights Apache-2.0; or onnxruntime MIT + ISNet MIT; not installed on this laptop) | partial (BackgroundRemovalService.RemoveAsync -> tools/remove_bg.py requires rembg; IsRembgAvailableAsync reports honestly) |
| img-rotate | Rotate IMAGE | Modify | Rotate many images at once | JPG, PNG, GIF; multiple | direction choice[left/right] per image, 90-degree steps; filter choice[all/portrait/landscape] for bulk (web); rotation int per file (API) | same format | WIC TransformedBitmap RotateTransform + EXIF orientation reset; GIF per frame | LOCAL | partial (ImageEditService.Rotate90Cw, clockwise only, single file) |
| img-watermark | Watermark IMAGE | Security | Stamp text or image over pictures | JPG, PNG, GIF; multiple | elements list, each: type choice[text/image]; text; image file; gravity grid 3x3 choice[NorthWest/North/NorthEast/West/Center/East/SouthWest/South/SouthEast]; vertical/horizontal adjustment int %; rotation int 0-360; font_family choice[Arial/Impact/Arial Unicode MS/Verdana/Courier/Comic Sans/Times New Roman/Lohit...]; font_style choice[normal/bold/italic]; font_size int; font_color color; background color + shadow checkbox (web); transparency int 1-100; mosaic checkbox (API) | same format | WPF DrawingVisual + RenderTargetBitmap or System.Drawing Graphics with alpha, tiled loop for mosaic | LOCAL | no |
| img-blur-face | Blur face | Security | Detect and blur faces or chosen areas | images (formats not stated); multiple | detection choice[automatic/customised]; sensitivity choice[low/recommended/high]; "add blur area" rectangles; reference portrait file to match a person; per-face toggle choice[blur/exclude] (web); blur strength not public | same format | Windows.Media.FaceAnalysis.FaceDetector for boxes + Gaussian/pixelate via WIC or Pillow; manual rectangles trivial | LOCAL-PARTIAL (detect + blur local; "match this reference face" needs face identification, which FaceDetector does not do) | no |
| img-html-to-image | HTML to IMAGE | Convert | Web page to JPG or SVG | URL text; HTML/CSS/JS files or ZIP; pasted HTML | output choice[jpg/svg]; viewport choice[1200/1920/1440/768/320 px]; block ads checkbox; remove popups checkbox; preview (web) | JPG or SVG | WebView2 CapturePreviewAsync (PNG/JPEG), full page by resizing to scrollHeight; SVG output only as a PNG wrapped in an SVG element | LOCAL-PARTIAL (JPG/PNG yes; true vector SVG snapshot not available) | no |
| img-photo-editor | Photo editor | Create | Text, effects, frames, stickers | JPG, PNG, GIF; single | text (font, size, color, shadow, opacity); effects/filters list not public; frames; stickers; crop/rotate (web) | same format | WPF canvas editor over WIC; effects via WPF ShaderEffect or Pillow ImageEnhance/ImageFilter; frames/stickers = own asset pack | LOCAL | no |
| img-meme | Meme generator | Create | Caption templates or own images | JPG, PNG, GIF; single or template | top/bottom text; text position choice[inside/outside]; font list as watermark; size int; color; background color; shadow checkbox; opacity int; extra text/image layers (web) | JPG / PNG / GIF | same stamping path as img-watermark plus letterbox pad | LOCAL (product rejected it on 2026-09-06; listed for completeness) | no |
| img-repair | Repair image (API-only) | Optimize | Recover a corrupted image | images | none (API) | image | Pillow LOAD_TRUNCATED_IMAGES + re-encode; WIC tolerant decode | LOCAL-PARTIAL (truncation/header repair only) | no |
| img-convert-any | Image converter (Clideo) | Convert | Any-to-any image conversion, image to video | GIF, BMP, HEIC, JPG, PNG, WEBP, TIFF, SVG | output format choice from the same list plus [mp4/avi/mov/webm]; per-format settings not public | image or video | WIC + Pillow; image-to-video via FFmpeg -loop 1 | LOCAL (HEIC/WEBP codec-pack caveat as above) | partial (convert_engine convert_image) |
| img-dpi | DPI converter (Clideo) | Convert | Change stored DPI metadata | JPEG, PNG, BMP, WBMP, GIF, WEBP, JP2, DCM, TIFF, PPM | dpi choice[600/300/200/150/100/72/custom int] | same format | Pillow save(dpi=) / WIC metadata writer | LOCAL | no |

## Netie Video

Source catalogs: 123apps video tools (online-video-cutter.com family, video-converter.com,
online-audio-converter.com for extraction) and Clideo /tools. The two lists are unioned
and de-duplicated; when one source lacks a tool the other has, the row names the source.
Grouping follows Clideo's headings, merged with 123apps' flat list. Format lists are the
sites' own claims (123apps "30+ formats, up to 4 GB"; Clideo "MP4, AVI, MOV, WMV, ...",
500 MB free tier). Neither site lists a frame/screenshot extractor, a fade-only tool or
a watermark-removal-by-inpainting tool; those are not invented here.

### Cut and assemble

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| vid-trim | Trim / Cut video (123apps Trim Video, Clideo Cut video) | Keep or drop a time range | video (MP4, MOV, AVI, WEBM, WMV, M4V, HEVC, MKV, MPEG, 3GP, GIF...); single | start/end time; mode choice[extract selected/delete selected] (Clideo); fade in/out checkbox and crossfade of remaining parts (Clideo); in-tool crop + rotate 90/180/270 (123apps); output format choice; quality choice (123apps) | video | FFmpeg -ss/-to; stream copy on keyframes, else re-encode | LOCAL | yes (FfmpegService.TrimAsync, DeleteMiddleAsync, ExportKeptSegmentsAsync) |
| vid-split | Split video (Clideo) | Cut into parts, rearrange, export separately | video; single | split points (time list); drag to reorder; per-segment settings; export then re-edit for more cuts | several video files | FFmpeg -ss/-to per part, or segment muxer | LOCAL | partial (VideoEditSession.SplitAt / SplitAtPacked on the timeline; no per-part file export) |
| vid-merge | Merge videos (both) | Join clips, images and audio in order | video + images + audio; multiple | drag order; trim on timeline (123apps scissors); image duration int s (Clideo); crossfade transition (Clideo); aspect ratio choice; "Transform" to remove black bars (123apps); add audio with length/volume (Clideo); output format choice[mp4/mkv/mov/...] | video | FFmpeg concat demuxer (same codec) or filter_complex concat + scale/pad + xfade + amix | LOCAL | yes (FfmpegService.MergeAsync, ExportTimelineAsync with xfade/acrossfade; FfmpegExportGraph.ScalePad; VideoOrderWindow) |
| vid-loop | Loop video (both) | Repeat a clip | video or GIF; single | repeat count int 2-16 (123apps) or choice[2/3/4/5/6/infinite] (Clideo, infinite = GIF); output format choice[mp4/mkv/mov/original] (123apps); 123apps has a GIF looper variant | video or GIF | FFmpeg -stream_loop N; infinite = GIF with loop=0 | LOCAL | no |

### Transform

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| vid-crop | Crop video (both) | Cut the frame | video; single | aspect preset choice[free/1:1/4:3/16:9/9:16 and social presets: YouTube cover, Facebook, Instagram story/feed] ; custom width/height px (Clideo); drag rectangle; output format choice | video | FFmpeg crop=w:h:x:y | LOCAL | no |
| vid-resize | Resize video (both) | Change resolution or fit a platform frame | video; single | resolution choice[1080p or custom w/h] (123apps); social presets (Instagram story/post/IGTV, YouTube 240p-2160p, Discovery/TrueView ads, Facebook cover/in-stream, Snapchat, Twitter, Pinterest, LinkedIn - exact ratios not public) (Clideo); fit mode choice[fit with background/fill-crop]; background choice[preset color/blur/hex color]; keep aspect checkbox; output format choice (20+) | video | FFmpeg scale + pad (colour) or crop; blurred background via split, scale, boxblur, overlay | LOCAL | partial (FfmpegService.ApplyExportPresetAsync = scale + pad to preset size only) |
| vid-rotate | Rotate video (both) | Turn the frame | video; single | angle choice[90 left/90 right/180] (123apps lists 90/180/270); whole video only (Clideo); output format choice; preview | video | FFmpeg transpose=1/2 or -display_rotation metadata without re-encode | LOCAL | no |
| vid-flip | Flip video (both) | Mirror | video; single | axis choice[horizontal/vertical]; output format choice; preview | video | FFmpeg hflip / vflip | LOCAL | no |
| vid-stabilize | Stabilize video (123apps) | Reduce camera shake | video; single | timeline range; strength not public; export format | video | FFmpeg vidstabdetect + vidstabtransform (libvidstab is in the full GPL build; called as a process) or deshake filter | LOCAL | no |

### Speed and time

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| vid-speed | Change video speed (both) | Faster or slower | video; single | speed slider 0.25x-4x with six presets (values not public) (Clideo); presets choice[0.5x/2x/3x] (123apps); audio choice[keep pitch-synced/mute] (Clideo); output format choice | video | FFmpeg setpts + chained atempo (0.5-2.0 per stage) | LOCAL | yes (VideoEditSession.ApplySpeedAt; FfmpegExportGraph.ClipVideoFilters setpts; FfmpegService atempo chain) |
| vid-reverse | Reverse video (Clideo) | Play backwards | video; single | speed choice[slow motion/real-time/extra fast]; audio choice[reverse with video/mute]; output format choice; preview | video | FFmpeg reverse + areverse (chunked for long inputs, memory-bound) | LOCAL (long files reversed in segments then concatenated) | no |
| vid-stop-motion | Stop motion (Clideo) | Choppy clip-rate effect, optional boomerang | video; single | clip rate int fps; speed; reverse/boomerang checkbox; output format choice | video | FFmpeg fps= + setpts + concat with reversed copy | LOCAL | no |

### Audio

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| vid-mute | Mute video (Clideo; 123apps converter "remove audio") | Strip every audio track | video; single | none; permanent; preview | video | FFmpeg -an -c:v copy | LOCAL | partial (TimelineProject clip/track Muted flags honoured on export; no one-click mute-and-save) |
| vid-volume | Change video volume (123apps) | Boost or lower loudness | video; single | volume slider (0-200 % on the sibling audio tool; range not stated here); no normalise/fade; output format choice[mp4/avi/flv/3gp/mkv/m4v] | video | FFmpeg volume=; loudnorm optional | LOCAL | no |
| vid-add-audio | Add audio / music to video (both) | Mix or replace the soundtrack | video + audio (MP3, WAV, M4A, AAC, ...) | mode choice[mix/replace] (Clideo); video volume int 0-200 %; audio volume int 0-200 %; trim audio range; start offset time; loop shorter audio checkbox (Clideo); detach original audio (123apps); single music track per project (Clideo) | video | FFmpeg -i audio, amix / volume / adelay / aloop, -shortest | LOCAL | partial (FfmpegService.ExportTimelineAsync amix over multi-track project) |
| vid-extract-audio | Extract audio / video to MP3 (123apps audio converter; Clideo video converter to MP3/WAV/OGG/FLAC) | Save the soundtrack as an audio file | video (300+ formats); multiple, zip batch (123apps) | format choice[mp3/wav/m4a/flac/ogg/amr/mp2/m4r]; quality/bitrate choice incl. VBR; sample rate; channels; ID3 tags text (title, artist, album, year, genre); fade in/out; reverse; voice removal; pitch; speed; equaliser (123apps advanced) | audio, zip | FFmpeg -vn with libmp3lame/aac/flac/libvorbis/libopus; afade, areverse, asetrate/atempo, equalizer; metadata via -metadata | LOCAL | partial (convert_engine export_media to AUDIO_EXTS) |
| vid-tts | Text to speech (both) | Narration audio from text | text | voice choice; language; speed (not detailed on the pages) | audio / video track | Windows.Media.SpeechSynthesis offline voices | LOCAL (voice quality = installed Windows voices) | no |
| vid-audio-cut-merge | Cut audio / Merge audio / Audio joiner (both) | Trim or join audio files | audio; multiple | range; order; fade; format | audio | FFmpeg | LOCAL | no |

### Overlays and text

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| vid-add-text | Add text to video (both) | Text overlays | video; single | text; font choice (hundreds of Google fonts on Clideo; system fonts locally); size int; color; semi-transparent background checkbox + color (Clideo); style presets; drag position; time range; animation choice (123apps); multiple text layers; output resolution choice | video | FFmpeg drawtext with fontfile, box, enable=between(t,a,b) | LOCAL | yes (FfmpegService text overlay via drawtext, subtitles fallback; TimelineProject text overlays) |
| vid-add-image | Add image / photo / logo / watermark to video (both) | Picture overlay, logo, watermark or cover | video + image (JPG, PNG, GIF, HEIC; PNG alpha kept) | drag position; size; rotation; opacity slider; time range on timeline; multiple images; per-image brightness/contrast/color (Clideo); use as cover/thumbnail (123apps) | video | FFmpeg overlay=x:y with format=yuva420p, colorchannelmixer=aa=, enable=between; animated GIF via -ignore_loop 0 | LOCAL | partial (FfmpegService overlay step: full-frame image with opacity + time window, no position/size) |
| vid-stickers | Add stickers / emojis / shapes (Clideo) | Animated stickers, emoji, shapes | video; single | library pick; size; color (shapes); position; time | video | FFmpeg overlay (animated WebP/GIF), drawtext with Segoe UI Emoji, drawbox; needs an own sticker pack | LOCAL | no |
| vid-remove-logo | Remove logo from video (123apps) | Hide a watermark | video; single | method choice[blur area/crop away/overlay own image]; area rectangle; time range not public | video | FFmpeg delogo=x:y:w:h, or crop + boxblur + overlay | LOCAL | no |

### Subtitles and speech

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| vid-subtitles | Add subtitles (Clideo) | Manual or SRT captions | video + SRT | mode choice[type manually with timing/upload SRT]; font; size; color; background color; alignment; position; export choice[burn-in/SRT sidecar]; 42 chars per line, 2 lines guidance; output format choice | video (burned) and/or SRT | FFmpeg subtitles= (libass) with force_style; SRT written as-is | LOCAL | yes (FfmpegService.BurnSubtitlesIfNeededAsync; ToTimedSrt) |
| vid-auto-subtitles | Auto subtitle generator / Video to text / Audio to text (Clideo) | Speech recognition captions and transcripts | video or audio; single | language choice (100+); edit cues; style templates; export choice[burn-in/SRT/TXT]; translate generated subtitles checkbox (AI); monthly free limits | video / SRT / TXT | whisper.cpp with a ggml Whisper model; Windows.Media.SpeechRecognition is dictation-grade only | NEEDS-ENGINE (whisper.cpp MIT + Whisper ggml weights MIT; not in this laptop's confirmed engine list); translate sub-option is AI-EGRESS | partial (WhisperSubtitleService.GenerateSrtAsync when FindWhisperCli/FindModel succeed; NormalizeSrtTimings) |
| vid-translate | Video translator / Audio translator (Clideo) | Translated subtitles plus AI voice-over | video or audio | source/target language choice; voice choice | video / audio | cloud machine translation + neural TTS; no local MT | AI-EGRESS | no |

### Convert and optimise

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| vid-convert | Video converter (both) | Change container, codec, size | video; multiple, up to 4 GB, no file-count limit (123apps) | output format choice[mp4/avi/mov/webm/wmv/ogv/mkv/3gp/asf/hevc/m4v/swf/ts/vob/xvid/gif/mp3/wav/ogg/flac]; video codec choice (h264, h265/HEVC, ...); audio codec choice; resolution choice; quality choice[5 levels worst-best] (123apps); frame rate, audio frequency, channels (Clideo); remove audio checkbox; estimated size preview (123apps) | video / audio / GIF | FFmpeg libx264, libx265, libvpx-vp9, aac, libmp3lame, flac, libvorbis | LOCAL | partial (FfmpegService extension map for .webm/.mkv/.mov/.avi; convert_engine export_media) |
| vid-compress | Compress video (Clideo; 123apps via converter quality) | Reduce file size | video; single | compression preset choice (names not public: best-quality ... smallest); estimated ratio and size preview; resolution/target size not public | video | FFmpeg CRF ladder + optional scale; two-pass -b:v for a target size | LOCAL | no |
| vid-to-gif | Video to GIF / GIF maker (Clideo; 123apps GIF looper) | Make a GIF from video or images | video (MP4, MOV, AVI, WMV), images (JPG, PNG), GIF | time range (max 30 s); speed 0.25x-4x; crop/resize; saturation/color; rotate; text, stickers, emojis; loop checkbox (infinite); 2-6 s recommended | GIF | FFmpeg fps= + scale + palettegen/paletteuse | LOCAL | partial (convert_engine export-as .gif from video) |
| vid-gif-editor | GIF editor (Clideo) | Edit an existing GIF | GIF (or video) | text (font, size, weight, color); speed; crop/resize; trim/split; brightness/contrast; merge GIFs; stickers | GIF | FFmpeg / Pillow per frame | LOCAL | no |

### Create

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| vid-slideshow | Slideshow maker / Video maker / images to video (Clideo; 123apps merge accepts images) | Photos, GIFs, clips and music into a video | images (JPG, PNG, GIF), videos, audio (MP3, WAV, M4A) | per-slide duration int s (3-5 s guidance), set all at once; transition choice[crossfade/none]; crop/aspect choice; music trim/loop; text; stickers; picture-in-picture, split-screen, freeze frame, logo (video maker); output resolution choice[480p/720p/1080p/4K] | video | FFmpeg -loop 1 -t d per image, scale/pad, xfade chain, amix | LOCAL | partial (FfmpegService.MergeAsync + xfade for video clips; image inputs not wired) |
| vid-meme | Meme maker (Clideo) | Caption video, GIF or image memes | video, GIF, image (JPEG, TIFF, BMP, PNG, GIF, MP4, WMV, MOV, VOB, AVI) | template choice (9); border choice[black/white/none]; text; font choice (12); alignment; crop; mute checkbox | video / GIF / image | FFmpeg pad + drawtext; still images via WIC | LOCAL | no |

### Effects and colour

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| vid-filters | Filter video (Clideo) | Preset looks | video; single | filter choice (black and white, sepia, vintage, ...; full list not public); one filter at a time; real-time preview | video | FFmpeg lut3d with .cube LUTs, or hue/curves/colorbalance | LOCAL | yes (FfmpegService LUT step lut3d over TimelineProject) |
| vid-adjust | Adjust video (Clideo; 123apps editor brightness/contrast) | Brightness, contrast, saturation, hue, vignette, fade | video; single | brightness int; contrast int; saturation int; hue int; vignette; fade (ranges not public); preview; output format choice | video | FFmpeg eq=brightness:contrast:saturation, hue=h, vignette, fade | LOCAL | partial (VideoEditSession.ApplyFadeAt + FfmpegExportGraph fade in/out only) |

### Recording

| id | name | what it does | inputs | options | output | local engine candidate | verdict | exists |
|---|---|---|---|---|---|---|---|---|
| vid-screen-record | Screen recorder (both) | Capture the screen | live | area; microphone; webcam bubble (pages give few details) | video | Windows.Graphics.Capture + Media Foundation encode, or FFmpeg ddagrab/gdigrab + dshow | LOCAL | no |
| vid-webcam-record | Video recorder / Camera recorder (both) | Record the webcam | live | none public | video | Windows.Media.Capture MediaCapture | LOCAL | no |
| vid-presentation-record | Presentation recorder (Clideo) | Slides with camera overlay | PPTX/PDF + camera | none public | video | PptxSlideImageService slide PNGs + MediaCapture picture-in-picture + FFmpeg overlay | LOCAL | no |
| vid-audio-record | Audio recorder / Voice recorder (both) | Record the microphone | live | none public | audio | Windows.Media.Capture audio, or NAudio (MIT) | LOCAL | no |
| vid-editor | Video editor (both) | Multi-track timeline editor | video, audio, images | unlimited clips; multiple lanes (123apps); cut, crop, rotate 90 or arbitrary, flip, text, audio, speed, filters, logo removal, resize, loop, stabilise, brightness/contrast (123apps); export 780p free / 1080p premium (123apps), 480p-4K (Clideo), 30+ formats | video | existing Space timeline (VideoEditSession + TimelineProject) exported by FfmpegService | LOCAL | yes (VideoEditSession, FfmpegService.ExportTimelineAsync, FfmpegExportGraph, BackgroundExportService) |

## Adobe family map

Structure only, for grouping the three Netie apps. Adobe naming is not to be reused in the
product. Sources: Adobe products catalog page and the Adobe Express quick-actions page;
Premiere Rush dates come from the Adobe help-centre end-of-life article surfaced by web
search (not fetched directly).

| Netie app | Adobe desktop counterpart(s) | Adobe Express quick actions in the same lane | Note |
|---|---|---|---|
| Netie PDF | Acrobat Pro (Acrobat Reader for view-only) | Combine PDF, Convert to PDF, Convert from PDF, Edit PDF, Organize Pages | Acrobat is the one-app-does-everything model iLovePDF mirrors tool-by-tool |
| Netie Image | Photoshop (pixel editing, compositing), Lightroom and Lightroom Classic (develop, batch, organise), Adobe Fresco (paint), Bridge (asset browser) | Remove Background, Change Background, Resize Image, Crop Image, Convert to JPG / PNG / SVG | iLoveIMG's catalog is the quick-actions tier, not Photoshop; Bridge maps to Space's explorer role |
| Netie Video | Premiere (Premiere Pro, "industry-standard pro video"), Media Encoder (batch convert/compress), After Effects (motion graphics, outside scope) | Trim Video, Crop Video, Resize Video, Merge Video, Convert Video to GIF, Convert Video to MP4, Caption Video, Remove Video Background, Clip Maker, Animate Characters | Premiere Rush is gone: downloads ended 2025-09-30, support and the app itself end 2026-09-30, replaced by Premiere on iPhone and Premiere on desktop; 123apps/Clideo sit at the Rush/Express tier |

## Fetch log

Fetched OK (98):
- Catalogs: https://www.ilovepdf.com/ ; https://www.iloveimg.com/ ; https://123apps.com/ ;
  https://clideo.com/tools ; https://clideo.com/ ; https://www.iloveapi.com/docs/api-reference
- iLovePDF tool pages (32): merge_pdf, split_pdf, compress_pdf, pdf_to_word,
  pdf_to_powerpoint, pdf_to_excel, word_to_pdf, powerpoint_to_pdf, excel_to_pdf, edit-pdf,
  pdf_to_jpg, jpg_to_pdf, sign-pdf, pdf_add_watermark, rotate_pdf, html-to-pdf, unlock_pdf,
  protect-pdf, organize-pdf, convert-pdf-to-pdfa, repair-pdf, add_pdf_page_number, scan-pdf,
  ocr-pdf, compare-pdf, redact-pdf, crop-pdf, pdf-forms, pdf-summarize, translate-pdf,
  pdf-to-markdown, remove-pages
- iLoveIMG tool pages (13): compress-image, resize-image, crop-image, convert-to-jpg,
  photo-editor, upscale-image, remove-background, meme-generator, rotate-image, jpg-to-image,
  html-to-image, watermark-image, blur-face
- 123apps (16): online-video-cutter.com root, video-editor, merge-videos, add-music-to-video,
  add-image-to-video, add-text-to-video, remove-logo, crop-video, rotate-video, resize-video,
  loop-video, volume, change-video-speed, stabilize-video; video-converter.com;
  online-audio-converter.com
- Clideo tool pages (29): cut-video, crop-video, merge-video, change-video-speed,
  rotate-video, flip-video, reverse-video, resize-video, add-subtitles-to-video,
  add-audio-to-video, mute-video, compress-video, video-converter, gif-maker, loop-video,
  split-video, filter-video, adjust-video, slideshow-maker, add-image-to-video, stop-motion,
  dpi-converter, add-text-to-video, auto-subtitle-generator, image-converter, gif-editor,
  video-maker, video-to-text, meme-maker
- Adobe (2): https://www.adobe.com/products/catalog.html ;
  https://www.adobe.com/express/feature/quick-actions

Redirected (1): https://developer.ilovepdf.com/docs/api-reference -> 301 to
https://www.iloveapi.com/docs/api-reference (followed; content used from the iloveapi page).

Hard failures: none.

Search-only (not fetched): https://helpx.adobe.com/premiere-rush/desktop/kb/end-of-life.html
(Premiere Rush dates via WebSearch snippet).

Options not public even after fetching (recorded as "not public" in the rows): iLoveIMG
compress level on the web tool (API has it); iLoveIMG photo-editor effect and frame list;
iLoveIMG blur strength; Clideo compress preset names; Clideo filter list; Clideo six speed
preset values; Clideo resize preset ratios; 123apps stabilise strength; 123apps volume range
on the video tool; 123apps flip-video page and screen/webcam recorder pages were not fetched
(catalog entries only).

## Counts

- Netie PDF: 37 rows (33 web tools, Sign split into self/request, 3 API-only).
  LOCAL 22, LOCAL-PARTIAL 9, NEEDS-ENGINE 2, CLOUD-ONLY 1, AI-EGRESS 3.
- Netie Image: 16 rows (13 web tools, 1 API-only, 2 Clideo image-adjacent).
  LOCAL 10, LOCAL-PARTIAL 4, NEEDS-ENGINE 2.
- Netie Video: 38 rows (123apps + Clideo union).
  LOCAL 36, NEEDS-ENGINE 1, AI-EGRESS 1.
- Total 91 rows: LOCAL 68, LOCAL-PARTIAL 13, NEEDS-ENGINE 5, CLOUD-ONLY 1, AI-EGRESS 4.

NEEDS-ENGINE with licenses:
1. pdf-to-pdfa - Ghostscript (AGPL, banned); LibreOffice (MPL-2.0, not installed, Office
   sources only); Word COM PDF/A-1b re-export is a lossy fallback only.
2. pdf-validate-pdfa - veraPDF (dual GPLv3 / MPL-2.0, Java, not installed).
3. img-upscale - ONNX Runtime (MIT) + Real-ESRGAN weights (BSD-3-Clause), or ComfyUI
   (GPL-3.0) as an opt-in HTTP service (already the ComfyUiClient pattern).
4. img-remove-bg - rembg (MIT) + U2-Net weights (Apache-2.0), or ONNX Runtime (MIT) +
   ISNet/BiRefNet (MIT); not installed.
5. vid-auto-subtitles - whisper.cpp (MIT) + Whisper ggml weights (MIT); Space already probes
   for it (WhisperSubtitleService.FindWhisperCli) but it is not in this laptop's confirmed
   engine list.

Invariant note (R-0011, R-0004): the AGPL pymupdf dependency in tools/convert_engine.py is a
root-cause class, not a per-tool bug - one `require_pymupdf()` helper feeds eight PDF write
paths. Fix it once at the helper (pikepdf / PdfSharpCore), not per action.

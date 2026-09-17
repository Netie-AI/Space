---
keywords: [ui-copy, teaching-text, dead-controls, devleak, preview-window, netie-viewer, md-editor, cleanup-checklist]
main_idea: 247 UI copy findings across 18 surfaces (62 TEACH, 75 WORDY, 60 DEVLEAK, 12 DEAD, 17 DUP, 21 CONFUSING) - the viewer HTML toolbar and status never render, the PDF workbench shows every tool three times, and dev jargon (PDFSpace, rembg, whisper.cpp, xfade, sidecar, scripts paths) leaks into the status strip.
date: 2026-09-17
kind: audit
product: Space
---

# UI copy audit - Netie Space (read-only)

Founder ask: "improve ui, non make sense part can remove, words also dont random instructions everywhere."
This is the checklist an implementer works from and a verifier checks against. No source file was edited.

## How visibility was decided (so the verifier can re-derive it)

- `PreviewWindow.xaml.cs` `ApplyWorkbenchShell` (line 750-870) and `UpdateActionButtons` (line 440-560): the PDF
  workbench (`_pdfSpaceMode`) also sets `_editMode = true` (`SetPdfWorkbenchAsync` line 680), so the PDF ribbon,
  the right TOOLS rail and the File/Edit menu bar are all visible at the same time. Every DUP below is judged on that.
- `PostDocChrome` (line 930-945) sends `slim = pdf && !workbench` and `canvasOnly = workbench || edit` to
  `netie-viewer.html`. CSS lines 139-143 hide `#toolbar` and `#pageHud` in BOTH cases and `#banner` only in slim.
  So the viewer's HTML toolbar (and the `#status` span inside it) never renders for a PDF in the shipped app; the
  banner renders in light Edit and in the workbench. `NavigatePdfJsAsync` is only called for `PreviewKind.Pdf`
  (line 1194), so no other kind can show the toolbar either.
- DEAD claims list every reference (xaml + cs) that was grepped before recommending removal.

## Legend

- `class`: TEACH (delete), WORDY (shorten), DEVLEAK (rewrite/delete), DEAD (remove control), DUP (keep one), CONFUSING (rename).
- `current text` is trimmed to 80 chars. Source glyphs are transliterated (em dash -> " - ", middle dot -> " . ",
  ellipsis -> "...", arrows -> "->", check/close/scissors glyphs -> [check]/X/[scissors]) and the cell ends with
  "(non-ASCII in source)".
- `gate`: a test or script that asserts the string or element. The implementer updates the gate honestly instead of breaking it.

## Gate index (asserted strings / ids that touch UI copy)

| Gate | Asserts |
|---|---|
| tests/test_toolbar_discipline.py:20,25,26,27,31,32,33,35 | `Content="More..."`, "Export all", "+ Clip", "Order...", "Preset: 1080p" (ribbon), "LUT...", "Export range (In to Out)", no `Content="Match"` |
| tests/test_video_clip_order.py:39,52,53 | `Content="Order..."`; VideoOrderWindow.xaml "Top clip plays first", "Apply order" |
| tests/test_video_multisource_v1.py:83,88,89 | cs AddVideoButton_Click body "Add clip onto V1" (dialog Title); xaml `Content="+ Clip"`, "packs after existing clips" |
| tests/test_human_gates.py:66,67,113 | x:Name ManageBillingButton / ActivateLaptopButton; cs "Wipe already on that join" |
| tests/test_pdf_cms_sign.py:61,68 | xaml `Header="Cert sign (PFX)..."`; cs "Could not rewrite xref-stream/hybrid" |
| tests/test_comfyui_client.py:84,93,94 | xaml `Content="Send to Comfy..."`; cs "ToastHud.Show"; cs "rembg / Copy still work" |
| tests/test_windows_ai_client.py:35,39,41 | xaml `Content="Windows AI..."`; cs "rembg / Copy still work"; click body must NOT contain "Wrote {IoPath.GetFileName(sidecar)}. Live enhance" |
| tests/NetieSpace.Tests/WindowsAiClientTests.cs:15 | Services WindowsAiClient.OfflineMessage() contains "rembg / Copy still work" |
| scripts/check-acrobat-chrome.ps1:16-21,25-31,49-53,55,64,69 | viewer ids `prev`, `pageHud`, `fillBtn`; hiding CSS rules; x:Names DocNavBar, DocAppHeader, DocLeftNav, DocFileTab, PdfSaveStampsButton, PdfSpaceRail, DocFloatDoneBtn, DocToolFloat, DocZoomFloat; literal `DocModeOn`; viewer must not add `id="save"` or "Clear page"; cs must not call `SetRibbonFloated(on)` |
| scripts/check-acrobat-5d.ps1:15-18 | cs "Open in Netie Space"; xaml PdfFillAndSign_Click, PdfCreateBlank_Click; viewer "not full Acrobat OCR" |
| scripts/check-cloud-disclose.ps1:5,14 | CloudPathDisclosureWindow.cs "Cloud AI leaves this PC", "EnsureAcked", "Allow cloud AI"; App.xaml.cs "Cloud AI notice" |
| scripts/check-md-editor.mjs:9-12 and scripts/check-multieditor-assets.ps1:29,34 | md-editor ids/functions: side, outline, notes, btnAnnot, btnAsk, btnSide, noteIn, `<mark`, demo, btnSplit, btnWidth, btnTheme, btnLh, btnFont, mdAsk, mdSelection; csv-editor ids grid, btnAddCol, addRow |

## src/NetieSpace/Views/PreviewWindow.xaml

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-001 | PreviewWindow.xaml:328 | EditModeButton ToolTip | WORDY | Light PDF / video tools in this preview | "Edit tools" (runtime variants at cs:462-464, see U-097) | - |
| U-002 | PreviewWindow.xaml:329,333 | EditInAppButton Content + ToolTip | CONFUSING | Edit in app / PDFSpace workbench - Fill & Sign, Edit PDF, Organize, right-rail tools (non-ASCII in source) | Two buttons both called "Edit" sit side by side for PDFs. Content "Workbench"; ToolTip "Open the PDF workbench". Drop "PDFSpace" (internal name). | - |
| U-003 | PreviewWindow.xaml:338 | TextEditButton ToolTip | WORDY | Edit text - autosaves as you type (non-ASCII in source) | "Edit text" | - |
| U-004 | PreviewWindow.xaml:342 | SummaryToggleButton ToolTip | WORDY | Show or hide AI summary (off skips AI calls) | "AI summary" | - |
| U-005 | PreviewWindow.xaml:346 | ProviderButton ToolTip | TEACH | Switch AI. Cloud path is on when the label says Cloud: | "Switch AI provider" | - |
| U-006 | PreviewWindow.xaml:350 | ContinueChatButton ToolTip | WORDY | Side panel notes & chat - saved on this file (non-ASCII in source) | "Notes & chat" | - |
| U-007 | PreviewWindow.xaml:354 | LookupClipButton ToolTip | WORDY | Look Up highlighted text (or right-click the page) | "Look up selection" | - |
| U-008 | PreviewWindow.xaml:361 | OcrImageButton ToolTip | WORDY | Read text from this image (local OCR; cloud stays opt-in) | "Read text (OCR)" | - |
| U-009 | PreviewWindow.xaml:365 | CopyImageButton ToolTip | TEACH | Copy this image (bitmap + file). Drag the picture out to drop it into a folder or... | "Copy image" | - |
| U-010 | PreviewWindow.xaml:369 | CompressImageButton ToolTip | WORDY | Lower JPEG file size (quality picker) | "Reduce file size" | - |
| U-011 | PreviewWindow.xaml:370,373 | RotateImageButton Content + ToolTip | WORDY | Rotate 90 / Write a 90-degree clockwise copy beside this file | "Rotate" / "Rotate 90 deg clockwise (saves a copy)" | scripts/check-multieditor-assets.ps1:16 asserts the handler name only |
| U-012 | PreviewWindow.xaml:377 | SendToComfyButton ToolTip | DEVLEAK | Queue this image in local ComfyUI (127.0.0.1:8188). rembg / Copy still work if Comfy... | "Send to ComfyUI" (Content "Send to Comfy..." stays) | GATE: tests/test_comfyui_client.py:84 (Content only) |
| U-013 | PreviewWindow.xaml:381 | WindowsAiButton ToolTip | DEVLEAK | Probe local WinML / Windows AI. rembg / Copy still work if it is down. | "Windows AI" (Content stays) | GATE: tests/test_windows_ai_client.py:35 (Content only) |
| U-014 | PreviewWindow.xaml:386 | ShareButton ToolTip | WORDY | Share - the whole file, a converted copy, or just what you selected (non-ASCII in source) | "Share" | - |
| U-015 | PreviewWindow.xaml:395 | SetupHelpButton ToolTip | WORDY | Settings - plan . keys . My details . vault (non-ASCII in source) | "Settings" | - |
| U-016 | PreviewWindow.xaml:448 | ShareSelectionHint default | TEACH | Whatever you highlighted in the preview | Delete the default text; cs:7775 already swaps in the real selection, leave it empty when there is none | - |
| U-017 | PreviewWindow.xaml:463 | Copy file sub-hint | TEACH | Paste straight into Mail, Teams or a folder | Delete TextBlock | - |
| U-018 | PreviewWindow.xaml:479 | Email a copy sub-hint | TEACH | Opens your mail app with the text prefilled | Delete TextBlock | - |
| U-019 | PreviewWindow.xaml:493 | Open with sub-hint | TEACH | Hand off to any app Windows offers | Delete TextBlock | - |
| U-020 | PreviewWindow.xaml:507 | Show in folder sub-hint | DUP | Reveal in File Explorer | Restates the row label; delete TextBlock | - |
| U-021 | PreviewWindow.xaml:515 | ShareFormatsLabel | WORDY | Send as a different format | "Convert" | - |
| U-022 | PreviewWindow.xaml:547 | ShareHtmlRow label | CONFUSING | Edit Word / HTML (autosave) | An edit action listed under a convert heading. "Edit as HTML", placed above the Convert label | - |
| U-023 | PreviewWindow.xaml:554,565,566 | ShareExportAsLabel + ShareExportAsRow + ShareExportAsHint | DUP | Export as... / Pick format & save... / PDF, Word, Excel, HTML... (non-ASCII in source) | Label, row and hint say the same thing. Keep one row "Export as..."; delete the label; keep the hint only if cs:7766 fills it with the real format list | - |
| U-024 | PreviewWindow.xaml:582 | DocAppHeader Done ToolTip | WORDY | Leave workbench - back to Space preview (non-ASCII in source) | "Back to preview" | - |
| U-025 | PreviewWindow.xaml:595 | File > Leave workbench | WORDY | Leave workbench | "Back to preview" (same words as U-024) | - |
| U-026 | PreviewWindow.xaml:591,858,1490 | File > _Combine... / Organize panel Merge... / rail Combine (all PdfMerge_Click) | CONFUSING | Combine... / Merge... / Combine | One verb. The dialog is titled "Merge PDFs" (PdfMergeWindow.xaml:4), so use "Merge" in all three | - |
| U-027 | PreviewWindow.xaml:601 | Edit > Add Text (PdfModeFill_Click) | DUP | Add Text | Fill & Sign (line 599) already enters the same fill-typing mode (cs:6454 "fillsign" sets fillOn). Remove the menu item | - |
| U-028 | PreviewWindow.xaml:615-617 | Help > Settings... | CONFUSING | Help / Settings... | Settings is not help. Move the item under File (or rename the menu "Settings") | - |
| U-029 | PreviewWindow.xaml:623-625 | DocHomeTab "Home" ToolTip "Leave workbench" | DUP | Home / Leave workbench | Fourth control that leaves the workbench (Done line 580, File > Leave line 595, DocFileTab X line 634). Remove the Home tab. Refs: xaml:623 only; cs has 0 references to DocHomeTab | - |
| U-030 | PreviewWindow.xaml:626-628 | DocToolsTab "Tools" ToolTip "Document tools on the right" | DEAD | Tools / Document tools on the right | Handler DocToolsTab_Click (cs:1047-1051) only re-applies the rail that is already on; the tab is always styled active and does nothing. Remove tab + handler. Refs: xaml:626, cs:1047 | scripts/check-acrobat-chrome.ps1 does not name DocToolsTab |
| U-031 | PreviewWindow.xaml:634-636 | DocFileTab X ToolTip "Close this document" | CONFUSING | Close this document | Says close, but Click=EditInAppButton_Click only leaves the workbench (file stays open). Remove the X or tooltip "Back to preview" | GATE: scripts/check-acrobat-chrome.ps1:28 keeps x:Name="DocFileTab" (the Border, not the X) |
| U-032 | PreviewWindow.xaml:667 | PdfSaveStampsButton ToolTip | WORDY | Save - write signatures, typed text, and highlights into this PDF (non-ASCII in source) | "Save (Ctrl+S)" | GATE: scripts/check-acrobat-chrome.ps1:29 keeps the x:Name |
| U-033 | PreviewWindow.xaml:668-673 | Find icon button + DocSearchBox | DUP | Find in PDF / Find in PDF | An icon that focuses the search box sitting next to that box. Drop the icon; Enter in the box and Edit > Find (Ctrl+F) cover it | - |
| U-034 | PreviewWindow.xaml:696-699 | DocAreaBtn "Area" ToolTip "Select area for chat" | CONFUSING | Select area for chat | In the workbench the Chat button is hidden (cs HidePeekChrome), so "for chat" points at nothing visible. Either remove from DocNavBar or make the regionClip handler (cs:2672) open the chat panel and keep the tooltip "Send an area to chat" | - |
| U-035 | PreviewWindow.xaml:709 | AddChatClipButton ToolTip | WORDY | Stage this file (parsed) as an attachment - nothing sends until you press Send (non-ASCII in source) | "Attach to chat" | - |
| U-036 | PreviewWindow.xaml:710-713 | SendSlideChatButton Content + ToolTip | CONFUSING | Send slide / Attach the visible slide image to chat - not the whole deck. Nothing sends... (non-ASCII in source) | Label says Send, tooltip says nothing sends. "Add slide" / "Attach this slide to chat" | - |
| U-037 | PreviewWindow.xaml:716 | RegionSelectButton ToolTip | TEACH | Drag a box over text or a figure - it parses first, then stages as an attachment (non-ASCII in source) | "Select an area for chat" | - |
| U-038 | PreviewWindow.xaml:720 | OpenExternallyButton ToolTip | DEVLEAK | Open this PDF in PDFSpace - not Acrobat (non-ASCII in source) | "Open in the PDF workbench" (Content "Open in Netie Space" stays) | GATE: scripts/check-acrobat-5d.ps1:15 needs "Open in Netie Space" in cs (set at cs:2416) |
| U-039 | PreviewWindow.xaml:735 | SummaryFloatChip ToolTip | WORDY | Hide or show the summary panel | "Summary" | - |
| U-040 | PreviewWindow.xaml:769-783 | unnamed DockPanel: PdfRibbonTitle, DocCloseModeBtn, PdfRibbonHint | DEAD | PDF tools / Close (Leave this tool - page stays open) / Pick a mode . tools appear below (non-ASCII in source) | The DockPanel is Visibility=Collapsed and has no x:Name, so nothing can show it; cs only forces the children Collapsed or "" (cs:770-771, 782-783, 6387-6388). Delete the DockPanel and those three cs guard blocks. Refs: xaml:769-783; cs:770,771,782,783,6387,6388. Zero hits in tests/ and scripts/ | - |
| U-041 | PreviewWindow.xaml:789 | PdfFillSignBtn ToolTip | TEACH | Type in a cell and place a signature | Delete tooltip (label already says Fill & Sign) | - |
| U-042 | PreviewWindow.xaml:798 | PdfEditPdfBtn ToolTip | TEACH | Native text only - click a word to replace (non-ASCII in source) | "Edit text" | - |
| U-043 | PreviewWindow.xaml:787-804,1448-1465,599-600 | ribbon Fill & Sign + Edit PDF vs rail Fill & Sign + Edit PDF vs Edit menu | DUP | Fill & Sign / Edit PDF (three copies each, all visible in the workbench) | Keep the rail (TOOLS) and the menu. Collapse PdfFillSignBtn / PdfEditPdfBtn when `_pdfSpaceMode` and move the active-mode style (DocModeOn) onto the rail button so the mode is still shown | GATE: scripts/check-acrobat-chrome.ps1:31 needs the literal `DocModeOn` in xaml (keep the style resource); check-acrobat-5d.ps1:16 needs PdfFillAndSign_Click in xaml (rail keeps it) |
| U-044 | PreviewWindow.xaml:805-807 | DocFloatDoneBtn "Done" ToolTip "Leave workbench" | DEAD | Done / Leave workbench | Visibility=Collapsed and cs forces Collapsed at 513-514 and 796-797; never shown. Keep the node only because the gate asserts it; delete its ToolTip. If removing, update the gate line too | GATE: scripts/check-acrobat-chrome.ps1:51 "Done node kept" |
| U-045 | PreviewWindow.xaml:808-810 | PdfModeOrganizeBtn ToolTip | WORDY | Pages: reorder, extract, split, rotate, merge | "Pages". Also DUP with rail Organize (line 1475) in the workbench: collapse the chip when `_pdfSpaceMode` | - |
| U-046 | PreviewWindow.xaml:812 | PdfModeSignBtn ToolTip | TEACH | Place a signature | Delete tooltip | - |
| U-047 | PreviewWindow.xaml:815 | PdfModeFillBtn ToolTip | TEACH | Type into a cell or blank | Delete tooltip | - |
| U-048 | PreviewWindow.xaml:818 | PdfModeCommentBtn ToolTip | WORDY | Highlight markup | "Highlight". Also DUP with rail Comment (line 1466) in the workbench: collapse the chip when `_pdfSpaceMode` | - |
| U-049 | PreviewWindow.xaml:821 | PdfMoreButton ToolTip | WORDY | Watermark, protect, scan OCR, export | "More tools" | GATE: tests/test_toolbar_discipline.py:20 `Content="More..."` (Content only) |
| U-050 | PreviewWindow.xaml:824-825 | More > Edit text / OCR... ToolTip | DUP | Edit text / OCR... / Edit native text or seed OCR boxes, then Save into this PDF | PdfEditTextMode_Click is already on the ribbon, the rail and the Edit menu; this is a fourth entry. Remove the item | - |
| U-051 | PreviewWindow.xaml:827-828 | More > Compress... (IsEnabled=False) ToolTip | DEAD | Compress... / Coming soon - not a fake rewrite (non-ASCII in source) | Disabled, no handler, says coming soon. Remove the MenuItem. Refs: xaml:827-828 only; no cs handler; zero hits in tests/ and scripts/ | - |
| U-052 | PreviewWindow.xaml:829-830 | More > Export as... ToolTip | DUP | Export as... / Pick another format via the Share export picker | PdfExportAsMenu_Click is also the rail Export (line 1502) and File > Export as (line 592). Remove from More; delete the tooltip | - |
| U-053 | PreviewWindow.xaml:833 | Watermark... ToolTip | DEVLEAK | Stamp text across pages (pymupdf) | "Stamp text across pages" | - |
| U-054 | PreviewWindow.xaml:834-835 | More > Protect... ToolTip | DUP | Protect... / AES-256 password lock | PdfProtect_Click is also the rail Protect (line 1510) and File > Protect (line 593). Remove from More; rail tooltip "Password-protect" | - |
| U-055 | PreviewWindow.xaml:837 | Unlock... ToolTip | WORDY | Remove password (needs the password) | "Remove password" | - |
| U-056 | PreviewWindow.xaml:839 | Scan & OCR... ToolTip | DEVLEAK | Make a scanned PDF searchable (needs Tesseract) | "Make a scanned PDF searchable" | - |
| U-057 | PreviewWindow.xaml:841 | Cert sign (PFX)... ToolTip | DEVLEAK | Embed a PKCS#7 detached signature. Visual Sign stamp stays available. | "Sign with a certificate file (.pfx)" (Header stays) | GATE: tests/test_pdf_cms_sign.py:61 `Header="Cert sign (PFX)..."` |
| U-058 | PreviewWindow.xaml:845-847 | PdfLightSaveBtn "Save" ToolTip | DUP | Save / Write signatures, typed text, and highlights into this PDF | In the workbench this chip, the DocNavBar Save icon (line 665) and File > Save (line 590) all call PdfSaveStamps_Click. Collapse the chip when `_pdfSpaceMode` (cs:784-785 rule `_editMode && pdf` -> `lightEdit`); tooltip "Save (Ctrl+S)" | - |
| U-059 | PreviewWindow.xaml:857 | Organize panel Reorder... ToolTip | WORDY | Drag pages . rotate . delete (non-ASCII in source) | Delete tooltip | - |
| U-060 | PreviewWindow.xaml:861,1023-1025 | Organize panel "Rotate 90 deg" and DocLeftNav rotate icon (both PdfRotate90_Click) | DUP | Rotate 90 deg / Rotate page 90 (non-ASCII in source) | Both visible in workbench Organize mode with thumbnails on. Keep the Organize chip; drop the left-nav icon | - |
| U-061 | PreviewWindow.xaml:874 | New signature ToolTip | TEACH | Type name . pick font . place on page (non-ASCII in source) | Delete tooltip | - |
| U-062 | PreviewWindow.xaml:885-888,907-908 | A- / A+ in Sign panel and A- / A+ in Fill panel | DUP | A- / A+ (two pairs) | Both panels are open in Fill & Sign mode (cs SetPdfToolMode: signOn && fillOn), so two size pairs show at once. Keep the Fill panel pair; tooltips "Smaller" / "Larger" | - |
| U-063 | PreviewWindow.xaml:898-900 | OCR this page ToolTip | WORDY | Read the visible page with local OCR and drop editable text boxes | Delete tooltip | - |
| U-064 | PreviewWindow.xaml:901-903 | Autofill ToolTip | WORDY | Fill empty boxes from your local profile (never uploaded) | "Fill from My details" | - |
| U-065 | PreviewWindow.xaml:904-906 | My details... ToolTip | WORDY | Name, email, phone, address, DOB, nationality - this PC only (non-ASCII in source) | "Your saved details" | - |
| U-066 | PreviewWindow.xaml:909 | Exit Fill (PdfExitFill_Click) | CONFUSING | Exit Fill | The panel is also open in Fill & Sign and Edit PDF modes, and the button exits every mode. "Done" | - |
| U-067 | PreviewWindow.xaml:918 | Comment panel header | DUP | Comment | Repeats the chip / rail label directly above it. Delete the header | - |
| U-068 | PreviewWindow.xaml:922 | Highlight ToolTip | TEACH | Drag on the page to highlight. Save writes it into the PDF. | Delete tooltip | - |
| U-069 | PreviewWindow.xaml:925 | Mark ToolTip | TEACH | Drag to draw a mark. Save writes it into the PDF. | Delete tooltip | - |
| U-070 | PreviewWindow.xaml:939-940 | VideoRibbonHint (and runtime cs:2350-2358) | TEACH | Export . + Clip . Order . More (non-ASCII in source) | Lists the four chips sitting next to it. Delete the TextBlock and cs:2350-2358; if the preset must show, put it on the More label ("More (1080p)") | GATE: tests/test_toolbar_discipline.py:31 needs "Preset: 1080p" inside the ribbon - that is the MenuItem Header at line 963, untouched |
| U-071 | PreviewWindow.xaml:945 | ExportEditButton ToolTip | WORDY | Export the full packed timeline (all clips) | "Export all clips" | GATE: tests/test_toolbar_discipline.py:25 "Export all" (Content) |
| U-072 | PreviewWindow.xaml:948 | AddVideoButton ToolTip | TEACH | Add one or more videos onto V1 -- packs after existing clips in dialog order (no ove... | "Add clips - packs after existing clips" | GATE: tests/test_video_multisource_v1.py:89 needs "packs after existing clips" in xaml; :88 `Content="+ Clip"` |
| U-073 | PreviewWindow.xaml:951 | OrderClipsButton ToolTip | WORDY | Arrange V1 clip order before Export. Top plays first. Drag or Up/Down. | "Reorder clips" | GATE: tests/test_toolbar_discipline.py:27 and tests/test_video_clip_order.py:39 `Content="Order..."` |
| U-074 | PreviewWindow.xaml:969 | LUT... ToolTip | WORDY | Apply .cube LUT on Export timeline | "Apply a color LUT" | GATE: tests/test_toolbar_discipline.py:32 "LUT..." (Header) |
| U-075 | PreviewWindow.xaml:998 | SummaryUndockButton ToolTip | WORDY | Put the summary back on top | "Undock" | - |
| U-076 | PreviewWindow.xaml:1029 | DocThumbsMoreBtn ToolTip | WORDY | Render the next batch of page thumbnails | Delete tooltip (label "Load more" is enough) | - |
| U-077 | PreviewWindow.xaml:1035 | thumbnail item ToolTip | TEACH | Go to this page | Delete tooltip | - |
| U-078 | PreviewWindow.xaml:1209-1212 | VideoLightHint | TEACH | Light preview - press Edit for the full Netie video editor (timeline, split, export). (non-ASCII in source) | Delete the TextBlock and cs:549-550; the Edit button is on the title bar | - |
| U-079 | PreviewWindow.xaml:1246 | CutMiddleButton ToolTip | WORDY | Select a clip first - remove it (merge or black gap) (non-ASCII in source) | "Delete selected clip" | - |
| U-080 | PreviewWindow.xaml:1247-1250 (cs:2039) | BladeModeButton "Select" / "Select ON" ToolTip | CONFUSING | Select / Select ON / Click a clip to select, then Delete or drag to reorder | A mode toggle whose label grows " ON". Keep "Select", show state by chip style only; tooltip "Select clips" | - |
| U-081 | PreviewWindow.xaml:1257 | Wipe ToolTip | DEVLEAK | 0.5s wipe into the next clip (ffmpeg xfade wipeleft) - burns on Export (non-ASCII in source) | "Wipe to next clip (0.5s)" | - |
| U-082 | PreviewWindow.xaml:1260 | Text ToolTip | WORDY | Overlay text on the canvas at the playhead - burns into Export (non-ASCII in source) | "Add text at playhead" | - |
| U-083 | PreviewWindow.xaml:1263 | Captions ToolTip | DEVLEAK | Local AI subtitles (whisper.cpp) - ZH+EN auto (non-ASCII in source) | "Auto subtitles (offline)" | - |
| U-084 | PreviewWindow.xaml:1264-1270 (cs:2032-2033) | TimelineHintText initial + first runtime text | TEACH | Move playhead -> Split -> select clip -> Delete / Timeline . Blade / Split . select clip . Delete . Export timeline (non-ASCII in source) | Initial text "" and delete cs:2032-2033 (no "Blade" or "Export timeline" button exists). Keep the later runtime clip readout from `_videoEdit.Describe()` - that one is useful | - |
| U-085 | PreviewWindow.xaml:1339 | AudioTrack2Host ToolTip | WORDY | Drop or move clips here for music / mute lane | "Music / mute lane" | - |
| U-086 | PreviewWindow.xaml:1349 | OverlayTrackHost ToolTip | DEVLEAK | Overlay cards (Leave black / Add black / text) - packed start + length. Burns on Export. (non-ASCII in source) | "Overlays: black, white, text" | - |
| U-087 | PreviewWindow.xaml:1359 | CaptionTrackHost ToolTip | WORDY | Caption cues - drag to shift timing (non-ASCII in source) | "Captions" | - |
| U-088 | PreviewWindow.xaml:1403-1418 | DocToolFloat popup | DEAD | (no copy inside) | Never opened: ApplyWorkbenchShell forces SetRibbonFloated(false) and the gate forbids SetRibbonFloated(on). Listed so the implementer does not try to remove it: keep the node, nothing to edit | GATE: scripts/check-acrobat-chrome.ps1:52,69 |
| U-089 | PreviewWindow.xaml:1450 | rail Fill & Sign ToolTip | TEACH | Type in a cell and place a signature | Delete tooltip | - |
| U-090 | PreviewWindow.xaml:1459 | rail Edit PDF ToolTip | TEACH | Native text - click a word to replace (non-ASCII in source) | "Edit text" | - |
| U-091 | PreviewWindow.xaml:1468 | rail Comment ToolTip | WORDY | Highlight markup | Delete tooltip | - |
| U-092 | PreviewWindow.xaml:1477 | rail Organize ToolTip | WORDY | Reorder, extract, split, rotate, merge | "Pages" | - |
| U-093 | PreviewWindow.xaml:1486,1490 | rail Combine ToolTip "Merge PDFs" | CONFUSING | Combine / Merge PDFs | Label and tooltip use different verbs. "Merge" (see U-026) | - |
| U-094 | PreviewWindow.xaml:1495 | rail Blank PDF ToolTip | WORDY | Create a blank Letter page next to this file | "New blank PDF" | GATE: scripts/check-acrobat-5d.ps1:17 PdfCreateBlank_Click (handler only) |

## src/NetieSpace/Views/PreviewWindow.xaml.cs (strings assigned from code)

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-095 | PreviewWindow.xaml.cs:353-356 | SummaryText (load) | TEACH | Summary hidden - turn Summary on to generate. / Preview ready - add an API key for AI summary (tray -> API Keys...). (non-ASCII in source) | First: "". Second: "No AI key - add one in Settings" | - |
| U-096 | PreviewWindow.xaml.cs:434 | ConvertStatusText | DEVLEAK | Shell thumbnail | "" | - |
| U-097 | PreviewWindow.xaml.cs:462-464 | EditModeButton ToolTip (runtime) | WORDY | Light tools in this preview / Return to light Space preview | "Edit tools" / "Back to preview" | - |
| U-098 | PreviewWindow.xaml.cs:469-472 | EditInAppButton ToolTip (runtime) | DEVLEAK | Full Fill & Sign / Edit PDF workbench / Leave PDFSpace workbench | "Open the PDF workbench" / "Back to preview" | - |
| U-099 | PreviewWindow.xaml.cs:478-481 | TextEditButton ToolTip (runtime) | DEVLEAK | Edit slides in installed LibreOffice (not OnlyOffice) / Edit Word/HTML in the preview - au... (non-ASCII in source) | "Edit in LibreOffice" / "Edit" | - |
| U-100 | PreviewWindow.xaml.cs:490 | BrandModeText | DEVLEAK | PDFSPACE | Shown nowhere: PeekTitleCluster is collapsed in the workbench, so this branch never renders. Drop the branch or "PDF workbench" | - |
| U-101 | PreviewWindow.xaml.cs:606 | ConvertStatusText | TEACH | Video editor - all tools ready from the first frame. (non-ASCII in source) | "" | - |
| U-102 | PreviewWindow.xaml.cs:629,690,712 | ConvertStatusText | DEVLEAK | Light preview / PDFSpace / Light preview | Internal mode names painted as status. "" | - |
| U-103 | PreviewWindow.xaml.cs:1114-1116 | SummaryText | TEACH | SVG vector preview - zoom with scroll / Ctrl+wheel. (non-ASCII in source) | Leave SummaryText alone | - |
| U-104 | PreviewWindow.xaml.cs:1160 | SummaryText | TEACH | Playing - press Edit for the full Netie video editor. (non-ASCII in source) | "" | - |
| U-105 | PreviewWindow.xaml.cs:1195,1204 | SummaryText | TEACH | PDF preview - press Edit for full Netie PDF tools (merge, sign, organize). / PDF preview - press Edit for tools. (non-ASCII in source) | "" | - |
| U-106 | PreviewWindow.xaml.cs:1180 | ConvertStatusText | DEVLEAK | Windows preview handler | "" | - |
| U-107 | PreviewWindow.xaml.cs:1307,1317 | ConvertStatusText | DEVLEAK | Codec not supported natively - transcoding with FFmpeg... / Playing H.264 preview (original unchanged) (non-ASCII in source) | Keep visible (R-0011) but plain: "Preparing preview..." / "Playing a converted preview (original unchanged)" | - |
| U-108 | PreviewWindow.xaml.cs:1343-1345 | ConvertStatusText + StatusText | DUP | AI -> {next} / Provider: {next} (non-ASCII in source) | Two strips announce the same switch. Keep one | - |
| U-109 | PreviewWindow.xaml.cs:1365 | ProviderButton ToolTip (runtime) | WORDY | Local label. Chat/summary stay off until you Allow cloud AI. | "Cloud AI is off" | - |
| U-110 | PreviewWindow.xaml.cs:1627,1631 | ConvertStatusText | DEVLEAK | Added page screenshot (thin text layer - parse/OCR light) (non-ASCII in source) | "Added page screenshot" | - |
| U-111 | PreviewWindow.xaml.cs:1697 | ConvertStatusText | TEACH | Drag a box over a table or figure - it parses, then stages in chat. (non-ASCII in source) | "Select area on" | - |
| U-112 | PreviewWindow.xaml.cs:1798 | ConvertStatusText | TEACH | Slide {n} staged in chat - add a question, then Send. (non-ASCII in source) | "Slide {n} added to chat" | - |
| U-113 | PreviewWindow.xaml.cs:2043-2045 | ConvertStatusText | TEACH | Select ON - click a colored clip block to highlight it, then Delete clip. / Click [scissors] Split here at the playhead to cut. (non-ASCII in source) | "Select on" / "Select off" | - |
| U-114 | PreviewWindow.xaml.cs:2113 | ConvertStatusText | TEACH | [scissors] Split -> {n} clips. Yellow marks = cuts. Click a clip (it highlights), then Delete. (non-ASCII in source) | "Split - {n} clips" | - |
| U-115 | PreviewWindow.xaml.cs:2133 | ConvertStatusText | WORDY | Select a clip on the timeline first (click it), then Delete. | "Select a clip first." | - |
| U-116 | PreviewWindow.xaml.cs:2141-2146 | GlassChoiceDialog "Delete clip" body + choices | DEVLEAK | Ripple closes the gap. Black leaves a blank scene you can title later. / Ripple delete (join) / Leave black scene | Body "". Choices "Close the gap" / "Leave black" | - |
| U-117 | PreviewWindow.xaml.cs:2209-2210 | ConvertStatusText | TEACH | Deleted - black scene ready. Right-click -> Add text when you want a title. / Deleted - ripple join (no gap). (non-ASCII in source) | "Deleted - black scene kept" / "Deleted" | - |
| U-118 | PreviewWindow.xaml.cs:2419 | SummaryText (PDF failure) | DEVLEAK | Could not display this PDF in the light preview. Open in Netie Space (PDFSpace) to edit, or... | "Could not display this PDF here. Open in Netie Space or in your default PDF app." | GATE: scripts/check-acrobat-5d.ps1:15 needs "Open in Netie Space" in cs (kept at 2416) |
| U-119 | PreviewWindow.xaml.cs:2434 | ConvertStatusText | DEVLEAK | Already in PDFSpace - Fill & Sign or Edit PDF from the right rail (non-ASCII in source) | "" (the button is hidden in the workbench anyway) | - |
| U-120 | PreviewWindow.xaml.cs:2501,2503 | ConvertStatusText | DEVLEAK | This PDF uses XFA - print to a static PDF first, then Fill. / No AcroForm widgets - click a cell or line to type. (non-ASCII in source) | "XFA form - cannot fill here. Print to PDF first." / "No form fields - click where you want to type" | - |
| U-121 | PreviewWindow.xaml.cs:2587 | ConvertStatusText | WORDY | Details saved on this PC - Autofill uses them, nothing leaves the machine. (non-ASCII in source) | "Details saved on this PC" | - |
| U-122 | PreviewWindow.xaml.cs:2619 | ConvertStatusText | WORDY | Autofill - empty boxes matched by type from your local details (non-ASCII in source) | "Autofill..." | - |
| U-123 | PreviewWindow.xaml.cs:2748-2749 | ConvertStatusText | TEACH | Signature placed - drag the box anytime . autosaving... / {n} signatures - drag boxes . autosaving... (non-ASCII in source) | "Signature placed . autosaving..." / "{n} signatures . autosaving..." | - |
| U-124 | PreviewWindow.xaml.cs:2823 | ConvertStatusText | TEACH | Form fields changed - Save writes into this PDF (non-ASCII in source) | "Form fields changed" | - |
| U-125 | PreviewWindow.xaml.cs:4556 | ConvertStatusText | DEVLEAK | Moved clip -> position {n}. A1 audio rebuilt to stay locked. (non-ASCII in source) | "Moved clip to position {n}" | - |
| U-126 | PreviewWindow.xaml.cs:4577,4588,4599 | ConvertStatusText | WORDY | Select a clip first, then Fade In. (and Fade Out / Wipe variants) | "Select a clip first." | - |
| U-127 | PreviewWindow.xaml.cs:4606 | ConvertStatusText | WORDY | Wipe needs two clips - Split or + Clip, then click Wipe on either side of the join. (non-ASCII in source) | "Wipe needs two clips." | GATE: tests/test_human_gates.py:113 "Wipe already on that join" at 4616 stays |
| U-128 | PreviewWindow.xaml.cs:4625 | ConvertStatusText | DEVLEAK | Wipe (xfade wipeleft {d}s) on clip {n} -> next. Either side of the join works. Burns on Export. (non-ASCII in source) | "Wipe on clip {n}" | - |
| U-129 | PreviewWindow.xaml.cs:4635 | ConvertStatusText | DEVLEAK | Text overlay on canvas - burns into Export (drawtext / SRT fallback). (non-ASCII in source) | "Text added" | - |
| U-130 | PreviewWindow.xaml.cs:4648 | ConvertStatusText | TEACH | No timeline clip - Split once, then select. (non-ASCII in source) | "No clip selected." | - |
| U-131 | PreviewWindow.xaml.cs:4662 | ConvertStatusText | WORDY | Fade set on clip {n}: in={a}s out={b}s (applied on Export timeline). | "Fade on clip {n}: in {a}s, out {b}s" | - |
| U-132 | PreviewWindow.xaml.cs:4675 | ConvertStatusText | DEVLEAK | Captions ready . {n} cues . continues after Space . burns on Export (non-ASCII in source) | "Captions ready . {n} cues" | - |
| U-133 | PreviewWindow.xaml.cs:4685,4697 | ConvertStatusText | DEVLEAK | Captions need whisper.cpp - run scripts\download-whisper.ps1 / Generating local captions (whisper.cpp)... (non-ASCII in source) | "Captions are not installed (speech model missing)" / "Generating captions..." | - |
| U-134 | PreviewWindow.xaml.cs:4745 | ConvertStatusText | DEVLEAK | No caption cues on the packed timeline. | "No captions found." | - |
| U-135 | PreviewWindow.xaml.cs:4873 | ConvertStatusText | TEACH | Selected clip {n}. Delete removes it; Split blades at playhead. | "Clip {n} selected" | - |
| U-136 | PreviewWindow.xaml.cs:4909 | ConvertStatusText | WORDY | Select a V1 clip first, then click A1/A2 to move/mute audio there. | "Select a clip first." | - |
| U-137 | PreviewWindow.xaml.cs:4945 | ConvertStatusText | DEVLEAK | A2 lane is preview-only. Export still uses V1/A1 (no amix). | Keep the limitation, plain: "A2 is preview only - export keeps A1 audio" | - |
| U-138 | PreviewWindow.xaml.cs:5001 | ConvertStatusText (MakeEffectItem) | CONFUSING | {header} - applied (export with Export timeline). (non-ASCII in source) | No "Export timeline" button exists. "{header} applied" | - |
| U-139 | PreviewWindow.xaml.cs:5094 | PromptDialog prompt | CONFUSING | Text to show (appear = clip In->Out): (non-ASCII in source) | "Text to show:" | - |
| U-140 | PreviewWindow.xaml.cs:5321,7445-7446 | ConvertStatusText / StatusText | DEVLEAK | Could not read those videos' duration (need FFmpeg). Run scripts/download-ffmpeg.ps1. / FFmpeg not found - run scripts/download-ffmpeg.ps1 (non-ASCII in source) | "Video tools are not installed (FFmpeg missing)" | - |
| U-141 | PreviewWindow.xaml.cs:5338-5339 | ConvertStatusText | TEACH | Added clip {n} on V1 ({name}) -- packed after existing clips. Order... to arrange. Fade/Wipe still join on Export. | "Added clip {n} . {name}" | GATE: tests/test_video_multisource_v1.py:83 "Add clip onto V1" is the dialog Title at 5291 - keep it |
| U-142 | PreviewWindow.xaml.cs:5346,5369 | ConvertStatusText | WORDY | No clips on V1 to order. / V1 clip order applied -- top of the list plays first. Export uses this packed order. | "No clips to order." / "Clip order applied" | - |
| U-143 | PreviewWindow.xaml.cs:5379,5385-5386,5390-5393,5407 | ConvertStatusText / StatusText | DEVLEAK | BG removal script missing (tools/remove_bg.py). / Installing rembg (one-time)... / rembg install failed: {msg}. Need .venv + pip. See optional/README.md... / Removing background (rembg)... | "Background removal is not installed." / "Installing background removal (one-time)..." / "Background removal setup failed - see log." / "Removing background..." | - |
| U-144 | PreviewWindow.xaml.cs:5424 | GlassChoiceDialog "Background removed" body | TEACH | Preview on checkerboard (transparent != black). Save, copy, or discard. (non-ASCII in source) | "" (title is enough) | - |
| U-145 | PreviewWindow.xaml.cs:5435,5491,7963,7975 | ConvertStatusText | TEACH | Copied transparent PNG - paste or drag the picture out. / Copied {file} - paste into Mail, Teams or a folder. / Copied {n} chars - paste anywhere. (non-ASCII in source) | "Copied" / "Copied {file}" | - |
| U-146 | PreviewWindow.xaml.cs:5514,5536-5537,5548-5549,5553-5555,5560 | ConvertStatusText + ToastHud (ComfyUI) | DEVLEAK | ComfyUI is not running -- rembg / Copy still work / Wrote {file} -- run that workflow in ComfyUI. rembg / Copy still work. / Comfy sidecar written -- run the workflow locally | "sidecar" and "rembg" are internals. "ComfyUI is not running" / "Saved {file} for ComfyUI". One literal "rembg / Copy still work" must remain somewhere in this file - keep it in the 5514 toast | GATE: tests/test_comfyui_client.py:93-94 (ToastHud.Show + "rembg / Copy still work" in cs); tests/test_windows_ai_client.py:39 |
| U-147 | PreviewWindow.xaml.cs:5606,5616,5622,5626 | ConvertStatusText + ToastHud (Windows AI) | DEVLEAK | Could not write Windows AI sidecar: ... / Windows AI is not available -- rembg / Copy still work / Windows AI runtime present. Live enhance is still human. rembg / Copy still work. | "Windows AI is not available" / "Windows AI is available". "Live enhance is still human" is a dev status note | GATE: tests/test_windows_ai_client.py:39 ("rembg / Copy still work" in cs, see U-146) and :41 (click body must not contain "Wrote {IoPath.GetFileName(sidecar)}. Live enhance"); Services OfflineMessage() gated by tests/NetieSpace.Tests/WindowsAiClientTests.cs:15 |
| U-148 | PreviewWindow.xaml.cs:5970 | ConvertStatusText | TEACH | Summary docked left - [undock] puts it back on top. (non-ASCII in source) | "Summary docked" | - |
| U-149 | PreviewWindow.xaml.cs:5979-5981 | ConvertStatusText | DEVLEAK | Opened in LibreOffice - save there, then reopen here. / Install LibreOffice to edit PPTX (OnlyOffice is not used). (non-ASCII in source) | Keep the first. Second: "Install LibreOffice to edit slides." | - |
| U-150 | PreviewWindow.xaml.cs:6045-6046,6102,7589 | ConvertStatusText | TEACH | CSV edit - Tab between cells . autosaves / Markdown edit - Split / Source / Preview . autosaves / Editing - autosaves as you type. (non-ASCII in source) | "Editing . autosaves" | - |
| U-151 | PreviewWindow.xaml.cs:6234 | ConvertStatusText | DEVLEAK | Saving... pulling edits from the viewer (non-ASCII in source) | "Saving..." | - |
| U-152 | PreviewWindow.xaml.cs:6239 | ConvertStatusText | TEACH | Nothing to save - type in a form field, place a signature, or add a highlight. (non-ASCII in source) | "Nothing to save." | - |
| U-153 | PreviewWindow.xaml.cs:6247 | ConvertStatusText | CONFUSING | Saved into {file} - reopen to confirm (non-ASCII in source) | "reopen to confirm" reads as doubt about the save. "Saved . {file}" | - |
| U-154 | PreviewWindow.xaml.cs:6283-6287 | GlassChoiceDialog "Keep these details for Autofill?" body | WORDY | Netie can remember what you just typed so Autofill can reuse it. It stays sealed on this PC (DPAPI, per Windows account)... | Consent text stays, trimmed: "Netie can reuse what you typed for Autofill. It stays on this PC and is never uploaded.\n\n{proposal}" | - |
| U-155 | PreviewWindow.xaml.cs:6297 | ConvertStatusText | WORDY | Autofill will not learn from saves - fill My details by hand instead. (non-ASCII in source) | "Autofill learning off." | - |
| U-156 | PreviewWindow.xaml.cs:6413,6423,6437 | ConvertStatusText (sign placement) | DUP | Sign - click the page to place / Click the white page to place your signature / Using '{label}' - click the white page to place (non-ASCII in source) | Same instruction is also painted by the viewer banner (U-183). Keep these one-liners on the WPF strip; hide the viewer banner | - |
| U-157 | PreviewWindow.xaml.cs:6457 | ConvertStatusText | TEACH | Fill & Sign - click a cell to type, or use Sign below to place a signature . Save writes... (non-ASCII in source) | "Fill & Sign" | - |
| U-158 | PreviewWindow.xaml.cs:6552-6553,6559 | ConvertStatusText | DEVLEAK | Cert sign written (rewrote xref-stream, CMS self-check passed). Open in Adobe Reader to check... / Cert sign failed - original bytes restored. {ex} (non-ASCII in source) | "Certificate signature added." / "Certificate signing failed - file unchanged. {ex}" | GATE: tests/test_pdf_cms_sign.py:68 "Could not rewrite xref-stream/hybrid" is the exception text at 6549, not a status - keep |
| U-159 | PreviewWindow.xaml.cs:6652 | ConvertStatusText | CONFUSING | Fill closed | Fires when leaving any mode (see U-066). "" | - |
| U-160 | PreviewWindow.xaml.cs:6655-6663 | PdfTextBoxes_Click + status | DEAD | Text boxes - use viewer 'Text boxes' for the page you're on. (non-ASCII in source) | Handler has no XAML caller (grep PreviewWindow.xaml: 0 hits) and names a control that does not exist. Delete the handler | - |
| U-161 | PreviewWindow.xaml.cs:6675,6679 | ConvertStatusText | DEVLEAK | Native text layer . {n} boxes (page 1). Use More -> Scan & OCR for searchable PDF. / No text layer - page looks scanned. Use More -> Scan & OCR (needs Tesseract). (non-ASCII in source) | "This PDF has a text layer." / "No text layer - looks scanned. Use Scan & OCR." | - |
| U-162 | PreviewWindow.xaml.cs:6928,6936 | ConvertStatusText | TEACH | Highlight on - drag across text. Save writes marks into the PDF. / Mark on - drag to draw. Save writes marks into the PDF. (non-ASCII in source) | "Highlight on" / "Mark on" | - |
| U-163 | PreviewWindow.xaml.cs:6948 | ConvertStatusText | TEACH | Edit PDF - native text boxes only. Click a word to replace (whiteout + type). Scans: use OCR this page (seeds boxes; not full... (non-ASCII in source) | "Edit PDF - text only" (the "not full Acrobat OCR" gate is on the html banner, not here) | - |
| U-164 | PreviewWindow.xaml.cs:6981 | ConvertStatusText | TEACH | Fill - type in the blue fields, or click a blank line, then press Save (non-ASCII in source) | "Fill" | - |
| U-165 | PreviewWindow.xaml.cs:7112,7016,7029,1593,1624 | ConvertStatusText / PromptDialog title | DEVLEAK | OCR . {engine} . {n} box(es) - edit, then Save writes into this PDF / OCR . {engine} / Added image + OCR . {engine} (non-ASCII in source) | Drop engine names: "OCR . {n} boxes" / "OCR" / "Added image + OCR" | - |
| U-166 | PreviewWindow.xaml.cs:7552 | ConvertStatusText | DEVLEAK | HTML edit needs the preview WebView. | "Editor is not ready yet." | - |
| U-167 | PreviewWindow.xaml.cs:7787 | ConvertStatusText | DEVLEAK | Conversion engine not found. Ensure tools/convert_engine.py exists. | "Conversion tools are not installed." | - |
| U-168 | PreviewWindow.xaml.cs:8013 | ConvertStatusText | WORDY | Mail draft opened - the file is on your clipboard, press Ctrl+V to attach. (non-ASCII in source) | Keep the instruction (mailto cannot attach): "Mail opened - press Ctrl+V to attach the file" | - |
| U-169 | PreviewWindow.xaml.cs:8191 | ConvertStatusText | WORDY | Summary is local-only until you Allow cloud AI (tray). | Keep visible (R-0011 degraded mode must show): "Local summary - cloud AI is off" | - |
| U-170 | PreviewWindow.xaml.cs:8414 | ConvertStatusText | TEACH | Chat open - Pin note works offline . Setup for AI keys / Netie plan (non-ASCII in source) | "" | - |
| U-171 | PreviewWindow.xaml.cs:9093,9103-9105 | ConvertStatusText | DEVLEAK | Opened . Space backs out ({n} deep) / Back . {n} level(s) left / Back to first preview (non-ASCII in source) | "" | - |
| U-172 | PreviewWindow.xaml.cs:3228 | ConvertStatusText | CONFUSING | Removed - file back to clean copy (non-ASCII in source) | "Signature removed" | - |
| U-173 | PreviewWindow.xaml.cs:2684 | ConvertStatusText | DEVLEAK | Stamp failed - incomplete placement data. Try Sign again. (non-ASCII in source) | "Could not place the signature. Try again." | - |
| U-174 | PreviewWindow.xaml.cs:2300,8575 | ConvertStatusText | WORDY | Export continues in background - toast when done. / Captions continue in background - toast when done. (non-ASCII in source) | "Exporting in background..." / "Captions running in background..." | - |

## src/NetieSpace/Assets/pdfjs/netie-viewer.html

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-175 | netie-viewer.html:274-337 | #toolbar (Prev, Next, Continuous, -, +, Fit, Cancel placement, Fill, Highlight, Mark, Area, Search + all title= text) | DEAD | e.g. title "Fill & type - click a table cell or line, then type", "Drag across text to highlight - Save writes it into the PDF", "Type page number, Enter to go" (non-ASCII in source) | The host always hides it: PostDocChrome (cs:930-945) sends slim=true for PDF peek and canvasOnly=true for edit / workbench; CSS 139-143 hides #toolbar in both. Visible only as a pre-ready flash. Do not delete nodes (JS and the gate need the ids); strip the teaching title= text to one or two words; stop adding copy here | GATE: scripts/check-acrobat-chrome.ps1:16-21,49 (ids prev / pageHud / fillBtn kept; hiding rules asserted); :64 forbids adding id="save" or "Clear page" |
| U-176 | netie-viewer.html:336 and every status.textContent write (581,583,615,638-640,979-981,1070-1083,1096,1128-1130,1383,1623-1624,1637,1870,1907-1910,1928,2073,2136,2175-2177,2207,2234,2449,2466,2529,2664,2730-2733,2791,2839-2841,2918) | #status | DEAD | e.g. "Type here . drag corner to resize . A / A / [trash] above", "Fill mode - type, or drag a box to add", "Move over the page . click to place (Shift = snap to field)" (non-ASCII in source) | #status lives inside the hidden #toolbar, so none of these ~40 strings ever render in the app. Delete the writes, or route the few real state changes ("Saved into PDF", "Save failed: ...", "Reload failed: ...", "Failed: ...") through post({type:'status'}) to the WPF strip. Do not add a visible status bar to the viewer | - |
| U-177 | netie-viewer.html:345-350 | #pageHud (hudPrev / hudNext / hudZoomOut / hudZoomIn) | DEAD | Previous page / Next page / Zoom out / Zoom in | Hidden by both slim-chrome and canvas-only (CSS 139-143); WPF DocNavBar and DocZoomFloat own paging and zoom. Keep the node (gated), nothing else | GATE: scripts/check-acrobat-chrome.ps1:17,20 |
| U-178 | netie-viewer.html:307 | #stampConfirmBtn "Keep stamp" title "Legacy - auto-kept on drop" | DEAD | Keep stamp / Legacy - auto-kept on drop (non-ASCII in source) | style display:none; only 1277 can show it and 2172 calls keepPendingStamp() first so `committed` is always true. Its own title says Legacy. Remove the button and refs 307, 422, 1277, 2203, 2233, 2243, 2666 | none (not in any gate) |
| U-179 | netie-viewer.html:341 | #banner default text | TEACH | Drag signature onto the page - release to place, then drag the box (non-ASCII in source) | "" - the banner renders in light Edit and the workbench and overlays the page | - |
| U-180 | netie-viewer.html:1636 | banner (enterFillMode) | TEACH | Click a field, or drag to draw a fill box . A / A / [trash] . Save writes into this PDF (non-ASCII in source) | Hide the banner in fill mode | - |
| U-181 | netie-viewer.html:2174 | banner (stamp placed) | TEACH | Drag the signature box anytime . autosaves into this PDF (non-ASCII in source) | Hide | - |
| U-182 | netie-viewer.html:2206 | banner (keepPendingStamp) | TEACH | Drag signature boxes . Save is optional (autosave on) (non-ASCII in source) | Hide | - |
| U-183 | netie-viewer.html:2230 | banner (enterStampMode) | DUP | Click the page to place - then drag the box anytime (non-ASCII in source) | The WPF strip already says "Click the white page to place your signature" (U-156). Hide the banner; keep the WPF status | - |
| U-184 | netie-viewer.html:2706 | banner (enterTextEditMode) | TEACH | Native text - click a word to edit. Scans need OCR seed (not full Acrobat OCR). (non-ASCII in source) | Shorten but keep the honest qualifier: "Edit text (not full Acrobat OCR)" | GATE: scripts/check-acrobat-5d.ps1:18 "not full Acrobat OCR" must stay in this file |
| U-185 | netie-viewer.html:352,357 | #stampFloatBar title / #fillFloatBar title | WORDY | Resize or delete signature / Resize text or delete box | Delete the container titles; the buttons carry their own | - |
| U-186 | netie-viewer.html:1748 | fill-text resize handle title | TEACH | Drag to resize box | Delete (cursor is already nwse-resize) | - |

## src/NetieSpace/Assets/markdown/md-editor.html

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-187 | md-editor.html:106 | #hint default | DEVLEAK | Offline Marked (MIT) . chrome hides idle (non-ASCII in source) | "" | - |
| U-188 | md-editor.html:304 | #hint (paintCount) | TEACH | {n} words . {n} chars . Split=syntax . Ctrl+B/I/M . chrome hides idle (non-ASCII in source) | "{n} words . {n} chars" | - |
| U-189 | md-editor.html:105,219 | #btnAsk title + mdAsk hint | DEAD | Send selection to chat (host wires later) / Ask posted . chat wire waits host (non-ASCII in source) | The host never handles mdAsk or mdSelection (grep PreviewWindow.xaml.cs: 0 hits), so the button does nothing visible. Hide btnAsk (display:none) until wired, or wire mdAsk to ChatPanel. Keep the id and function | GATE: scripts/check-md-editor.mjs:12 (btnAsk) and scripts/check-multieditor-assets.ps1:29 (mdAsk, btnAsk) - ids must exist |
| U-190 | md-editor.html:103 | #btnAnnot title | WORDY | Wrap selection as a highlight that saves in the file | "Highlight selection" | GATE: id btnAnnot only |
| U-191 | md-editor.html:104 | #noteIn title | WORDY | Annotate uses this as the mark title | Delete the title (placeholder "optional note" stays) | GATE: id noteIn only |
| U-192 | md-editor.html:100 | #btnLh label | CONFUSING | Space | The app is called Space; this button is line spacing. "Spacing" | GATE: id btnLh only |
| U-193 | md-editor.html:95 | #btnSide label | CONFUSING | Side | "Outline" | GATE: id btnSide only |
| U-194 | md-editor.html:102,389 | #btnImg label | WORDY | Img / Img 70% | "Images" / "Images 70%" | - |
| U-195 | md-editor.html:463 | demo text (params demo=1) | DEAD | ...Select a sentence, optional note, Annotate. Ctrl+M. Split shows syntax; Preview hides it. | The host only loads ?boot=1 (cs:6021), never ?demo=1. Keep the `demo` branch (gated needle) but drop the teaching sentence | GATE: scripts/check-md-editor.mjs:12 needs the literal `demo` |

## src/NetieSpace/Assets/markdown/csv-editor.html (sibling canvas, same folder)

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-196 | csv-editor.html:35 | .hint | TEACH | Offline . Tab between cells . autosaves (non-ASCII in source) | "autosaves" or "" | GATE: scripts/check-multieditor-assets.ps1:34 asserts ids only |

## src/NetieSpace/App.xaml.cs (tray menu, balloons, MessageBox)

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-197 | App.xaml.cs:771 | tray "Show tip" | CONFUSING | Show tip | Re-shows the startup HUD. "Show welcome", or remove | - |
| U-198 | App.xaml.cs:802 | tray "Sign out (clear license)" | WORDY | Sign out (clear license) | "Sign out" | - |
| U-199 | App.xaml.cs:822-824 | tray Local memory header | DEVLEAK | Local memory [check] (dual-brain on) / Local memory... (dual-brain) (non-ASCII in source) | "Local memory: on" / "Local memory..." | - |
| U-200 | App.xaml.cs:830-835 | MessageBox "Netie Space - Local memory" | DEVLEAK | Local dual-brain is ON (private folder on this PC only).\n\nYes = wipe all embeddings & revoke consent\nNo = keep memory\nCancel = close (non-ASCII in source) | "Local memory is on. Wipe it and turn it off?" with Yes / No | - |
| U-201 | App.xaml.cs:843 | MessageBox | DEVLEAK | Local memory wiped. Summaries will not use dual-brain until you Allow again. | "Local memory wiped." | - |
| U-202 | App.xaml.cs:860-861 | tray Cloud AI header | WORDY | Cloud AI [check] (chat/summary leave this PC) / Cloud AI notice... (non-ASCII in source) | "Cloud AI: on" / keep "Cloud AI notice..." | GATE: scripts/check-cloud-disclose.ps1:14 "Cloud AI notice" |
| U-203 | App.xaml.cs:867-871 | MessageBox "Netie Space - Cloud AI" | WORDY | Cloud AI is ON. Chat and summary send file text to Groq/Gemini/OpenRouter/SEA-LION.\n\nYes = turn off (local Autofill still works)\nNo = keep cloud on | Disclosure stays; drop the Yes/No legend: "Cloud AI is on - chat and summary send file text to your AI provider. Turn it off?" | - |
| U-204 | App.xaml.cs:895,900-906 | tray "Usage footprint..." + MessageBox | DEVLEAK | Usage footprint... / Local events: {n}\nLog size: {n} bytes\n\nTracks extensions + actions only (no file paths/contents).\nEncrypted learning blob:\n{export} | "Usage stats..."; drop the blob dump from the dialog (write it to the log folder instead) | - |
| U-205 | App.xaml.cs:1106-1114 | About MessageBox | CONFUSING | * Select a file in Explorer or Desktop -> Space to preview\n* Space again closes the preview\n* [down] MD / [down] PDF - saves next to original file\n* Tray -> API Keys... (non-ASCII in source) | "[down] MD / [down] PDF" buttons no longer exist (the Share sheet has Markdown / PDF rows). Rewrite the bullets to the current UI: Space = preview, Space again = close, Share = convert, Settings = keys | - |
| U-206 | App.xaml.cs:1165-1166 | balloon | WORDY | Netie Space is ready / Already running in the tray - select a file in Explorer or Desktop, then press Space. (non-ASCII in source) | "Already running - select a file and press Space" | - |

## src/NetieSpace/Controls/ChatPanel.xaml + .cs (the chat panel hosted inside PreviewWindow)

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-207 | ChatPanel.xaml:22 | close button ToolTip | WORDY | Close panel (chat stays saved on this file) | "Close" | - |
| U-208 | ChatPanel.xaml:28 + ChatPanel.xaml.cs:590-596 | SubtitleText and its runtime variants | WORDY | Saved with this file . works offline as notes / Notes mode - ideas stay on this file, no AI calls / Cite . quotes the open document with short citations / Mindmap . indented outline / mermaid tree... (non-ASCII in source) | "Saved with this file" / "Notes mode (no AI)" / "Cite mode" / "First Principles mode" / "Mindmap mode" | - |
| U-209 | ChatPanel.xaml:35 | New ToolTip | WORDY | Start a fresh thread (archives the current one) | "New thread" | - |
| U-210 | ChatPanel.xaml:42 + ChatPanel.xaml.cs:598 | NotesOnlyButton ToolTip + "Notes ON" | WORDY | Save ideas without calling AI / Notes ON | "Notes only (no AI)"; keep the label "Notes" and show the on-state by style | - |
| U-211 | ChatPanel.xaml:48,52,56 | mode chip ToolTips (Cite / First Principles / Mindmap) | WORDY | Quote the open document. Short citations (page / heading / excerpt). Click again for normal chat. / Strip to axioms, then rebuild. No fluff... / Compact indented outline / mermaid-friendly tree... | "Answer with citations" / "Reason from first principles" / "Outline this file" | - |
| U-212 | ChatPanel.xaml:79 | AssetLookupButton ToolTip | WORDY | Define / explain the first text attachment now | "Explain first attachment" | - |
| U-213 | ChatPanel.xaml:93 + ChatPanel.xaml.cs:293 | AssetHint default | TEACH | Add a question below, then Send - attachments go with it. (non-ASCII in source) | "" | - |
| U-214 | ChatPanel.xaml:115 | InputBox ToolTip | WORDY | Enter = send . Ctrl+Enter = new line . Notes mode saves without AI (non-ASCII in source) | "Enter to send, Ctrl+Enter for a new line" | - |
| U-215 | ChatPanel.xaml.cs:579-584 | ShowWelcome bubble | TEACH | Attached to this file. Notes work offline (Pin note). Send uses AI when a key is set.\n\nPDF: drag Select area to clip a table or figure... (non-ASCII in source) | Delete the welcome bubble | - |
| U-216 | ChatPanel.xaml.cs:512 | thinking bubble | DEVLEAK | Netie Cortex . thinking... (non-ASCII in source) | "Thinking..." | - |

## src/NetieSpace/Views/ApiSetupWindow.xaml + .cs

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-217 | ApiSetupWindow.xaml:64,67 | plan card title + body | DEVLEAK | 1 . Netie Space plan (recommended) / Unlock the full app first - device license, updates, and the path to Ult hosted AI. Start here before pasting random keys. (non-ASCII in source) | "Netie Space plan" / "Device license and updates." ("Ult" is roadmap; "random keys" talks down to the user) | - |
| U-218 | ApiSetupWindow.xaml:75 | ManageBillingButton ToolTip | WORDY | Stripe Customer Portal after you have paid once | "Billing portal" | GATE: tests/test_human_gates.py:66 x:Name only |
| U-219 | ApiSetupWindow.xaml:79 | ActivateLaptopButton ToolTip | DEVLEAK | After Stripe pay: bind this PC (same as tray Account Activate) | Delete tooltip | GATE: tests/test_human_gates.py:67 x:Name only |
| U-220 | ApiSetupWindow.xaml:101 | section title | DEVLEAK | 2 . Bring your own key (FreeRoute / BYO) (non-ASCII in source) | "Your own API keys" | - |
| U-221 | ApiSetupWindow.xaml:105 | section body | WORDY | Keys are masked after save (first/last chars only). You cannot edit an existing key - delete it, then add a new one. Prefer Connect OpenVault as the source of truth. (non-ASCII in source) | "Keys are masked after saving. To change one, delete it and add a new key." | - |
| U-222 | ApiSetupWindow.xaml:212 | OpenVault body | DEVLEAK | OpenVault (D:\OpenVault . http://127.0.0.1:5000) is the source of truth for API keys, passwords, and high-security secrets... (non-ASCII in source) | "OpenVault holds your keys and passwords. Netie keeps a masked copy." | - |
| U-223 | ApiSetupWindow.xaml:223 | Local password vault ToolTip | DEVLEAK | Offline DPAPI fallback - not a second SoT (non-ASCII in source) | Delete tooltip | - |
| U-224 | ApiSetupWindow.xaml.cs:31-32,305 | LicenseStatusText | DEVLEAK | This device is licensed. You're on the Netie path - BYO keys below are optional until Ult ships. / Not licensed yet on this device. Get a Netie plan first for the full product; keys below are the free FreeRoute/BYO path. (non-ASCII in source) | "This device is licensed. API keys below are optional." / "Not licensed on this device yet." | - |
| U-225 | ApiSetupWindow.xaml.cs:47-50 | LicenseStatusText append | DEVLEAK | Worker Stripe is configured -- Get Netie subscription should open Checkout. / Worker Stripe secrets are not set yet (stripe_not_configured). See docs/HUMAN_STRIPE_CHECKOUT.md. | Delete (log it instead) | - |
| U-226 | ApiSetupWindow.xaml.cs:122-124 | VaultStatusText | DEVLEAK | OpenVault online . {url} / OpenVault offline ({url}) - using local masked cache only. (non-ASCII in source) | "OpenVault online" / "OpenVault offline - using local copy" | - |
| U-227 | ApiSetupWindow.xaml.cs:360-371 | MessageBox "Netie subscription" | DEVLEAK | Checkout is not live yet (stripe_not_configured). ... 3. If still blocked: set Worker secrets in docs/HUMAN_STRIPE_CHECKOUT.md. ... Video Export clicks (Playwright cannot open this WPF app): docs/HUMAN_PILLAR1_TEST.md. | An internal runbook pasted into a user dialog. Keep only: "Checkout is not available yet. Sign in (tray > Account), then try again." | - |

## src/NetieSpace/Views/LoginWindow.xaml

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-228 | LoginWindow.xaml:64 | intro TextBlock | WORDY | Sign in with email. One laptop per license. Offline edit works after activate - AI still needs network. (non-ASCII in source) | "One laptop per license. AI features need internet." | - |
| U-229 | LoginWindow.xaml:103 | offline button | WORDY | Continue offline (if already activated) | "Continue offline" | - |

## src/NetieSpace/Views/FillProfileWindow.xaml

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-230 | FillProfileWindow.xaml:27 | intro TextBlock | DEVLEAK | Stored on this PC only (Windows DPAPI). Autofill matches field type - dates keep Jun vs June from the form. Nothing is uploaded. | "Stored on this PC only. Never uploaded." | - |
| U-231 | FillProfileWindow.xaml:44 | DobBox ToolTip | WORDY | Any common date. Autofill reformats to the field (27-Jun-2003 vs 27/06/2003). | Delete (the Preferred date format picker below covers it) | - |

## src/NetieSpace/Views/PdfMergeWindow.xaml

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-232 | PdfMergeWindow.xaml:38 | header | WORDY | Merge - arrange order (non-ASCII in source) | "Merge" | - |
| U-233 | PdfMergeWindow.xaml:48 | intro TextBlock | TEACH | Top file appears first. Add PDFs, images, Office docs, or a whole folder. Non-PDFs convert on Merge. | "Top file first." | - |

## src/NetieSpace/Views/PdfOrganizeWindow.xaml + .cs

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-234 | PdfOrganizeWindow.xaml:59 | intro TextBlock | TEACH | 4 pages per row . teal slot shows insert . long-press to peek. Apply writes a new PDF. (non-ASCII in source) | Keep only the honest half: "Apply writes a new PDF." | - |
| U-235 | PdfOrganizeWindow.xaml:163-166 | Add files... (InsertImage_Click) next to Merge... (OpenMerge_Click) | CONFUSING | Add files... / Merge... | Both add pages from other files. "Insert images..." / "Merge PDFs..." | - |
| U-236 | PdfOrganizeWindow.xaml.cs:236 | StatusText | DEVLEAK | Convert engine missing - cannot add this image type. (non-ASCII in source) | "Cannot add this image type." | - |

## src/NetieSpace/Views/SignatureDialog.xaml

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-237 | SignatureDialog.xaml:49 | hint TextBlock | TEACH | After place: drag the stamp anywhere. Hold Shift near a form field to snap. Stamps stay temporary until Save. | Delete | - |
| U-238 | SignatureDialog.xaml:54 | primary button | WORDY | Stamp onto PDF | "Place" | - |

## src/NetieSpace/Views/VideoOrderWindow.xaml

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-239 | VideoOrderWindow.xaml:38 | header | DEVLEAK | V1 - clip order (non-ASCII in source) | "Clip order" | - |
| U-240 | VideoOrderWindow.xaml:41 | close button Content | CONFUSING | X | Every other window uses the multiplication-cross close glyph (U+2715). Use the same glyph | - |
| U-241 | VideoOrderWindow.xaml:48 | intro TextBlock | TEACH | Top clip plays first. Drag or use Up/Down. Export uses this packed order (Fade/Wipe still join mixed files). | "Top clip plays first." | GATE: tests/test_video_clip_order.py:52 "Top clip plays first" (keep); :53 "Apply order" (keep) |

## src/NetieSpace/Views/PasswordVaultWindow.xaml

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-242 | PasswordVaultWindow.xaml:49 | intro TextBlock | DEVLEAK | Local DPAPI vault on this Windows user - like iCloud Passwords, Space-side. Sync across devices lands with Space bridge. (non-ASCII in source) | "Stored encrypted on this PC. Not synced." | - |

## src/NetieSpace/Views/StartupHud.xaml, ToastHud.xaml

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-243 | StartupHud.xaml:44 | cue TextBlock | TEACH | SELECT A FILE . PRESS SPACE (non-ASCII in source) | Keep. The app has no main window and this is the only first-run cue (already one-shot via StartupHudSeen). Do not add more copy here | - |
| U-244 | ToastHud.xaml:38 | footer TextBlock | TEACH | Click to open folder . auto-dismiss (non-ASCII in source) | "Click to open" | - |

## src/NetieSpace/Views/GlassChoiceDialog.xaml.cs, PromptDialog

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-245 | GlassChoiceDialog.xaml.cs:28,39,44 | extra-text label + placeholder shown twice | DUP | Optional title / on-screen text / {placeholder} as TextBox ToolTip and again as a TextBlock under it | Keep the label and the ToolTip; drop the TextBlock copy | - |

PromptDialog prompts ("Pages to extract (1-{n}). Examples: 1-3,5 or 2", "Pages to rotate (1-{n}). Leave blank for all.", "Text to stamp across every page:", "Password to open this PDF:") are fine; the one bad prompt is U-139.

## src/NetieSpace/Views/CloudPathDisclosureWindow.cs, EmbeddingConsentWindow.cs (disclosure text - WORDY only)

| # | file:line | element/id | class | current text (trimmed to 80 chars) | action | gate |
|---|---|---|---|---|---|---|
| U-246 | CloudPathDisclosureWindow.cs:57-62 | body bullets | WORDY | * Image OCR stays local unless you set NETIE_SPACE_OCR_CLOUD=1 / * Keys still live in user.env until the vault path ships (non-ASCII in source) | Disclosure stays (Space#3 signed). Trim to: "Chat, summary and word lookup send text from the open file to the cloud provider shown in the toolbar.\n\nAutofill, My details and image OCR stay on this PC.\n\nContinue with cloud AI on this PC?" | GATE: scripts/check-cloud-disclose.ps1:5 "Cloud AI leaves this PC", "Allow cloud AI", EnsureAcked must stay |
| U-247 | EmbeddingConsentWindow.cs:47,56-61 | title + body | WORDY | Local dual-brain memory / ...embeddings and a file graph - only on this PC under a hidden proprietary folder... (non-ASCII in source) | Title "Local memory". Body: "Netie can keep a private index of files you preview, only on this PC. Never uploaded. Wipe or turn off any time (tray > Local memory)." Keep "Allow local memory" / "Not now" | - |

## No findings

- `src/NetieSpace/Views/PreviewHandlerOverlay.cs`: no user-visible copy (the only string literals are Win32 class names).
- `src/NetieSpace/Assets/code-viewer/**`: only `github-dark.min.css` and `highlight.min.js`; no visible copy. The code page is built in `ContentParserService.cs` (out of scope).
- `PromptDialog.xaml`, `GlassChoiceDialog.xaml`: chrome only ("OK", "Cancel").

## Summary

### Counts per class (247 findings, U-001 to U-247)

| Surface | TEACH | WORDY | DEVLEAK | DEAD | DUP | CONFUSING | Total |
|---|---|---|---|---|---|---|---|
| PreviewWindow.xaml | 21 | 36 | 9 | 5 | 13 | 10 | 94 |
| PreviewWindow.xaml.cs | 25 | 15 | 32 | 1 | 2 | 5 | 80 |
| netie-viewer.html | 6 | 1 | 0 | 4 | 1 | 0 | 12 |
| md-editor.html | 1 | 3 | 1 | 2 | 0 | 2 | 9 |
| csv-editor.html | 1 | 0 | 0 | 0 | 0 | 0 | 1 |
| App.xaml.cs | 0 | 4 | 4 | 0 | 0 | 2 | 10 |
| Controls/ChatPanel | 2 | 7 | 1 | 0 | 0 | 0 | 10 |
| ApiSetupWindow | 0 | 2 | 9 | 0 | 0 | 0 | 11 |
| LoginWindow | 0 | 2 | 0 | 0 | 0 | 0 | 2 |
| FillProfileWindow | 0 | 1 | 1 | 0 | 0 | 0 | 2 |
| PdfMergeWindow | 1 | 1 | 0 | 0 | 0 | 0 | 2 |
| PdfOrganizeWindow | 1 | 0 | 1 | 0 | 0 | 1 | 3 |
| SignatureDialog | 1 | 1 | 0 | 0 | 0 | 0 | 2 |
| VideoOrderWindow | 1 | 0 | 1 | 0 | 0 | 1 | 3 |
| PasswordVaultWindow | 0 | 0 | 1 | 0 | 0 | 0 | 1 |
| StartupHud / ToastHud | 2 | 0 | 0 | 0 | 0 | 0 | 2 |
| GlassChoiceDialog | 0 | 0 | 0 | 0 | 1 | 0 | 1 |
| CloudPath / EmbeddingConsent | 0 | 2 | 0 | 0 | 0 | 0 | 2 |
| **Total** | **62** | **75** | **60** | **12** | **17** | **21** | **247** |

DEAD removals that a gate forbids (keep the node, or update the gate in the same PR): U-044 DocFloatDoneBtn,
U-088 DocToolFloat, U-175 viewer #toolbar ids, U-177 #pageHud, U-189 btnAsk, U-195 md demo branch.
DEAD removals with no gate at all: U-030 Tools tab, U-040 "PDF tools / Pick a mode" DockPanel, U-051 Compress
coming-soon, U-160 PdfTextBoxes_Click, U-178 "Keep stamp".

### Top 10 highest-impact items

1. U-175 / U-176 / U-177 - the viewer's HTML toolbar, its `#status` span and the page HUD never render in the app
   (hidden by slim-chrome in peek and canvas-only in edit / workbench). About 40 status strings and a dozen
   teaching tooltips are maintained for nothing. Stop writing copy there; route the four real state changes to the WPF strip.
2. U-043 / U-045 / U-048 / U-050 / U-052 / U-054 / U-058 - in the workbench every PDF tool has three entry points
   (ribbon chip, TOOLS rail, menu bar) and Save has three. One home per action: rail + menu, ribbon keeps only the
   secondary panel for the active mode plus More.
3. U-024 / U-025 / U-029 / U-031 / U-044 - five controls leave the workbench (Done, File > Leave workbench, Home tab,
   the file-tab X that says "Close", a permanently hidden Done). Keep Done + the menu item, both worded "Back to preview".
4. U-179 to U-184 - the viewer banner teaches on every mode switch ("Click a field, or drag to draw a fill box . A / A /
   trash . Save writes into this PDF"). The WPF strip already carries the one-line placement prompt (U-156). Hide the
   banner except the gated "not full Acrobat OCR" line, shortened.
5. U-040 / U-051 / U-178 / U-030 - dead chrome still in the tree: the collapsed "PDF tools / Pick a mode" header row,
   a disabled "Compress... (Coming soon)" menu item, a "Legacy" Keep stamp button, and a Tools tab whose handler is a
   no-op. None of the four is gated; delete them.
6. U-102 / U-120 / U-128 / U-133 / U-140 / U-143 / U-146 / U-158 - the status strip speaks developer: PDFSpace,
   AcroForm widgets, xfade wipeleft, whisper.cpp, scripts/download-ffmpeg.ps1, rembg + .venv + pip, sidecar,
   xref-stream / CMS self-check. Rewrite in user words; keep exactly one "rembg / Copy still work" for the gate.
7. U-217 / U-220 / U-222 / U-224 / U-225 / U-227 - Settings leaks the roadmap and the ops runbook: "Ult hosted AI",
   "FreeRoute / BYO", D:\OpenVault + 127.0.0.1:5000, "Worker Stripe secrets ... docs/HUMAN_STRIPE_CHECKOUT.md",
   and a MessageBox that ends with a Playwright note. This is the one surface a paying customer must read.
8. U-070 / U-078 / U-084 / U-081 / U-083 / U-113 / U-114 - the video editor narrates itself: a hint that lists the four
   chips beside it, a light-preview hint under the player, a "Move playhead -> Split -> select clip -> Delete" strip,
   and tooltips naming ffmpeg / whisper.cpp. Delete the three hints; shorten the tooltips.
9. U-199 to U-205 - tray and About: "dual-brain", "Encrypted learning blob" dumped into a MessageBox, Yes/No/Cancel
   legends inside the question, and About bullets that describe buttons ("down MD / down PDF") which no longer exist.
10. U-016 to U-023 and U-187 / U-189 / U-192 - the Share sheet explains every row under the row ("Paste straight into
    Mail, Teams or a folder"), and the Markdown editor has a button labelled "Space" (line spacing, in an app called
    Space), an Ask button the host never wires, and a hint that names the library licence.

### What this audit does not prove

- Line numbers are from the working tree on 2026-09-17 (branch chore/consolidate-space, uncommitted edits present);
  re-grep before editing.
- Visibility claims are static reads of ApplyWorkbenchShell / UpdateActionButtons / PostDocChrome; nobody ran the exe.
  The verifier should open one PDF in peek, light Edit and the workbench and confirm the HTML toolbar never shows.
- Services-layer strings (ImageEditService.PromptCompress, WindowsAiClient.OfflineMessage, LocalSummaryService errors)
  were out of scope and were not audited.

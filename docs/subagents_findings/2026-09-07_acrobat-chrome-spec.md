```yaml
keywords:
  - acrobat-chrome
  - netie-viewer
  - previewwindow
  - pdfeditorribbon
  - pdfspacerail
  - light-chrome
  - icon-label
  - thumbs
  - page-hud
  - organize
  - fill-and-sign
main_idea: Original Netie document chrome for Edit-in-app. Light gray/white on live IDs. Left thumbs + WPF right rail. Save stays PdfSaveStampsButton. No Adobe Home, Tools grid, dual-window, or brand.
date: 2026-09-07
kind: lens
product: Space
reuse: docs/subagents_findings/2026-09-07_acrobat-chrome-prd.md, docs/subagents_findings/2026-09-04_acrobat-pdfspace-engines.md, docs/research/findings/2026-09-05_acrobat_dc_distill.md
cite: https://helpx.adobe.com/acrobat/desktop/edit-documents/organize-pages/move-between-pdfs.html
```

# Netie document chrome spec (Edit-in-app)

PREFLIGHT: PARTIAL. Reused acrobat-chrome-prd, acrobat-pdfspace-engines, acrobat_dc_distill. Helpx organize/combine and Experience League new-experience timed out or were stubs; public search + founder screenshots + live HTML/XAML are the inventory.

Research-only. Do not clone Adobe brand, Home cloud, AI Assistant, Sign In, Share-to-Adobe. Do not scrape Adobe binaries. Do not invent Redact, Hand tool, FORMAT pane, Tools card home, or dual tiled PDF windows.

Scope: `netie-viewer.html` CSS/toolbar markup + WPF `PdfEditorRibbon` / `PdfSpaceRail` when `_pdfSpaceMode`. Keep every existing button ID and every `Click=` handler. Light chrome is local to workbench mode, not a global App.xaml glass rewrite.

---

## 1. Screenshot inventory (what was seen vs what exists)

Do not treat screenshot labels as new product. Ground truth is source IDs.

### 1.1 Current Netie viewer (screenshot 1, Chrome `127.0.0.1:8522/netie-viewer.html`)

Seen:

- Dark charcoal canvas outside the page; white page; teal active chip.
- Top text chips; left thumbnail column; status line `Fit width -- pages centered`.
- Two light-gray **Save** / **Clear page** rectangles sitting on the form.

Source (`netie-viewer.html` toolbar HTML, keep these IDs):

| ID | Label now | Group |
|---|---|---|
| `prev` | ‹ | page nav |
| `pageInput` | number | page nav |
| `pageTotal` | / N | page nav |
| `next` | › | page nav |
| `modeBtn` | Continuous / One page | page nav |
| `zoomOut` | - | zoom/fit |
| `zoomLabel` | 100% | zoom/fit |
| `zoomIn` | + | zoom/fit |
| `fit` | Fit | zoom/fit |
| `stampBtn` | Cancel placement | hidden until stamp |
| `stampConfirmBtn` | Keep stamp | `display:none` legacy |
| `fillBtn` | Fill text | fill/markup |
| `hlBtn` | Highlight | fill/markup |
| `markBtn` | Mark | fill/markup |
| `regionBtn` | Select area | fill/markup |
| `searchToggle` | Search | search |
| `searchBox` `searchPrev` `searchNext` | find panel | search |
| `status` | status | status |

Not in source (do not add): Thumbnail chip, Redact, overlay Save, overlay Clear page.

Screenshot **Save / Clear page** sits on the Singapore form pixels. There is no `#save` / `#clearPage` in the viewer. Host already has `clearFillTexts`. Save is WPF `PdfSaveStampsButton`. If those rectangles are AcroForm widgets, leave them as document content.

`#thumbs` always-on left rail. No page numbers under canvases. Selected state is teal 2px (`canvas.active`). `.thumb-more` loads batches of 12.

Other live chrome to restyle, not delete: `#banner`, `#stampFloatBar`, `#fillFloatBar` (A / A / trash). Those are mode HUDs, not page Save.

### 1.2 Acrobat Reader document toolbar (screenshot 2)

Classic light gray/white. Top icon groups with 1px vertical separators: file (Save/print), page up/down + `3 / 5`, select vs hand, zoom %, Share.

Map only what Space already has: page nav, zoom/fit, search. Do not add Hand, Print, Email, cloud upload, Share.

### 1.3 Acrobat Pro Edit PDF (screenshot 3)

Secondary mode row (Edit / Add Text / Add Image / Link / Crop) plus right FORMAT pane (font family, size, color, lists).

Map: WPF secondary panels already exist (`PdfFillPanel`, `PdfSignPanel`, `PdfOrganizePanel`, `PdfCommentPanel`). FORMAT = keep `#fillFloatBar` + ribbon A- / A+. Do not build a FORMAT properties pane. Do not add Add Image / Link / Crop / Header & Footer.

### 1.4 Acrobat Tools card grid (screenshot 4)

Card home with Open dropdowns. PARK. Space rail is an icon+label list, not a Tools home.

### 1.5 Acrobat Home (Andy) (screenshot 5)

Cloud recent files, AI banner, Sign In. PARK. Space Quick Look is not this surface.

### 1.6 Compact Edit PDF toolbar (screenshot 6)

Two stacked rows: (1) page `1 / 173`, select, hand, zoom, fit; (2) Edit / Add Text / Add Image / Link.

Netie split: row 1 lives in the WebView toolbar; row 2 lives in WPF `PdfPrimaryRow` + mode panel. No hand/select tools (viewer is already click-to-fill / drag-to-mark).

### 1.7 New Acrobat Menu > View > Page navigation (screenshot 7)

Public layout (search + screenshots, not scraped binaries):

- New experience: All tools LEFT, nav pane RIGHT, floating page HUD bottom-right.
- Classic: nav LEFT, tools RIGHT, top-docked toolbar.

Netie keeps **classic document geometry** (thumbs left, tools right) because that is already shipped. Do not flip rails to clone New Acrobat. Optional HUD is a convenience clone of `#prev`/`#next`/`#zoomIn`/`#zoomOut` only.

Nested Menu shortcuts seen: Next = Right Arrow, Previous = Left Arrow, First = Home, Last = End, Go to page = Shift+Ctrl+N, Find = Ctrl+F.

---

## 2. Target layout (existing capabilities only)

```
+------------------------------------------------------------------+
| WPF title (NETIE SPACE | filename)     [Edit in app = Done]      |
+------------------------------------------------------------------+
| WPF mode strip: Fill & Sign | Edit PDF | Organize | Comment |    |
|                 Save | More...                                   |
| WPF secondary: only the active mode panel                        |
+--------+---------------------------------------------+-----------+
| thumbs | WebView2 viewer (light canvas + white page) | PdfSpace  |
| left   | #toolbar: nav | zoom/fit | fill/hl/mark |   | Rail      |
| #thumbs| search                                      | icon+label|
| + nums | [optional #pageHud bottom-right]            | list      |
+--------+---------------------------------------------+-----------+
```

### 2.1 Viewer toolbar groups (HTML, same IDs)

Insert `<span class="sep">` between groups. Restyle buttons as icon+label (inline SVG + `<span class="lbl">`). Do not rename IDs.

1. Page nav: `prev` `pageInput` `pageTotal` `next` `modeBtn`
2. Zoom/fit: `zoomOut` `zoomLabel` `zoomIn` `fit`
3. Fill / highlight / mark / region: `fillBtn` `hlBtn` `markBtn` `regionBtn` (`stampBtn` stays in this group, hidden until `.active`)
4. Search: `searchWrap` (unchanged IDs)
5. `status` margin-left auto

Labels (text only, IDs stay):

| ID | New visible label |
|---|---|
| `prev` | Prev |
| `next` | Next |
| `modeBtn` | keep Continuous / One page (JS already sets textContent) |
| `zoomOut` | - |
| `zoomIn` | + |
| `fit` | Fit (status already says Fit width when continuous) |
| `fillBtn` | Fill |
| `hlBtn` | Highlight |
| `markBtn` | Mark |
| `regionBtn` | Area |
| `searchToggle` | Search |

### 2.2 Left thumbs

Keep `#thumbs`. In `renderThumbBatch` wrap each canvas:

```
div.thumb-item
  canvas[data-page]
  span.thumb-num   (page i)
```

CSS: selected `.thumb-item.active` or `canvas.active` = 2px `--doc-accent` ring, not teal. Page number 11px `--doc-muted` under the canvas. Pane width 120px. White/off-white thumbs on `#F5F5F5`. Keep `.thumb-more`.

### 2.3 Right tools rail (WPF, already exists)

`PdfSpaceRail` 188px. Restyle to icon+label rows (stretch buttons, 18px glyph + 12px label). Do not build screenshot-4 cards.

Keep Click handlers:

| Button text | Handler |
|---|---|
| Fill & Sign | `PdfFillAndSign_Click` |
| Edit PDF | `PdfEditTextMode_Click` |
| Sign | `PdfModeSign_Click` |
| Organize | `PdfModeOrganize_Click` |
| Comment | `PdfModeComment_Click` |
| Autofill form | `PdfAutofill_Click` |
| My details | `PdfFillProfile_Click` |
| OCR this page | `PdfOcrSeed_Click` |
| Blank PDF | `PdfCreateBlank_Click` |
| Combine | `PdfMerge_Click` |
| Export | `PdfExportAsMenu_Click` |
| Watermark | `PdfWatermark_Click` |
| Protect | `PdfProtect_Click` |

Wordmark: "Tools" or keep "PDFSpace" as mode label. Not PDFSPACE teal caps. Not Adobe Tools.

### 2.4 WPF mode strip (`PdfPrimaryRow`)

Today: Organize, Sign, Fill, Comment, |, Save, More.

Target primary (same handlers, fewer competing chips):

| Visible | Click |
|---|---|
| Fill & Sign | `PdfFillAndSign_Click` (already opens fill+sign panels) |
| Edit PDF | `PdfEditTextMode_Click` (today buried in More + rail) |
| Organize | `PdfModeOrganize_Click` |
| Comment | `PdfModeComment_Click` |
| Save | `PdfSaveStamps_Click` |
| More... | `PdfMoreButton_Click` |

Sign stays as secondary under Fill & Sign (`PdfSignPanel` when `_pdfToolMode` is `sign` or `fillsign`). Do not drop `PdfModeSignBtn` if other code styles it; hide it in the primary row and keep the handler on the rail.

Save is this ribbon button only. Never a floating chip on `#pages`.

### 2.5 Optional page HUD

New wrapper `#pageHud` is allowed. Buttons inside must **not** duplicate IDs. They click the live controls:

- up -> `#prev.click()`
- down -> `#next.click()`
- + / - -> `#zoomIn` / `#zoomOut`

Park the HUD if the top toolbar already feels crowded. Do not add rotate or marquee zoom (no handlers).

### 2.6 Keyboard (only existing handlers)

| Key | Today | This chrome pass |
|---|---|---|
| `#prev` / `#next` click | `goToPage(pageNum +/- 1)` | Keep. Optional: keydown Left/Right calls those clicks when target is not INPUT/TEXTAREA/contenteditable |
| Home / End in viewer | none | Do not add. WPF Home/End is Explorer folder travel and returns early when `_editMode` |
| Ctrl+F | none | Optional: `ctrlKey && key==='f'` -> existing `searchToggle.click()`. Do not write a second search |
| Shift+Ctrl+N | none | Park. Focus `#pageInput` is enough |
| Delete on stamp | exists | Keep |
| Ctrl+/- / Ctrl+0 | WPF `AdjustZoom` | Leave. PDF scale is `#zoomIn`/`#zoomOut` |

Do not steal arrows from fill typing. Do not re-enable Explorer sibling travel inside Edit-in-app.

### 2.7 Organize between PDFs

Helpx move-between-pdfs: open two PDFs, Tile Vertically, drag thumbs across.

Space map (already shipped):

- Within one file: `PdfOrganizeWindow` (Reorder, rotate, delete, Add files, Merge)
- Across files: `PdfMergeWindow` via rail Combine / ribbon Merge
- Extract / Split / Rotate 90 / Reverse: existing `PdfExtract_Click` etc.

Do not clone dual-window tile. Distill = "use Organize + Combine dialogs".

---

## 3. Color tokens (hex) -- original Netie document app

Apply when viewer CSS is light AND when `_pdfSpaceMode` on ribbon/rail. Do not retoken all of `App.xaml`.

```css
html.doc-app {
  color-scheme: light;
  --doc-canvas: #E8E8E8;
  --doc-toolbar: #F5F5F5;
  --doc-rail: #F3F3F3;
  --doc-page: #FFFFFF;
  --doc-line: #D6D6D6;
  --doc-text: #2C2C2C;
  --doc-muted: #6E6E6E;
  --doc-accent: #0B5CAB;   /* document blue, not teal #5EC4B8, not Adobe red */
  --doc-hover: #E8F2FF;
  --doc-on: #D6E8F8;
  --doc-save: #0B5CAB;
}
```

WPF local brushes (only PdfEditorRibbon / PdfSpaceRail / mode panels while `_pdfSpaceMode`):

| Role | Hex |
|---|---|
| Ribbon/rail bg | `#F5F5F5` |
| Mode panel bg | `#EFEFEF` |
| Border | `#D6D6D6` |
| Text | `#2C2C2C` |
| Muted | `#6E6E6E` |
| Active mode | `#D6E8F8` border `#0B5CAB` |
| Save primary | `#0B5CAB` on `#FFFFFF` text |

Fill/stamp float bars may stay `#2B6CB0` (already document blue). Change teal overlay borders (`#5EC4B8` stamp, mark, region, search-hit, bbox) to `--doc-accent` or keep yellow highlight (`#FFD64A`) for highlight boxes only.

Spacing:

- Toolbar pad 6px 10px; button pad 6px 8px; radius 4px (not 12px)
- Group gap 8px; `.sep` 1x20 `#D6D6D6` margin 0 8px
- Thumb gap 8px; page-wrap shadow `0 1px 4px #0002`; radius 0 or 2px
- Rail row min-height 36px; icon 18px; label 12px; section header 10px `#6E6E6E`

Icon style: 18x18 thin-line SVG, 1.5px stroke, currentColor, no fill except active. Monochrome. Not 3D, not Adobe multi-color tool glyphs.

---

## 4. DELETE (visual only)

| Delete | Keep instead |
|---|---|
| `:root` dark `--glass` `--chip` `--accent: #5ec4b8` | `html.doc-app` tokens |
| `backdrop-filter` liquid toolbar | flat `#F5F5F5` |
| 12px rounded glass chips | 4px icon+label |
| teal hover / `.hot` / `.on` teal | `--doc-on` / `--doc-accent` |
| teal `#banner` pill | light bar, `#2C2C2C` on `#E8F2FF` |
| page-wrap 10px radius + heavy `#00000066` | slight gray shadow |
| WPF `EditorChip` glass + `#605EC4B8` while `_pdfSpaceMode` | light document chips |
| Floating Save / Clear on the page | `PdfSaveStampsButton`; host `clearFillTexts` only if a ribbon control is added later |
| Tools card home, Home (Andy), nested File/Edit/View clone | existing More context menu |

Do not delete JS functions, overlay bake path, or annotationLayer glue.

---

## 5. Host theme hook (minimal)

No `setChrome` message today. Parent may add one branch in `onHostMessage`:

`if (msg?.type === 'setDocChrome') document.documentElement.classList.toggle('doc-app', !!msg.on);`

Call from `ApplyPdfSpaceRail` when `_pdfSpaceMode`. Default the HTML class on for Edit-in-app; standalone `python -m http.server` glue can omit it (dark remains for debug) or always use `doc-app` if the founder wants light even in Chrome.

---

## 6. 15-line implement checklist (`netie-viewer.html`, no JS ID rewrites)

1. Add `html.doc-app` token block; leave old `:root` as fallback when the class is off.
2. `#toolbar`: drop blur/gradient; `--doc-toolbar`; `gap: 8px`; buttons radius 4px, no scale(0.97) teal hover.
3. After `next`/`modeBtn`, after `fit`, after `regionBtn`, insert `<span class="sep" aria-hidden="true"></span>`.
4. Inside each of `prev next zoomOut zoomIn fit fillBtn hlBtn markBtn regionBtn searchToggle`, add a 18px SVG + `<span class="lbl">`; keep the element id on the `<button>`.
5. `#fit` label stays Fit; do not rename the id. Status text already distinguishes Fit width vs Fit page.
6. `#thumbs`: `--doc-rail`; wrap canvas+`<span class="thumb-num">`; selected ring `--doc-accent` 2px.
7. `#viewer` background `--doc-canvas`; `.page-wrap` white, 2px radius, light shadow.
8. `.on` / `.accent` / search current-hit: `--doc-on` or yellow highlight; kill teal fills.
9. `#banner`: light overlay, not teal glass; keep `position:absolute` so it never reflows pages.
10. Optional `#pageHud`: no id collision; `onclick` -> `#prev`/`#next`/`#zoomIn`/`#zoomOut`. Skip if toolbar is enough.
11. Optional keydown: Left/Right -> `#prev`/`#next` when not typing; Ctrl+F -> `#searchToggle.click()`. No Home/End/Shift+Ctrl+N.
12. Do not add Redact, Hand, Save, or Clear page buttons. Do not float Save.
13. `#stampConfirmBtn` stays `display:none`. `#stampBtn` still toggles `.active` in stamp mode.
14. Do not change `fillBtn.onclick`, `hlBtn`/`markBtn` `setMarkupMode`, `goToPage`, or host `onHostMessage` types except optional `setDocChrome`.
15. Verify: all listed IDs still queryable; thumbs click still `goToPage`; Search panel still `#searchPanel.open`.
)

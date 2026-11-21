# WPF thumbs glue (Edit-in-app)

**Keywords:** wpf-thumbs, DocLeftNav, no-thumbs, renderThumbBatch, setDocChrome, gotoPage, page, ready, thumbBatch  
**main_idea:** Cheapest path = keep pdf.js `renderThumbBatch` as the only rasterizer; hide HTML `#thumbs` via `setDocChrome.thumbs:false`; one new outbound `thumb` message (page + dataUrl) per batch item; WPF `DocLeftNav` ~120px ItemsControl; nav reuse `gotoPage` + existing `page` sync. No Pdfium, no dual thumbs, no Organize/PdfPig pipeline in preview.

## MUST keep (existing IDs / handlers)

| Layer | Keep |
|---|---|
| HTML | `#thumbs`, `.thumb-item`, `.thumb-num`, `.thumb-more`, `renderThumbBatch`, `syncThumbActive`, `THUMB_BATCH=12`, CSS `#thumbs { width:120px }`, `html.canvas-only.no-thumbs #thumbs { display:none }` |
| Host -> viewer | `setDocChrome`, `gotoPage`, `setZoom`, `fitPage`, `find` / `findNext`, all fill/stamp/highlight messages in `onHostMessage` (2653-2751) |
| Viewer -> host | `ready` (pages), `page` (page/pages/scale), `zoom`, `pagePng` (full-page export only) |
| WPF | `PreviewWebView_WebMessageReceived`, `PostDocChrome`, `PostViewer`, `DocPrev`/`DocNext`/`DocPageInput` -> `gotoPage`, `ready`/`page`/`zoom` handlers (2865-2904) |

## Can viewer post thumb dataUrls / page count today?

- **Page count:** yes -- `post({ type:'ready', pages })` on load (614); also on every `page` scroll (1137, 1149). WPF sets `_pdfViewPages` (2874-2886).
- **Thumb dataUrls:** **no.** `renderThumbBatch` (799-828) draws to DOM canvas only; never `post()`s. Closest existing capture is `exportPagePng` -> `pagePng` (2058-2077, WPF 2827-2851) -- full main canvas, one page, async TCS; wrong cost for a 12-up strip.

## Minimum glue: one new message vs reuse

| Option | Verdict |
|---|---|
| Reuse `pagePng` loop | BAN -- N round-trips, full-page PNG, 4s timeout each |
| WPF `CapturePreviewAsync` | BAN -- whole WebView shot, not per-page |
| `PdfOrganizeWindow` / PdfPig thumbs | BAN -- second engine; modal-only today |
| **New `thumb` (or `thumbBatch`) outbound** | **Ship** -- 3-5 lines inside `renderThumbBatch` when `no-thumbs`: `c.toDataURL('image/jpeg', 0.85)` + `post({ type:'thumb', page:i, dataUrl, more: thumbCursor<pdf.numPages })` |

**Inbound:** no new host type required. WPF thumb click -> existing `PostViewer({ type:'gotoPage', page })`. Active ring -> existing `page` handler updates `_pdfViewPage` + new `SyncWpfThumbsActive()`.

**PostDocChrome fix (required):** workbench/edit must always send `thumbs:false` so HTML stays hidden; `_pdfThumbsOn` toggles WPF pane width (120 vs 44 icon-only), not HTML visibility. Today `_pdfThumbsOn=true` on workbench entry (673) flips HTML thumbs back on -- dual-thumb bug vs PRD.

## Touch list (line ranges, Sep 2026)

| File | Lines | Change |
|---|---|---|
| `src/NetieSpace/Assets/pdfjs/netie-viewer.html` | 799-828 | After `page.render`, if `no-thumbs`, `post` thumb dataUrl |
| same | 2653-2658 | Keep `setDocChrome` / `no-thumbs` toggle unchanged |
| `src/NetieSpace/Views/PreviewWindow.xaml` | 969, 1001-1011 | `DocLeftNavColumn` 120px; ScrollViewer + ItemsControl under icon stack |
| `src/NetieSpace/Views/PreviewWindow.xaml.cs` | 912-926 | Workbench/edit: `thumbs:false` always; decouple from `_pdfThumbsOn` |
| same | 752-755 | `DocLeftNavColumn.Width = 120` (workbench/lightEdit) |
| same | 1010-1015, 1002-1008 | `DocThumbsToggle_Click` -> collapse/expand WPF thumb list, not HTML |
| same | 2592-2914 | Add `type=='thumb'` branch; bind ImageSource; handle `page` for active |
| same | 1032-1054 | Reuse `gotoPage` from thumb `MouseLeftButtonUp` |

**Do not touch:** `PdfOrganizeWindow`, `RequestViewerPagePngAsync` (7060), pdf.js main `web/viewer.html`.

## WPF reference (not for preview glue)

`PdfOrganizeWindow.xaml` ~120: `ItemsControl` + `{Binding Thumb}` -- PdfPig line preview + Shell fallback (72-116, 521-680). Pattern only; do not wire into Edit-in-app.

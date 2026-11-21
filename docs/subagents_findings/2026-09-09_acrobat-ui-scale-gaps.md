---
keywords: acrobat-ui-scale, row-49, DocLeftNav, thumbs-pane, PostDocChrome, no-thumbs, workbench-min-size, execute-now-gaps
main_idea: Row 49 docked chrome mostly landed (header, nav, rail, docked ribbon, canvas-only, light glue). Remaining execute-now gaps are WPF left thumbs (~120px pane + hide HTML thumbs), PostDocChrome thumbs flag inversion, and workbench window/content-well scale polish.
date: 2026-09-09
kind: audit
product: Space
reuse: docs/subagents_findings/2026-09-09_acrobat-ui-rebuild-prd.md
cite: scripts/check-acrobat-chrome.ps1
---

# Acrobat UI scale audit — execute-now gaps (row 49)

PREFLIGHT: HIT. Reused `2026-09-09_acrobat-ui-rebuild-prd.md` checklist items 1–18.

Audit surface: live code in `PreviewWindow.xaml`, `PreviewWindow.xaml.cs`, `netie-viewer.html`, `scripts/check-acrobat-chrome.ps1`. `check-acrobat-chrome.ps1` exits 0 (string asserts only; does not catch semantic gaps below).

## Checklist pass summary

| # | Area | Status |
|---|------|--------|
| 1 | Docked `DocAppHeader` / `DocNavBar` / `PdfEditorRibbon` in `RibbonBand` | PASS |
| 2 | `SetRibbonFloated(false)`, `DocToolFloat.IsOpen=false`, `DocZoomFloat` closed in workbench | PASS |
| 3 | `PdfSpaceRail` visible, column 188px | PASS |
| 4 | `DocLeftNav` ~120px WPF thumbs; HTML `#thumbs` hidden | **GAP** |
| 5 | `DocShareBtn` hidden; peek title/Look Up/Add to Chat collapsed in workbench | PASS |
| 6 | Mode row Fill & Sign / Edit PDF / Organize / Comment / Save / More | PASS (no wrong Collapsed) |
| 7 | WebView canvas-only; host message ids kept | PASS |
| 8 | Workbench tokens `#F5F5F5` / `#E8E8E8` / `#0B5CAB` | PASS |
| 9 | Page centered in viewer; WebView bg `#E8E8E8` | PASS |
| 10–14 | Chrome glue light default, toolbar+thumbs, no Save/Clear | PASS |
| 15–17 | Peek density, light Edit, Space swallow | PASS |
| 18 | Script updated for docked chrome | PASS (shallow) |

## Focus answers (founder questions)

**A. DocLeftNav still 44px icon strip?** Yes. `ApplyWorkbenchShell` sets `DocLeftNavColumn.Width = 44` when `on`. XAML `DocLeftNav` is two icon buttons (`DocThumbsBtn`, rotate) — no WPF thumbnail list.

**B. HTML `#thumbs` in workbench?** Yes, can show. `PostDocChrome` sends `thumbs: true` when `_pdfThumbsOn` (default on enter). `setDocChrome` only adds `no-thumbs` when `msg.thumbs === false`. Workbench always uses `canvas-only` but that does not hide `#thumbs` without `no-thumbs`.

**C. Window/layout scale?** Partial. `SetPdfWorkbenchAsync` one-shot expands to ~1180×920 if small; window `MinWidth`/`MinHeight` stay 640×420. `ContentWellBorder` workbench corners set to 8 but inner child `Border` still `CornerRadius="0,0,21,21"`. Rail 188px PASS. `TitleBarHost` workbench: white fill, radius 8 top — not peek glass gradient; `AllowsTransparency` + `GlassShell` acrylic shell remain.

**D. Mode row Collapsed wrongly?** No. Workbench shows `PdfFillSignBtn`, `PdfEditPdfBtn`, `PdfModeOrganizeBtn`, `PdfModeCommentBtn`, `PdfMoreButton`, `PdfLightSaveBtn`. Light-edit-only `PdfModeSignBtn` / `PdfModeFillBtn` correctly hidden in workbench.

**E. Peek/light-edit density?** PASS. `PeekActionStrip` stays collapsed; `HidePeekChrome` restores title actions in peek; teaching dock `Visibility="Collapsed"`; light edit uses equal Organize/Sign/Fill/Comment chips.

**F. Leftover float on workbench path?** PASS. No `SetRibbonFloated(true)` in repo. Workbench calls `SetRibbonFloated(false)` and `DocToolFloat.IsOpen = false`.

---

## Execute-now punch list (remaining only)

1. **`PreviewWindow.xaml.cs` — `ApplyWorkbenchShell` / `DocLeftNavColumn.Width`**
   - Change workbench width from `44` to `~120` (checklist item 4).

2. **`PreviewWindow.xaml` — `DocLeftNav`**
   - Replace icon-only strip with a WPF thumbnails pane (~120px): scrollable page-thumb list bound to viewer page count/current page; wire `DocThumbsToggle_Click` / `SyncThumbsBtn` to pane visibility, not HTML thumbs.

3. **`PreviewWindow.xaml.cs` — `PostDocChrome` / `thumbs` payload**
   - When `workbench || edit`, always send `thumbs: false` so `html.canvas-only.no-thumbs` hides HTML `#thumbs` while WPF owns the pane. Current: `thumbs = … || _pdfThumbsOn` inverts intent (true => HTML thumbs visible).

4. **`PreviewWindow.xaml.cs` — `SetPdfWorkbenchAsync` (and restore on leave)**
   - Raise `MinWidth`/`MinHeight` while `_pdfSpaceMode` (e.g. >=1180×760) so workbench cannot shrink back to peek 640×420; keep one-shot width/height bump.

5. **`PreviewWindow.xaml` — `ContentWellBorder` inner `Border` (`CornerRadius="0,0,21,21"`)**
   - Sync corner radius with workbench `ContentWellBorder.CornerRadius` (0,0,8,8) via trigger or code in `ApplyWorkbenchShell` so the well fills flush without peek-era 21px bottom rounding.

6. **`scripts/check-acrobat-chrome.ps1`**
   - Add asserts for `DocLeftNavColumn` ~120 in workbench, `PostDocChrome` sending `thumbs: false` (or `no-thumbs`) when workbench, and optional workbench `MinWidth` — shallow string pass today misses gaps 1–3.

---

## Not listed (PARK or already done)

- WPF Home cloud, Tools card grid, FORMAT pane, Hand, dual-window, Adobe brand — PARK per row 49.
- `SetRibbonFloated` / `DocToolFloat` float path — fixed on workbench.
- Chrome glue light doc-app — done.
- Mode-row button visibility — done.

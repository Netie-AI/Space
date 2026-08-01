# OpenEdit / Acrobat-class PDF — preliminary audit

**Date:** 2026-07-22 · **updated same day by Fable pass (see §Fable pass below)**  
**Status:** Packet 1 SHIPPED (Fill typing + cell detect + float bars) · ghost root-caused & fixed for real (Y-mirror + render race) · autosave now live-annotation (no reload)  
**Product note:** “OpenEdit” is the aspirational full-file editor shell. Today it is **Netie Space** light preview → **Edit** ribbon morph. No separate OpenEdit binary exists yet.

**Token / ledger note:** `D:\Cortex\docs\research\findings\S1_TOKEN_BUDGET.md` is Cortex DMS (SQL detectors, F1 ledger). Reuse only the **research packet pattern** (budget table, hard rails, gap list). Do not wire Ponytail/CostLedger into PDF UI work.

---

## Verdict

Stamp **drag ghosting is a real bug** (stale `overlays[]` after soft-reload) — **fixed in this pass**. Fill & Sign text/comb, table cell detect, OCR edit, Protect/Export, and a true Acrobat Pro shell are **not built**; treat them as Fable Ultrathink tracks.

---

## What Acrobat buttons mean vs Netie today

| Acrobat tool | User expectation | Netie today | Quality |
|---|---|---|---|
| **Create PDF** | Blank / from files | Convert-to-PDF + drop-append | Partial (no blank) |
| **Combine Files** | Multi-file merge UI | Merge dialog + drop-append | Good |
| **Organize Pages** | Thumb grid reorder/rotate/delete | `PdfOrganizeWindow` | Good |
| **Edit PDF** | In-place text + format sidebar | Bbox overlay only; click → status text | Stub |
| **Export PDF** | PDF → Word/image/etc. | Missing | Missing |
| **Scan & OCR** | Recognize scanned text | Explicit “coming later” | Missing |
| **Share / Send for Comments** | Collab | Missing | Missing |
| **Comment** | Highlight, sticky, markup | Highlight draw only; **not persisted** | Insufficient |
| **Stamp** | Rubber stamps | Signature image stamps | Good (sign path) |
| **Compare Files** | Diff two PDFs | Missing | Missing |
| **Fill & Sign** | Type in fields + sig + A/A/trash bar | Click cell → snap → type → autosave bake; A/A/🗑 on stamps **and** text | **Working v1 (Fable pass, 26/26 automated)** |
| **Protect** | Password / permissions | Missing | Missing |

---

## Bug fixed earlier (Grok) — stale overlay ghost

`reloadPdf keepOverlays` didn’t clear `overlays[]` → orphaned elements resurrected. Fixed (overlays reset before rebuild). **Necessary but not sufficient** — the ghost the user kept seeing had two deeper causes, found and fixed in the Fable pass below.

---

## Fable pass 2026-07-22 — root causes found, fixed, and proven

Verified end-to-end by the new headless harness `scripts/pdf-stress` (**26/26**) plus a compiled PdfSharpCore probe. Screenshots + `stress_results.json` produced by the harness.

| # | Bug | Evidence | Fix |
|---|---|---|---|
| 1 | **Every bake vertically mirrored.** `StampSignatureImage` treated XGraphics y as bottom-up; PdfSharpCore’s `WorldToView` proves world space is **top-left, y-down** (“Convert a point from *Windows world space* to PDF world space”, `y_pdf = pageH − y`). A signature dropped in the lower half baked near the top → the un-moving “original object” in the user’s report. | Framework-csc probe: raw `y=200` rect landed at **562–592 pt from bottom** (mirror), converted math landed at **100–130 pt** as intended | Convert once: `yTop = page.Height − h − yFromBottom` (`PdfEditService.StampSignatureImage`; same convention in new `DrawFillTexts`) |
| 2 | **Concurrent canvas renders raced** (`renderGen` global): “Cannot use the same canvas during multiple render() operations” killed `load()` mid-soft-reload → viewer froze on stale pixels; other pages starved as placeholders. | Reproduced in harness (pageerror + S5 timeout) | Per-page `entry.gen` + `entry.renderTask` cancel/await; cancellation-tolerant catch (`netie-viewer.html renderPageAt`) |
| 3 | **Every autosave reload jumped to page 1** (`load()` hardcoded `scrollToPage(1)`), so signing on page 3 bounced the view every ~1 s. | Harness S8 (failed before, passes now) | `load()` keeps `pageNum`; reload returns to the reading position |
| 4 | **Bake-then-reload churn on every drag** (flicker + 1–2 s stale window even when it worked). Acrobat keeps annotations live and flattens on save. | Design trace | **Live-annotation model:** autosave still re-bakes the file on disk from the clean base, but the viewer is *never* reloaded — HTML overlays stay the single visual truth (`FlushPendingPdfStampsAsync` no longer soft-reloads; `SoftReloadPdfKeepingOverlaysAsync` removed) |
| 5 | **Deleting the last stamp left it baked in the file** — `stampDeleted` set `_pdfDirty=false` at count 0, so the scheduled flush (which restores the clean base) aborted. | Code trace | Stay dirty on delete; flush restores base when both stamps and fill texts are empty |
| 6 | **Float bars floated at off-screen coords** when their anchor scrolled out (fixed-position bar at negative y). | Harness S6 diag (`btn.y = −529`) | `anchorOutOfView()` hides stamp/fill bars when the anchor leaves the viewer (Acrobat behaviour) |
| 7 | **Mode banner reflowed the document** (`position: sticky` in-flow): entering Sign/Fill shifted pages ~48 px under the cursor. | Demo screenshot regression (text landed one row up) | Banner is an absolute overlay, scroll-pinned; toggling it never moves pages |

Also: Delete/Backspace while typing in a fill box can no longer trigger stamp deletion (`isContentEditable` guard).

### Shipped in this pass — Packet 1 (Fill & Sign v1)

- **Table-cell auto-detect** on click (raster ruling-line scan on the rendered canvas — works for vector *and* scanned tables; AcroForm rects snap first when present; underline-only fields get a box sitting on the line; blank areas get an Acrobat-style free text box). Accuracy on the REFEREES-style grid: ±3 pt.
- **Click-to-type** contenteditable overlays, font auto-sized to the cell, Enter/Esc/click-away commit, empty boxes self-remove.
- **Floating A / A / 🗑** for text boxes (font scale ×1.15/×0.87, delete), mirroring the stamp bar.
- **Persistence:** debounced `fillTexts` → host `_pendingFillTexts` → baked with stamps from the same clean base via new `PdfEditService.DrawFillTexts` (vector `XFont` text, GDI PNG fallback if font resolution fails). Restore-clean-base covers “everything deleted”.
- **Dev/test hook** `window.__netie` (plain-browser only, inert under WebView2) + permanent harness `scripts/pdf-stress` (26 assertions) + test PDF generator.
- In-app steps: `docs/research/findings/FILLSIGN_TEST.md`. Build must happen in Cursor/VS — this machine has no .NET SDK.

**Known limits (v1, by design):** rotated pages untested for bake coordinates; comb-style per-letter boxes (IAb) not yet; typed text overflowing a small cell isn’t auto-shrunk (use A−).

---

## Light edit vs full OpenEdit app

| Mode | Entry | Scope today |
|---|---|---|
| **Light preview** | Explorer/Desktop **Space** (`GlobalKeyboardHook` → `PreviewWindow`) | Preview + chat + optional Edit ribbon |
| **Edit (in-window)** | **Edit** button on preview | PDF/video ribbon morph — not a separate process |
| **OpenEdit full app** (desired) | Enter / file open → Acrobat-like shell | **Not implemented** — needs IA redesign + tool rails |

PDF is one OpenEdit surface; Word/Excel/image/video already have other preview paths — keep multi-type architecture, don’t hard-code PDF-only.

---

## Gap map (implement carefully)

### A — Cursor-ready plans (easy/local — paste each block into Cursor)

1. ~~Stamp ghost~~ / ~~stamp float bar~~ / ~~Fill typing + cell detect + text float bar~~ **DONE**
2. **Persist Comment highlights** (viewer already draws + posts them, host drops the message):
   - `PreviewWindow.WebMessageReceived`: add `else if (type == "highlight")` → collect `{page,x,y,w,h}` into a new `_pendingHighlights` list (same shape as `_pendingFillTexts`), `EnsureStampBaseLocked`, `_pdfDirty = true`, `ScheduleStampAutosave()`.
   - `PdfEditService`: add `DrawHighlights(string pdfPath, IReadOnlyList<PdfHighlightItem> items)` cloned from `DrawFillTexts` — per page `XGraphics.FromPdfPage(page, Append)`, for each item `gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(90, 255, 214, 74)), x, page.Height − y − h, w, h)`. **Copy the Y conversion exactly — XGraphics is top-left (see Fable pass #1).**
   - `FlushPendingPdfStampsAsync`: bake highlights after fill texts; include the list in the two `Count == 0` checks and in `ClearStampSession`.
   - Viewer `reloadPdf keepOverlays`: snapshot/restore `highlights[]` like fills (they’re already re-painted per page in `renderPageAt`).
   - Stress: add a `highlight` scenario to `scripts/pdf-stress/stress.mjs` (drag with `hlBtn` on, assert host message + survives reload).
3. **Comment ribbon button → viewer Highlight mode**: `PdfCommentHighlight_Click` currently only sets status text. Post `{ type: "toggleHighlight" }`; in `netie-viewer.html onHostMessage` add the handler that calls the existing `hlBtn` click logic. 10 lines total.
4. **Honest labels**: Compress currently fakes success — either hide the button or label “Compress (coming soon)” and make the handler a no-op with an honest status. Same for any Export/Protect menu stubs. Grep ribbon XAML for `Compress`.
5. **Blank Create PDF**: `PdfEditService.CreateBlankPdf(string path, int pages = 1)` → `new PdfDocument()`, `AddPage()` (Letter), `Save`. Wire to the Create menu. ~20 lines.
6. **Comb text (IAb) v1** (optional): in `netie-viewer.html`, per fill field add `comb: true` flag toggled from a 4th float-bar button; render with `letter-spacing` computed as `cellW/maxChars`; bake by drawing each char at `x + i*cellW/n` in `DrawFillTexts`.

### B — Fable Ultrathink (hard, keep here)

1. ~~Table cell auto-detect + Fill typing + A/A/🗑~~ **DONE this pass** (raster ruling scan; AcroForm snap; free box fallback).
2. **True AcroForm value write-back** (real `/V` updates for interactive forms; PdfSharpCore AcroForm API is weak → spike PdfPig write / QuestPDF / iText-free options; today we flatten visually instead — acceptable for flat forms).
3. **True Edit PDF** — edit native text / OCR layer + format sidebar (font/size/align). See Packet 3.
4. **OpenEdit shell** — Tools home grid + right rail + per-mode toolbars. See Packet 2.
5. **Protect / Export / real compress / OCR pipeline** — library-capability gated.

---

## Key files

| File | Role |
|---|---|
| `src/NetieSpace/Assets/pdfjs/netie-viewer.html` | Viewer, stamps, float bar, reloadPdf |
| `src/NetieSpace/Views/PreviewWindow.xaml(.cs)` | Ribbon, WebView bridge, autosave |
| `src/NetieSpace/Services/PdfEditService.cs` | PDF mutate / form rects / text boxes |
| `src/NetieSpace/Views/PdfOrganizeWindow.*` | Organize |
| `src/NetieSpace/App.xaml.cs` | Space entry |

---

## Fable prompt packets (copy-paste)

### Packet 1 — Fill & Sign text + table cells ✅ SHIPPED 2026-07-22

Delivered (see §Fable pass): cell detect (raster ruling scan, AcroForm snap, line/free fallback), click-to-type overlays, A/A/🗑 float bar, `DrawFillTexts` bake from clean base, live-annotation autosave, `scripts/pdf-stress` 26/26, `FILLSIGN_TEST.md`.

### Packet 2 — OpenEdit full Acrobat shell (next Fable job)

```
You are Claude Fable on Netie Space (WPF + WebView2 pdf.js). Build OpenEdit full-app mode
(local, homemade Acrobat Pro DC-class shell).

Read first:
- docs/research/findings/OPENEDIT_ACROBAT_AUDIT.md  (esp. §Fable pass — architecture now
  live-annotation: autosave bakes to disk, viewer overlays are visual truth, NO reload)
- src/NetieSpace/Assets/pdfjs/netie-viewer.html  (fill/stamp overlay infra to reuse)
- src/NetieSpace/Views/PreviewWindow.xaml(.cs)   (Edit ribbon morph today)

Entry: from file only (Space light preview → Enter / “Open in OpenEdit” → full shell).
PDF is one module; keep hooks for Word/Excel/image/video modules.

UI targets (Acrobat screenshots in chat history):
- Tools home grid: Create, Combine, Organize, Edit PDF, Export, Scan&OCR, Share, Comment,
  Stamp, Fill & Sign, Protect — each card = Open/Add split button.
- Doc chrome: Home | Tools | <document tab>; “Search tools”; right shortcut rail.
- Edit PDF mode: toolbar (Edit, Add Text, Add Image, Link, Crop, Header&Footer, Watermark,
  More) + FORMAT sidebar (font/size/bullets/align/spacing) — sidebar may ship disabled
  until Packet 3 lands.
- Fill & Sign mode: reuse the shipped fill infra 1:1 (enterFillMode message).

Rules: wire real capabilities (Merge/Organize/Sign/Fill); honest “not yet” stubs — never
fake results (no fake compress). Do not regress Space light preview. Incremental: shell
chrome first, then per-tool routing. WPF side stays thin; page-level UI lives in the
HTML viewer where possible.
Gotchas: PdfSharpCore XGraphics is TOP-LEFT origin (convert y-from-bottom!); no .NET SDK
on this machine — build via Cursor; regression-check with scripts/pdf-stress (26/26 must hold).
Deliver: code + updated audit §shell + TEST additions.
```

### Packet 3 — Edit text / OCR / Format panel (after Packet 2)

```
Implement real Edit PDF text + scanned-page OCR editing in Netie Space.

Today: PdfEditService.GetTextBoxes draws word bboxes; click only posts status text.
Fill & Sign v1 (shipped) already bakes NEW text; this packet is about EDITING EXISTING text.

Plan of attack:
1) Native text: PdfPig gives word boxes + glyphs. “Edit” = white-out original span
   (draw bg-colored rect) + DrawFillTexts-style replacement text in matched font size.
   Start with single-line spans; document ligature/kerning limits honestly.
2) OCR path for scans: Windows.Media.Ocr first (zero-install), Tesseract as opt-in
   fallback; overlay editable boxes on recognized words, bake like fills.
3) FORMAT sidebar: font family/size/align for the active box (extends fill float bar).
4) Reuse: fill overlay/commit/autosave pipeline from Packet 1; same clean-base re-bake.
Constraints: local-only OCR by default; PdfSharpCore XGraphics is TOP-LEFT origin;
extend scripts/pdf-stress with edit scenarios.
```

---

## Verification assets (this pass)

| Asset | Where |
|---|---|
| Headless stress harness (26 checks) | `scripts/pdf-stress/` (`npm i && node stress.mjs all`, server on :8517 — see its README) |
| Test PDF generator (REFEREES grid, known coords) | `scripts/pdf-stress/make_test_pdf.py` → `Assets/pdfjs/__stress/test.pdf` |
| In-app test steps | `docs/research/findings/FILLSIGN_TEST.md` |
| Launch config for the viewer server | `.claude/launch.json` → `pdfjs-stress` |

---

## Ownership

| Owner | Work |
|---|---|
| **Grok (earlier)** | Overlay-array ghost fix; stamp A/A/✕ bar v1; this audit skeleton |
| **Fable (2026-07-22)** | Y-mirror root cause + fix; render-race fix; live-annotation autosave; page-position keep; Fill & Sign v1 (cell detect, typing, float bar, bake); float-bar/banner UX fixes; `scripts/pdf-stress` harness |
| **Cursor (you run)** | §A plans 2–6: highlight persist, Comment wiring, honest labels, blank Create, comb v1 |
| **Fable next** | Packet 2 (OpenEdit shell) → Packet 3 (Edit text/OCR) |
| **You** | Build in Cursor/VS, run `FILLSIGN_TEST.md` steps A+B, then send Packet 2 |

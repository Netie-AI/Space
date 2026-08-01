# Fill & Sign v1 — in-app test steps (2026-07-22 Fable pass)

Automated coverage already ran green: `scripts/pdf-stress` → **26/26**
(place/drag/zoom/scroll/reload-storm/float-bars/cell-detect/typing/persist).
These are the remaining **in-app** checks that need the WPF host (this machine
has no .NET SDK — build in Cursor/VS first).

## A. Signature — the old ghost is two fixes deep

1. Space → open a multi-page PDF → **Edit** → **Sign** → place a signature in
   the **lower half of page 2**.
2. Drag it around. Expect: box follows 1:1, no second image anywhere, page does
   **not** jump to page 1, status shows `Saved · <file> — keep dragging or typing`.
3. Wait ~2 s (autosave), close the preview, reopen the same file.
   **Expect the signature baked exactly where you dropped it.**
   (Before this pass it baked **vertically mirrored** — a lower-half signature
   appeared near the top; that phantom was the "original object still showing".)
4. Place two signatures → delete one via the floating ✕ → other stays.
5. Delete the **last** signature → close → reopen. Expect **clean file, no
   leftover baked signature** (previously the flush skipped and it stayed).
6. Scroll the signature off-screen → the A/A/✕ bar hides; scroll back → returns.

## B. Fill & type (new)

1. Edit ribbon → **Fill forms** (or viewer toolbar **Fill text**).
2. Click inside an empty table cell (e.g. REFEREES row) → blue box snaps to the
   cell edges, cursor ready. Type a name.
3. Toolbar above the box: small **A** shrinks text, large **A** grows, **🗑**
   deletes. Enter/Esc/click-away commits; an empty box removes itself.
4. Click a blank area (no table) → free text box appears at the click point.
5. Type in 2–3 cells, wait ~2 s, close, reopen → text is baked into the PDF in
   the right cells (black, Segoe UI/Helvetica, vertically centered).
6. Mix: signature + typed cells in one session → both persist after reopen.
7. AcroForm PDFs: clicking inside a real form field snaps to the field rect.

## C. Regression sweep (quick)

- Zoom +/-, Fit, One page/Continuous, Search, Highlight draw, thumbnails.
- Organize/Merge/Extract/Split/Rotate unchanged.
- Non-PDF previews (video/image/office) unchanged.

## Re-run the automated suite any time

```bash
cd scripts/pdf-stress && npm install
python -m http.server 8517 --directory ../../src/NetieSpace/Assets/pdfjs   # terminal 1
node stress.mjs all                                                        # terminal 2
```

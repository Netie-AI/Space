# Human Pillar 1 test (Windows only)

Playwright cannot open Netie Space. This app is a WPF desktop `.exe`,
not a browser page. There is no Playwright test in this repo. Open the
app on a Windows PC and click through this list.

Cloud agents run Linux. They cannot launch `NetieSpace.exe`.

## 1. Install and open (about 2 minutes)

1. Clone or pull `main` on Windows.
2. Double-click `install.bat` (or `run.bat` after the first build).
   `install.bat` / `publish.ps1` download `tools/ffmpeg` when missing
   (that folder is gitignored) and copy it next to the exe. Re-run
   `install.bat` if Export says FFmpeg is missing.
3. Wait until `dist\NetieSpace\NetieSpace.exe` exists.
4. Launch that `.exe`. Do not expect a browser window.

Preview without the full ribbon: select a file in Explorer or Desktop
and press Space.

## 2. Video Select -> Split -> Drag -> Delete -> Fade -> Wipe -> Export

Make the two clips first (about 30 seconds):

```
.\scripts\make-pillar1-clips.ps1
```

That script uses `tools/ffmpeg` from `install.bat` (not PATH-only).

or `./scripts/make-pillar1-clips.sh`. You get `pillar1-clips/clip_a.mp4` (1.6s blue)
and `clip_b.mp4` (2.0s red). Expected wipe export is about 3.1s.
Linux reference encode (same graph Space burns): `./scripts/encode-pillar1-wipe.sh`
fails unless duration is 2.8..3.4s (Cursor CI runs it). On Windows:
`.\scripts\encode-pillar1-wipe.ps1` (Windows CI runs that). Your app
Export should land near that.

1. Home -> open a folder that has the two clips, or drop them on the window.
2. Open a clip. The Video ribbon should show **Export all**, **+ Clip**,
   **Order...**, **More...**.
3. Click **+ Clip** and add the second file. Two clips should appear on
   the timeline.
4. Click a clip so it is selected (blue outline).
5. Drag the clip left or right. It should move. Drop it.
6. Move the playhead to the middle of a clip (clip 2 is OK). Click **Split**.
   You should now have three clips. The packed slider and the playhead
   line on the clip track must stay on that clip -- both used to sit on
   clip 1 after + Clip (`GetPlayhead` is still the open file). Yellow
   cut marks should cover the split clip, not jump to clip 1.
   Mark In / Mark Out chips and yellow handles store packed seconds.
   + Clip expands In/Out and the packed slider max to the new length
   (MediaOpened used to leave both on clip 1, so a clip-2 seek clamped
   to the join and Split failed as an edge; Export range burned 1.6s).
   More... Export range burns that packed window (not a trim of clip 1).
7. Select the extra piece. Press Delete (same ripple/black dialog as the
   ribbon Delete). The piece
   should disappear. Ripple (default) joins the gap. "Leave black scene"
   puts the title card at the packed clip start (not source in 0 on + Clip).
   Ctrl+Z after Leave black also clears the fill-gaps flag (used to stay
   on and insert same-source holes later). A2 clicks use packed start too
   (not source in 0). A2 is preview-only -- Export does not amix.
   The hint text says: "Move playhead -> Split ->
   select clip -> Delete".
8. Split/Delete is the click check. Ctrl+Z Undo (or close and + Clip both
   originals) so the timeline is full `clip_a` (1.6s) + `clip_b` (2.0s)
   again. A leftover half-clip makes Export ~2.3s and fails the 3.1s gate.
   Wipe-then-split keeps wipe on the outgoing half only (one join).
   Fade / Wipe / drag now push undo, so an extra Ctrl+Z after those
   chips undoes the chip -- not + Clip. Ctrl+Z after Leave black also
   removes that title card (Export used to burn the leftover overlay).
   A failed Split (playhead on
   an edge), a fade-handle click that does not move the diamond, a
   fade-handle or clip-drag that loses mouse capture, or a
   second Wipe on an already-wiped join discards that undo so Ctrl+Z
   cannot pop + Clip. A clip-drag alt-tab still drops (session MoveClip),
   not a leftover ghost transform.
9. Select the first remaining clip. Click **Fade In**. Select the last
   clip. Click **Fade Out**.    Context-menu Fade and timeline handles write
   the same session (Export syncs from session, not a leftover timeline fade).
   Context-menu Speed / Add black / Add text push undo. An accidental
   Speed 2x or black card used to have no Ctrl+Z (2x blows the 3.1s
   gate; black card fails "picture is not black"). Speed 2x now shrinks
   that clip on the packed timeline (used to stay full width, so the 2x
   was invisible and Export was ~2.3s).
10. Click **Wipe** on either clip at the join (first or second). Export
    burns a wipeleft xfade of 0.5s. You do not have to pick the outgoing clip.
11. Click **Export all**. Pick a folder. Wait for the toast. Space-close is
    OK -- export snapped the timeline, so opening another file cannot
    rewrite the encode. The toast should show the duration (about 3.1s /
    `wipe expect ~3.1s` on these clips). Duration uses the same
    `tools/ffmpeg/ffprobe` as Export (not PATH only). Status says
    "captions burned in" only if the SRT was already on the snapshot
    when you clicked Export (a whisper finish after that does not count).

### What to check after Export

- A new `.mp4` exists in the folder you picked.
- The file plays in Movies & TV or VLC. Picture is not black.
- Duration is about `clip1 + clip2 - 0.5s` when Wipe is on
  (example: 1.6s + 2.0s - 0.5s = about 3.1s).
  Check: `.\scripts\check-pillar1-export.ps1 path\to\export.mp4` (2.8..3.4s).
- Fade In/Out is visible at the ends. Wipe is visible at the join.
- The app did not crash. The toast said export finished, not failed.
- If ffmpeg is missing, Export should say so instead of hanging.

If all of that works, tick the parking-lot line:

`User-validated Select->Split->Drag->Delete->Fade->Export`

## 3. Word / PDF / Chat smoke (optional, 3 minutes)

1. Open a `.docx`. Type a sentence. Save. Re-open. Text is still there.
2. Open a `.pdf`. Highlight a word. Save. Re-open. Highlight is still there.
3. Chat: send one local question. You get an answer, not a blank pane.

## 4. What this agent already proved (you do not repeat)

- Cursor-origin CI: `./scripts/ci-cursor.sh` PASS.
- Linux ffmpeg wipe encode: 3.2s mp4, 145528 bytes.
- Windows xunit + live ffmpeg encode: GitHub Actions, last green run
  before billing lock (`2f4b64b`, all four jobs SUCCESS).
- WPF UI clicks: not proven. That is only this document.

## 5. If something fails

- Black export or 0-byte file: ffmpeg path. Re-run `install.bat`.
- Split does nothing: playhead must sit inside the selected clip.
- Wipe looks like a hard cut: confirm Wipe is on before Export all.
- App will not start: `.NET 8` Desktop Runtime x64.

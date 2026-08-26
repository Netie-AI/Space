# Human Pillar 1 test (Windows only)

Playwright cannot open Netie Space. This app is a WPF desktop `.exe`,
not a browser page. There is no Playwright test in this repo. Open the
app on a Windows PC and click through this list.

Cloud agents run Linux. They cannot launch `NetieSpace.exe`.

## 1. Install and open (about 2 minutes)

1. Clone or pull `main` on Windows.
2. Double-click `install.bat` (or `run.bat` after the first build).
3. Wait until `dist\NetieSpace\NetieSpace.exe` exists.
4. Launch that `.exe`. Do not expect a browser window.

Preview without the full ribbon: select a file in Explorer or Desktop
and press Space.

## 2. Video Select -> Split -> Drag -> Delete -> Fade -> Wipe -> Export

Use two short clips (about 1-2 seconds each). MP4 or MOV is fine.

1. Home -> open a folder that has the two clips, or drop them on the window.
2. Open a clip. The Video ribbon should show **Export all**, **+ Clip**,
   **Order...**, **More...**.
3. Click **+ Clip** and add the second file. Two clips should appear on
   the timeline.
4. Click a clip so it is selected (blue outline).
5. Drag the clip left or right. It should move. Drop it.
6. Move the playhead to the middle of a clip. Click **Split**. You should
   now have three clips.
7. Select the extra piece. Press Delete (or the ribbon delete). The piece
   should disappear. The hint text says: "Move playhead -> Split ->
   select clip -> Delete".
8. Select the first remaining clip. Click **Fade In**. Select the last
   clip. Click **Fade Out**.
9. Click **Wipe** on the join between the two remaining clips (or the
   wipe control on the transition). Export should burn a wipeleft xfade
   of 0.5s.
10. Click **Export all**. Pick a folder. Wait for the toast.

### What to check after Export

- A new `.mp4` exists in the folder you picked.
- The file plays in Movies & TV or VLC. Picture is not black.
- Duration is about `clip1 + clip2 - 0.5s` when Wipe is on
  (example: 1.6s + 2.0s - 0.5s = about 3.1s).
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

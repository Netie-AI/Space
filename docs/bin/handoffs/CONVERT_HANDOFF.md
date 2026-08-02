# Universal converter — engine done, C# picker patch for Cursor

**Status:** the Python engine (`tools/convert_engine.py`, `export-as` action) now covers the full
online-converter matrix and is **verified green** by `scripts/convert-stress/test_matrix.py`
(17/17 on D). C# `ExportAsAsync` already calls it. The only thing left is to **widen the Export-as
file-type picker** so users can actually pick image/media targets.

Run the suite any time:

```
"D:\Space\.venv\Scripts\python.exe" "D:\Space\scripts\convert-stress\test_matrix.py"
```

## What the engine now supports (all tested)

| Source | Targets that work |
|---|---|
| Image (png/jpg/jpeg/gif/bmp/webp/tiff/ico/heic*) | any other raster, **animated gif↔webp keeps frames**, and PDF |
| PDF | png/jpg/webp/tiff/bmp (one file per page: page 1 = chosen path, page 2 → `name_2.ext`), plus Word/HTML/etc via LibreOffice |
| Word/Excel/PPT | PDF (LibreOffice, layout-faithful), and any format in `SOFFICE_CONVERT` |
| Video (mp4/mov/mkv/webm/avi/…) | mp4/mov/mkv/webm (container+codec map), **video→gif** (2-pass palette), audio-only mp3/m4a/wav/flac/ogg/opus |
| Audio | any audio container |

`*` HEIC read/write needs `pillow-heif` — **not installed on D**. To enable:
`"D:\Space\.venv\Scripts\python.exe" -m pip install pillow-heif`. Without it, HEIC sources fail
cleanly (no crash); every other format works.

Engine edges handled: src==dst is a no-op (not `SameFileError`); unsupported target returns
`{"success": false, "error": "Cannot export .x to .y"}` — never throws.

## Cursor: land this C# picker patch (PreviewWindow.xaml.cs)

The two switch methods below currently only offer doc formats. Replace them so image and video
sources expose their real targets. (I did **not** edit these on D — you're live in this file; land it
your way to avoid a conflict.)

### `DescribeExportFormats` — add image + video hints

```csharp
_ when kind == PreviewKind.Image => "JPG, PNG, WEBP, GIF, PDF…",
_ when kind == PreviewKind.Video => "MP4, MOV, MKV, WEBM, GIF, MP3…",
".pdf" => "PDF, JPG, PNG, Word, HTML…",   // pdf→image now works
```

### `BuildExportAsDialog` — add the matching filters

```csharp
".pdf" => (
    "PDF document|*.pdf|JPG image|*.jpg|PNG image|*.png|Word document|*.docx|Web page|*.html|All files|*.*",
    "pdf", 1, stem),

_ when kind == PreviewKind.Image => (
    "JPG image|*.jpg|PNG image|*.png|WEBP image|*.webp|GIF image|*.gif|BMP image|*.bmp|TIFF image|*.tif|Icon|*.ico|PDF document|*.pdf|All files|*.*",
    // default index: keep source type first if you want "same type" to be the default click
    "jpg", 1, stem),

_ when kind == PreviewKind.Video => (
    "MP4 video|*.mp4|MOV video|*.mov|MKV video|*.mkv|WEBM video|*.webm|Animated GIF|*.gif|MP3 audio|*.mp3|All files|*.*",
    "mp4", 1, stem),
```

Everything else in `RunExportAsAsync` already works — it shells `export-as <src> <out>` and shows the
returned `tool`/`bytes`. No other C# change needed.

## Also for the compress quick-win (ImageEditService)

The image **compress** path is separate from export and still (a) silently sends webp/heic → jpg
because WPF has no encoder, and (b) flattens animated GIF to one frame. Simplest fix that matches the
engine: route compress for webp/heic/animated-gif through the same Python engine (Pillow) instead of
WPF `BitmapEncoder`, or gate those in `PromptCompress` with an explicit "Convert to JPG?" choice.
Non-animated jpg/png/bmp/tiff stay on the fast WPF path.

## Landed by Cursor 2026-07-25 22:23
- `DescribeExportFormats` / `BuildExportAsDialog` widened for image + video + PDF?image
- Compress: animated GIF refused; webp/heic ask explicit JPEG convert (no silent downgrade)
- Matrix re-checked 17/17; portable republished

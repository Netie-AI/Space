"""Contract checks for multi-source V1 polish (PARKING_LOT P5 + Clip)."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SESSION = ROOT / "src/NetieSpace/Services/VideoEditSession.cs"
TIMELINE = ROOT / "src/NetieSpace/Services/Timeline/TimelineProject.cs"
FFMPEG = ROOT / "src/NetieSpace/Services/FfmpegService.cs"
PREVIEW = ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs"
PREVIEW_XAML = ROOT / "src/NetieSpace/Views/PreviewWindow.xaml"
PARKING = ROOT / "PARKING_LOT.md"


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_append_and_import_pack_without_overlap():
    session = _read(SESSION)
    timeline = _read(TIMELINE)
    assert "public int AppendClip(" in session
    assert "pack on the end (no overlap)" in session.lower() or "Pack a second/third video" in session
    assert "HasMultipleSources" in session
    assert "ApplyTo(TimelineProject" in session
    assert "SplitAtPacked" in session
    assert "ApplyBlade" in session
    assert "wipe on both halves" in session
    assert "ClipSourceEquals" in session
    assert "PackedStartOf" in session
    assert "PackedEndOf" in session
    assert "source in-point" in session
    assert "public bool SlicePacked(" in timeline
    assert "SliceSubtitleBurn" in timeline
    assert "MapCuesToPacked" in timeline
    assert "WritePackedCaptions" in timeline
    assert "RemapPackedCuesAfterSpeed" in timeline
    assert "RemapPackedCuesAfterRepack" in timeline
    assert "project?.SubtitleBurnPath" in session
    assert "project.SubtitleBurnPath = frame.SubtitleBurnPath" in session
    assert "WriteSubtitleSnapshot" in session
    assert "ReadSubtitleSnapshot" in session
    assert "project?.GlobalLutPath" in session
    assert "project.GlobalLutPath = frame.GlobalLutPath" in session
    assert "SourceToPacked(TimeSpan sourceTime, string? sourcePath" in session
    assert "public TimeSpan MarkToPacked(" in session
    assert "public TimelineProject Clone(" in timeline
    assert "public TimelineClip? ImportClip(" in timeline
    assert "never lands on top" in timeline
    assert "PackLinkedVideoAudio()" in timeline
    assert "HasMultipleVideoSources" in timeline
    assert "IReadOnlyList<(TimeSpan Start, TimeSpan End, string SourcePath)>" in timeline


def test_export_keeps_fade_wipe_and_skips_primary_only_concat():
    ffmpeg = _read(FFMPEG)
    graph = _read(ROOT / "src/NetieSpace/Services/FfmpegExportGraph.cs")
    assert "HasMultipleVideoSources" in ffmpeg
    assert "so a second/third V1 clip is not exported from the primary path" in ffmpeg
    assert "FfmpegExportGraph.XfadeWithAudio" in ffmpeg
    assert "FfmpegExportGraph.PadBlackAfterVideo" in ffmpeg
    assert "xfade=transition=" in graph
    assert "XfadeWipeName" in graph
    assert "ProbeDurationAsync" in ffmpeg
    assert "fade=t=in" in graph
    assert "fade=t=out" in graph
    # Simple concat must not run when mixed sources are on V1.
    idx = ffmpeg.index("hasMultipleSources")
    simple = ffmpeg.index("ExportKeptSegmentsAsync", idx)
    assert "!hasMultipleSources" in ffmpeg[idx:simple]


def test_add_clip_button_packs_on_v1_not_file_merge():
    preview = _read(PREVIEW)
    xaml = _read(PREVIEW_XAML)
    start = preview.index("private async void AddVideoButton_Click")
    end = preview.index("private async void RemoveBgButton_Click")
    body = preview[start:end]
    assert "AppendClip" in body
    assert "ProbeDurationAsync" in body
    assert "SyncPackedSlider" in body
    assert "Add clip onto V1" in body
    assert "MergeAsync" not in body
    assert "PromptVideoSavePath(\"merged\")" not in body
    assert 'Content="+ Clip"' in xaml
    assert "packs after existing clips" in xaml
    assert "WipeButton_Click" in preview
    assert "ApplyWipeAtJoin" in preview
    assert "PackedStartOf(_videoEdit.SelectedIndex)" in preview
    seek = preview[preview.index("private void SeekTimelineSeconds"):preview.index("private void VideoSlider_ValueChanged")]
    assert "updateSlider: false" in seek
    assert "VideoSlider.Value = seconds" in seek
    assert "private TimeSpan PackedPlayhead()" in preview
    assert "AddOverlay(OverlayKind.Black, PackedPlayhead()" in preview
    assert "AddOverlay(OverlayKind.White, PackedPlayhead()" in preview
    assert "AddOverlay(OverlayKind.Text, start" in preview
    assert "AddOverlay(OverlayKind.Black, GetPlayhead()" not in preview
    assert "AddOverlay(OverlayKind.White, GetPlayhead()" not in preview
    assert "AddOverlay(OverlayKind.Text, GetPlayhead()" not in preview
    layout = preview[preview.index("private void UpdateTimelineLayout()"):preview.index("if (TimelinePlayhead is not null)")]
    assert "PackedPlayhead().TotalSeconds" in layout
    assert "GetPlayhead().TotalSeconds" not in layout
    assert "MarkToPacked" not in layout
    marks = preview[preview.index("private TimeSpan MarkFromPlayhead()"):preview.index("private void UpdateMarkLabel()")]
    assert "return PackedPlayhead()" in marks
    assert "PackedToSource(PackedPlayhead())" not in marks
    assert "_markIn = GetPlayhead()" not in marks
    assert "_markOut = GetPlayhead()" not in marks
    trim = preview[preview.index("private async void TrimVideoButton_Click"):preview.index("private void PreviewWebView_NavigationCompleted")]
    assert "SlicePacked" in trim
    assert "ExportTimelineAsync" in trim
    caps = preview[preview.index("private void ApplyCaptionsFromBackground"):preview.index("private void RenderAudioTrackClips")]
    assert "PushVideoUndo" in caps
    assert "WritePackedCaptions" in caps
    assert "PackedStartOf" in caps
    assert "srtBySource" in caps
    speed = preview[preview.index("private void ApplySpeedToSelected"):preview.index("private void AddTextOverlayInteractive")]
    assert "RemapPackedCuesAfterSpeed" in speed
    assert "_ns_spd_" in speed
    assert "RemapCaptionsAfterRepack" in preview
    assert "SnapshotCaptionClipPacked" in preview
    drag = preview[preview.index("private void EndClipDrag"):preview.index("private void ApplyFadeToSelected")]
    assert "RemapCaptionsAfterRepack" in drag
    assert "beforeCaptions" in preview[preview.index("private void OrderClipsButton_Click"):preview.index("private async void RemoveBgButton_Click")]
    assert "WipeJoinIndex" in _read(SESSION)
    assert "ApplyWipeAtJoin" in _read(SESSION)


def test_parking_lot_p5_checked():
    text = _read(PARKING)
    assert "- [x] Multi-source V1 polish (+ Clip)" in text
    assert "- [x] Transitions beyond fade; overlay text on canvas" in text


if __name__ == "__main__":
    test_append_and_import_pack_without_overlap()
    test_export_keeps_fade_wipe_and_skips_primary_only_concat()
    test_add_clip_button_packs_on_v1_not_file_merge()
    test_parking_lot_p5_checked()
    print("PASS test_video_multisource_v1")

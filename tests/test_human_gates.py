"""Contract checks for human-gate helper scripts + Manage billing."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CLIPS = ROOT / "scripts/make-pillar1-clips.sh"
PFX = ROOT / "scripts/make-test-pfx.sh"
PILLAR = ROOT / "docs/HUMAN_PILLAR1_TEST.md"
PARKED = ROOT / "docs/HUMAN_PARKED.md"
SETUP_XAML = ROOT / "src/NetieSpace/Views/ApiSetupWindow.xaml"


def test_scripts_and_docs():
    clips = CLIPS.read_text(encoding="utf-8")
    clips_ps = (ROOT / "scripts/make-pillar1-clips.ps1").read_text(encoding="utf-8")
    helper_ps = (ROOT / "scripts/_ffmpeg.ps1").read_text(encoding="utf-8")
    helper_sh = (ROOT / "scripts/_ffmpeg.sh").read_text(encoding="utf-8")
    assert "Get-NetieFfmpeg" in helper_ps
    assert "Get-NetieFfprobe" in helper_ps
    assert "tools\\ffmpeg" in helper_ps or "tools/ffmpeg" in helper_ps.replace("\\", "/")
    assert "Get-NetieFfmpeg" in clips_ps
    assert "_ffmpeg.ps1" in clips_ps
    assert "netie_ffmpeg" in helper_sh
    assert "_ffmpeg.sh" in clips
    check_ps = (ROOT / "scripts/check-pillar1-export.ps1").read_text(encoding="utf-8")
    assert "Get-NetieFfprobe" in check_ps
    assert "ffprobe not on PATH" not in check_ps
    pfx = PFX.read_text(encoding="utf-8")
    pillar = PILLAR.read_text(encoding="utf-8")
    parked = PARKED.read_text(encoding="utf-8")
    xaml = SETUP_XAML.read_text(encoding="utf-8")
    assert "clip_a.mp4" in clips and "clip_b.mp4" in clips
    assert "1.6" in clips and "2.0" in clips
    assert "3.1" in clips
    assert "pass:test" in pfx
    pfx_ps = (ROOT / "scripts/make-test-pfx.ps1").read_text(encoding="utf-8")
    assert "New-SelfSignedCertificate" in pfx_ps
    assert "Export-PfxCertificate" in pfx_ps
    assert "password: test" in pfx_ps
    assert "openssl not on PATH" not in pfx_ps
    assert "make-pillar1-clips.ps1" in pillar
    assert "3.1s" in pillar
    assert "2.3s" in pillar
    assert "Undo" in pillar
    assert "Ctrl+Z" in pillar
    assert "extra Ctrl+Z" in pillar or "push undo" in pillar.lower()
    assert "Leave black" in pillar
    assert "title card" in pillar
    assert "no amix" in pillar.lower() or "preview-only" in pillar.lower() or "preview only" in pillar.lower()
    assert "mouse capture" in pillar.lower()
    assert "Speed 2x" in pillar or "Speed 2" in pillar
    assert "Play after" in pillar or "MediaEnded" in pillar
    assert "abort the drag" in pillar
    assert "Space-close" in pillar or "snapped the timeline" in pillar
    assert "make-test-pfx.ps1" in parked
    assert "New-SelfSignedCertificate" in parked
    assert 'x:Name="ManageBillingButton"' in xaml
    assert 'x:Name="ActivateLaptopButton"' in xaml
    assert (ROOT / "scripts/make-pillar1-clips.ps1").is_file()
    assert (ROOT / "scripts/make-test-pfx.ps1").is_file()
    check_sh = (ROOT / "scripts/check-pillar1-export.sh").read_text(encoding="utf-8")
    check_ps = (ROOT / "scripts/check-pillar1-export.ps1").read_text(encoding="utf-8")
    assert "2.8" in check_sh and "3.4" in check_sh
    assert "2.8" in check_ps and "3.4" in check_ps
    assert "check-pillar1-export.ps1" in pillar
    export = (ROOT / "src/NetieSpace/Services/BackgroundExportService.cs").read_text(encoding="utf-8")
    toast = (ROOT / "src/NetieSpace/Views/ToastHud.xaml.cs").read_text(encoding="utf-8")
    assert "TryFormatDuration" in export
    assert "FfmpegService.ResolveFfprobe" in export
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    start = preview.index("private async Task ExportEditButton_ClickAsync")
    end = preview.index("private ExportPreset ReadExportPreset")
    assert "_timeline.Clone()" in preview[start:end]
    assert "timelineSnapshot.SubtitleBurnPath" in preview[start:end]
    fade_bool = preview.index("private void ApplyFadeToSelected(bool fadeIn)")
    fade_bool_end = preview.index("private void ApplySpeedToSelected")
    fade_bool_body = preview[fade_bool:fade_bool_end]
    assert "ApplyFadeToSelected(fadeIn: TimeSpan.FromMilliseconds(500)" in fade_bool_body
    fade_span = preview.index("private void ApplyFadeToSelected(TimeSpan? fadeIn")
    fade_span_end = preview.index("private void ApplyCaptionsFromBackground")
    assert "PushVideoUndo" in preview[fade_span:fade_span_end]
    assert "DiscardLastVideoUndo" in preview[fade_span:fade_span_end]
    wipe = preview.index("private void WipeButton_Click")
    wipe_end = preview.index("private void OverlayTextButton_Click")
    assert "PushVideoUndo" in preview[wipe:wipe_end]
    assert "DiscardLastVideoUndo" in preview[wipe:wipe_end]
    assert "PushUndo(_timeline)" in preview
    assert "TryUndo(_timeline)" in preview
    fx = preview[preview.index("private void TimelineTrack_PreviewMouseRightButtonUp"):preview.index("private void ApplySpeedToSelected")]
    assert "PushVideoUndo" in fx
    assert "AddOverlay(OverlayKind.Black" in fx
    speed = preview[preview.index("private void ApplySpeedToSelected"):preview.index("private void AddTextOverlayInteractive")]
    assert "ApplySpeedAt" in speed
    assert "DiscardLastVideoUndo" in speed
    assert "SyncPackedSlider" in speed
    assert "Wipe already on that join" in preview[wipe:wipe_end]
    split = preview.index("private void SplitAtPlayhead_Click")
    split_end = preview.index("private void CutMiddleButton_Click")
    assert "DiscardLastVideoUndo" in preview[split:split_end]
    add = preview.index("private async void AddVideoButton_Click")
    add_end = preview.index("private void OrderClipsButton_Click")
    assert "DiscardLastVideoUndo" in preview[add:add_end]
    assert "SyncPackedSlider" in preview[add:add_end]
    assert "PackedPlayheadAfterElapsed" in preview
    assert "PackedPlayContinuesAfterSourceEnded" in preview
    assert "BeginPackedPlayClock" in preview
    seek = preview[preview.index("private void SeekTimelineSeconds"):preview.index("private void VideoSlider_ValueChanged")]
    assert "BeginPackedPlayClock" in seek
    ui = preview[preview.index("private void UpdateVideoUi"):preview.index("private async void AddVideoButton_Click")]
    assert "_packedPlayUtc is null" in ui
    assert "BeginPackedPlayClock" in ui
    assert "RebasePackedPlayClockIfPlaying" in ui
    assert "RebasePackedPlayClockIfPlaying" in preview
    add = preview[preview.index("private async void AddVideoButton_Click"):preview.index("private void OrderClipsButton_Click")]
    assert "BeginPackedPlayClock" in add
    ended = preview[preview.index("private void PreviewVideo_MediaEnded"):preview.index("private void PlayPauseButton_Click")]
    assert "PackedPlayContinuesAfterSourceEnded" in ended
    assert "PreviewVideo.Stop()" in ended
    sync = preview[preview.index("private void SyncPackedSlider"):preview.index("private void PushVideoUndo")]
    assert "PackedDuration" in sync
    assert "VideoSlider.Maximum" in sync
    handle_up = preview[preview.index("FadeHandle_PreviewMouseLeftButtonUp"):preview.index("private void RenderCaptionTrack")]
    assert "ApplyFadeAt" in handle_up
    assert "DiscardLastVideoUndo" in handle_up
    assert "EndFadeHandleDrag" in handle_up
    assert "FadeHandle_LostMouseCapture" in handle_up
    assert "LostMouseCapture += FadeHandle_LostMouseCapture" in preview
    assert "LostMouseCapture += ClipBlock_LostMouseCapture" in preview
    assert "EndClipDrag" in preview
    layout = preview[preview.index("private void UpdateTimelineLayout"):preview.index("private void RenderKeptClipSegments")]
    assert "_dragClipIndex >= 0" in layout
    assert "_fadeDragClip >= 0" in layout
    assert "return" in layout
    clip_drag = preview[preview.index("private void ClipBlock_PreviewMouseLeftButtonUp"):preview.index("private void FadeInButton_Click")]
    assert "DiscardLastVideoUndo" in clip_drag
    delete_body = preview[preview.index("Delete clip"):preview.index("private async void ExportEditButton_Click")]
    assert "PackedStartOf" in delete_body
    assert "PackedEndOf" in delete_body
    assert "Kept[_videoEdit.SelectedIndex].End -" not in delete_body
    a2 = preview[preview.index("private void AudioTrack_PreviewMouseLeftButtonDown"):preview.index("private void TimelineTrack_PreviewMouseRightButtonUp")]
    assert "PackedStartOf" in a2
    assert "TimelineStart = a" not in a2
    assert "no amix" in a2
    assert "export mix" not in a2
    keys = preview.index("private void Window_KeyDown")
    keys_end = preview.index("public bool IsTypingOrEditCapture")
    assert "Key.Delete" in preview[keys:keys_end]
    assert "CutMiddleButton_Click" in preview[keys:keys_end]
    assert "IsTypingOrEditCapture" in preview[keys:keys_end]
    burned_line = [ln for ln in preview[start:end].splitlines() if "SubtitleBurnPath" in ln]
    assert burned_line, "export status must mention SubtitleBurnPath"
    assert all("timelineSnapshot.SubtitleBurnPath" in ln for ln in burned_line)
    assert "wipe expect ~3.1s" in export
    ffmpeg_cs = (ROOT / "src/NetieSpace/Services/FfmpegService.cs").read_text(encoding="utf-8")
    assert "internal static string? ResolveFfprobe" in ffmpeg_cs
    assert 'IoPath.Combine(root, "tools", "ffmpeg"' in ffmpeg_cs
    assert "durationNote" in toast
    enc = (ROOT / "scripts/encode-pillar1-wipe.sh").read_text(encoding="utf-8")
    assert "wipeleft" in enc
    assert "offset=1.1" in enc
    assert "_ffmpeg.sh" in enc
    assert "check-pillar1-export.sh" in enc
    enc_ps = (ROOT / "scripts/encode-pillar1-wipe.ps1").read_text(encoding="utf-8")
    assert "wipeleft" in enc_ps
    assert "offset=1.1" in enc_ps
    assert "_ffmpeg.ps1" in enc_ps
    assert "Get-NetieFfmpeg" in enc_ps
    assert "check-pillar1-export.ps1" in enc_ps
    cursor_ci = (ROOT / "scripts/ci-cursor.sh").read_text(encoding="utf-8")
    assert "encode-pillar1-wipe.sh" in cursor_ci
    yml = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
    assert "encode-pillar1-wipe.ps1" in yml
    assert "encode-pillar1-wipe.sh" in yml
    assert "tools/ffmpeg" in pillar
    assert "price_1U8ckCFV5wcFod2faXiUCsaV" in (ROOT / "docs/HUMAN_STRIPE_CHECKOUT.md").read_text(encoding="utf-8")
    publish_ps = (ROOT / "scripts/publish.ps1").read_text(encoding="utf-8")
    ensure_ps = (ROOT / "scripts/ensure-ai-tools.ps1").read_text(encoding="utf-8")
    assert "download-ffmpeg.ps1" in publish_ps
    assert "download-ffmpeg.ps1" in ensure_ps
    assert "ffmpeg copied to dist" in ensure_ps
    assert "gitignored" in pillar or "download-ffmpeg" in pillar


if __name__ == "__main__":
    test_scripts_and_docs()
    print("PASS test_human_gates")

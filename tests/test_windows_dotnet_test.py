"""Contract: Windows CI runs xunit NLE tests, not compile-only."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CI = ROOT / ".github/workflows/ci.yml"
SLN = ROOT / "NetieSpace.sln"
CSPROJ = ROOT / "tests/NetieSpace.Tests/NetieSpace.Tests.csproj"
NLE = ROOT / "tests/NetieSpace.Tests/VideoNleSessionTests.cs"


def test_windows_ci_runs_dotnet_test():
    yml = CI.read_text(encoding="utf-8")
    sln = SLN.read_text(encoding="utf-8")
    csproj = CSPROJ.read_text(encoding="utf-8")
    nle = NLE.read_text(encoding="utf-8")
    assert "dotnet test NetieSpace.sln" in yml
    assert "--blame-hang-timeout 2m" in yml
    assert "name: build (windows)" in yml
    assert "name: python syntax" in yml
    assert "name: license-api" in yml
    assert "needs: [build-windows, python-syntax, license-api]" not in yml
    assert "npm ci --legacy-peer-deps" in yml
    assert "npm run typecheck" in yml
    assert "actions/setup-node@v5" in yml
    assert "actions/checkout@v5" in yml
    assert "actions/setup-dotnet@v5" in yml
    assert "actions/setup-python@v6" in yml
    assert "actions/checkout@v4" not in yml
    assert "actions/setup-dotnet@v4" not in yml
    assert "actions/setup-python@v5" not in yml
    assert "NetieSpace.Tests" in sln
    assert "xunit" in csproj
    assert "<ProjectReference" not in csproj
    assert "Do not ProjectReference the WPF WinExe" in csproj
    assert "Select_split_drag_delete_ripples" in nle
    assert "Wipe_from_either_clip_survives_ApplyTo" in nle
    assert "Undo_after_fade_and_wipe_keeps_plus_clip" in nle
    assert "Failed_split_undo_does_not_consume_plus_clip" in nle
    assert "Unchanged_fade_undo_does_not_consume_plus_clip" in nle
    assert "Second_wipe_undo_does_not_consume_plus_clip" in nle
    assert "Leave_black_undo_removes_overlay_card" in nle
    assert "Undo_restores_subtitle_burn_path" in nle
    assert "Undo_restores_global_lut_path" in nle
    assert "Undo_restores_overwritten_srt_text" in nle
    assert "Leave_black_last_clip_keeps_ruler_for_overlay" in nle
    assert "Leave_black_inserts_v1_hole_not_cover_next_clip" in nle
    assert "SlicePacked_second_clip_drops_clip1_captions" in nle
    assert "SlicePacked_shifts_caption_into_range" in nle
    assert "MapCuesToPacked_second_clip_offsets_by_packed_start" in nle
    assert "WritePackedCaptions_merges_two_clip_srts" in nle
    assert "RemapPackedCuesAfterSpeed_2x_shrinks_clip1_cue" in nle
    assert "RemapPackedCuesAfterSpeed_2x_shifts_later_clip_cues" in nle
    assert "RemapPackedCuesAfterRepack_swap_moves_clip1_cue_onto_clip2" in nle
    assert "RemapPackedCuesAfterRepack_ripple_delete_drops_removed_clip_cues" in nle
    graph_cs = (ROOT / "tests/NetieSpace.Tests/FfmpegExportGraphTests.cs").read_text(encoding="utf-8")
    assert "Pad_black_after_video_uses_add_not_clone" in graph_cs
    assert "Escape_filter_path_escapes_apostrophe_and_drive_colon" in graph_cs
    assert "Concat_demuxer_line_uses_file_name_only" in graph_cs
    assert "Speed_survives_ApplyTo" in nle
    assert "Speed_2x_shrinks_packed_duration" in nle
    assert "Packed_play_clock_continues_past_clip1_end" in nle
    assert "Unchanged_speed_undo_does_not_consume_plus_clip" in nle
    assert "Context_black_overlay_undo_removes_card" in nle
    assert "Does not check the PARKING_LOT human Export gate" in nle
    assert "FedericoCarboni/setup-ffmpeg@v3" in yml
    assert "choco install ffmpeg" not in yml
    cursor_ci = (ROOT / "scripts/ci-cursor.sh").read_text(encoding="utf-8")
    assert "python3 -m compileall" in cursor_ci
    assert "npm run typecheck" in cursor_ci
    assert "dotnet test tests/NetieSpace.Tests/NetieSpace.Tests.csproj" in cursor_ci
    assert "--blame-hang-timeout 2m" in cursor_ci
    assert "encode-pillar1-wipe.sh" in cursor_ci
    assert "encode-pillar1-wipe.ps1" in yml
    assert "encode-pillar1-wipe.sh" in yml
    install = (ROOT / "scripts/ci-cursor-install.sh").read_text(encoding="utf-8")
    assert "dotnet-install.sh" in install
    assert "--channel 8.0" in install
    live = (ROOT / "tests/NetieSpace.Tests/FfmpegExportLiveTests.cs").read_text(encoding="utf-8")
    assert "FfmpegExportGraph.XfadeWithAudio" in live
    assert "ffmpeg/ffprobe required on CI" in live
    assert "2.8" in live and "3.4" in live
    assert "dur > 2.5 && dur < 4.0" not in live
    assert "ExportTimeline_playbook_fade_wipe_to_apostrophe_folder" in live
    assert "ExportTimeline_hardcut_concat_to_apostrophe_folder" in live
    assert "ExportTimeline_leave_black_last_clip_keeps_card_duration" in live
    assert "ExportTimeline_caption_and_text_from_apostrophe_folder" in live
    assert "ExportTimeline_slice_packed_second_clip_is_not_clip1_trim" in live
    assert "ExportTimeline_slice_packed_full_window_keeps_wipe" in live
    assert "ExportTimeline_lut_from_apostrophe_folder" in live
    assert "CopyFilterFileToTemp" in (ROOT / "src/NetieSpace/Services/FfmpegService.cs").read_text(encoding="utf-8")
    assert "ExportTimelineAsync" in live
    assert "FfmpegService.cs" in csproj
    assert "ProjectRoot.cs" in csproj
    assert "<ProjectReference" not in csproj
    nle_py = (ROOT / "tests/test_ffmpeg_nle_export.py").read_text(encoding="utf-8")
    assert "assertGreaterEqual(dur, 2.8)" in nle_py
    assert "assertLessEqual(dur, 3.4)" in nle_py


if __name__ == "__main__":
    test_windows_ci_runs_dotnet_test()
    print("PASS test_windows_dotnet_test")

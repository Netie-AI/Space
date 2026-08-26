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
    assert "Does not check the PARKING_LOT human Export gate" in nle
    assert "FedericoCarboni/setup-ffmpeg@v3" in yml
    assert "choco install ffmpeg" not in yml
    cursor_ci = (ROOT / "scripts/ci-cursor.sh").read_text(encoding="utf-8")
    assert "python3 -m compileall" in cursor_ci
    assert "npm run typecheck" in cursor_ci
    assert "dotnet test tests/NetieSpace.Tests/NetieSpace.Tests.csproj" in cursor_ci
    assert "--blame-hang-timeout 2m" in cursor_ci
    install = (ROOT / "scripts/ci-cursor-install.sh").read_text(encoding="utf-8")
    assert "dotnet-install.sh" in install
    assert "--channel 8.0" in install
    live = (ROOT / "tests/NetieSpace.Tests/FfmpegExportLiveTests.cs").read_text(encoding="utf-8")
    assert "FfmpegExportGraph.XfadeWithAudio" in live
    assert "ffmpeg/ffprobe required on CI" in live
    assert "2.8" in live and "3.4" in live
    assert "dur > 2.5 && dur < 4.0" not in live
    nle_py = (ROOT / "tests/test_ffmpeg_nle_export.py").read_text(encoding="utf-8")
    assert "assertGreaterEqual(dur, 2.8)" in nle_py
    assert "assertLessEqual(dur, 3.4)" in nle_py


if __name__ == "__main__":
    test_windows_ci_runs_dotnet_test()
    print("PASS test_windows_dotnet_test")

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
    assert "needs: [build-windows, python-syntax]" in yml
    assert "NetieSpace.Tests" in sln
    assert "xunit" in csproj
    assert "<ProjectReference" not in csproj
    assert "Do not ProjectReference the WPF WinExe" in csproj
    assert "Select_split_drag_delete_ripples" in nle
    assert "Does not check the PARKING_LOT human Export gate" in nle


if __name__ == "__main__":
    test_windows_ci_runs_dotnet_test()
    print("PASS test_windows_dotnet_test")

"""Contract checks for the fail-closed Windows AI probe (no WinML required)."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CLIENT = ROOT / "src/NetieSpace/Services/WindowsAiClient.cs"
PREVIEW = ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs"
PREVIEW_XAML = ROOT / "src/NetieSpace/Views/PreviewWindow.xaml"
TEST_CS = ROOT / "tests/NetieSpace.Tests/WindowsAiClientTests.cs"
TEST_CSPROJ = ROOT / "tests/NetieSpace.Tests/NetieSpace.Tests.csproj"
PARKING = ROOT / "PARKING_LOT.md"
HUMAN = ROOT / "docs/HUMAN_PARKED.md"


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_client_fail_closed():
    text = _read(CLIENT)
    assert "class WindowsAiClient" in text
    assert "System.Windows" not in text
    assert "IsAvailable" in text
    assert "WriteInputSidecar" in text
    assert ".winai-input.json" in text
    assert "rembg / Copy still work" in text
    assert "Never throw" in text or "Never throws" in text
    assert "OperatingSystem.IsWindows" in text


def test_menu_and_handler():
    xaml = _read(PREVIEW_XAML)
    cs = _read(PREVIEW)
    assert 'Content="Windows AI..."' in xaml
    assert "WindowsAiButton" in xaml
    assert "WindowsAiButton_Click" in cs
    assert "WindowsAiClient.IsAvailable" in cs
    assert "WindowsAiButton.Visibility = isImage" in cs
    assert "rembg / Copy still work" in cs
    click = cs[cs.index("WindowsAiButton_Click") : cs.index("PreviewImage_MouseLeftButtonDown")]
    assert click.index("WindowsAiClient.IsAvailable") < click.index("WriteInputSidecar")
    assert "Sidecar only when WinML is down" in click
    assert "Wrote {IoPath.GetFileName(sidecar)}. Live enhance" not in click


def test_xunit_and_docs():
    csproj = _read(TEST_CSPROJ)
    tests = _read(TEST_CS)
    parking = _read(PARKING)
    human = _read(HUMAN)
    assert "WindowsAiClient.cs" in csproj
    assert "<ProjectReference" not in csproj
    assert "Linux_or_missing_runtime_is_unavailable" in tests
    assert "code-ready local client; live GPU still human" in parking
    assert "Windows AI..." in human
    assert "WinML" in human


if __name__ == "__main__":
    test_client_fail_closed()
    test_menu_and_handler()
    test_xunit_and_docs()
    print("PASS test_windows_ai_client")

"""Contract checks for PDF/video toolbar discipline (primary chips + More...)."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PREVIEW = ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs"
PREVIEW_XAML = ROOT / "src/NetieSpace/Views/PreviewWindow.xaml"
PARKING = ROOT / "PARKING_LOT.md"
CI = ROOT / ".github/workflows/ci.yml"


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_pdf_and_video_more_menus():
    xaml = _read(PREVIEW_XAML)
    cs = _read(PREVIEW)
    assert 'Content="More..."' in xaml
    assert "PdfMoreButton" in xaml
    assert "VideoMoreButton" in xaml
    assert "VideoPrimaryRow" in xaml
    assert "PdfPrimaryRow" in xaml
    assert "Export all" in xaml
    assert "+ Clip" in xaml
    assert "Order..." in xaml
    # Presets / LUT / range live in More, not as always-visible chips.
    ribbon = xaml[xaml.index("VideoEditorRibbon") : xaml.index("SummaryRailColumn")]
    assert "VideoPresetMatchMenu" in ribbon
    assert "Preset: 1080p" in ribbon
    assert "LUT..." in ribbon
    assert "Export range (In to Out)" in ribbon
    assert "ComboBox" not in ribbon
    assert 'Content="Match"' not in ribbon
    assert "PdfMoreButton_Click" in cs
    assert "VideoMoreButton_Click" in cs
    assert "FrameworkElement { Tag: string tag }" in cs


def test_parking_lot_toolbar_checked():
    text = _read(PARKING)
    assert "- [x] Toolbar discipline polish (primary chips + More...)" in text


def test_ci_runs_contract_scripts():
    yml = _read(CI)
    assert "python -m compileall -q -f tests scripts" in yml
    assert "for f in tests/test_*.py" in yml
    assert 'python "$f"' in yml
    assert "dotnet test NetieSpace.sln" in yml
    assert "name: license-api" in yml
    assert "needs: [build-windows, python-syntax, license-api]" not in yml


if __name__ == "__main__":
    test_pdf_and_video_more_menus()
    test_parking_lot_toolbar_checked()
    test_ci_runs_contract_scripts()
    print("PASS test_toolbar_discipline")

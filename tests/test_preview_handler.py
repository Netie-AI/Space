"""Contract checks for optional Windows IPreviewHandler host (PARKING_LOT P8)."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SERVICE = ROOT / "src/NetieSpace/Services/PreviewHandlerService.cs"
OVERLAY = ROOT / "src/NetieSpace/Views/PreviewHandlerOverlay.cs"
PREVIEW = ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs"
PARKING = ROOT / "PARKING_LOT.md"


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_overlay_is_opaque_not_in_glass_window():
    overlay = _read(OVERLAY)
    service = _read(SERVICE)
    preview = _read(PREVIEW)
    assert "AllowsTransparency = false" in overlay
    assert "IPreviewHandler" in overlay
    assert "8895b1c6-b41f-4c1c-a562-0d564250836f" in service
    assert "NETIE_SPACE_PREVIEW_HANDLER" in service
    assert "TryParseClsid" in service
    assert "IInitializeWithFile" in service
    assert "IInitializeWithStream" in service
    assert "HidePreviewHandler" in preview
    assert "EnsurePreviewHandler" in preview
    assert "PreviewKind.ShellInfo" in preview
    assert "TryShow" in preview
    # Must not host the HWND inside the layered PreviewWindow tree.
    assert "HwndHost" not in _read(ROOT / "src/NetieSpace/Views/PreviewWindow.xaml")


def test_parking_lot_p8_handler_checked():
    text = _read(PARKING)
    assert "- [x] Windows IPreviewHandler host (optional)" in text


if __name__ == "__main__":
    test_overlay_is_opaque_not_in_glass_window()
    test_parking_lot_p8_handler_checked()
    print("PASS test_preview_handler")

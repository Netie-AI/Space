"""Contract checks for Phase 3 ordered multi-clip V1 merge UX."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SESSION = ROOT / "src/NetieSpace/Services/VideoEditSession.cs"
PREVIEW = ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs"
PREVIEW_XAML = ROOT / "src/NetieSpace/Views/PreviewWindow.xaml"
ORDER_CS = ROOT / "src/NetieSpace/Views/VideoOrderWindow.xaml.cs"
ORDER_XAML = ROOT / "src/NetieSpace/Views/VideoOrderWindow.xaml"
PARKING = ROOT / "PARKING_LOT.md"


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_apply_order_is_permutation():
    session = _read(SESSION)
    assert "public bool ApplyOrder(IReadOnlyList<int> fromIndices)" in session
    assert "seen[i]" in session
    assert "MoveClip(" in session


def test_add_clip_multiselect_and_order_dialog():
    preview = _read(PREVIEW)
    xaml = _read(PREVIEW_XAML)
    start = preview.index("private async void AddVideoButton_Click")
    end = preview.index("private async void RemoveBgButton_Click")
    body = preview[start:end]
    assert "Multiselect = true" in body
    assert "foreach (var file in dlg.FileNames)" in body
    assert "AppendClip" in body
    assert "OrderClipsButton_Click" in body
    assert "VideoOrderWindow.Show" in body
    assert "ApplyOrder" in body
    assert "MergeAsync" not in body
    assert 'Content="Order..."' in xaml
    assert "OrderClipsButton" in xaml
    assert "Multiselect" in body


def test_order_window_top_plays_first():
    cs = _read(ORDER_CS)
    xaml = _read(ORDER_XAML)
    assert "class VideoOrderWindow" in cs
    assert "OrderedIndices" in cs
    assert "_rows.Move" in cs
    assert "Top clip plays first" in xaml
    assert "Apply order" in xaml
    assert "DropShadow" not in xaml


def test_parking_lot_phase3_checked():
    text = _read(PARKING)
    assert "- [x] Phase 3: richer ordered multi-clip merge UX" in text


if __name__ == "__main__":
    test_apply_order_is_permutation()
    test_add_clip_multiselect_and_order_dialog()
    test_order_window_top_plays_first()
    test_parking_lot_phase3_checked()
    print("PASS test_video_clip_order")

"""Contract checks for the Word/HTML contenteditable editor (PARKING_LOT P8)."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SERVICE = ROOT / "src/NetieSpace/Services/OfficePreviewService.cs"
PREVIEW = ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs"
CSPROJ = ROOT / "src/NetieSpace/NetieSpace.csproj"
PARKING = ROOT / "PARKING_LOT.md"


def test_no_itext_or_new_preview_handler():
    csproj = CSPROJ.read_text(encoding="utf-8")
    assert "itext" not in csproj.lower()
    assert "AGPL" not in csproj
    assert "IPreviewHandler" not in SERVICE.read_text(encoding="utf-8")
    assert "IPreviewHandler" not in PREVIEW.read_text(encoding="utf-8")


def test_save_api_and_autosave_path_exist():
    service = SERVICE.read_text(encoding="utf-8")
    preview = PREVIEW.read_text(encoding="utf-8")
    assert "SaveEditedPreview" in service
    assert "IsHtmlWordEditable" in service
    assert "HtmlDocxWriter" in service
    assert "type == \"htmlEdit\"" in preview
    assert "ScheduleHtmlAutosave" in preview
    assert "ToggleHtmlEditModeAsync" in preview
    assert "contenteditable" in preview


def test_parking_lot_p8_checked():
    text = PARKING.read_text(encoding="utf-8")
    assert "- [x] Word/HTML contenteditable editor" in text
    assert "- [ ] Windows IPreviewHandler host" in text


if __name__ == "__main__":
    test_no_itext_or_new_preview_handler()
    test_save_api_and_autosave_path_exist()
    test_parking_lot_p8_checked()
    print("PASS test_word_html_editor")

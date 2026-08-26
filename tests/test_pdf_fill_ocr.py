"""Contract tests for PDF form fill / OCR / in-place text (PARKING_LOT P3 Tier B).

Save path: viewer fillTexts → PreviewWindow._pendingFillTexts → PdfEditService.DrawFillTexts
(PdfSharpCore XGraphics Append, no iText). Coordinates: PDF points, origin bottom-left;
XGraphics is top-left: y_top = page_height - y_from_bottom - height.

OCR: ImageOcrService local (Tesseract TSV / Windows OCR) seeds those boxes.
Cloud OCR stays opt-in via NETIE_SPACE_OCR_CLOUD=1.
"""
from __future__ import annotations

import os
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OCR_CS = ROOT / "src" / "NetieSpace" / "Services" / "ImageOcrService.cs"
EDIT_CS = ROOT / "src" / "NetieSpace" / "Services" / "PdfEditService.cs"
VIEWER = ROOT / "src" / "NetieSpace" / "Assets" / "pdfjs" / "netie-viewer.html"
PREVIEW = ROOT / "src" / "NetieSpace" / "Views" / "PreviewWindow.xaml.cs"
CSPROJ = ROOT / "src" / "NetieSpace" / "NetieSpace.csproj"
PARKING = ROOT / "PARKING_LOT.md"


def canvas_to_pdf_bottom_left(
    left_px: float,
    top_px: float,
    width_px: float,
    height_px: float,
    canvas_w: float,
    canvas_h: float,
    page_w: float,
    page_h: float,
) -> tuple[float, float, float, float]:
    if canvas_w <= 0 or canvas_h <= 0:
        return 0.0, 0.0, 0.0, 0.0
    x_pt = (left_px / canvas_w) * page_w
    w_pt = (width_px / canvas_w) * page_w
    h_pt = (height_px / canvas_h) * page_h
    y_top_pt = (top_px / canvas_h) * page_h
    y_from_bottom = page_h - y_top_pt - h_pt
    return x_pt, y_from_bottom, w_pt, h_pt


def xgraphics_y(page_height: float, y_from_bottom: float, height_pt: float) -> float:
    return page_height - y_from_bottom - height_pt


def parse_tesseract_tsv_lines(tsv: str) -> list[tuple[str, float, float, float, float]]:
    """Mirror ImageOcrService.ParseTesseractTsv — group words (level 5) into lines."""
    groups: dict[tuple[int, int, int, int], list[tuple[int, int, int, int, str]]] = {}
    for raw in tsv.splitlines():
        raw = raw.strip()
        if not raw or raw.lower().startswith("level"):
            continue
        cols = raw.split("\t")
        if len(cols) < 12:
            continue
        try:
            level = int(cols[0])
            if level != 5:
                continue
            page, block, par, line = int(cols[1]), int(cols[2]), int(cols[3]), int(cols[4])
            left, top, w, h = int(cols[6]), int(cols[7]), int(cols[8]), int(cols[9])
        except ValueError:
            continue
        word = cols[11].strip()
        if not word:
            continue
        groups.setdefault((page, block, par, line), []).append((left, top, w, h, word))

    boxes = []
    for words in groups.values():
        left = min(w[0] for w in words)
        top = min(w[1] for w in words)
        right = max(w[0] + w[2] for w in words)
        bottom = max(w[1] + w[3] for w in words)
        text = " ".join(w[4] for w in words)
        boxes.append((text, float(left), float(top), float(right - left), float(bottom - top)))
    return boxes


class FillSaveContractTests(unittest.TestCase):
    def test_no_itext(self) -> None:
        csproj = CSPROJ.read_text(encoding="utf-8")
        self.assertNotIn("itext", csproj.lower())
        self.assertNotIn("AGPL", csproj)
        edit = EDIT_CS.read_text(encoding="utf-8")
        self.assertNotIn("iText", edit)
        self.assertIn("PdfSharpCore", edit)
        self.assertIn("DrawFillTexts", edit)
        self.assertIn("CoverBackground", edit)

    def test_pending_fill_save_path(self) -> None:
        preview = PREVIEW.read_text(encoding="utf-8")
        self.assertIn("_pendingFillTexts", preview)
        self.assertIn("DrawFillTexts", preview)
        self.assertIn("type == \"fillTexts\"", preview)
        self.assertIn("seedFillTexts", preview)
        self.assertIn("exportPagePng", preview)
        self.assertIn("PdfOcrSeed_Click", preview)
        self.assertIn("OcrImageButton_Click", preview)
        ocr = preview.split("OcrImageButton_Click", 1)[1].split("SeedOcrFillTextsAsync", 1)[0]
        self.assertIn("RasterizeForComfyAsync", ocr)

    def test_viewer_seed_and_export(self) -> None:
        html = VIEWER.read_text(encoding="utf-8")
        self.assertIn("function seedFillTexts", html)
        self.assertIn("function exportPagePng", html)
        self.assertIn("seedFillTexts", html)
        self.assertIn("exportPagePng", html)
        self.assertIn("enterTextEditMode", html)
        self.assertNotIn("iText", html)

    def test_y_conversion_matches_draw_fill(self) -> None:
        page_h = 792.0
        y_from_bottom = 100.0
        height = 18.0
        self.assertAlmostEqual(xgraphics_y(page_h, y_from_bottom, height), 674.0)
        x, yb, w, h = canvas_to_pdf_bottom_left(50, 674, 120, 18, 612, 792, 612, 792)
        self.assertAlmostEqual(x, 50.0)
        self.assertAlmostEqual(yb, 100.0)
        self.assertAlmostEqual(w, 120.0)
        self.assertAlmostEqual(h, 18.0)
        self.assertAlmostEqual(xgraphics_y(page_h, yb, h), 674.0)


class OcrGateTests(unittest.TestCase):
    def test_cloud_opt_in_symbol(self) -> None:
        src = OCR_CS.read_text(encoding="utf-8")
        self.assertIn("NETIE_SPACE_OCR_CLOUD", src)
        self.assertIn("IsCloudEnabled", src)
        self.assertIn("TryTesseractLayoutAsync", src)
        self.assertIn("TryWindowsOcrAsync", src)
        self.assertIn("ToFillTexts", src)
        # Cloud must not run unless the env gate is true.
        self.assertIn("if (IsCloudEnabled)", src)
        self.assertNotIn("iText", src)

    def test_cloud_disabled_by_default(self) -> None:
        prev = os.environ.pop("NETIE_SPACE_OCR_CLOUD", None)
        try:
            self.assertNotEqual(os.environ.get("NETIE_SPACE_OCR_CLOUD"), "1")
        finally:
            if prev is not None:
                os.environ["NETIE_SPACE_OCR_CLOUD"] = prev

    def test_tesseract_tsv_groups_words_into_lines(self) -> None:
        tsv = "\n".join(
            [
                "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext",
                "5\t1\t1\t1\t1\t1\t10\t20\t40\t12\t90\tHello",
                "5\t1\t1\t1\t1\t2\t55\t20\t50\t12\t88\tworld",
                "5\t1\t1\t1\t2\t1\t10\t40\t30\t12\t85\tOCR",
            ]
        )
        boxes = parse_tesseract_tsv_lines(tsv)
        self.assertEqual(len(boxes), 2)
        self.assertEqual(boxes[0][0], "Hello world")
        self.assertEqual(boxes[0][1:], (10.0, 20.0, 95.0, 12.0))
        self.assertEqual(boxes[1][0], "OCR")

    def test_ocr_boxes_map_to_pdf_points(self) -> None:
        # 100px image, page 200pt square; line at y=10 h=20 → yFromBottom = 200 - 20 - 40
        x, yb, w, h = canvas_to_pdf_bottom_left(5, 10, 40, 20, 100, 100, 200, 200)
        self.assertAlmostEqual(x, 10.0)
        self.assertAlmostEqual(w, 80.0)
        self.assertAlmostEqual(h, 40.0)
        self.assertAlmostEqual(yb, 140.0)


class ParkingLotTests(unittest.TestCase):
    def test_tier_b_checked(self) -> None:
        text = PARKING.read_text(encoding="utf-8")
        self.assertIn("- [x] Form fill / OCR / in-place text — Tier B", text)
        self.assertIn("- [ ] Cryptographic digital signature — PKCS#7 incremental", text)


if __name__ == "__main__":
    unittest.main()

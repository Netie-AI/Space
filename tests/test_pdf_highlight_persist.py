"""Contract tests for PDF highlight persist (PdfSharpCore appearance, no iText).

Coordinates: PDF points, origin bottom-left (same as stamps/fills).
PdfSharpCore XGraphics origin is top-left:
    y_top = page_height - y_from_bottom - height

Fail-closed: bake to a sibling temp; only replace the dest after the temp
write succeeds. If bake raises, dest bytes stay untouched.
"""
from __future__ import annotations

import json
import os
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VIEWER = ROOT / "src" / "NetieSpace" / "Assets" / "pdfjs" / "netie-viewer.html"
EDIT_CS = ROOT / "src" / "NetieSpace" / "Services" / "PdfEditService.cs"
VIEWER_CS = ROOT / "src" / "NetieSpace" / "Services" / "PdfJsViewerService.cs"
CSPROJ = ROOT / "src" / "NetieSpace" / "NetieSpace.csproj"


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
    """Match PdfJsViewerService.CanvasToPdfBottomLeft / netie-viewer.html mouseup."""
    if canvas_w <= 0 or canvas_h <= 0:
        return 0.0, 0.0, 0.0, 0.0
    x_pt = (left_px / canvas_w) * page_w
    w_pt = (width_px / canvas_w) * page_w
    h_pt = (height_px / canvas_h) * page_h
    y_top_pt = (top_px / canvas_h) * page_h
    y_from_bottom = page_h - y_top_pt - h_pt
    return x_pt, y_from_bottom, w_pt, h_pt


def xgraphics_y(page_height: float, y_from_bottom: float, height_pt: float) -> float:
    """Match PdfJsViewerService.PdfBottomLeftToXGraphicsTop / DrawHighlights."""
    return page_height - y_from_bottom - height_pt


def fail_closed_replace(dest: Path, payload: bytes, bake) -> None:
    """Write via dest.tmp; replace dest only after bake() returns. On error, dest unchanged."""
    tmp = dest.with_suffix(dest.suffix + ".tmp")
    try:
        baked = bake(payload)
        tmp.write_bytes(baked)
        os.replace(tmp, dest)
    except Exception:
        if tmp.exists():
            tmp.unlink()
        raise


class HighlightCoordTests(unittest.TestCase):
    def test_round_trip_letter_page(self) -> None:
        page_w, page_h = 612.0, 792.0
        canvas_w, canvas_h = 612.0, 792.0
        x, yb, w, h = canvas_to_pdf_bottom_left(
            50, 100, 200, 20, canvas_w, canvas_h, page_w, page_h
        )
        self.assertAlmostEqual(x, 50.0)
        self.assertAlmostEqual(w, 200.0)
        self.assertAlmostEqual(h, 20.0)
        # Canvas y=100 from top → PDF yFromBottom = 792 - 100 - 20
        self.assertAlmostEqual(yb, 672.0)
        self.assertAlmostEqual(xgraphics_y(page_h, yb, h), 100.0)

    def test_scaled_canvas_keeps_pdf_points(self) -> None:
        page_w, page_h = 612.0, 792.0
        canvas_w, canvas_h = 1224.0, 1584.0  # 2×
        x, yb, w, h = canvas_to_pdf_bottom_left(
            100, 200, 400, 40, canvas_w, canvas_h, page_w, page_h
        )
        self.assertAlmostEqual(x, 50.0)
        self.assertAlmostEqual(w, 200.0)
        self.assertAlmostEqual(h, 20.0)
        self.assertAlmostEqual(yb, 672.0)

    def test_legacy_top_left_y_converts_with_page_height(self) -> None:
        msg = {
            "type": "highlight",
            "page": 1,
            "x": 40,
            "y": 80,
            "w": 120,
            "h": 16,
            "pageHeightPt": 792,
        }
        y_from_bottom = msg["pageHeightPt"] - msg["y"] - msg["h"]
        self.assertAlmostEqual(y_from_bottom, 696.0)
        self.assertAlmostEqual(xgraphics_y(792, y_from_bottom, 16), 80.0)


class FailClosedTests(unittest.TestCase):
    def test_failed_bake_leaves_original(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            dest = Path(td) / "doc.pdf"
            original = b"%PDF-1.4 original-bytes"
            dest.write_bytes(original)

            def boom(_payload: bytes) -> bytes:
                raise RuntimeError("bake exploded")

            with self.assertRaises(RuntimeError):
                fail_closed_replace(dest, original, boom)
            self.assertEqual(dest.read_bytes(), original)
            self.assertFalse((dest.with_suffix(".pdf.tmp")).exists())

    def test_successful_bake_replaces(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            dest = Path(td) / "doc.pdf"
            dest.write_bytes(b"%PDF-1.4 old")

            def bake(_payload: bytes) -> bytes:
                return b"%PDF-1.4 with-highlight"

            fail_closed_replace(dest, dest.read_bytes(), bake)
            self.assertEqual(dest.read_bytes(), b"%PDF-1.4 with-highlight")


class WiringTests(unittest.TestCase):
    def test_no_itext_package(self) -> None:
        csproj = CSPROJ.read_text(encoding="utf-8")
        self.assertNotIn("iText", csproj)
        self.assertNotIn("itext", csproj.lower())
        self.assertIn("PdfSharpCore", csproj)
        self.assertIn("PdfPig", csproj)

    def test_edit_service_has_draw_highlights(self) -> None:
        src = EDIT_CS.read_text(encoding="utf-8")
        self.assertIn("DrawHighlights", src)
        self.assertIn("PdfHighlightItem", src)
        self.assertIn("XGraphicsPdfPageOptions.Append", src)
        self.assertNotRegex(src, r"using\s+iText")
        self.assertNotIn("iTextSharp", src)

    def test_viewer_posts_bottom_left_and_keeps_overlay_on_fail(self) -> None:
        html = VIEWER.read_text(encoding="utf-8")
        self.assertIn("yFromBottom", html)
        self.assertIn("type: 'highlight'", html)
        self.assertIn("postHighlights", html)
        self.assertIn("persistFailed", html)
        self.assertIn("marks kept in session", html)
        self.assertIn("snapshotHighlights", html)
        self.assertIn("id=\"markBtn\"", html)

    def test_viewer_service_parses_highlight_payload(self) -> None:
        src = VIEWER_CS.read_text(encoding="utf-8")
        self.assertIn("ReadHighlights", src)
        self.assertIn("yFromBottom", src)
        self.assertIn("CanvasToPdfBottomLeft", src)

    def test_highlight_json_shape(self) -> None:
        payload = {
            "type": "highlight",
            "id": "s1",
            "page": 1,
            "xPt": 72.0,
            "yFromBottom": 680.0,
            "wPt": 160.0,
            "hPt": 14.0,
            "kind": "highlight",
            "pageHeightPt": 792.0,
        }
        dumped = json.dumps(payload)
        loaded = json.loads(dumped)
        self.assertEqual(loaded["page"], 1)
        self.assertGreater(loaded["wPt"], 0.5)
        self.assertAlmostEqual(
            xgraphics_y(loaded["pageHeightPt"], loaded["yFromBottom"], loaded["hPt"]),
            98.0,
        )


if __name__ == "__main__":
    raise SystemExit(unittest.main())

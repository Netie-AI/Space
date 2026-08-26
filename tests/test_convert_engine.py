"""Contract tests for tools/convert_engine.py.

The engine is the only tracked file under tools/ (gitignore exception).
C# ConversionService and the pdf/convert stress scripts spawn it.

ci-cursor.sh runs every tests/test_*.py -- pymupdf smoke is skipped when the
dep is missing so the gate still PASSes.
"""
from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ENGINE = ROOT / "tools" / "convert_engine.py"

REQUIRED_ACTIONS = {
    "to-md",
    "to-pdf",
    "preview",
    "preview-html",
    "preview-pptx-html",
    "export-as",
    "pdf-watermark",
    "pdf-protect",
    "pdf-unlock",
    "pdf-ocr",
}


def load_engine():
    spec = importlib.util.spec_from_file_location("convert_engine", ENGINE)
    assert spec is not None and spec.loader is not None
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def pymupdf_available() -> bool:
    try:
        import pymupdf  # noqa: F401
        return True
    except ImportError:
        return False


def run_engine(args: list[str]) -> dict:
    proc = subprocess.run(
        [sys.executable, str(ENGINE), *args],
        capture_output=True,
        text=True,
        timeout=60,
    )
    line = next((ln for ln in reversed(proc.stdout.splitlines()) if ln.strip().startswith("{")), "")
    try:
        return json.loads(line)
    except Exception:
        return {"success": False, "error": (proc.stderr or proc.stdout or "no json")[:400]}


def test_engine_file_exists() -> None:
    assert ENGINE.is_file(), f"missing {ENGINE}"
    text = ENGINE.read_text(encoding="utf-8")
    assert "success" in text
    assert "output_path" in text
    assert "pdf-watermark" in text
    assert "pdf-protect" in text


def test_actions_registered() -> None:
    mod = load_engine()
    names = set(getattr(mod, "ACTIONS", {}))
    missing = REQUIRED_ACTIONS - names
    assert not missing, f"unregistered actions: {sorted(missing)}"


def test_unknown_action_emits_json() -> None:
    res = run_engine(["not-an-action", "nope.bin"])
    assert res.get("success") is False
    assert res.get("error")


def test_watermark_protect_smoke() -> None:
    if not pymupdf_available():
        print("SKIP watermark+protect smoke -- pymupdf not importable")
        return
    import pymupdf

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        src = d / "tiny.pdf"
        doc = pymupdf.open()
        page = doc.new_page(width=300, height=200)
        page.insert_text((40, 80), "Hello", fontsize=16)
        doc.save(str(src))
        doc.close()

        wm = d / "wm.pdf"
        res = run_engine(["pdf-watermark", str(src), str(wm), "--text", "DRAFT"])
        assert res.get("success"), res
        assert wm.is_file() and wm.stat().st_size > 200
        doc = pymupdf.open(str(wm))
        text = doc[0].get_text()
        doc.close()
        assert "DRAFT" in text, text

        locked = d / "locked.pdf"
        res = run_engine(["pdf-protect", str(src), str(locked), "--password", "secret"])
        assert res.get("success"), res
        doc = pymupdf.open(str(locked))
        needs_pass = doc.needs_pass
        doc.close()
        assert needs_pass

        res = run_engine(["pdf-watermark", str(d / "missing.pdf"), str(d / "x.pdf"), "--text", "X"])
        assert res.get("success") is False and res.get("error")


def test_export_as_docx_from_text_sources() -> None:
    """Share Export as Word. Engine used to return unsupported target: .docx."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def write_docx" in src
    assert "def file_to_docx" in src
    assert 'if dest_ext == ".docx"' in src
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    dialog = preview.split("private static (string filter", 1)[1]
    assert "Word document|*.docx" in dialog
    assert "OpenDocument Text|*.odt" not in dialog
    assert "OpenDocument Spreadsheet|*.ods" not in dialog

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        md = d / "note.md"
        md.write_text("Hello export\nSecond line\n", encoding="utf-8")
        out = d / "note.docx"
        res = run_engine(["export-as", str(md), str(out)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res
        assert out.is_file() and out.stat().st_size > 200
        import zipfile

        with zipfile.ZipFile(out) as zf:
            xml = zf.read("word/document.xml").decode("utf-8")
        assert "Hello export" in xml
        assert "Second line" in xml

        sample = ROOT / "src" / "NetieSpace" / "Assets" / "pdfjs" / "sample.pdf"
        if sample.is_file() and pymupdf_available():
            pdf_out = d / "sample.docx"
            res = run_engine(["export-as", str(sample), str(pdf_out)])
            assert res.get("success"), res
            assert pdf_out.is_file() and pdf_out.stat().st_size > 200
            with zipfile.ZipFile(pdf_out) as zf:
                assert "word/document.xml" in zf.namelist()

        same = d / "note-copy.docx"
        res = run_engine(["export-as", str(out), str(same)])
        assert res.get("success"), res
        assert res.get("tool") == "copy", res
        assert same.is_file() and same.stat().st_size == out.stat().st_size


def test_export_as_xlsx_from_csv() -> None:
    """Share Export as Excel on a CSV. Engine used to return unsupported target: .xlsx."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def write_xlsx" in src
    assert "def file_to_xlsx" in src
    assert "def via_soffice" in src
    assert 'if dest_ext == ".xlsx"' in src
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    assert "Excel workbook|*.xlsx" in preview
    assert "PowerPoint presentation|*.pptx" in preview

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        csv_path = d / "table.csv"
        csv_path.write_text("name,qty\nwidget,2\n", encoding="utf-8")
        out = d / "table.xlsx"
        res = run_engine(["export-as", str(csv_path), str(out)])
        assert res.get("success"), res
        assert res.get("tool") == "xlsx-zip", res
        assert out.is_file() and out.stat().st_size > 200
        import zipfile

        with zipfile.ZipFile(out) as zf:
            sheet = zf.read("xl/worksheets/sheet1.xml").decode("utf-8")
        assert "widget" in sheet
        assert "2" in sheet

        same = d / "table-copy.xlsx"
        res = run_engine(["export-as", str(out), str(same)])
        assert res.get("success"), res
        assert res.get("tool") == "copy", res


def test_pdf_ocr_force_uses_ocrmypdf_force_api() -> None:
    """ocrmypdf 17: skip_text=False aborts on text pages. --force must send force_ocr."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "skip_text=not force" not in src
    assert "force_ocr=True" in src
    assert "skip_text=True" in src


def ocrmypdf_available() -> bool:
    try:
        import ocrmypdf  # noqa: F401
        return True
    except ImportError:
        return False


def test_pdf_ocr_force_live() -> None:
    """Live --force on a text PDF. Skip if ocrmypdf/tesseract are not installed."""
    import shutil

    if not ocrmypdf_available() or shutil.which("tesseract") is None:
        print("SKIP pdf-ocr --force live -- ocrmypdf or tesseract missing")
        return
    sample = ROOT / "src" / "NetieSpace" / "Assets" / "pdfjs" / "sample.pdf"
    if not sample.is_file():
        print("SKIP pdf-ocr --force live -- sample.pdf missing")
        return
    with tempfile.TemporaryDirectory() as tmp:
        dest = Path(tmp) / "ocr-force.pdf"
        proc = subprocess.run(
            [sys.executable, str(ENGINE), "pdf-ocr", str(sample), str(dest), "--force"],
            capture_output=True,
            text=True,
            timeout=180,
        )
        line = next(
            (ln for ln in reversed(proc.stdout.splitlines()) if ln.strip().startswith("{")),
            "",
        )
        try:
            res = json.loads(line)
        except Exception:
            res = {"success": False, "error": (proc.stderr or proc.stdout or "no json")[:400]}
        assert res.get("success"), res
        assert res.get("tool") == "ocrmypdf", res
        assert dest.is_file() and dest.stat().st_size > 200


if __name__ == "__main__":
    test_engine_file_exists()
    test_actions_registered()
    test_unknown_action_emits_json()
    test_watermark_protect_smoke()
    test_export_as_docx_from_text_sources()
    test_export_as_xlsx_from_csv()
    test_pdf_ocr_force_uses_ocrmypdf_force_api()
    test_pdf_ocr_force_live()
    print("PASS test_convert_engine")

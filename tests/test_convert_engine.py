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


def test_export_as_markdown_suffix() -> None:
    """Share Export as on README.markdown. Parser only treated .md so convertOk hid the row."""
    parser = (ROOT / "src/NetieSpace/Services/ContentParserService.cs").read_text(encoding="utf-8")
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    explorer = (ROOT / "src/NetieSpace/Services/ExplorerFileService.cs").read_text(encoding="utf-8")
    assert 'extension is ".md" or ".markdown"' in parser
    assert '".md" or ".markdown"' in preview
    assert '".markdown"' in explorer
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _markdown_to_html_stdlib" in src
    dlg = preview.split("var imageDefaultExt", 1)[1]
    md_dlg = dlg.split('".md"', 1)[1][:240]
    assert "PDF document|*.pdf" in md_dlg

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        md = d / "README.markdown"
        md.write_text("# Hello markdown suffix\n", encoding="utf-8")
        pdf = d / "README.pdf"
        res = run_engine(["export-as", str(md), str(pdf)])
        assert res.get("success"), res
        assert pdf.is_file() and pdf.stat().st_size > 200
        if pymupdf_available():
            import pymupdf

            doc = pymupdf.open(pdf)
            try:
                text = doc[0].get_text()
            finally:
                doc.close()
            assert "Hello markdown" in text
            assert "# Hello" not in text
        docx = d / "README.docx"
        res = run_engine(["export-as", str(md), str(docx)])
        assert res.get("success"), res
        import zipfile

        with zipfile.ZipFile(docx) as zf:
            xml = zf.read("word/document.xml").decode("utf-8")
        assert "Hello markdown suffix" in xml
        assert "# Hello" not in xml


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


def test_csv_utf16_export() -> None:
    """Share dest Excel on Notepad Unicode / UTF-8 BOM CSV keeps the cell text."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def csv_to_rows" in src
    assert "def _read_markup" in src
    import zipfile
    import re

    def cells(path: Path) -> list[str]:
        with zipfile.ZipFile(path) as zf:
            xml = zf.read("xl/worksheets/sheet1.xml").decode("utf-8")
        return re.findall(r"<t[^>]*>([^<]*)</t>", xml)

    body = "name,qty\nleftover,3\n"
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        utf16 = d / "u.csv"
        utf16.write_bytes(body.encode("utf-16"))
        out = d / "u.xlsx"
        res = run_engine(["export-as", str(utf16), str(out)])
        assert res.get("success"), res
        assert res.get("tool") == "xlsx-zip", res
        got = cells(out)
        assert "name" in got, got
        assert "leftover" in got, got
        assert not any("\ufeff" in c or "\ufffd" in c for c in got), got
        bom = d / "bom.csv"
        bom.write_bytes(b"\xef\xbb\xbf" + body.encode("utf-8"))
        bout = d / "bom.xlsx"
        res = run_engine(["export-as", str(bom), str(bout)])
        assert res.get("success"), res
        got = cells(bout)
        assert "name" in got, got
        assert not any("\ufeff" in c for c in got), got


def _write_odt(path: Path, paragraphs: list[str]) -> None:
    paras = "".join(f"<text:p>{p}</text:p>" for p in paragraphs)
    content = (
        '<?xml version="1.0" encoding="UTF-8"?>'
        '<office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"'
        ' xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">'
        f"<office:body><office:text>{paras}</office:text></office:body>"
        "</office:document-content>"
    )
    import zipfile

    with zipfile.ZipFile(path, "w") as zf:
        zf.writestr("mimetype", "application/vnd.oasis.opendocument.text")
        zf.writestr("content.xml", content)
        zf.writestr(
            "META-INF/manifest.xml",
            '<?xml version="1.0"?><manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0"/>',
        )


def _write_ods(path: Path, rows: list[list[str]]) -> None:
    body = []
    for row in rows:
        cells = "".join(f"<table:table-cell><text:p>{c}</text:p></table:table-cell>" for c in row)
        body.append(f"<table:table-row>{cells}</table:table-row>")
    content = (
        '<?xml version="1.0" encoding="UTF-8"?>'
        '<office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"'
        ' xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"'
        ' xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">'
        "<office:body><office:spreadsheet><table:table>"
        f"{''.join(body)}</table:table></office:spreadsheet></office:body>"
        "</office:document-content>"
    )
    import zipfile

    with zipfile.ZipFile(path, "w") as zf:
        zf.writestr("mimetype", "application/vnd.oasis.opendocument.spreadsheet")
        zf.writestr("content.xml", content)
        zf.writestr(
            "META-INF/manifest.xml",
            '<?xml version="1.0"?><manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0"/>',
        )


def test_export_as_rtf_odt_ods() -> None:
    """Share default Export as PDF on RTF/ODT/ODS used to need markitdown/LibreOffice."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def rtf_to_text" in src
    assert "def odt_to_text" in src
    assert "def ods_to_rows" in src
    parser = (ROOT / "src/NetieSpace/Services/ContentParserService.cs").read_text(encoding="utf-8")
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    explorer = (ROOT / "src/NetieSpace/Services/ExplorerFileService.cs").read_text(encoding="utf-8")
    # ShellInfo hid Share Export as (convertOk). These must preview as Office.
    assert 'extension is ".odt" or ".rtf" or ".ods" or ".odp"' in parser
    assert "not PreviewKind.ShellInfo" in preview
    assert '".odt"' in explorer and '".xlsm"' in explorer
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        rtf = d / "note.rtf"
        rtf.write_text(r"{\rtf1\ansi\deff0 Hello RTF\par}", encoding="ascii")
        rtf_pdf = d / "note.pdf"
        res = run_engine(["export-as", str(rtf), str(rtf_pdf)])
        assert res.get("success"), res
        assert rtf_pdf.is_file() and rtf_pdf.stat().st_size > 200
        rtf_docx = d / "note.docx"
        res = run_engine(["export-as", str(rtf), str(rtf_docx)])
        assert res.get("success"), res
        with zipfile.ZipFile(rtf_docx) as zf:
            assert "Hello RTF" in zf.read("word/document.xml").decode("utf-8")

        odt = d / "note.odt"
        _write_odt(odt, ["Hello ODT", "Second para"])
        odt_docx = d / "from-odt.docx"
        res = run_engine(["export-as", str(odt), str(odt_docx)])
        assert res.get("success"), res
        with zipfile.ZipFile(odt_docx) as zf:
            xml = zf.read("word/document.xml").decode("utf-8")
        assert "Hello ODT" in xml
        assert "Second para" in xml

        ods = d / "table.ods"
        _write_ods(ods, [["name", "qty"], ["widget", "2"]])
        ods_xlsx = d / "from-ods.xlsx"
        res = run_engine(["export-as", str(ods), str(ods_xlsx)])
        assert res.get("success"), res
        assert res.get("tool") == "xlsx-zip", res
        with zipfile.ZipFile(ods_xlsx) as zf:
            sheet = zf.read("xl/worksheets/sheet1.xml").decode("utf-8")
        assert "widget" in sheet


def _write_odp(path: Path, slides: list[str]) -> None:
    pages = "".join(
        "<draw:page><draw:frame><draw:text-box>"
        f"<text:p>{title}</text:p>"
        "</draw:text-box></draw:frame></draw:page>"
        for title in slides
    )
    content = (
        '<?xml version="1.0" encoding="UTF-8"?>'
        '<office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"'
        ' xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"'
        ' xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">'
        f"<office:body><office:presentation>{pages}</office:presentation></office:body>"
        "</office:document-content>"
    )
    import zipfile

    with zipfile.ZipFile(path, "w") as zf:
        zf.writestr("mimetype", "application/vnd.oasis.opendocument.presentation")
        zf.writestr("content.xml", content)
        zf.writestr(
            "META-INF/manifest.xml",
            '<?xml version="1.0"?><manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0"/>',
        )


def test_export_as_odp() -> None:
    """Share default Export as PDF on ODP used to need pptx2md."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def odp_to_text" in src
    assert "def via_soffice_markdown" in src
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        odp = d / "deck.odp"
        _write_odp(odp, ["Slide one title", "Slide two body"])
        pdf = d / "deck.pdf"
        res = run_engine(["export-as", str(odp), str(pdf)])
        assert res.get("success"), res
        assert pdf.is_file() and pdf.stat().st_size > 200
        html_out = d / "deck.html"
        res = run_engine(["export-as", str(odp), str(html_out)])
        assert res.get("success"), res
        html_text = html_out.read_text(encoding="utf-8")
        assert "Slide one title" in html_text
        docx = d / "deck.docx"
        res = run_engine(["export-as", str(odp), str(docx)])
        assert res.get("success"), res
        with zipfile.ZipFile(docx) as zf:
            xml = zf.read("word/document.xml").decode("utf-8")
        assert "Slide one title" in xml
        assert "Slide two body" in xml


def test_export_as_ppsx() -> None:
    """Share default Export as PDF on a desktop .ppsx. Used to be ShellInfo + need markitdown."""
    src = ENGINE.read_text(encoding="utf-8")
    assert '".pptx", ".ppsx", ".ppt"' in src or '".pptx", ".ppsx"' in src
    parser = (ROOT / "src/NetieSpace/Services/ContentParserService.cs").read_text(encoding="utf-8")
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    assert 'extension is ".pptx" or ".ppsx"' in parser
    assert '".pptx" or ".ppsx" or ".ppt"' in preview
    merge = (ROOT / "src/NetieSpace/Views/PdfMergeWindow.xaml.cs").read_text(encoding="utf-8")
    assert '".ppsx"' in merge and '".xlsm"' in merge

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        pptx = d / "deck.pptx"
        _write_pptx(pptx, ["Hello PPSX"])
        ppsx = d / "deck.ppsx"
        ppsx.write_bytes(pptx.read_bytes())
        pdf = d / "deck.pdf"
        res = run_engine(["export-as", str(ppsx), str(pdf)])
        assert res.get("success"), res
        assert pdf.is_file() and pdf.stat().st_size > 200
        html_out = d / "deck.html"
        res = run_engine(["export-as", str(ppsx), str(html_out)])
        assert res.get("success"), res
        assert "Hello PPSX" in html_out.read_text(encoding="utf-8")


def test_export_as_pps() -> None:
    """Share / drop a desktop .pps. Probe already found it; parser was ShellInfo and engine said need markitdown."""
    src = ENGINE.read_text(encoding="utf-8")
    assert '".ppt", ".pps"' in src or '".pps"' in src
    parser = (ROOT / "src/NetieSpace/Services/ContentParserService.cs").read_text(encoding="utf-8")
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    explorer = (ROOT / "src/NetieSpace/Services/ExplorerFileService.cs").read_text(encoding="utf-8")
    assert '".pps"' in explorer
    assert 'extension is ".pptx" or ".ppsx" or ".pps"' in parser
    assert '".pptx" or ".ppsx" or ".ppt" or ".pps"' in preview
    convert_ok = preview.split("var convertOk", 1)[1].split(";", 1)[0]
    assert "PreviewKind.ShellInfo" in convert_ok
    merge = (ROOT / "src/NetieSpace/Views/PdfMergeWindow.xaml.cs").read_text(encoding="utf-8")
    assert '".pps"' in merge
    drop = preview.split(
        "private static bool TryGetDroppedConvertibles", 1
    )[1].split("private static bool TryGetDroppedImages", 1)[0]
    assert '".pps"' in drop

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        pptx = d / "deck.pptx"
        _write_pptx(pptx, ["Hello PPS"])
        pps = d / "deck.pps"
        pps.write_bytes(pptx.read_bytes())
        pdf = d / "deck.pdf"
        res = run_engine(["export-as", str(pps), str(pdf)])
        assert res.get("success"), res
        assert pdf.is_file() and pdf.stat().st_size > 200
        html_out = d / "deck.html"
        res = run_engine(["export-as", str(pps), str(html_out)])
        assert res.get("success"), res
        assert "Hello PPS" in html_out.read_text(encoding="utf-8")


def _write_pptx(path: Path, slides: list[str]) -> None:
    import zipfile

    with zipfile.ZipFile(path, "w") as zf:
        for i, text in enumerate(slides, 1):
            xml = (
                '<?xml version="1.0"?>'
                '<s:sld xmlns:s="http://schemas.openxmlformats.org/presentationml/2006/main" '
                'xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">'
                f"<a:t>{text}</a:t></s:sld>"
            )
            zf.writestr(f"ppt/slides/slide{i}.xml", xml)


def test_export_as_docx_xlsx_to_pdf() -> None:
    """Share default Export as PDF on Word/Excel used to need markitdown."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def docx_to_text" in src
    assert "def xlsx_to_rows" in src
    assert 'if ext in {".xlsx", ".xlsm"}' in src
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    assert '".xlsx" or ".xlsm" or ".xls"' in preview

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        md = d / "note.md"
        md.write_text("Hello DOCX source\n", encoding="utf-8")
        docx = d / "note.docx"
        res = run_engine(["export-as", str(md), str(docx)])
        assert res.get("success"), res
        pdf = d / "note.pdf"
        res = run_engine(["export-as", str(docx), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-story" or (pdf.is_file() and pdf.stat().st_size > 200), res
        assert pdf.is_file() and pdf.stat().st_size > 200

        csv_path = d / "table.csv"
        csv_path.write_text("name,qty\nwidget,2\n", encoding="utf-8")
        xlsx = d / "table.xlsx"
        res = run_engine(["export-as", str(csv_path), str(xlsx)])
        assert res.get("success"), res
        xpdf = d / "table.pdf"
        res = run_engine(["export-as", str(xlsx), str(xpdf)])
        assert res.get("success"), res
        assert xpdf.is_file() and xpdf.stat().st_size > 200
        xhtml = d / "table.html"
        res = run_engine(["export-as", str(xlsx), str(xhtml)])
        assert res.get("success"), res
        assert "widget" in xhtml.read_text(encoding="utf-8")

        # Preview already treats .xlsm as Excel; default Export as PDF used to need markitdown.
        xlsm = d / "table.xlsm"
        xlsm.write_bytes(xlsx.read_bytes())
        xmpdf = d / "table-xlsm.pdf"
        res = run_engine(["export-as", str(xlsm), str(xmpdf)])
        assert res.get("success"), res
        assert xmpdf.is_file() and xmpdf.stat().st_size > 200
        xmhtml = d / "table-xlsm.html"
        res = run_engine(["export-as", str(xlsm), str(xmhtml)])
        assert res.get("success"), res
        assert "widget" in xmhtml.read_text(encoding="utf-8")
        xmxlsx = d / "table-from-xlsm.xlsx"
        res = run_engine(["export-as", str(xlsm), str(xmxlsx)])
        assert res.get("success"), res
        assert res.get("tool") == "copy", res


def test_export_as_preview_text_to_pdf() -> None:
    """Share default Export as PDF on previewed text/code used to need markitdown."""
    src = ENGINE.read_text(encoding="utf-8")
    assert '".json"' in src
    assert '".py"' in src
    assert '".cs"' in src
    parser = (ROOT / "src/NetieSpace/Services/ContentParserService.cs").read_text(encoding="utf-8")
    explorer = (ROOT / "src/NetieSpace/Services/ExplorerFileService.cs").read_text(encoding="utf-8")
    assert '".json"' in parser and '".py"' in parser
    # Engine TEXT_EXTS listed .text; parser only had .txt so ShellInfo hid Share Export as.
    assert '".text"' in src
    assert '".text"' in parser
    assert '".text"' in explorer

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        js = d / "config.json"
        js.write_text('{"name": "netie", "n": 1}\n', encoding="utf-8")
        pdf = d / "config.pdf"
        res = run_engine(["export-as", str(js), str(pdf)])
        assert res.get("success"), res
        assert pdf.is_file() and pdf.stat().st_size > 200
        html = d / "config.html"
        res = run_engine(["export-as", str(js), str(html)])
        assert res.get("success"), res
        assert "netie" in html.read_text(encoding="utf-8")

        py = d / "hello.py"
        py.write_text("print('hello space')\n", encoding="utf-8")
        pypdf = d / "hello.pdf"
        res = run_engine(["export-as", str(py), str(pypdf)])
        assert res.get("success"), res
        assert pypdf.is_file() and pypdf.stat().st_size > 200

        notes = d / "notes.text"
        notes.write_text("hello space from .text\n", encoding="utf-8")
        text_pdf = d / "notes.pdf"
        res = run_engine(["export-as", str(notes), str(text_pdf)])
        assert res.get("success"), res
        assert text_pdf.is_file() and text_pdf.stat().st_size > 200


def test_html_export_same_file() -> None:
    """Share dest 1 on HTML is HTML in the source folder. copy2 raised SameFileError."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def copy_file" in src
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    dlg = preview.split("var imageDefaultExt", 1)[1]
    html_dlg = dlg.split('".html"', 1)[1][:220]
    assert "Web page|*.html" in html_dlg
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        html = d / "page.html"
        body = "<html><body><h1>Hello leftover</h1></body></html>"
        html.write_text(body, encoding="utf-8")
        res = run_engine(["export-as", str(html), str(html)])
        assert res.get("success"), res
        assert res.get("tool") == "copy", res
        assert html.read_text(encoding="utf-8") == body
        other = d / "page-out.html"
        res = run_engine(["export-as", str(html), str(other)])
        assert res.get("success"), res
        assert other.read_text(encoding="utf-8") == body
        docx = d / "note.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        res = run_engine(["export-as", str(docx), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "copy", res
        pdf = d / "page.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-story-html", res
        if pymupdf_available():
            import pymupdf

            doc = pymupdf.open(pdf)
            try:
                text = doc[0].get_text()
            finally:
                doc.close()
            assert "Hello leftover" in text
            assert "<html" not in text.lower()


def test_html_photo_export() -> None:
    """Share dest 2 PDF / dest 3 Word on photo-only HTML dumped source / empty Word."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def html_to_pdf" in src
    assert "def html_media_pngs" in src
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    dlg = preview.split("var imageDefaultExt", 1)[1]
    html_dlg = dlg.split('".html"', 1)[1][:280]
    assert "PDF document|*.pdf" in html_dlg
    assert "Word document|*.docx" in html_dlg
    if not pymupdf_available():
        print("SKIP html photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html photo export -- Pillow missing")
        return
    import base64
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 160, 60)).save(buf, format="PNG")
    png = buf.getvalue()
    b64 = base64.b64encode(png).decode("ascii")
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        html = d / "only.html"
        html.write_text(
            f'<html><body><img src="data:image/png;base64,{b64}" alt="pic"></body></html>',
            encoding="utf-8",
        )
        preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
        assert "ShareMdRow.Visibility = docFormats" in preview
        md = d / "only.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        pdf = d / "only.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            page = doc[0]
            text = page.get_text()
            images = page.get_images()
        finally:
            doc.close()
        assert "<html" not in text.lower()
        assert images
        docx = d / "only.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
        sib = d / "pic.png"
        Image.new("RGB", (64, 48), (20, 80, 200)).save(sib, format="PNG")
        rel = d / "rel.html"
        rel.write_text('<html><body><img src="pic.png" alt="pic"></body></html>', encoding="utf-8")
        rmd = d / "rel.md"
        res = run_engine(["to-md", str(rel), str(rmd)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in rmd.read_text(encoding="utf-8")
        rpdf = d / "rel.pdf"
        res = run_engine(["export-as", str(rel), str(rpdf)])
        assert res.get("success"), res
        doc = pymupdf.open(rpdf)
        try:
            images = doc[0].get_images()
        finally:
            doc.close()
        assert images
        rdocx = d / "rel.docx"
        res = run_engine(["export-as", str(rel), str(rdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(rdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist())
        md_img = d / "img.md"
        md_img.write_text("![pic](pic.png)\n", encoding="utf-8")
        mpdf = d / "img.pdf"
        res = run_engine(["export-as", str(md_img), str(mpdf)])
        assert res.get("success"), res
        doc = pymupdf.open(mpdf)
        try:
            assert doc[0].get_images()
            assert "![pic]" not in doc[0].get_text()
        finally:
            doc.close()
        bg = d / "bg.html"
        bg.write_text(
            '<html><body><div style="background-image:url(pic.png);width:64px;height:48px"></div></body></html>',
            encoding="utf-8",
        )
        bgmd = d / "bg.md"
        res = run_engine(["to-md", str(bg), str(bgmd)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in bgmd.read_text(encoding="utf-8")
        bgdocx = d / "bg.docx"
        res = run_engine(["export-as", str(bg), str(bgdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(bgdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist())
        bgpdf = d / "bg.pdf"
        res = run_engine(["export-as", str(bg), str(bgpdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        assert bgpdf.is_file() and bgpdf.stat().st_size > 2000
        doc = pymupdf.open(bgpdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()


def test_html_svg_photo_export() -> None:
    """Share dest Word / Markdown on photo-only HTML with SVG pictures was empty / 1-byte."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _svg_bytes_to_png" in src
    assert "svg\\+xml" in src
    if not pymupdf_available():
        print("SKIP html svg photo export -- pymupdf missing")
        return
    import base64
    import zipfile

    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="40" height="30">'
        '<rect width="40" height="30" fill="#00aa00"/></svg>'
    )
    b64 = base64.b64encode(svg.encode("utf-8")).decode("ascii")
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        html = d / "svgpic.html"
        html.write_text(
            f'<html><body><img src="data:image/svg+xml;base64,{b64}" alt=""></body></html>',
            encoding="utf-8",
        )
        md = d / "svgpic.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "svgpic.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "svgpic.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
            assert "[image]" not in doc[0].get_text()
        finally:
            doc.close()
        sib = d / "pic.svg"
        sib.write_text(svg, encoding="utf-8")
        rel = d / "rel.svg.html"
        rel.write_text('<html><body><img src="pic.svg" alt=""></body></html>', encoding="utf-8")
        rmd = d / "rel.svg.md"
        res = run_engine(["to-md", str(rel), str(rmd)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in rmd.read_text(encoding="utf-8")
        rdocx = d / "rel.svg.docx"
        res = run_engine(["export-as", str(rel), str(rdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(rdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist())
        from urllib.parse import quote

        enc = d / "enc.svg.html"
        enc.write_text(
            f'<html><body><img src="data:image/svg+xml,{quote(svg)}" alt=""></body></html>',
            encoding="utf-8",
        )
        emd = d / "enc.svg.md"
        res = run_engine(["to-md", str(enc), str(emd)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in emd.read_text(encoding="utf-8")
        edocx = d / "enc.svg.docx"
        res = run_engine(["export-as", str(enc), str(edocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(edocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist())
        inline = d / "inline.svg.html"
        inline.write_text(f"<html><body>{svg}</body></html>", encoding="utf-8")
        imd = d / "inline.svg.md"
        res = run_engine(["to-md", str(inline), str(imd)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in imd.read_text(encoding="utf-8")
        idocx = d / "inline.svg.docx"
        res = run_engine(["export-as", str(inline), str(idocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(idocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist())
        quoted = d / "sq.svg.html"
        quoted.write_text(
            f"<html><body><img src='data:image/svg+xml,{svg}' alt=''></body></html>",
            encoding="utf-8",
        )
        qmd = d / "sq.svg.md"
        res = run_engine(["to-md", str(quoted), str(qmd)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in qmd.read_text(encoding="utf-8")
        qdocx = d / "sq.svg.docx"
        res = run_engine(["export-as", str(quoted), str(qdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(qdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist())
        utf8 = d / "utf8.svg.html"
        utf8.write_text(
            f'<html><body><img src="data:image/svg+xml;utf8,{quote(svg)}" alt=""></body></html>',
            encoding="utf-8",
        )
        umd = d / "utf8.svg.md"
        res = run_engine(["to-md", str(utf8), str(umd)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in umd.read_text(encoding="utf-8")


def test_html_linked_css_photo_export() -> None:
    """Share dest Word/MD/PDF on photo-only HTML whose picture is only in a sibling .css."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _stylesheet_hrefs" in src
    assert "def _sibling_url_pngs" in src
    if not pymupdf_available():
        print("SKIP html linked css photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html linked css photo export -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 160, 60)).save(buf, format="PNG")
    png = buf.getvalue()
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        pic = d / "pic.png"
        pic.write_bytes(png)
        css = d / "page.css"
        css.write_text(".hero{background-image:url(pic.png)}", encoding="utf-8")
        html = d / "link.html"
        html.write_text(
            '<html><head><link rel="stylesheet" href="page.css"></head>'
            '<body><div class="hero"></div></body></html>',
            encoding="utf-8",
        )
        md = d / "link.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "link.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "link.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        assert pdf.is_file() and pdf.stat().st_size > 2000
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        nest_dir = d / "css"
        nest_dir.mkdir()
        nest_css = nest_dir / "page.css"
        nest_css.write_text(".hero{background-image:url(../pic.png)}", encoding="utf-8")
        nest = d / "nest.html"
        nest.write_text(
            '<html><head><link href="css/page.css" rel="stylesheet"></head>'
            '<body><div class="hero"></div></body></html>',
            encoding="utf-8",
        )
        ndocx = d / "nest.docx"
        res = run_engine(["export-as", str(nest), str(ndocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(ndocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()


def test_html_css_utf16_photo_export() -> None:
    """Share dest Word/MD/PDF on photo HTML whose sibling CSS is UTF-16 embeds the picture."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _read_markup" in src
    assert "def _load_css_file_pngs" in src
    if not pymupdf_available():
        print("SKIP html css utf16 photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html css utf16 photo export -- Pillow missing")
        return
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 80, 200)).save(d / "pic.png", format="PNG")
        (d / "page.css").write_bytes(
            "div{background-image:url(pic.png)}".encode("utf-16")
        )
        html = d / "page.html"
        html.write_text(
            '<html><head><link rel="stylesheet" href="page.css"></head>'
            "<body><div></div></body></html>",
            encoding="utf-8",
        )
        md = d / "page.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "page.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "page.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()


def test_html_css_at_charset_photo_export() -> None:
    """Share dest Word/MD/PDF on photo HTML whose sibling CSS declares @charset."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "_AT_CHARSET" in src
    if not pymupdf_available():
        print("SKIP html css at-charset photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html css at-charset photo export -- Pillow missing")
        return
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 80, 200)).save(d / "été.png", format="PNG")
        (d / "page.css").write_bytes(
            '@charset "windows-1252"; div{background-image:url(été.png)}'.encode(
                "windows-1252"
            )
        )
        html = d / "page.html"
        html.write_text(
            '<html><head><link rel="stylesheet" href="page.css"></head>'
            "<body><div></div></body></html>",
            encoding="utf-8",
        )
        md = d / "page.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "page.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "page.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        (d / "iso.css").write_bytes(
            '@charset "iso-8859-1"; div{background-image:url(été.png)}'.encode(
                "iso-8859-1"
            )
        )
        iso_html = d / "iso.html"
        iso_html.write_text(
            '<html><head><link rel="stylesheet" href="iso.css"></head>'
            "<body><div></div></body></html>",
            encoding="utf-8",
        )
        idocx = d / "iso.docx"
        res = run_engine(["export-as", str(iso_html), str(idocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(idocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()


def test_html_css_import_photo_export() -> None:
    """Share dest Word/MD/PDF on photo-only HTML whose picture is only behind @import."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _css_import_hrefs" in src
    assert "def _imported_css_pngs" in src
    if not pymupdf_available():
        print("SKIP html css import photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html css import photo export -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 160, 60)).save(buf, format="PNG")
    png = buf.getvalue()
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        (d / "pic.png").write_bytes(png)
        (d / "page.css").write_text(".hero{background-image:url(pic.png)}", encoding="utf-8")
        html = d / "import.html"
        html.write_text(
            "<html><head><style>@import url(page.css);</style></head>"
            '<body><div class="hero"></div></body></html>',
            encoding="utf-8",
        )
        md = d / "import.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "import.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "import.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        assert pdf.is_file() and pdf.stat().st_size > 2000
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        (d / "outer.css").write_text('@import url(page.css);', encoding="utf-8")
        chained = d / "chain.html"
        chained.write_text(
            '<html><head><link rel="stylesheet" href="outer.css"></head>'
            '<body><div class="hero"></div></body></html>',
            encoding="utf-8",
        )
        cdocx = d / "chain.docx"
        res = run_engine(["export-as", str(chained), str(cdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(cdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()


def test_html_srcset_photo_export() -> None:
    """Share dest Word/MD/PDF on photo-only HTML whose picture is only in srcset."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _srcset_hrefs" in src
    if not pymupdf_available():
        print("SKIP html srcset photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html srcset photo export -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 160, 60)).save(buf, format="PNG")
    png = buf.getvalue()
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        (d / "pic.png").write_bytes(png)
        html = d / "srcset.html"
        html.write_text(
            '<html><body><img srcset="pic.png 1x" alt=""></body></html>',
            encoding="utf-8",
        )
        md = d / "srcset.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "srcset.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "srcset.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        assert pdf.is_file() and pdf.stat().st_size > 2000
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        pic = d / "picture.html"
        pic.write_text(
            '<html><body><picture><source srcset="pic.png"><img alt=""></picture></body></html>',
            encoding="utf-8",
        )
        pdocx = d / "picture.docx"
        res = run_engine(["export-as", str(pic), str(pdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(pdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()


def test_html_src_spaced_eq_photo_export() -> None:
    """Share dest Word/MD/PDF on photo-only HTML with pretty-printed src = \"pic.png\"."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "src\\s*=\\s*" in src
    if not pymupdf_available():
        print("SKIP html src spaced eq photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html src spaced eq photo export -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 160, 60)).save(buf, format="PNG")
    png = buf.getvalue()
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        (d / "pic.png").write_bytes(png)
        html = d / "spaces.html"
        html.write_text(
            '<html><body><img src = "pic.png" alt=""></body></html>',
            encoding="utf-8",
        )
        md = d / "spaces.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "spaces.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "spaces.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        assert pdf.is_file() and pdf.stat().st_size > 2000
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()


def test_html_unquoted_src_photo_export() -> None:
    """Share dest Word/MD/PDF on photo-only HTML5 unquoted src=/srcset=/href=."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _html_attr" in src
    if not pymupdf_available():
        print("SKIP html unquoted src photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html unquoted src photo export -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 160, 60)).save(buf, format="PNG")
    png = buf.getvalue()
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        (d / "pic.png").write_bytes(png)
        html = d / "uq.html"
        html.write_text('<html><body><img src=pic.png alt=""></body></html>', encoding="utf-8")
        md = d / "uq.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "uq.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "uq.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        assert pdf.is_file() and pdf.stat().st_size > 2000
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        ss = d / "uq-srcset.html"
        ss.write_text('<html><body><img srcset=pic.png alt=""></body></html>', encoding="utf-8")
        sdocx = d / "uq-srcset.docx"
        res = run_engine(["export-as", str(ss), str(sdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(sdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        (d / "page.css").write_text(".hero{background-image:url(pic.png)}", encoding="utf-8")
        link = d / "uq-link.html"
        link.write_text(
            '<html><head><link rel=stylesheet href=page.css></head>'
            '<body><div class="hero"></div></body></html>',
            encoding="utf-8",
        )
        ldocx = d / "uq-link.docx"
        res = run_engine(["export-as", str(link), str(ldocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(ldocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()


def test_html_src_strip_photo_export() -> None:
    """Share dest Word/MD/PDF on photo-only HTML with padded / hashed sibling src."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _local_href" in src
    if not pymupdf_available():
        print("SKIP html src strip photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html src strip photo export -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 160, 60)).save(buf, format="PNG")
    png = buf.getvalue()
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        (d / "pic.png").write_bytes(png)
        html = d / "pad.html"
        html.write_text(
            '<html><body><img src=" pic.png " alt=""></body></html>',
            encoding="utf-8",
        )
        md = d / "pad.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "pad.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        frag = d / "frag.html"
        frag.write_text(
            '<html><body><img src="pic.png#hero" alt=""></body></html>',
            encoding="utf-8",
        )
        fpdf = d / "frag.pdf"
        res = run_engine(["export-as", str(frag), str(fpdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        assert fpdf.is_file() and fpdf.stat().st_size > 2000
        import pymupdf

        doc = pymupdf.open(fpdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()


def test_html_imageset_photo_export() -> None:
    """Share dest Word/MD/PDF on photo-only HTML whose picture is only in image-set()."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _sibling_imageset_pngs" in src
    if not pymupdf_available():
        print("SKIP html imageset photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html imageset photo export -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 160, 60)).save(buf, format="PNG")
    png = buf.getvalue()
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        (d / "pic.png").write_bytes(png)
        html = d / "imset.html"
        html.write_text(
            "<html><body><div style=\"background-image:image-set('pic.png' 1x)\"></div></body></html>",
            encoding="utf-8",
        )
        md = d / "imset.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "imset.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "imset.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        assert pdf.is_file() and pdf.stat().st_size > 2000
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()


def test_html_amp_img_photo_export() -> None:
    """Share dest Word/MD/PDF on photo-only AMP HTML whose picture is only in amp-img."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "amp-img" in src
    if not pymupdf_available():
        print("SKIP html amp-img photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html amp-img photo export -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 160, 60)).save(buf, format="PNG")
    png = buf.getvalue()
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        (d / "pic.png").write_bytes(png)
        html = d / "amp.html"
        html.write_text(
            '<html><body><amp-img src="pic.png" width="64" height="48"></amp-img></body></html>',
            encoding="utf-8",
        )
        md = d / "amp.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "amp.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "amp.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        assert pdf.is_file() and pdf.stat().st_size > 2000
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        ss = d / "amp-srcset.html"
        ss.write_text(
            '<html><body><amp-img srcset="pic.png" width="64" height="48"></amp-img></body></html>',
            encoding="utf-8",
        )
        sdocx = d / "amp-srcset.docx"
        res = run_engine(["export-as", str(ss), str(sdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(sdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()


def test_md_image_title_ref_export() -> None:
    """Share dest Word/HTML/PDF on MD images with title, <dest>, or [ref]."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _md_inline_html" in src
    assert "def _md_link_defs" in src
    if not pymupdf_available():
        print("SKIP md image title/ref export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP md image title/ref export -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 160, 60)).save(buf, format="PNG")
    png = buf.getvalue()
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        (d / "pic.png").write_bytes(png)
        import pymupdf

        def assert_dests(md_path: Path, stem: str) -> None:
            docx = d / f"{stem}.docx"
            res = run_engine(["export-as", str(md_path), str(docx)])
            assert res.get("success"), res
            assert res.get("tool") == "docx-zip-images", res
            with zipfile.ZipFile(docx) as zf:
                assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
            html = d / f"{stem}.html"
            res = run_engine(["export-as", str(md_path), str(html)])
            assert res.get("success"), res
            body = html.read_text(encoding="utf-8")
            assert "data:image/png;base64," in body
            assert "![" not in body
            assert "pic.png &quot;" not in body
            pdf = d / f"{stem}.pdf"
            res = run_engine(["export-as", str(md_path), str(pdf)])
            assert res.get("success"), res
            doc = pymupdf.open(pdf)
            try:
                assert doc[0].get_images(), doc[0].get_text()
                dumped = doc[0].get_text()
                assert "![" not in dumped
                assert "[id]:" not in dumped
            finally:
                doc.close()

        titled = d / "title.md"
        titled.write_text('![cat](pic.png "A cat")\n', encoding="utf-8")
        assert_dests(titled, "title")
        angled = d / "angle.md"
        angled.write_text("![cat](<pic.png>)\n", encoding="utf-8")
        assert_dests(angled, "angle")
        ref = d / "ref.md"
        ref.write_text("![cat][id]\n\n[id]: pic.png\n", encoding="utf-8")
        assert_dests(ref, "ref")
        mid = d / "mid.md"
        mid.write_text("see ![cat](pic.png) here\n", encoding="utf-8")
        html = d / "mid.html"
        res = run_engine(["export-as", str(mid), str(html)])
        assert res.get("success"), res
        body = html.read_text(encoding="utf-8")
        assert "data:image/png;base64," in body
        assert "see " in body
        pdf = d / "mid.pdf"
        res = run_engine(["export-as", str(mid), str(pdf)])
        assert res.get("success"), res
        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images(), doc[0].get_text()
            assert "![" not in doc[0].get_text()
        finally:
            doc.close()


def test_md_wrap_dest_export() -> None:
    """Share dest Word/HTML/PDF on MD images whose dest wraps to the next line."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _md_unwrap_dests" in src
    if not pymupdf_available():
        print("SKIP md wrap dest export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP md wrap dest export -- Pillow missing")
        return
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 160, 60)).save(d / "pic.png", format="PNG")

        def assert_dests(md_path: Path, stem: str) -> None:
            docx = d / f"{stem}.docx"
            res = run_engine(["export-as", str(md_path), str(docx)])
            assert res.get("success"), res
            assert res.get("tool") == "docx-zip-images", res
            with zipfile.ZipFile(docx) as zf:
                assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
            html = d / f"{stem}.html"
            res = run_engine(["export-as", str(md_path), str(html)])
            assert res.get("success"), res
            body = html.read_text(encoding="utf-8")
            assert "data:image/png;base64," in body
            assert "![" not in body
            pdf = d / f"{stem}.pdf"
            res = run_engine(["export-as", str(md_path), str(pdf)])
            assert res.get("success"), res
            import pymupdf

            doc = pymupdf.open(pdf)
            try:
                assert doc[0].get_images(), doc[0].get_text()
                assert "![" not in doc[0].get_text()
            finally:
                doc.close()

        wrapped = d / "wrap.md"
        wrapped.write_text("![x](\npic.png\n)\n", encoding="utf-8")
        assert_dests(wrapped, "wrap")
        titled = d / "wrap-title.md"
        titled.write_text('![x](\npic.png "t"\n)\n', encoding="utf-8")
        assert_dests(titled, "wrap-title")
        ref = d / "wrap-ref.md"
        ref.write_text("![x][id]\n\n[id]:\n    pic.png\n", encoding="utf-8")
        assert_dests(ref, "wrap-ref")


def test_html_pct_href_photo_export() -> None:
    """Share dest Word/MD/PDF on photo-only HTML whose sibling src is percent-encoded."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "unquote(href)" in src
    if not pymupdf_available():
        print("SKIP html pct href photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html pct href photo export -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 160, 60)).save(buf, format="PNG")
    png = buf.getvalue()
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        (d / "my pic.png").write_bytes(png)
        html = d / "pct.html"
        html.write_text(
            '<html><body><img src="my%20pic.png" alt=""></body></html>',
            encoding="utf-8",
        )
        md = d / "pct.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "pct.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "pct.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        assert pdf.is_file() and pdf.stat().st_size > 2000
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        css = d / "css-pct.html"
        css.write_text(
            '<html><body><div style="background-image:url(my%20pic.png)"></div></body></html>',
            encoding="utf-8",
        )
        cdocx = d / "css-pct.docx"
        res = run_engine(["export-as", str(css), str(cdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(cdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        md_src = d / "pct.mdsrc.md"
        md_src.write_text("![](my%20pic.png)\n", encoding="utf-8")
        mdocx = d / "pct.mdsrc.docx"
        res = run_engine(["export-as", str(md_src), str(mdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(mdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()


def test_html_src_not_data_src_photo_export() -> None:
    """Share dest Word/MD/PDF on photo HTML whose real src sits next to data-src."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "(?<![\\w-])src" in src
    assert "html.unescape" in src
    if not pymupdf_available():
        print("SKIP html src not data-src photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html src not data-src photo export -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 160, 60)).save(buf, format="PNG")
    png = buf.getvalue()
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        (d / "pic.png").write_bytes(png)
        (d / "pic&co.png").write_bytes(png)
        html = d / "lazy.html"
        html.write_text(
            '<html><body><img src="pic.png" data-src="missing.png" alt=""></body></html>',
            encoding="utf-8",
        )
        md = d / "lazy.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "lazy.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "lazy.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        first = d / "first.html"
        first.write_text(
            '<html><body><img data-src="missing.png" src="pic.png" alt=""></body></html>',
            encoding="utf-8",
        )
        fdocx = d / "first.docx"
        res = run_engine(["export-as", str(first), str(fdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(fdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        ent = d / "amp.html"
        ent.write_text(
            '<html><body><img src="pic&amp;co.png" alt=""></body></html>',
            encoding="utf-8",
        )
        edocx = d / "amp.docx"
        res = run_engine(["export-as", str(ent), str(edocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(edocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()


def test_html_num_entity_href_photo_export() -> None:
    """Share dest Word/MD/PDF on photo HTML whose sibling src uses &#46; / &#x2e;."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "html.unescape(href)" in src
    if not pymupdf_available():
        print("SKIP html num entity href photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html num entity href photo export -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 160, 60)).save(buf, format="PNG")
    png = buf.getvalue()
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        (d / "pic.png").write_bytes(png)
        html = d / "num.html"
        html.write_text(
            '<html><body><img src="pic&#46;png" alt=""></body></html>',
            encoding="utf-8",
        )
        md = d / "num.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "num.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "num.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        css = d / "css-num.html"
        css.write_text(
            '<html><body><div style="background-image:url(pic&#x2e;png)"></div></body></html>',
            encoding="utf-8",
        )
        cdocx = d / "css-num.docx"
        res = run_engine(["export-as", str(css), str(cdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(cdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()


def test_md_num_entity_href_photo_export() -> None:
    """Share dest Word/HTML/PDF on MD ![](pic&#46;png). html.escape used to leave &amp;#46;."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "html.unescape" in src
    if not pymupdf_available():
        print("SKIP md num entity href photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP md num entity href photo export -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 160, 60)).save(buf, format="PNG")
    png = buf.getvalue()
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        (d / "pic.png").write_bytes(png)
        md = d / "ent.md"
        md.write_text("![](pic&#46;png)\n", encoding="utf-8")
        docx = d / "ent.docx"
        res = run_engine(["export-as", str(md), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        html = d / "ent.html"
        res = run_engine(["export-as", str(md), str(html)])
        assert res.get("success"), res
        assert "data:image/png;base64," in html.read_text(encoding="utf-8")
        pdf = d / "ent.pdf"
        res = run_engine(["export-as", str(md), str(pdf)])
        assert res.get("success"), res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images(), doc[0].get_text()
            assert "![" not in doc[0].get_text()
        finally:
            doc.close()


def test_html_heic_sibling_photo_export() -> None:
    """Share dest Word/MD/PDF on photo HTML whose sibling is .heic. File HEIC dests already rasterize."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "register_heif_opener" in src
    if not pymupdf_available():
        print("SKIP html heic sibling photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
        import pillow_heif

        pillow_heif.register_heif_opener()
    except Exception:
        print("SKIP html heic sibling photo export -- pillow_heif missing (optional, not a Cursor CI dep)")
        return
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        heic = d / "pic.heic"
        Image.new("RGB", (64, 48), (180, 90, 30)).save(heic)
        html = d / "heic.html"
        html.write_text(
            '<html><body><img src="pic.heic" alt=""></body></html>',
            encoding="utf-8",
        )
        md = d / "heic.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "heic.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "heic.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()


def test_html_data_uri_charset_photo_export() -> None:
    """Share dest Word/MD/PDF on photo HTML whose picture is a charset data URI."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "(?:;[^,;]+)*;base64," in src
    if not pymupdf_available():
        print("SKIP html data uri charset photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html data uri charset photo export -- Pillow missing")
        return
    import base64
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 160, 60)).save(buf, format="PNG")
    b64 = base64.b64encode(buf.getvalue()).decode("ascii")
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        html = d / "charset.html"
        html.write_text(
            f'<html><body><img src="data:image/png;charset=utf-8;base64,{b64}" alt=""></body></html>',
            encoding="utf-8",
        )
        md = d / "charset.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "charset.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "charset.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        named = d / "named.html"
        named.write_text(
            f'<html><body><img src="data:image/png;name=pic.png;base64,{b64}" alt=""></body></html>',
            encoding="utf-8",
        )
        ndocx = d / "named.docx"
        res = run_engine(["export-as", str(named), str(ndocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(ndocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()


def test_html_title_visible_photo_export() -> None:
    """Share dest Word/MD/PDF on photo HTML with <title> still embeds the picture."""
    src = ENGINE.read_text(encoding="utf-8")
    assert r"<head\b" in src
    assert r"(?:[\w.-]+:)?title\b" in src
    if not pymupdf_available():
        print("SKIP html title visible photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html title visible photo export -- Pillow missing")
        return
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        sib = d / "pic.png"
        Image.new("RGB", (64, 48), (20, 80, 200)).save(sib, format="PNG")
        html = d / "titled.html"
        html.write_text(
            "<html><head><title>Photo</title></head>"
            '<body><img src="pic.png"></body></html>',
            encoding="utf-8",
        )
        md = d / "titled.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "titled.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "titled.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        svg = (
            '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            "<title>Mark</title>"
            '<rect width="64" height="48" fill="#1460c8"/>'
            "</svg>"
        )
        inline = d / "svg-title.html"
        inline.write_text(f"<html><body>{svg}</body></html>", encoding="utf-8")
        idocx = d / "svg-title.docx"
        res = run_engine(["export-as", str(inline), str(idocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(idocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        text = d / "note.html"
        text.write_text(
            "<html><head><title>Note</title></head>"
            "<body><h1>Hello leftover</h1></body></html>",
            encoding="utf-8",
        )
        tpdf = d / "note.pdf"
        res = run_engine(["export-as", str(text), str(tpdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-story-html", res


def test_html_base_href_photo_export() -> None:
    """Share dest Word/MD/PDF on photo HTML with <base href=images/> embeds that picture."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _html_base_dir" in src
    if not pymupdf_available():
        print("SKIP html base href photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html base href photo export -- Pillow missing")
        return
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        images = d / "images"
        images.mkdir()
        Image.new("RGB", (64, 48), (20, 80, 200)).save(images / "pic.png", format="PNG")
        html = d / "base.html"
        html.write_text(
            '<html><head><base href="images/"></head>'
            '<body><img src="pic.png"></body></html>',
            encoding="utf-8",
        )
        md = d / "base.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "base.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "base.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        remote = d / "remote.html"
        Image.new("RGB", (64, 48), (200, 20, 80)).save(d / "sib.png", format="PNG")
        remote.write_text(
            '<html><head><base href="https://example.com/img/"></head>'
            '<body><img src="sib.png"></body></html>',
            encoding="utf-8",
        )
        rdocx = d / "remote.docx"
        res = run_engine(["export-as", str(remote), str(rdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(rdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        css = images / "page.css"
        css.write_text("div{background-image:url(pic.png)}", encoding="utf-8")
        linked = d / "linked.html"
        linked.write_text(
            '<html><head><base href="images/">'
            '<link rel="stylesheet" href="page.css"></head>'
            "<body><div></div></body></html>",
            encoding="utf-8",
        )
        ldocx = d / "linked.docx"
        res = run_engine(["export-as", str(linked), str(ldocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(ldocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()


def test_html_bom_visible_photo_export() -> None:
    """Share dest Word/MD/PDF on UTF-8 BOM photo HTML still embeds the picture."""
    src = ENGINE.read_text(encoding="utf-8")
    assert '"\\ufeff"' in src or "'\\ufeff'" in src
    if not pymupdf_available():
        print("SKIP html bom visible photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html bom visible photo export -- Pillow missing")
        return
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 80, 200)).save(d / "pic.png", format="PNG")
        html = d / "bom.html"
        html.write_text(
            '\ufeff<html><body><img src="pic.png"></body></html>',
            encoding="utf-8-sig",
        )
        md = d / "bom.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "bom.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "bom.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        text = d / "note.html"
        text.write_text(
            "\ufeff<html><body><h1>Hello leftover</h1></body></html>",
            encoding="utf-8-sig",
        )
        tpdf = d / "note.pdf"
        res = run_engine(["export-as", str(text), str(tpdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-story-html", res


def test_html_utf16_photo_export() -> None:
    """Share dest Word/MD/PDF on Notepad Unicode (UTF-16) photo HTML embeds the picture."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _read_markup" in src
    if not pymupdf_available():
        print("SKIP html utf16 photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html utf16 photo export -- Pillow missing")
        return
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 80, 200)).save(d / "pic.png", format="PNG")
        body = '<html><body><img src="pic.png"></body></html>'
        html = d / "utf16.html"
        html.write_bytes(body.encode("utf-16"))
        md = d / "utf16.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "utf16.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "utf16.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        be = d / "be.html"
        be.write_bytes(b"\xfe\xff" + body.encode("utf-16-be"))
        bdocx = d / "be.docx"
        res = run_engine(["export-as", str(be), str(bdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(bdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        note = d / "note.html"
        note.write_bytes("<html><body><h1>Hello leftover</h1></body></html>".encode("utf-16"))
        tpdf = d / "note.pdf"
        res = run_engine(["export-as", str(note), str(tpdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-story-html", res


def test_html_meta_charset_photo_export() -> None:
    """Share dest Word/MD/PDF on windows-1252 photo HTML with a non-ASCII sibling."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _meta_charset" in src
    if not pymupdf_available():
        print("SKIP html meta charset photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html meta charset photo export -- Pillow missing")
        return
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        pic = d / "été.png"
        Image.new("RGB", (64, 48), (20, 80, 200)).save(pic, format="PNG")
        html = d / "ansi.html"
        html.write_bytes(
            (
                '<html><head><meta charset="windows-1252"></head>'
                '<body><img src="été.png"></body></html>'
            ).encode("windows-1252")
        )
        md = d / "ansi.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "ansi.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "ansi.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        iso = d / "iso.html"
        iso.write_bytes(
            (
                '<html><head><meta http-equiv="Content-Type" '
                'content="text/html; charset=iso-8859-1"></head>'
                '<body><img src="été.png"></body></html>'
            ).encode("iso-8859-1")
        )
        idocx = d / "iso.docx"
        res = run_engine(["export-as", str(iso), str(idocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(idocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()


def test_html_xml_encoding_photo_export() -> None:
    """Share dest Word/MD/PDF on windows-1252 XHTML with xml encoding + non-ASCII sibling."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "_XML_ENCODING" in src
    if not pymupdf_available():
        print("SKIP html xml encoding photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html xml encoding photo export -- Pillow missing")
        return
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        pic = d / "été.png"
        Image.new("RGB", (64, 48), (20, 80, 200)).save(pic, format="PNG")
        html = d / "xhtml.html"
        html.write_bytes(
            (
                '<?xml version="1.0" encoding="windows-1252"?>'
                '<html><body><img src="été.png"></body></html>'
            ).encode("windows-1252")
        )
        md = d / "xhtml.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "xhtml.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "xhtml.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        iso = d / "iso-xml.html"
        iso.write_bytes(
            (
                '<?xml version="1.0" encoding="iso-8859-1"?>'
                '<html><body><img src="été.png"></body></html>'
            ).encode("iso-8859-1")
        )
        idocx = d / "iso-xml.docx"
        res = run_engine(["export-as", str(iso), str(idocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(idocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()


def test_html_cjk_charset_photo_export() -> None:
    """Share dest Word/MD/PDF on gbk / shift_jis photo HTML with a non-ASCII sibling."""
    src = ENGINE.read_text(encoding="utf-8")
    assert '"gbk": "gbk"' in src
    if not pymupdf_available():
        print("SKIP html cjk charset photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html cjk charset photo export -- Pillow missing")
        return
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 80, 200)).save(d / "été.png", format="PNG")
        Image.new("RGB", (64, 48), (200, 80, 20)).save(d / "写真.png", format="PNG")
        gbk = d / "gbk.html"
        gbk.write_bytes(
            (
                '<html><head><meta charset="gbk"></head>'
                '<body><img src="été.png"></body></html>'
            ).encode("gbk")
        )
        md = d / "gbk.md"
        res = run_engine(["to-md", str(gbk), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "gbk.docx"
        res = run_engine(["export-as", str(gbk), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "gbk.pdf"
        res = run_engine(["export-as", str(gbk), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        sjis = d / "sjis.html"
        sjis.write_bytes(
            (
                '<html><head><meta charset="shift_jis"></head>'
                '<body><img src="写真.png"></body></html>'
            ).encode("shift_jis")
        )
        sdocx = d / "sjis.docx"
        res = run_engine(["export-as", str(sjis), str(sdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(sdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        mac = d / "mac.html"
        mac.write_bytes(
            (
                '<html><head><meta charset="macintosh"></head>'
                '<body><img src="été.png"></body></html>'
            ).encode("mac_roman")
        )
        mdocx = d / "mac.docx"
        res = run_engine(["export-as", str(mac), str(mdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(mdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()


def test_html_iana_charset_photo_export() -> None:
    """Share dest Word/MD/PDF on windows-1251 photo HTML with a non-ASCII sibling."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _declared_codec" in src
    if not pymupdf_available():
        print("SKIP html iana charset photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html iana charset photo export -- Pillow missing")
        return
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 80, 200)).save(d / "фото.png", format="PNG")
        html = d / "cp1251.html"
        html.write_bytes(
            (
                '<html><head><meta charset="windows-1251"></head>'
                '<body><img src="фото.png"></body></html>'
            ).encode("cp1251")
        )
        md = d / "cp1251.md"
        res = run_engine(["to-md", str(html), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "html-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "cp1251.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
        pdf = d / "cp1251.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            assert doc[0].get_images()
        finally:
            doc.close()
        Image.new("RGB", (64, 48), (200, 80, 20)).save(d / "사진.png", format="PNG")
        kr = d / "euckr.html"
        kr.write_bytes(
            (
                '<html><head><meta charset="euc-kr"></head>'
                '<body><img src="사진.png"></body></html>'
            ).encode("euc_kr")
        )
        kdocx = d / "euckr.docx"
        res = run_engine(["export-as", str(kr), str(kdocx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(kdocx) as zf:
            assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()


def test_html_curly_src_photo_export() -> None:
    """Share dest Word/MD/PDF on photo HTML whose src uses Word smart quotes."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _straight_quotes" in src
    if not pymupdf_available():
        print("SKIP html curly src photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html curly src photo export -- Pillow missing")
        return
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 80, 200)).save(d / "pic.png", format="PNG")

        def assert_dests(html_path: Path, stem: str) -> None:
            md = d / f"{stem}.md"
            res = run_engine(["to-md", str(html_path), str(md)])
            assert res.get("success"), res
            assert res.get("tool") == "html-md-images", res
            assert "data:image/png;base64," in md.read_text(encoding="utf-8")
            docx = d / f"{stem}.docx"
            res = run_engine(["export-as", str(html_path), str(docx)])
            assert res.get("success"), res
            assert res.get("tool") == "docx-zip-images", res
            with zipfile.ZipFile(docx) as zf:
                assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
            pdf = d / f"{stem}.pdf"
            res = run_engine(["export-as", str(html_path), str(pdf)])
            assert res.get("success"), res
            assert res.get("tool") == "pymupdf-html-images", res
            import pymupdf

            doc = pymupdf.open(pdf)
            try:
                assert doc[0].get_images()
            finally:
                doc.close()

        curly = d / "curly.html"
        curly.write_text(
            "<html><body><img src=\u201cpic.png\u201d></body></html>",
            encoding="utf-8",
        )
        assert_dests(curly, "curly")
        css = d / "css.html"
        css.write_text(
            "<html><body><style>div{background-image:url(\u201cpic.png\u201d)}</style>"
            "<div></div></body></html>",
            encoding="utf-8",
        )
        assert_dests(css, "css")
        srcset = d / "srcset.html"
        srcset.write_text(
            "<html><body><img srcset=\u201cpic.png 1x\u201d></body></html>",
            encoding="utf-8",
        )
        assert_dests(srcset, "srcset")


def test_md_curly_dest_export() -> None:
    """Share dest Word/HTML/PDF on MD images whose dest uses Word smart quotes."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _md_dest" in src
    if not pymupdf_available():
        print("SKIP md curly dest export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP md curly dest export -- Pillow missing")
        return
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 160, 60)).save(d / "pic.png", format="PNG")

        def assert_dests(md_path: Path, stem: str) -> None:
            docx = d / f"{stem}.docx"
            res = run_engine(["export-as", str(md_path), str(docx)])
            assert res.get("success"), res
            assert res.get("tool") == "docx-zip-images", res
            with zipfile.ZipFile(docx) as zf:
                assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
            html = d / f"{stem}.html"
            res = run_engine(["export-as", str(md_path), str(html)])
            assert res.get("success"), res
            body = html.read_text(encoding="utf-8")
            assert "data:image/png;base64," in body
            assert "![" not in body
            pdf = d / f"{stem}.pdf"
            res = run_engine(["export-as", str(md_path), str(pdf)])
            assert res.get("success"), res
            import pymupdf

            doc = pymupdf.open(pdf)
            try:
                assert doc[0].get_images(), doc[0].get_text()
                assert "![" not in doc[0].get_text()
            finally:
                doc.close()

        curly = d / "curly.md"
        curly.write_text("![x](\u201cpic.png\u201d)\n", encoding="utf-8")
        assert_dests(curly, "curly")
        ref = d / "ref.md"
        ref.write_text("![x][id]\n\n[id]: \u201cpic.png\u201d\n", encoding="utf-8")
        assert_dests(ref, "ref")


def test_md_utf16_photo_export() -> None:
    """Share dest Word/HTML/PDF on Notepad Unicode (UTF-16) Markdown embeds the picture."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _read_markup" in src
    if not pymupdf_available():
        print("SKIP md utf16 photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP md utf16 photo export -- Pillow missing")
        return
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 160, 60)).save(d / "pic.png", format="PNG")
        body = "![](pic.png)\n"

        def assert_dests(md_path: Path, stem: str) -> None:
            docx = d / f"{stem}.docx"
            res = run_engine(["export-as", str(md_path), str(docx)])
            assert res.get("success"), res
            assert res.get("tool") == "docx-zip-images", res
            with zipfile.ZipFile(docx) as zf:
                assert any(n.startswith("word/media/") for n in zf.namelist()), zf.namelist()
            html = d / f"{stem}.html"
            res = run_engine(["export-as", str(md_path), str(html)])
            assert res.get("success"), res
            assert "data:image/png;base64," in html.read_text(encoding="utf-8")
            pdf = d / f"{stem}.pdf"
            res = run_engine(["export-as", str(md_path), str(pdf)])
            assert res.get("success"), res
            import pymupdf

            doc = pymupdf.open(pdf)
            try:
                assert doc[0].get_images(), doc[0].get_text()
            finally:
                doc.close()

        utf16 = d / "utf16.md"
        utf16.write_bytes(body.encode("utf-16"))
        assert_dests(utf16, "utf16")
        bom = d / "bom.md"
        bom.write_bytes(b"\xef\xbb\xbf" + body.encode("utf-8"))
        assert_dests(bom, "bom")
        note = d / "note.txt"
        note.write_bytes("Hello leftover\n".encode("utf-16"))
        tpdf = d / "note.pdf"
        res = run_engine(["export-as", str(note), str(tpdf)])
        assert res.get("success"), res
        if pymupdf_available():
            import pymupdf

            doc = pymupdf.open(tpdf)
            try:
                assert "Hello leftover" in doc[0].get_text()
            finally:
                doc.close()


def test_export_as_svg() -> None:
    """Share Export as PDF/JPG on a previewed SVG. Engine used to need markitdown / cannot export."""
    src = ENGINE.read_text(encoding="utf-8")
    assert '".svg"' in src
    assert "convert_to_pdf" in src
    parser = (ROOT / "src/NetieSpace/Services/ContentParserService.cs").read_text(encoding="utf-8")
    assert 'extension == ".svg"' in parser
    if not pymupdf_available():
        print("SKIP svg export live -- pymupdf not importable")
        return
    svg = (
        '<?xml version="1.0"?>\n'
        '<svg xmlns="http://www.w3.org/2000/svg" width="120" height="80">'
        '<rect width="120" height="80" fill="#1a2030"/>'
        '<text x="12" y="46" fill="#9fd4c8" font-size="18">Netie</text>'
        "</svg>\n"
    )
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        mark = d / "mark.svg"
        mark.write_text(svg, encoding="utf-8")
        pdf = d / "mark.pdf"
        res = run_engine(["export-as", str(mark), str(pdf)])
        assert res.get("success"), res
        assert pdf.is_file() and pdf.stat().st_size > 200
        jpg = d / "mark.jpg"
        res = run_engine(["export-as", str(mark), str(jpg)])
        assert res.get("success"), res
        assert jpg.is_file() and jpg.stat().st_size > 200


def test_svg_image_href_export() -> None:
    """Share dest PNG/PDF on SVG whose picture is a sibling <image href>."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _inline_svg_sibling_images" in src
    if not pymupdf_available():
        print("SKIP svg image href export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP svg image href export -- Pillow missing")
        return

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 180, 80)).save(d / "pic.png", format="PNG")
        svg = d / "mark.svg"
        svg.write_text(
            '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<image href="pic.png" width="64" height="48"/>'
            "</svg>",
            encoding="utf-8",
        )
        png = d / "mark.png"
        res = run_engine(["export-as", str(svg), str(png)])
        assert res.get("success"), res
        im = Image.open(png)
        try:
            assert im.size == (64, 48)
            # Was a white page (MuPDF ignores external image).
            assert im.convert("RGB").getpixel((8, 8))[1] >= 160
        finally:
            im.close()
        pdf = d / "mark.pdf"
        res = run_engine(["export-as", str(svg), str(pdf)])
        assert res.get("success"), res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            pix = doc[0].get_pixmap()
            # Solid sibling green, not a white page (MuPDF used to drop href).
            assert pix.samples[1] >= 160 and pix.samples[0] < 40
        finally:
            doc.close()
        html = d / "mark.html"
        html.write_text(
            '<html><body><svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<image href="pic.png" width="64" height="48"/></svg></body></html>',
            encoding="utf-8",
        )
        docx = d / "mark.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        import zipfile

        with zipfile.ZipFile(docx) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            blob = zf.read(media[0])
        import io

        im = Image.open(io.BytesIO(blob))
        try:
            assert im.convert("RGB").getpixel((8, 8))[1] >= 160
        finally:
            im.close()


def test_svg_use_href_export() -> None:
    """Share dest PNG/PDF on SVG whose picture is a sibling <use href='icon.svg'>."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "<use\\b" in src or "repl_use" in src
    if not pymupdf_available():
        print("SKIP svg use href export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP svg use href export -- Pillow missing")
        return

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        (d / "icon.svg").write_text(
            '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<rect width="64" height="48" fill="#14b450"/>'
            "</svg>",
            encoding="utf-8",
        )
        svg = d / "mark.svg"
        svg.write_text(
            '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<use href="icon.svg"/>'
            "</svg>",
            encoding="utf-8",
        )
        png = d / "mark.png"
        res = run_engine(["export-as", str(svg), str(png)])
        assert res.get("success"), res
        im = Image.open(png)
        try:
            px = im.convert("RGB").getpixel((8, 8))
            assert px[1] >= 160 and px[0] < 40, px
        finally:
            im.close()
        (d / "sprite.svg").write_text(
            '<svg xmlns="http://www.w3.org/2000/svg">'
            '<g id="icon"><rect width="64" height="48" fill="#14b450"/></g>'
            "</svg>",
            encoding="utf-8",
        )
        frag = d / "frag.svg"
        frag.write_text(
            '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<use href="sprite.svg#icon"/>'
            "</svg>",
            encoding="utf-8",
        )
        fpng = d / "frag.png"
        res = run_engine(["export-as", str(frag), str(fpng)])
        assert res.get("success"), res
        im = Image.open(fpng)
        try:
            px = im.convert("RGB").getpixel((8, 8))
            assert px[1] >= 160 and px[0] < 40, px
        finally:
            im.close()
        pdf = d / "mark.pdf"
        res = run_engine(["export-as", str(svg), str(pdf)])
        assert res.get("success"), res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            pix = doc[0].get_pixmap()
            assert pix.samples[1] >= 160 and pix.samples[0] < 40
        finally:
            doc.close()
        local = d / "local.svg"
        local.write_text(
            '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<g id="icon"><rect width="64" height="48" fill="#14b450"/></g>'
            '<use href="#icon"/>'
            "</svg>",
            encoding="utf-8",
        )
        lpng = d / "local.png"
        res = run_engine(["export-as", str(local), str(lpng)])
        assert res.get("success"), res
        im = Image.open(lpng)
        try:
            px = im.convert("RGB").getpixel((8, 8))
            assert px[1] >= 160 and px[0] < 40, px
        finally:
            im.close()


def test_svg_prefixed_href_export() -> None:
    """Share dest PNG/Word on SVG whose picture is <svg:image href> / <svg:use href>."""
    src = ENGINE.read_text(encoding="utf-8")
    assert r"(?:[\w.-]+:)?image\b" in src
    if not pymupdf_available():
        print("SKIP svg prefixed href export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP svg prefixed href export -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 180, 80)).save(d / "pic.png", format="PNG")
        (d / "icon.svg").write_text(
            '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<rect width="64" height="48" fill="#14b450"/>'
            "</svg>",
            encoding="utf-8",
        )
        svg = d / "mark.svg"
        svg.write_text(
            '<svg xmlns="http://www.w3.org/2000/svg" xmlns:svg="http://www.w3.org/2000/svg" '
            'width="64" height="48">'
            '<svg:image href="pic.png" width="64" height="48"/>'
            "</svg>",
            encoding="utf-8",
        )
        png = d / "mark.png"
        res = run_engine(["export-as", str(svg), str(png)])
        assert res.get("success"), res
        im = Image.open(png)
        try:
            px = im.convert("RGB").getpixel((8, 8))
            assert px[1] >= 160 and px[0] < 40, px
        finally:
            im.close()
        use = d / "use.svg"
        use.write_text(
            '<svg xmlns="http://www.w3.org/2000/svg" xmlns:svg="http://www.w3.org/2000/svg" '
            'width="64" height="48">'
            '<svg:use href="icon.svg"/>'
            "</svg>",
            encoding="utf-8",
        )
        upng = d / "use.png"
        res = run_engine(["export-as", str(use), str(upng)])
        assert res.get("success"), res
        im = Image.open(upng)
        try:
            px = im.convert("RGB").getpixel((8, 8))
            assert px[1] >= 160 and px[0] < 40, px
        finally:
            im.close()
        html = d / "mark.html"
        html.write_text(
            '<html><body>'
            '<svg xmlns="http://www.w3.org/2000/svg" xmlns:svg="http://www.w3.org/2000/svg" '
            'width="64" height="48">'
            '<svg:image href="pic.png" width="64" height="48"/>'
            "</svg></body></html>",
            encoding="utf-8",
        )
        docx = d / "mark.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()


def test_svg_close_tag_export() -> None:
    """Share dest PNG/Word on SVG whose <image> / HTML <svg:svg> uses a close tag."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "</(?:[\\w.-]+:)?image" in src or "image\\s*>)?" in src
    if not pymupdf_available():
        print("SKIP svg close tag export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP svg close tag export -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 180, 80)).save(d / "pic.png", format="PNG")
        svg = d / "mark.svg"
        svg.write_text(
            '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<image href="pic.png" width="64" height="48"></image>'
            "</svg>",
            encoding="utf-8",
        )
        png = d / "mark.png"
        res = run_engine(["export-as", str(svg), str(png)])
        assert res.get("success"), res
        im = Image.open(png)
        try:
            px = im.convert("RGB").getpixel((8, 8))
            assert px[1] >= 160 and px[0] < 40, px
        finally:
            im.close()
        html = d / "mark.html"
        html.write_text(
            '<html><body>'
            '<svg:svg xmlns:svg="http://www.w3.org/2000/svg" width="64" height="48">'
            '<svg:image href="pic.png" width="64" height="48"></svg:image>'
            "</svg:svg></body></html>",
            encoding="utf-8",
        )
        docx = d / "mark.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()


def test_svg_upper_tag_export() -> None:
    """Share dest PNG/Word on Word-style uppercase <SVG> / <IMAGE> photo markup."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _svg_lower_root" in src
    if not pymupdf_available():
        print("SKIP svg upper tag export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP svg upper tag export -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 180, 80)).save(d / "pic.png", format="PNG")
        svg = d / "mark.svg"
        svg.write_text(
            '<SVG xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<IMAGE href="pic.png" width="64" height="48"/>'
            "</SVG>",
            encoding="utf-8",
        )
        png = d / "mark.png"
        res = run_engine(["export-as", str(svg), str(png)])
        assert res.get("success"), res
        im = Image.open(png)
        try:
            px = im.convert("RGB").getpixel((8, 8))
            assert px[1] >= 160 and px[0] < 40, px
        finally:
            im.close()
        html = d / "mark.html"
        html.write_text(
            '<HTML><BODY>'
            '<SVG xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<IMAGE href="pic.png" width="64" height="48"/>'
            "</SVG></BODY></HTML>",
            encoding="utf-8",
        )
        docx = d / "mark.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()


def test_svg_preamble_size_utf16_export() -> None:
    """Share dest PNG/Word on long-comment SVG, unsized <image>, UTF-16 sibling SVG."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _image_rest_with_size" in src
    if not pymupdf_available():
        print("SKIP svg preamble/size/utf16 export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP svg preamble/size/utf16 export -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 180, 80)).save(d / "pic.png", format="PNG")

        pre = d / "preamble.svg"
        pre.write_text(
            "<?xml version='1.0' encoding='UTF-8'?>\n"
            "<!-- " + ("x" * 400) + " -->\n"
            '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<image href="pic.png" width="64" height="48"/></svg>',
            encoding="utf-8",
        )
        png = d / "preamble.png"
        res = run_engine(["export-as", str(pre), str(png)])
        assert res.get("success"), res
        im = Image.open(png)
        try:
            px = im.convert("RGB").getpixel((8, 8))
            assert px[1] >= 160 and px[0] < 40, px
        finally:
            im.close()

        nosz = d / "nosz.svg"
        nosz.write_text(
            '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<image href="pic.png"/></svg>',
            encoding="utf-8",
        )
        dest = d / "nosz.png"
        res = run_engine(["export-as", str(nosz), str(dest)])
        assert res.get("success"), res
        im = Image.open(dest)
        try:
            px = im.convert("RGB").getpixel((8, 8))
            assert px[1] >= 160 and px[0] < 40, px
        finally:
            im.close()
        html = d / "nosz.html"
        html.write_text(
            '<html><body><svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<image href="pic.png"/></svg></body></html>',
            encoding="utf-8",
        )
        docx = d / "nosz.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()

        u16 = d / "u16.svg"
        u16.write_text(
            '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<rect width="64" height="48" fill="#14b450"/></svg>',
            encoding="utf-16",
        )
        html2 = d / "u16.html"
        html2.write_text('<html><body><img src="u16.svg"></body></html>', encoding="utf-8")
        docx2 = d / "u16.docx"
        res = run_engine(["export-as", str(html2), str(docx2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx2) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()


def test_svg_switch_a_export() -> None:
    """Share dest PNG/Word on SVG whose picture is <switch>/<a> around <image href>."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "MuPDF skips <image> inside <a> / <switch>" in src
    if not pymupdf_available():
        print("SKIP svg switch/a export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP svg switch/a export -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 180, 80)).save(d / "pic.png", format="PNG")
        for name, markup in (
            (
                "sw.svg",
                '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
                '<switch><image href="pic.png" width="64" height="48"/></switch></svg>',
            ),
            (
                "a.svg",
                '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
                '<a href="#"><image href="pic.png" width="64" height="48"/></a></svg>',
            ),
        ):
            svg = d / name
            svg.write_text(markup, encoding="utf-8")
            png = d / (name + ".png")
            res = run_engine(["export-as", str(svg), str(png)])
            assert res.get("success"), (name, res)
            im = Image.open(png)
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, (name, px)
            finally:
                im.close()
        html = d / "mark.html"
        html.write_text(
            '<html><body><svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<a href="#"><switch><image href="pic.png" width="64" height="48"/>'
            "</switch></a></svg></body></html>",
            encoding="utf-8",
        )
        docx = d / "mark.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()


def test_html_svg_desc_title_export() -> None:
    """Share dest Word/MD/PDF on photo HTML whose inline SVG has <desc> / <metadata> / <svg:title>."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "SVG <desc> / <metadata> / <svg:title> are not page copy" in src
    if not pymupdf_available():
        print("SKIP html svg desc/title export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html svg desc/title export -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 180, 80)).save(d / "pic.png", format="PNG")
        for name, markup in (
            (
                "desc.html",
                '<html><body><svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
                "<desc>A photo</desc>"
                '<image href="pic.png" width="64" height="48"/></svg></body></html>',
            ),
            (
                "meta.html",
                '<html><body><svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
                "<metadata>Created in Inkscape</metadata>"
                '<image href="pic.png" width="64" height="48"/></svg></body></html>',
            ),
            (
                "title.html",
                '<html><body><svg:svg xmlns:svg="http://www.w3.org/2000/svg" width="64" height="48">'
                "<svg:title>Logo</svg:title>"
                '<svg:image href="pic.png" width="64" height="48"/></svg:svg></body></html>',
            ),
        ):
            html = d / name
            html.write_text(markup, encoding="utf-8")
            docx = d / (name + ".docx")
            res = run_engine(["export-as", str(html), str(docx)])
            assert res.get("success"), (name, res)
            assert res.get("tool") == "docx-zip-images", (name, res)
            with zipfile.ZipFile(docx) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (name, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (name, px)
                finally:
                    im.close()
            md = d / (name + ".md")
            res = run_engine(["export-as", str(html), str(md)])
            assert res.get("success"), (name, res)
            assert res.get("tool") == "html-md-images", (name, res)
            assert "data:image" in md.read_text(encoding="utf-8")


def test_html_nbsp_template_photo_export() -> None:
    """Share dest Word/MD on photo HTML whose leftover is &nbsp; / <template> / <noscript> / Inkscape RDF."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "Word HTML <p>&nbsp;</p>" in src
    assert "<template>" in src and "<noscript>" in src
    if not pymupdf_available():
        print("SKIP html nbsp/template photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html nbsp/template photo export -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 180, 80)).save(d / "pic.png", format="PNG")
        for name, markup in (
            (
                "nbsp.html",
                '<html><body><p>&nbsp;</p><img src="pic.png"></body></html>',
            ),
            (
                "num.html",
                '<html><body><p>&#160;</p><img src="pic.png"></body></html>',
            ),
            (
                "tpl.html",
                '<html><body><template>Photo</template><img src="pic.png"></body></html>',
            ),
            (
                "ns.html",
                '<html><body><noscript>Enable JS</noscript><img src="pic.png"></body></html>',
            ),
            (
                "ink.html",
                '<html><body><svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
                "<inkscape:label>Photo</inkscape:label>"
                '<image href="pic.png" width="64" height="48"/></svg></body></html>',
            ),
            (
                "rdf.html",
                '<html><body><svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
                '<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">'
                "<rdf:Description>Photo</rdf:Description></rdf:RDF>"
                '<image href="pic.png" width="64" height="48"/></svg></body></html>',
            ),
        ):
            html = d / name
            html.write_text(markup, encoding="utf-8")
            docx = d / (name + ".docx")
            res = run_engine(["export-as", str(html), str(docx)])
            assert res.get("success"), (name, res)
            assert res.get("tool") == "docx-zip-images", (name, res)
            with zipfile.ZipFile(docx) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (name, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (name, px)
                finally:
                    im.close()
            md = d / (name + ".md")
            res = run_engine(["export-as", str(html), str(md)])
            assert res.get("success"), (name, res)
            assert res.get("tool") == "html-md-images", (name, res)
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><p>Hello&nbsp;world</p><img src="pic.png"></body></html>',
            encoding="utf-8",
        )
        dest = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_html_shy_comment_photo_export() -> None:
    """Share dest Word/MD on photo HTML whose leftover is &shy; or an IE/Word comment."""
    src = ENGINE.read_text(encoding="utf-8")
    assert r"(?s)<!--.*?-->" in src
    assert 'unicodedata.category(ch) != "Cf"' in src
    if not pymupdf_available():
        print("SKIP html shy/comment photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html shy/comment photo export -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 180, 80)).save(d / "pic.png", format="PNG")
        for name, markup in (
            (
                "shy.html",
                '<html><body><p>&shy;</p><img src="pic.png"></body></html>',
            ),
            (
                "ie.html",
                '<!--[if IE]><p>Photo</p><![endif]-->'
                '<html><body><img src="pic.png"></body></html>',
            ),
            (
                "mso.html",
                '<!--[if gte mso 9]><xml>Photo</xml><![endif]-->'
                '<html><body><img src="pic.png"></body></html>',
            ),
        ):
            html = d / name
            html.write_text(markup, encoding="utf-8")
            docx = d / (name + ".docx")
            res = run_engine(["export-as", str(html), str(docx)])
            assert res.get("success"), (name, res)
            assert res.get("tool") == "docx-zip-images", (name, res)
            with zipfile.ZipFile(docx) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (name, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (name, px)
                finally:
                    im.close()
            md = d / (name + ".md")
            res = run_engine(["export-as", str(html), str(md)])
            assert res.get("success"), (name, res)
            assert res.get("tool") == "html-md-images", (name, res)
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><p>Hel&shy;lo</p><img src="pic.png"></body></html>',
            encoding="utf-8",
        )
        dest = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_html_amp_nbsp_md_photo_export() -> None:
    """Share dest Word/MD on photo HTML &amp;nbsp; and photo MD leftover &nbsp; / comment."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "One unescape left" in src
    assert "Naive tag strip left &nbsp;" in src
    if not pymupdf_available():
        print("SKIP html amp-nbsp/md photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html amp-nbsp/md photo export -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 180, 80)).save(d / "pic.png", format="PNG")
        html = d / "amp.html"
        html.write_text(
            '<html><body><p>&amp;nbsp;</p><img src="pic.png"></body></html>',
            encoding="utf-8",
        )
        docx = d / "amp.docx"
        res = run_engine(["export-as", str(html), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        for name, markup in (
            ("nbsp.md", "&nbsp;\n\n![](pic.png)\n"),
            ("cmt.md", "<!-- Photo -->\n\n![](pic.png)\n"),
            ("shy.md", "&shy;\n\n![](pic.png)\n"),
        ):
            md = d / name
            md.write_text(markup, encoding="utf-8")
            dest = d / (name + ".docx")
            res = run_engine(["export-as", str(md), str(dest)])
            assert res.get("success"), (name, res)
            assert res.get("tool") == "docx-zip-images", (name, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (name, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (name, px)
                finally:
                    im.close()
        mixed = d / "mixed.md"
        mixed.write_text("Hello\n\n![](pic.png)\n", encoding="utf-8")
        dest = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_office_svg_media_export() -> None:
    """Share dest PDF/Word on photo ODT/DOCX whose zip picture is icon.svg."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "Pillow cannot open SVG. Photo ODT dest PDF raised" in src
    if not pymupdf_available():
        print("SKIP office svg media export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP office svg media export -- Pillow missing")
        return
    import zipfile
    import io

    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
        '<rect width="64" height="48" fill="#14b450"/></svg>'
    ).encode("utf-8")

    def write_odt(path: Path) -> None:
        with zipfile.ZipFile(path, "w") as zf:
            zf.writestr("mimetype", "application/vnd.oasis.opendocument.text")
            zf.writestr(
                "content.xml",
                '<?xml version="1.0"?>'
                '<office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"'
                ' xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">'
                "<office:body><office:text></office:text></office:body>"
                "</office:document-content>",
            )
            zf.writestr(
                "META-INF/manifest.xml",
                '<?xml version="1.0"?><manifest:manifest '
                'xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0"/>',
            )
            zf.writestr("Pictures/icon.svg", svg)

    def write_docx(path: Path) -> None:
        with zipfile.ZipFile(path, "w") as zf:
            zf.writestr(
                "[Content_Types].xml",
                '<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
                '<Default Extension="xml" ContentType="application/xml"/>'
                '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'
                "</Types>",
            )
            zf.writestr(
                "_rels/.rels",
                '<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>'
                "</Relationships>",
            )
            zf.writestr(
                "word/document.xml",
                '<?xml version="1.0"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'
                "<w:body><w:p/></w:body></w:document>",
            )
            zf.writestr(
                "word/_rels/document.xml.rels",
                '<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>',
            )
            zf.writestr("word/media/icon.svg", svg)

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        odt = d / "photo.odt"
        write_odt(odt)
        pdf = d / "photo.pdf"
        res = run_engine(["export-as", str(odt), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-office-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            pix = doc[0].get_pixmap()
            assert pix.samples[1] >= 160 and pix.samples[0] < 40
        finally:
            doc.close()
        dest = d / "photo.docx"
        res = run_engine(["export-as", str(odt), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        docx = d / "in.docx"
        write_docx(docx)
        pdf2 = d / "in.pdf"
        res = run_engine(["export-as", str(docx), str(pdf2)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-office-images", res
        doc = pymupdf.open(pdf2)
        try:
            pix = doc[0].get_pixmap()
            assert pix.samples[1] >= 160 and pix.samples[0] < 40
        finally:
            doc.close()


def test_rtf_pict_photo_export() -> None:
    """Share dest Word/PDF on WordPad photo RTF whose picture is \\pngblip hex."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def rtf_media_pngs" in src
    if not pymupdf_available():
        print("SKIP rtf pict photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP rtf pict photo export -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        rtf = d / "photo.rtf"
        rtf.write_text(
            r"{\rtf1\ansi{\pict\pngblip\picw64\pich48 " + png.hex() + "}}",
            encoding="ascii",
        )
        dest = d / "photo.docx"
        res = run_engine(["export-as", str(rtf), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        pdf = d / "photo.pdf"
        res = run_engine(["export-as", str(rtf), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-rtf-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            pix = doc[0].get_pixmap()
            assert pix.samples[1] >= 160 and pix.samples[0] < 40
        finally:
            doc.close()
        mixed = d / "mixed.rtf"
        mixed.write_text(
            r"{\rtf1\ansi Hello{\pict\pngblip " + png.hex() + "}}",
            encoding="ascii",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_rtf_fonttbl_photo_export() -> None:
    """Share dest Word/PDF on WordPad photo RTF whose leftover is fonttbl / info."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _rtf_strip_dest_groups" in src
    if not pymupdf_available():
        print("SKIP rtf fonttbl -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP rtf fonttbl -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        rtf = d / "photo.rtf"
        rtf.write_text(
            r"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset0 Calibri;}}"
            r"{\colortbl;\red0\green0\blue0;}"
            r"{\info{\title Photo}{\author Photo}}"
            r"{\pict\pngblip\picw64\pich48 " + png.hex() + "}}",
            encoding="ascii",
        )
        dest = d / "photo.docx"
        res = run_engine(["export-as", str(rtf), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        pdf = d / "photo.pdf"
        res = run_engine(["export-as", str(rtf), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-rtf-images", res
        import pymupdf
        doc = pymupdf.open(pdf)
        try:
            pix = doc[0].get_pixmap()
            assert pix.samples[1] >= 160 and pix.samples[0] < 40
        finally:
            doc.close()
        mixed = d / "mixed.rtf"
        mixed.write_text(
            r"{\rtf1\ansi{\fonttbl{\f0 Arial;}} Hello{\pict\pngblip "
            + png.hex()
            + "}}",
            encoding="ascii",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_rtf_dest_group_photo_export() -> None:
    """Share dest Word/PDF on Word photo RTF whose leftover is nested {\\*listtable}."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _rtf_strip_dest_groups" in src
    assert "Nested braces used to leak" in src
    if not pymupdf_available():
        print("SKIP rtf dest group -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP rtf dest group -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        rtf = d / "photo.rtf"
        rtf.write_text(
            r"{\rtf1\ansi{\*\listtable{\list\listid1 Photo}}"
            r"{\*\latentstyles{\lsdstimax267}}"
            r"{\*\fonttbl{\f0\fnil{\*\panose 020b0604020202020204}Calibri;}}"
            r"{\info{\*\userprops{\propname Photo}{\staticval X}}}"
            r"{\pict\pngblip\picw64\pich48 " + png.hex() + "}}",
            encoding="ascii",
        )
        dest = d / "photo.docx"
        res = run_engine(["export-as", str(rtf), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        pdf = d / "photo.pdf"
        res = run_engine(["export-as", str(rtf), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-rtf-images", res
        import pymupdf
        doc = pymupdf.open(pdf)
        try:
            pix = doc[0].get_pixmap()
            assert pix.samples[1] >= 160 and pix.samples[0] < 40
        finally:
            doc.close()
        mixed = d / "mixed.rtf"
        mixed.write_text(
            r"{\rtf1\ansi{\*\listtable{\list Photo}} Hello{\pict\pngblip "
            + png.hex()
            + "}}",
            encoding="ascii",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_rtf_filetbl_photo_export() -> None:
    """Share dest Word/PDF on photo RTF whose leftover is unstarred filetbl / listtable."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "fonttbl|colortbl|stylesheet|info|filetbl|" in src
    assert "listoverridetable|listtable|revtbl|rsidtbl|xmlnstbl|generator" in src
    assert "Unstarred \\\\filetbl /" in src or "Unstarred \\filetbl /" in src
    if not pymupdf_available():
        print("SKIP rtf filetbl -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP rtf filetbl -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        hexpng = png.hex()
        for leftover in (
            r"{\filetbl{\f0 Photo}}",
            r"{\listtable{\list Photo}}",
            r"{\listoverridetable{Photo}}",
            r"{\generator Photo;}",
        ):
            rtf = d / "photo.rtf"
            rtf.write_text(
                r"{\rtf1\ansi" + leftover + r"{\pict\pngblip\picw64\pich48 " + hexpng + "}}",
                encoding="ascii",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(rtf), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(rtf), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-rtf-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                assert pix.samples[1] >= 160 and pix.samples[0] < 40, leftover
            finally:
                doc.close()
        mixed = d / "mixed.rtf"
        mixed.write_text(
            r"{\rtf1\ansi{\filetbl{\f0 Photo}} Hello{\pict\pngblip " + hexpng + "}}",
            encoding="ascii",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res
        header = d / "header.rtf"
        header.write_text(
            r"{\rtf1\ansi{\header Hello}{\pict\pngblip " + hexpng + "}}",
            encoding="ascii",
        )
        dest3 = d / "header.docx"
        res = run_engine(["export-as", str(header), str(dest3)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_rtf_themedata_photo_export() -> None:
    """Share dest Word/PDF on photo RTF whose leftover is unstarred themedata / datastore."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "themedata|colorschememapping|passwordhash|datastore|mmathPr|" in src
    assert "Unstarred \\\\themedata /" in src or "Unstarred \\themedata /" in src
    if not pymupdf_available():
        print("SKIP rtf themedata -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP rtf themedata -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        hexpng = png.hex()
        for leftover in (
            r"{\themedata Photo}",
            r"{\datastore Photo}",
            r"{\latentstyles Photo}",
            r"{\colorschememapping Photo}",
        ):
            rtf = d / "photo.rtf"
            rtf.write_text(
                r"{\rtf1\ansi" + leftover + r"{\pict\pngblip\picw64\pich48 " + hexpng + "}}",
                encoding="ascii",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(rtf), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(rtf), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-rtf-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                assert pix.samples[1] >= 160 and pix.samples[0] < 40, leftover
            finally:
                doc.close()
        mixed = d / "mixed.rtf"
        mixed.write_text(
            r"{\rtf1\ansi{\themedata Photo} Hello{\pict\pngblip " + hexpng + "}}",
            encoding="ascii",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_rtf_nonshppict_photo_export() -> None:
    """Share dest Word/PDF on photo RTF whose leftover is unstarred nonshppict / shppict."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "wgrffmtfilter|nonshppict|shppict|pnseclvl" in src
    assert "Unstarred \\\\nonshppict /" in src or "Unstarred \\nonshppict /" in src
    assert "Do not strip \\\\upr (fallback is page copy)" in src or "Do not strip \\upr (fallback is page copy)" in src
    if not pymupdf_available():
        print("SKIP rtf nonshppict -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP rtf nonshppict -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        hexpng = png.hex()
        for leftover in (
            r"{\nonshppict Photo}",
            r"{\shppict Photo}",
            r"{\pnseclvl Photo}",
        ):
            rtf = d / "photo.rtf"
            rtf.write_text(
                r"{\rtf1\ansi" + leftover + r"{\pict\pngblip\picw64\pich48 " + hexpng + "}}",
                encoding="ascii",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(rtf), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(rtf), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-rtf-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                assert pix.samples[1] >= 160 and pix.samples[0] < 40, leftover
            finally:
                doc.close()
        mixed = d / "mixed.rtf"
        mixed.write_text(
            r"{\rtf1\ansi{\nonshppict Photo} Hello{\pict\pngblip " + hexpng + "}}",
            encoding="ascii",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res
        upr = d / "upr.rtf"
        upr.write_text(
            r"{\rtf1\ansi{\upr Hello}{\pict\pngblip " + hexpng + "}}",
            encoding="ascii",
        )
        dest3 = d / "upr.docx"
        res = run_engine(["export-as", str(upr), str(dest3)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_rtf_annotation_photo_export() -> None:
    """Share dest Word/PDF on photo RTF whose leftover is unstarred annotation."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "annotation|atnauthor|atrfstart|atrfend|atnid" in src
    assert "Unstarred \\\\annotation /" in src or "Unstarred \\annotation /" in src
    assert "Do not strip \\\\upr (fallback is page copy)" in src or "Do not strip \\upr (fallback is page copy)" in src
    if not pymupdf_available():
        print("SKIP rtf annotation -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP rtf annotation -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        hexpng = png.hex()
        for leftover in (
            r"{\annotation Photo}",
            r"{\atnauthor Photo}",
            r"{\atrfstart Photo}",
        ):
            rtf = d / "photo.rtf"
            rtf.write_text(
                r"{\rtf1\ansi" + leftover + r"{\pict\pngblip\picw64\pich48 " + hexpng + "}}",
                encoding="ascii",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(rtf), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(rtf), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-rtf-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                assert pix.samples[1] >= 160 and pix.samples[0] < 40, leftover
            finally:
                doc.close()
        mixed = d / "mixed.rtf"
        mixed.write_text(
            r"{\rtf1\ansi{\annotation Photo} Hello{\pict\pngblip " + hexpng + "}}",
            encoding="ascii",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res
        header = d / "header.rtf"
        header.write_text(
            r"{\rtf1\ansi{\header Hello}{\pict\pngblip " + hexpng + "}}",
            encoding="ascii",
        )
        dest3 = d / "header.docx"
        res = run_engine(["export-as", str(header), str(dest3)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_rtf_fldinst_photo_export() -> None:
    """Share dest Word/PDF on photo RTF whose leftover is unstarred fldinst / datafield."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "fldinst|datafield" in src
    assert "Unstarred \\\\fldinst /" in src or "Unstarred \\fldinst /" in src
    assert "Do not strip \\\\fldrslt (field result is page copy)" in src or "Do not strip \\fldrslt (field result is page copy)" in src
    assert "Do not strip \\\\upr (fallback is page copy)" in src or "Do not strip \\upr (fallback is page copy)" in src
    if not pymupdf_available():
        print("SKIP rtf fldinst -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP rtf fldinst -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        hexpng = png.hex()
        for leftover in (
            r"{\fldinst Photo}",
            r"{\datafield Photo}",
        ):
            rtf = d / "photo.rtf"
            rtf.write_text(
                r"{\rtf1\ansi" + leftover + r"{\pict\pngblip\picw64\pich48 " + hexpng + "}}",
                encoding="ascii",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(rtf), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(rtf), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-rtf-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                assert pix.samples[1] >= 160 and pix.samples[0] < 40, leftover
            finally:
                doc.close()
        mixed = d / "mixed.rtf"
        mixed.write_text(
            r"{\rtf1\ansi{\fldinst Photo} Hello{\pict\pngblip " + hexpng + "}}",
            encoding="ascii",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res
        result = d / "result.rtf"
        result.write_text(
            r"{\rtf1\ansi{\fldrslt Hello}{\pict\pngblip " + hexpng + "}}",
            encoding="ascii",
        )
        dest3 = d / "result.docx"
        res = run_engine(["export-as", str(result), str(dest3)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_odf_object_replacements_export() -> None:
    """Share dest PDF/Word on photo ODT whose zip picture is ObjectReplacements/."""
    src = ENGINE.read_text(encoding="utf-8")
    assert '"ObjectReplacements/"' in src
    if not pymupdf_available():
        print("SKIP odf object replacements -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP odf object replacements -- Pillow missing")
        return
    import zipfile
    import io

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
    png = buf.getvalue()

    def write_odf(path: Path, mime: str, extra_xml: str = "") -> None:
        with zipfile.ZipFile(path, "w") as zf:
            zf.writestr("mimetype", mime)
            zf.writestr(
                "content.xml",
                '<?xml version="1.0"?>'
                '<office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"'
                ' xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">'
                f"<office:body><office:text>{extra_xml}</office:text></office:body>"
                "</office:document-content>",
            )
            zf.writestr(
                "META-INF/manifest.xml",
                '<?xml version="1.0"?><manifest:manifest '
                'xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0"/>',
            )
            zf.writestr("ObjectReplacements/Object 1", png)

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        odt = d / "photo.odt"
        write_odf(odt, "application/vnd.oasis.opendocument.text")
        pdf = d / "photo.pdf"
        res = run_engine(["export-as", str(odt), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-office-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            pix = doc[0].get_pixmap()
            assert pix.samples[1] >= 160 and pix.samples[0] < 40
        finally:
            doc.close()
        dest = d / "photo.docx"
        res = run_engine(["export-as", str(odt), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        ods = d / "photo.ods"
        write_odf(ods, "application/vnd.oasis.opendocument.spreadsheet")
        pdf2 = d / "sheet.pdf"
        res = run_engine(["export-as", str(ods), str(pdf2)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-office-images", res
        odp = d / "photo.odp"
        write_odf(odp, "application/vnd.oasis.opendocument.presentation")
        dest3 = d / "photo.pptx"
        res = run_engine(["export-as", str(odp), str(dest3)])
        assert res.get("success"), res
        assert res.get("tool") == "pptx-zip-images", res
        mixed = d / "mixed.odt"
        write_odf(
            mixed,
            "application/vnd.oasis.opendocument.text",
            extra_xml="<text:p>Hello</text:p>",
        )
        dest4 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest4)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_md_image_photo_pdf() -> None:
    """Share dest PDF on photo Markdown whose only picture is ![](sib.png)."""
    src = ENGINE.read_text(encoding="utf-8")
    assert 'tool="pymupdf-md-images"' in src
    if not pymupdf_available():
        print("SKIP md image photo pdf -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP md image photo pdf -- Pillow missing")
        return
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        (d / "sib.png").write_bytes(buf.getvalue())
        md = d / "photo.md"
        md.write_text("![](sib.png)\n", encoding="utf-8")
        pdf = d / "photo.pdf"
        res = run_engine(["export-as", str(md), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-md-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            pix = doc[0].get_pixmap()
            assert pix.samples[1] >= 160 and pix.samples[0] < 40
        finally:
            doc.close()
        mixed = d / "mixed.md"
        mixed.write_text("Hello\n\n![](sib.png)\n", encoding="utf-8")
        pdf2 = d / "mixed.pdf"
        res = run_engine(["export-as", str(mixed), str(pdf2)])
        assert res.get("success"), res
        assert res.get("tool") != "pymupdf-md-images", res


def test_html_lrm_wj_photo_export() -> None:
    """Share dest Word on photo HTML whose leftover is Word/RTL &lrm; / &NoBreak;."""
    src = ENGINE.read_text(encoding="utf-8")
    assert 'unicodedata.category(ch) != "Cf"' in src
    if not pymupdf_available():
        print("SKIP html lrm wj photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html lrm wj photo export -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        (d / "pic.png").write_bytes(buf.getvalue())
        for leftover in ("&lrm;", "&rlm;", "&NoBreak;", "&#x2060;"):
            html = d / "photo.html"
            html.write_text(
                f'<html><body><img src="pic.png">{leftover}</body></html>',
                encoding="utf-8",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(html), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><img src="pic.png">Hello&lrm;</body></html>',
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_html_empty_svg_photo_export() -> None:
    """Share dest Word on photo HTML whose leftover is an empty <svg>."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "letter-size white page" in src
    if not pymupdf_available():
        print("SKIP html empty svg photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html empty svg photo export -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        (d / "pic.png").write_bytes(buf.getvalue())
        html = d / "photo.html"
        html.write_text(
            '<html><body><img src="pic.png">'
            '<svg xmlns="http://www.w3.org/2000/svg"></svg>'
            "<svg><text> </text></svg></body></html>",
            encoding="utf-8",
        )
        dest = d / "photo.docx"
        res = run_engine(["export-as", str(html), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert len(media) == 1, media
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        painted = d / "painted.html"
        painted.write_text(
            '<html><body><svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<rect width="64" height="48" fill="#14b450"/></svg></body></html>',
            encoding="utf-8",
        )
        dest2 = d / "painted.docx"
        res = run_engine(["export-as", str(painted), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest2) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()


def test_html_vs16_photo_export() -> None:
    """Share dest Word/PDF on photo HTML whose leftover is emoji &#xfe0f;."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "0xFE00 <= ord(ch) <= 0xFE0F" in src
    if not pymupdf_available():
        print("SKIP html vs16 photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html vs16 photo export -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        (d / "pic.png").write_bytes(buf.getvalue())
        html = d / "photo.html"
        html.write_text(
            '<html><body><img src="pic.png">&#xfe0f;&#x034f;</body></html>',
            encoding="utf-8",
        )
        dest = d / "photo.docx"
        res = run_engine(["export-as", str(html), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        pdf = d / "photo.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            pix = doc[0].get_pixmap()
            assert pix.samples[1] >= 160 and pix.samples[0] < 40
        finally:
            doc.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><img src="pic.png">Hello&#xfe0f;</body></html>',
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_html_svg_white_raster_photo_export() -> None:
    """Share dest Word on photo HTML whose leftover SVG is <use href="#missing"/>."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "letter-size white page that steals dest Word first" in src
    if not pymupdf_available():
        print("SKIP html svg white raster -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html svg white raster -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        (d / "pic.png").write_bytes(buf.getvalue())
        html = d / "photo.html"
        html.write_text(
            '<html><body><img src="pic.png">'
            '<svg xmlns="http://www.w3.org/2000/svg"><use href="#missing"/></svg>'
            '<svg xmlns="http://www.w3.org/2000/svg"><path d=""/></svg>'
            "</body></html>",
            encoding="utf-8",
        )
        dest = d / "photo.docx"
        res = run_engine(["export-as", str(html), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert len(media) == 1, media
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        painted = d / "painted.html"
        painted.write_text(
            '<html><body><svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<rect width="64" height="48" fill="#14b450"/></svg></body></html>',
            encoding="utf-8",
        )
        dest2 = d / "painted.docx"
        res = run_engine(["export-as", str(painted), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest2) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()


def test_html_svg_black_raster_photo_export() -> None:
    """Share dest Word on photo HTML whose leftover SVG is <pattern> / feImage."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "solid black (same steal)" in src
    if not pymupdf_available():
        print("SKIP html svg black raster -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html svg black raster -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        (d / "pic.png").write_bytes(buf.getvalue())
        html = d / "photo.html"
        html.write_text(
            '<html><body><img src="pic.png">'
            '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<defs><pattern id="p" width="64" height="48">'
            '<image href="pic.png" width="64" height="48"/></pattern></defs>'
            '<rect width="64" height="48" fill="url(#p)"/></svg></body></html>',
            encoding="utf-8",
        )
        dest = d / "photo.docx"
        res = run_engine(["export-as", str(html), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert len(media) == 1, media
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()


def test_svg_unused_defs_photo_export() -> None:
    """Share dest Word/PDF/PNG on unused <defs><image href> (no <use>)."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def _svg_unused_container_image" in src
    assert "MuPDF does not paint unused" in src
    if not pymupdf_available():
        print("SKIP svg unused defs -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP svg unused defs -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        Image.new("RGB", (64, 48), (20, 180, 80)).save(d / "pic.png", format="PNG")
        html = d / "photo.html"
        html.write_text(
            '<html><body><svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<defs><image href="pic.png" width="64" height="48"/></defs>'
            "</svg></body></html>",
            encoding="utf-8",
        )
        dest = d / "photo.docx"
        res = run_engine(["export-as", str(html), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        pdf = d / "photo.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf

        doc = pymupdf.open(pdf)
        try:
            pix = doc[0].get_pixmap()
            i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
            r, g = pix.samples[i], pix.samples[i + 1]
            assert g >= 160 and r < 40, (r, g)
        finally:
            doc.close()
        svg = d / "mark.svg"
        svg.write_text(
            '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48">'
            '<defs><image href="pic.png" width="64" height="48"/></defs>'
            "</svg>",
            encoding="utf-8",
        )
        png = d / "mark.png"
        res = run_engine(["export-as", str(svg), str(png)])
        assert res.get("success"), res
        im = Image.open(png)
        try:
            px = im.convert("RGB").getpixel((8, 8))
            assert px[1] >= 160 and px[0] < 40, px
        finally:
            im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body>Hello<svg xmlns="http://www.w3.org/2000/svg">'
            '<defs><image href="pic.png"/></defs></svg></body></html>',
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res
        with zipfile.ZipFile(dest2) as zf:
            xml = zf.read("word/document.xml").decode("utf-8")
            assert "Hello" in xml
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert not media, media


def test_html_delinstr_photo_export() -> None:
    """Share dest Word/PDF on photo HTML/XML whose leftover is <w:delInstrText>."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "delInstrText|delText|instrText|commentText" in src
    if not pymupdf_available():
        print("SKIP html delinstr -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html delinstr -- Pillow missing")
        return
    import zipfile
    import io
    import base64

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        (d / "pic.png").write_bytes(png)
        for leftover in (
            "<w:delInstrText>Photo</w:delInstrText>",
            "<w:delInstrText> HYPERLINK Photo </w:delInstrText>",
            "<w:delInstrText>Photo",
        ):
            html = d / "photo.html"
            html.write_text(
                '<html><body><img src="pic.png">' + leftover + "</body></html>",
                encoding="utf-8",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(html), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(html), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-html-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (leftover, r, g)
            finally:
                doc.close()
        b64 = base64.b64encode(png).decode("ascii")
        xml = d / "photo.xml"
        xml.write_text(
            '<?xml version="1.0"?>'
            '<w:wordDocument xmlns:w="http://schemas.microsoft.com/office/word/2003/wordml">'
            f'<w:binData w:name="wordml://1.png">{b64}</w:binData>'
            "<w:delInstrText>Photo</w:delInstrText></w:wordDocument>",
            encoding="utf-8",
        )
        dest = d / "xml.docx"
        res = run_engine(["export-as", str(xml), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><img src="pic.png">'
            "<w:delInstrText>Photo</w:delInstrText><p>Hello</p></body></html>",
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res
        with zipfile.ZipFile(dest2) as zf:
            xml = zf.read("word/document.xml").decode("utf-8")
            assert "Hello" in xml
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert not media, media


def test_html_msocomsubj_photo_export() -> None:
    """Share dest Word/PDF on photo Word-HTML whose leftover is class=MsoCommentSubject."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "msoCommentText|msoCommentReference|msoCommentSubject" in src
    assert "Do not strip msoIns (page copy)" in src
    if not pymupdf_available():
        print("SKIP html msocomsubj -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html msocomsubj -- Pillow missing")
        return
    import zipfile
    import io
    import base64

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        (d / "pic.png").write_bytes(png)
        for leftover in (
            "<p class=MsoCommentSubject>Photo</p>",
            '<span class="msoCommentSubject">Photo</span>',
            "<p class=MsoCommentSubject>Photo",
        ):
            html = d / "photo.html"
            html.write_text(
                '<html><body><img src="pic.png">' + leftover + "</body></html>",
                encoding="utf-8",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(html), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(html), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-html-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (leftover, r, g)
            finally:
                doc.close()
        b64 = base64.b64encode(png).decode("ascii")
        xml = d / "photo.xml"
        xml.write_text(
            '<?xml version="1.0"?>'
            '<w:wordDocument xmlns:w="http://schemas.microsoft.com/office/word/2003/wordml">'
            f'<w:binData w:name="wordml://1.png">{b64}</w:binData>'
            "<p class=MsoCommentSubject>Photo</p></w:wordDocument>",
            encoding="utf-8",
        )
        dest = d / "xml.docx"
        res = run_engine(["export-as", str(xml), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><img src="pic.png">'
            "<p class=MsoCommentSubject>Photo</p><p>Hello</p></body></html>",
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res
        with zipfile.ZipFile(dest2) as zf:
            xml = zf.read("word/document.xml").decode("utf-8")
            assert "Hello" in xml
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert not media, media


def test_html_flddata_photo_export() -> None:
    """Share dest Word/PDF on photo HTML/XML whose leftover is <w:fldData>."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "delInstrText|delText|instrText|commentText|fldData" in src
    if not pymupdf_available():
        print("SKIP html flddata -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html flddata -- Pillow missing")
        return
    import zipfile
    import io
    import base64

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        (d / "pic.png").write_bytes(png)
        for leftover in (
            "<w:fldData>Photo</w:fldData>",
            "<w:fldData>QUJDRA==</w:fldData>",
            "<w:fldData>Photo",
        ):
            html = d / "photo.html"
            html.write_text(
                '<html><body><img src="pic.png">' + leftover + "</body></html>",
                encoding="utf-8",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(html), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(html), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-html-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (leftover, r, g)
            finally:
                doc.close()
        b64 = base64.b64encode(png).decode("ascii")
        xml = d / "photo.xml"
        xml.write_text(
            '<?xml version="1.0"?>'
            '<w:wordDocument xmlns:w="http://schemas.microsoft.com/office/word/2003/wordml">'
            f'<w:binData w:name="wordml://1.png">{b64}</w:binData>'
            "<w:fldData>Photo</w:fldData></w:wordDocument>",
            encoding="utf-8",
        )
        dest = d / "xml.docx"
        res = run_engine(["export-as", str(xml), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><img src="pic.png">'
            "<w:fldData>Photo</w:fldData><p>Hello</p></body></html>",
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res
        with zipfile.ZipFile(dest2) as zf:
            xml = zf.read("word/document.xml").decode("utf-8")
            assert "Hello" in xml
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert not media, media


def test_html_mailmerge_photo_export() -> None:
    """Share dest Word/PDF on photo Word-HTML whose leftover is <w:mailMerge>."""
    src = ENGINE.read_text(encoding="utf-8")
    assert r"(?:[\w.-]+:)(?:mailMerge|odso|recipients)" in src
    assert 'Leftover "Photo" in <w:mailMerge> / <w:odso> / <w:recipients> stole dest Word / dest PDF Story.' in src
    if not pymupdf_available():
        print("SKIP html mailmerge -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html mailmerge -- Pillow missing")
        return
    import zipfile
    import io
    import base64

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        (d / "pic.png").write_bytes(png)
        for leftover in (
            "<w:mailMerge>Photo</w:mailMerge>",
            "<w:mailMerge><w:odso>Photo</w:odso></w:mailMerge>",
            "<w:recipients>Photo</w:recipients>",
            "<w:mailMerge>Photo",
        ):
            html = d / "photo.html"
            html.write_text(
                '<html><body><img src="pic.png">' + leftover + "</body></html>",
                encoding="utf-8",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(html), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(html), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-html-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (leftover, r, g)
            finally:
                doc.close()
        b64 = base64.b64encode(png).decode("ascii")
        xml = d / "photo.xml"
        xml.write_text(
            '<?xml version="1.0"?>'
            '<w:wordDocument xmlns:w="http://schemas.microsoft.com/office/word/2003/wordml">'
            f'<w:binData w:name="wordml://1.png">{b64}</w:binData>'
            "<w:mailMerge>Photo</w:mailMerge></w:wordDocument>",
            encoding="utf-8",
        )
        dest = d / "xml.docx"
        res = run_engine(["export-as", str(xml), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><img src="pic.png">'
            "<w:mailMerge>Photo</w:mailMerge><p>Hello</p></body></html>",
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res
        with zipfile.ZipFile(dest2) as zf:
            xml = zf.read("word/document.xml").decode("utf-8")
            assert "Hello" in xml
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert not media, media


def test_html_msoacetate_photo_export() -> None:
    """Share dest Word/PDF on photo Word-HTML whose leftover is class=MsoAcetate."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "msoCommentSubject|msoAcetate|msoCommentReply|msoCommentList" in src
    assert "Do not strip MsoHeader / MsoCaption (page copy)" in src
    if not pymupdf_available():
        print("SKIP html msoacetate -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html msoacetate -- Pillow missing")
        return
    import zipfile
    import io
    import base64

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        (d / "pic.png").write_bytes(png)
        for leftover in (
            "<div class=MsoAcetate>Photo</div>",
            "<p class=MsoCommentReply>Photo</p>",
            "<div class=MsoCommentList>Photo</div>",
            "<div class=MsoAcetate>Photo",
        ):
            html = d / "photo.html"
            html.write_text(
                '<html><body><img src="pic.png">' + leftover + "</body></html>",
                encoding="utf-8",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(html), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(html), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-html-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (leftover, r, g)
            finally:
                doc.close()
        b64 = base64.b64encode(png).decode("ascii")
        xml = d / "photo.xml"
        xml.write_text(
            '<?xml version="1.0"?>'
            '<w:wordDocument xmlns:w="http://schemas.microsoft.com/office/word/2003/wordml">'
            f'<w:binData w:name="wordml://1.png">{b64}</w:binData>'
            "<div class=MsoAcetate>Photo</div></w:wordDocument>",
            encoding="utf-8",
        )
        dest = d / "xml.docx"
        res = run_engine(["export-as", str(xml), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><img src="pic.png">'
            "<div class=MsoAcetate>Photo</div><p>Hello</p></body></html>",
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res
        with zipfile.ZipFile(dest2) as zf:
            xml = zf.read("word/document.xml").decode("utf-8")
            assert "Hello" in xml
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert not media, media
        header = d / "header.html"
        header.write_text(
            '<html><body><img src="pic.png"><p class=MsoHeader>Hello</p></body></html>',
            encoding="utf-8",
        )
        dest3 = d / "header.docx"
        res = run_engine(["export-as", str(header), str(dest3)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_html_msoballoon_photo_export() -> None:
    """Share dest Word/PDF on photo Word-HTML whose leftover is class=MsoBalloonText."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "msoCommentList|msoBalloonText|msoAnnotation" in src
    assert "class=MsoBalloonText / MsoAnnotation is the same balloon chrome." in src
    assert "Do not strip MsoHeader / MsoCaption (page copy)" in src
    if not pymupdf_available():
        print("SKIP html msoballoon -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html msoballoon -- Pillow missing")
        return
    import zipfile
    import io
    import base64

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        (d / "pic.png").write_bytes(png)
        for leftover in (
            "<p class=MsoBalloonText>Photo</p>",
            "<p class=MsoAnnotation>Photo</p>",
            '<span class="msoBalloonText">Photo</span>',
            "<p class=MsoBalloonText>Photo",
        ):
            html = d / "photo.html"
            html.write_text(
                '<html><body><img src="pic.png">' + leftover + "</body></html>",
                encoding="utf-8",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(html), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(html), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-html-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (leftover, r, g)
            finally:
                doc.close()
        b64 = base64.b64encode(png).decode("ascii")
        xml = d / "photo.xml"
        xml.write_text(
            '<?xml version="1.0"?>'
            '<w:wordDocument xmlns:w="http://schemas.microsoft.com/office/word/2003/wordml">'
            f'<w:binData w:name="wordml://1.png">{b64}</w:binData>'
            "<p class=MsoBalloonText>Photo</p></w:wordDocument>",
            encoding="utf-8",
        )
        dest = d / "xml.docx"
        res = run_engine(["export-as", str(xml), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><img src="pic.png">'
            "<p class=MsoBalloonText>Photo</p><p>Hello</p></body></html>",
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res
        with zipfile.ZipFile(dest2) as zf:
            xml = zf.read("word/document.xml").decode("utf-8")
            assert "Hello" in xml
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert not media, media


def test_html_msocomhide_photo_export() -> None:
    """Share dest Word/PDF on photo Word-HTML whose leftover is class=msocomhide."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "msoBalloonText|msoAnnotation|msocomhide|msocomanch|msocomanchor" in src
    assert "class=msocomhide / msocomanch / msocomanchor is Office comment-hide" in src
    if not pymupdf_available():
        print("SKIP html msocomhide -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html msocomhide -- Pillow missing")
        return
    import zipfile
    import io
    import base64

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        (d / "pic.png").write_bytes(png)
        for leftover in (
            "<span class=msocomhide>Photo</span>",
            "<span class=msocomanch>Photo</span>",
            "<span class=msocomanchor>Photo</span>",
            "<span class=msocomhide>Photo",
        ):
            html = d / "photo.html"
            html.write_text(
                '<html><body><img src="pic.png">' + leftover + "</body></html>",
                encoding="utf-8",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(html), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(html), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-html-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (leftover, r, g)
            finally:
                doc.close()
        b64 = base64.b64encode(png).decode("ascii")
        xml = d / "photo.xml"
        xml.write_text(
            '<?xml version="1.0"?>'
            '<w:wordDocument xmlns:w="http://schemas.microsoft.com/office/word/2003/wordml">'
            f'<w:binData w:name="wordml://1.png">{b64}</w:binData>'
            "<span class=msocomhide>Photo</span></w:wordDocument>",
            encoding="utf-8",
        )
        dest = d / "xml.docx"
        res = run_engine(["export-as", str(xml), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><img src="pic.png">'
            "<span class=msocomhide>Photo</span><p>Hello</p></body></html>",
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res
        with zipfile.ZipFile(dest2) as zf:
            xml = zf.read("word/document.xml").decode("utf-8")
            assert "Hello" in xml
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert not media, media


def test_html_unclosed_style_photo_export() -> None:
    """Share dest Word on photo HTML whose leftover is unclosed <style> / <title>."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "(?=</?(?:body|div|p|img|h[1-6]|table)\\b)" in src
    if not pymupdf_available():
        print("SKIP html unclosed style -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html unclosed style -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        (d / "pic.png").write_bytes(buf.getvalue())
        for leftover in (
            '<style>body{color:red}\n</body></html>',
            '<script>var x=1\n</body></html>',
            '<title>Photo<body><img src="pic.png"></body></html>',
        ):
            html = d / "photo.html"
            if leftover.startswith("<title>"):
                html.write_text(
                    "<html><head>" + leftover,
                    encoding="utf-8",
                )
            else:
                html.write_text(
                    '<html><body><img src="pic.png">' + leftover,
                    encoding="utf-8",
                )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(html), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            "<html><head><title>Photo</title></head>"
            '<body><img src="pic.png">Hello</body></html>',
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_html_unclosed_desc_photo_export() -> None:
    """Share dest Word on photo HTML whose leftover is unclosed <desc> / <template> / <head>."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "Unclosed tags stop at the next tag so <desc>A photo<img stays pictures" in src
    if not pymupdf_available():
        print("SKIP html unclosed desc -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html unclosed desc -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        (d / "pic.png").write_bytes(buf.getvalue())
        for leftover in (
            "<desc>Photo",
            "<metadata>Photo",
            "<rdf:RDF>Photo",
            "<inkscape:label>Photo",
            "<template>Photo",
            "<noscript>Enable JS",
            '<head>Photo<img src="pic.png">',
            '<svg><desc>A photo<img src="pic.png">',
        ):
            html = d / "photo.html"
            if leftover.startswith("<head>") or leftover.startswith("<svg>"):
                html.write_text("<html>" + leftover, encoding="utf-8")
            else:
                html.write_text(
                    '<html><body><img src="pic.png">' + leftover,
                    encoding="utf-8",
                )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(html), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            "<html><head><title>Photo</title></head>"
            '<body><img src="pic.png">Hello</body></html>',
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_html_noframes_photo_export() -> None:
    """Share dest Word/PDF on photo HTML whose leftover is <noframes> / <noembed>."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "template|noscript|noframes|noembed" in src
    if not pymupdf_available():
        print("SKIP html noframes -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html noframes -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        (d / "pic.png").write_bytes(buf.getvalue())
        for leftover in (
            "<noframes>Photo</noframes>",
            "<noembed>Photo</noembed>",
            "<noframes>Photo",
            '<noframes>Photo<img src="pic.png">',
        ):
            html = d / "photo.html"
            html.write_text(
                '<html><body><img src="pic.png">' + leftover,
                encoding="utf-8",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(html), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(html), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-html-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (leftover, r, g)
            finally:
                doc.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><img src="pic.png"><noframes>Photo</noframes>'
            "<p>Hello</p></body></html>",
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_html_head_xml_photo_export() -> None:
    """Share dest Word on photo Word-HTML whose leftover is <xml> inside <head>."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "(?=<(?:body|img)\\b)" in src
    assert "(?=<) used to win first" in src
    if not pymupdf_available():
        print("SKIP html head xml -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html head xml -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        (d / "pic.png").write_bytes(buf.getvalue())
        html = d / "photo.html"
        html.write_text(
            '<html><head><xml><o:DocumentProperties>'
            "<o:Author>Photo</o:Author></o:DocumentProperties></xml>"
            '</head><body><img src="pic.png"></body></html>',
            encoding="utf-8",
        )
        dest = d / "photo.docx"
        res = run_engine(["export-as", str(html), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><head><xml><o:Author>Photo</o:Author></xml></head>'
            '<body><img src="pic.png">Hello</body></html>',
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_html_bindata_photo_export() -> None:
    """Share dest Word/PDF on photo Word-HTML whose picture is leftover <w:binData>."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "Word 2003 HTML / WordML leftover <w:binData>" in src
    if not pymupdf_available():
        print("SKIP html bindata -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html bindata -- Pillow missing")
        return
    import zipfile
    import io
    import base64

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        b64 = base64.b64encode(buf.getvalue()).decode("ascii")
        html = d / "photo.html"
        html.write_text(
            '<html><body><img src="wordml://1.png">'
            f'<w:binData w:name="wordml://1.png">{b64}</w:binData>'
            "</body></html>",
            encoding="utf-8",
        )
        dest = d / "photo.docx"
        res = run_engine(["export-as", str(html), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        pdf = d / "photo.pdf"
        res = run_engine(["export-as", str(html), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        import pymupdf
        doc = pymupdf.open(pdf)
        try:
            pix = doc[0].get_pixmap()
            i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
            r, g = pix.samples[i], pix.samples[i + 1]
            assert g >= 160 and r < 40, (r, g)
        finally:
            doc.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            f'<html><body><w:binData w:name="wordml://1.png">{b64}</w:binData>'
            "<p>Hello</p></body></html>",
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_xml_bindata_photo_export() -> None:
    """Share dest Word/PDF on photo Word 2003 XML whose picture is leftover <w:binData>."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def xml_photo_pngs" in src
    if not pymupdf_available():
        print("SKIP xml bindata -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP xml bindata -- Pillow missing")
        return
    import zipfile
    import io
    import base64

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        b64 = base64.b64encode(buf.getvalue()).decode("ascii")
        xml = d / "photo.xml"
        xml.write_text(
            '<?xml version="1.0"?>'
            '<w:wordDocument xmlns:w="http://schemas.microsoft.com/office/word/2003/wordml">'
            f'<w:binData w:name="wordml://1.png">{b64}</w:binData>'
            "</w:wordDocument>",
            encoding="utf-8",
        )
        dest = d / "photo.docx"
        res = run_engine(["export-as", str(xml), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        pdf = d / "photo.pdf"
        res = run_engine(["export-as", str(xml), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-xml-images", res
        import pymupdf
        doc = pymupdf.open(pdf)
        try:
            pix = doc[0].get_pixmap()
            i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
            r, g = pix.samples[i], pix.samples[i + 1]
            assert g >= 160 and r < 40, (r, g)
        finally:
            doc.close()
        mixed = d / "mixed.xml"
        mixed.write_text("<root>Hello</root>", encoding="utf-8")
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_html_deltext_photo_export() -> None:
    """Share dest Word/PDF on photo HTML/XML whose leftover is <w:delText> Photo."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "delText|instrText|commentText" in src
    if not pymupdf_available():
        print("SKIP html deltext -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html deltext -- Pillow missing")
        return
    import zipfile
    import io
    import base64

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        (d / "pic.png").write_bytes(png)
        for leftover in (
            "<w:delText>Photo</w:delText>",
            "<w:instrText> HYPERLINK Photo </w:instrText>",
            "<w:commentText>Photo</w:commentText>",
            "<w:delText>Photo",
        ):
            html = d / "photo.html"
            html.write_text(
                '<html><body><img src="pic.png">' + leftover + "</body></html>",
                encoding="utf-8",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(html), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(html), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-html-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (leftover, r, g)
            finally:
                doc.close()
        b64 = base64.b64encode(png).decode("ascii")
        xml = d / "photo.xml"
        xml.write_text(
            '<?xml version="1.0"?>'
            '<w:wordDocument xmlns:w="http://schemas.microsoft.com/office/word/2003/wordml">'
            f'<w:binData w:name="wordml://1.png">{b64}</w:binData>'
            "<w:delText>Photo</w:delText></w:wordDocument>",
            encoding="utf-8",
        )
        dest = d / "xml.docx"
        res = run_engine(["export-as", str(xml), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><img src="pic.png"><w:delText>Photo</w:delText>'
            "<p>Hello</p></body></html>",
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_html_aml_comment_photo_export() -> None:
    """Share dest Word/PDF on photo HTML/XML whose leftover is Word comment / moveFrom."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "aml:annotation|w:annotation" in src
    assert "Do not strip MathML <annotation> (parked)" in src
    if not pymupdf_available():
        print("SKIP html aml comment -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html aml comment -- Pillow missing")
        return
    import zipfile
    import io
    import base64

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        (d / "pic.png").write_bytes(png)
        for leftover in (
            '<aml:annotation aml:type="Word.Comment">Photo</aml:annotation>',
            "<w:comment>Photo</w:comment>",
            "<w:moveFromText>Photo</w:moveFromText>",
            "<aml:annotation>Photo",
        ):
            html = d / "photo.html"
            html.write_text(
                '<html><body><img src="pic.png">' + leftover + "</body></html>",
                encoding="utf-8",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(html), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(html), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-html-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (leftover, r, g)
            finally:
                doc.close()
        b64 = base64.b64encode(png).decode("ascii")
        xml = d / "photo.xml"
        xml.write_text(
            '<?xml version="1.0"?>'
            '<w:wordDocument xmlns:w="http://schemas.microsoft.com/office/word/2003/wordml">'
            f'<w:binData w:name="wordml://1.png">{b64}</w:binData>'
            "<aml:annotation>Photo</aml:annotation></w:wordDocument>",
            encoding="utf-8",
        )
        dest = d / "xml.docx"
        res = run_engine(["export-as", str(xml), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><img src="pic.png">'
            "<aml:annotation>Photo</aml:annotation><p>Hello</p></body></html>",
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res
        math = d / "math.html"
        math.write_text(
            '<html><body><img src="pic.png">'
            "<math><annotation>Photo</annotation></math></body></html>",
            encoding="utf-8",
        )
        dest3 = d / "math.docx"
        res = run_engine(["export-as", str(math), str(dest3)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_html_msodel_photo_export() -> None:
    """Share dest Word/PDF on photo Word-HTML whose leftover is class=msoDel / msocomtxt."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "msoDel|msocomtxt|msocomoff|msoCommentText|msoCommentReference" in src
    assert "Do not strip msoIns (page copy)" in src
    if not pymupdf_available():
        print("SKIP html msodel -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html msodel -- Pillow missing")
        return
    import zipfile
    import io
    import base64

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        (d / "pic.png").write_bytes(png)
        for leftover in (
            "<span class=msoDel>Photo</span>",
            '<span class="MsoDel">Photo</span>',
            "<div class=msocomtxt>Photo</div>",
            "<p class=MsoCommentText>Photo</p>",
            "<span class=msoDel>Photo",
        ):
            html = d / "photo.html"
            html.write_text(
                '<html><body><img src="pic.png">' + leftover + "</body></html>",
                encoding="utf-8",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(html), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(html), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-html-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (leftover, r, g)
            finally:
                doc.close()
        b64 = base64.b64encode(png).decode("ascii")
        xml = d / "photo.xml"
        xml.write_text(
            '<?xml version="1.0"?>'
            '<w:wordDocument xmlns:w="http://schemas.microsoft.com/office/word/2003/wordml">'
            f'<w:binData w:name="wordml://1.png">{b64}</w:binData>'
            "<span class=msoDel>Photo</span></w:wordDocument>",
            encoding="utf-8",
        )
        dest = d / "xml.docx"
        res = run_engine(["export-as", str(xml), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><img src="pic.png"><span class=msoDel>Photo</span>'
            "<p>Hello</p></body></html>",
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res
        ins = d / "ins.html"
        ins.write_text(
            '<html><body><img src="pic.png"><span class=msoIns>Hello</span></body></html>',
            encoding="utf-8",
        )
        dest3 = d / "ins.docx"
        res = run_engine(["export-as", str(ins), str(dest3)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_html_wdel_photo_export() -> None:
    """Share dest Word/PDF on photo HTML/XML whose leftover is <w:del><w:t>Photo."""
    src = ENGINE.read_text(encoding="utf-8")
    assert r"(?:[\w.-]+:)(?:del|moveFrom)" in src
    assert "Do not strip HTML <del> (page copy)" in src
    if not pymupdf_available():
        print("SKIP html wdel -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html wdel -- Pillow missing")
        return
    import zipfile
    import io
    import base64

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        png = buf.getvalue()
        (d / "pic.png").write_bytes(png)
        for leftover in (
            "<w:del><w:r><w:t>Photo</w:t></w:r></w:del>",
            "<w:moveFrom><w:r><w:t>Photo</w:t></w:r></w:moveFrom>",
            "<w:del><w:r><w:t>Photo",
        ):
            html = d / "photo.html"
            html.write_text(
                '<html><body><img src="pic.png">' + leftover + "</body></html>",
                encoding="utf-8",
            )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(html), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(html), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-html-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (leftover, r, g)
            finally:
                doc.close()
        b64 = base64.b64encode(png).decode("ascii")
        xml = d / "photo.xml"
        xml.write_text(
            '<?xml version="1.0"?>'
            '<w:wordDocument xmlns:w="http://schemas.microsoft.com/office/word/2003/wordml">'
            f'<w:binData w:name="wordml://1.png">{b64}</w:binData>'
            "<w:del><w:r><w:t>Photo</w:t></w:r></w:del></w:wordDocument>",
            encoding="utf-8",
        )
        dest = d / "xml.docx"
        res = run_engine(["export-as", str(xml), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><img src="pic.png">'
            "<w:del><w:r><w:t>Photo</w:t></w:r></w:del>"
            "<p>Hello</p></body></html>",
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res
        html_del = d / "htmldel.html"
        html_del.write_text(
            '<html><body><img src="pic.png"><del>Photo</del></body></html>',
            encoding="utf-8",
        )
        dest3 = d / "htmldel.docx"
        res = run_engine(["export-as", str(html_del), str(dest3)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_docx_del_t_photo_export() -> None:
    """Share dest PDF/MD on photo DOCX whose leftover is <w:del><w:t>Photo."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "Leftover <w:t>Photo</w:t> inside <w:del> stole dest PDF" in src
    assert 'local in {"del", "moveFrom"}' in src
    if not pymupdf_available():
        print("SKIP docx del-t -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP docx del-t -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
    png = buf.getvalue()

    def write_photo_docx(path: Path, body: str) -> None:
        with zipfile.ZipFile(path, "w") as zf:
            zf.writestr(
                "[Content_Types].xml",
                '<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                '<Default Extension="png" ContentType="image/png"/>'
                '<Override PartName="/word/document.xml" '
                'ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'
                "</Types>",
            )
            zf.writestr(
                "word/document.xml",
                '<?xml version="1.0"?><w:document '
                'xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'
                f"<w:body>{body}</w:body></w:document>",
            )
            zf.writestr("word/media/image1.png", png)

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        for leftover in (
            "<w:p><w:del><w:r><w:t>Photo</w:t></w:r></w:del></w:p>",
            "<w:p><w:moveFrom><w:r><w:t>Photo</w:t></w:r></w:moveFrom></w:p>",
        ):
            docx = d / "photo.docx"
            write_photo_docx(docx, leftover)
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(docx), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-office-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (leftover, r, g)
            finally:
                doc.close()
            md = d / "photo.md"
            res = run_engine(["export-as", str(docx), str(md)])
            assert res.get("success"), (leftover, res)
            assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        mixed = d / "mixed.docx"
        write_photo_docx(
            mixed,
            "<w:p><w:del><w:r><w:t>Photo</w:t></w:r></w:del></w:p>"
            "<w:p><w:r><w:t>Hello</w:t></w:r></w:p>",
        )
        dest = d / "mixed.pdf"
        res = run_engine(["export-as", str(mixed), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-story", res
        ins = d / "ins.docx"
        write_photo_docx(ins, "<w:p><w:ins><w:r><w:t>Hello</w:t></w:r></w:ins></w:p>")
        dest2 = d / "ins.pdf"
        res = run_engine(["export-as", str(ins), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-story", res


def test_odf_annotation_photo_export() -> None:
    """Share dest PDF/Word on photo ODT/ODP whose leftover is office:annotation / text:deletion."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "Leftover <office:annotation><text:p>Photo stole dest PDF" in src
    assert 'local in {"annotation", "deletion"}' in src
    if not pymupdf_available():
        print("SKIP odf annotation -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP odf annotation -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
    png = buf.getvalue()
    ns = (
        'xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" '
        'xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" '
        'xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0" '
        'xmlns:xlink="http://www.w3.org/1999/xlink"'
    )

    def write_odf(path: Path, body: str, mime: str) -> None:
        content = (
            '<?xml version="1.0" encoding="UTF-8"?>'
            f"<office:document-content {ns}>{body}</office:document-content>"
        )
        with zipfile.ZipFile(path, "w") as zf:
            zf.writestr("mimetype", mime)
            zf.writestr("content.xml", content)
            zf.writestr("Pictures/image1.png", png)

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        leftovers = (
            (
                d / "ann.odt",
                "<office:body><office:text>"
                "<office:annotation><text:p>Photo</text:p></office:annotation>"
                '<draw:frame><draw:image xlink:href="Pictures/image1.png"/></draw:frame>'
                "</office:text></office:body>",
                "application/vnd.oasis.opendocument.text",
            ),
            (
                d / "del.odt",
                "<office:body><office:text>"
                "<text:tracked-changes><text:changed-region><text:deletion>"
                "<text:p>Photo</text:p></text:deletion></text:changed-region>"
                "</text:tracked-changes>"
                '<draw:frame><draw:image xlink:href="Pictures/image1.png"/></draw:frame>'
                "</office:text></office:body>",
                "application/vnd.oasis.opendocument.text",
            ),
            (
                d / "ann.odp",
                "<office:body><office:presentation><draw:page>"
                "<office:annotation><text:p>Photo</text:p></office:annotation>"
                '<draw:frame><draw:image xlink:href="Pictures/image1.png"/></draw:frame>'
                "</draw:page></office:presentation></office:body>",
                "application/vnd.oasis.opendocument.presentation",
            ),
        )
        for src_path, body, mime in leftovers:
            write_odf(src_path, body, mime)
            pdf = d / (src_path.stem + ".pdf")
            res = run_engine(["export-as", str(src_path), str(pdf)])
            assert res.get("success"), (src_path.name, res)
            assert res.get("tool") == "pymupdf-office-images", (src_path.name, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (src_path.name, r, g)
            finally:
                doc.close()
            dest = d / (src_path.stem + ".docx")
            res = run_engine(["export-as", str(src_path), str(dest)])
            assert res.get("success"), (src_path.name, res)
            assert res.get("tool") == "docx-zip-images", (src_path.name, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (src_path.name, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (src_path.name, px)
                finally:
                    im.close()
        mixed = d / "mixed.odt"
        write_odf(
            mixed,
            "<office:body><office:text>"
            "<office:annotation><text:p>Photo</text:p></office:annotation>"
            "<text:p>Hello</text:p>"
            "</office:text></office:body>",
            "application/vnd.oasis.opendocument.text",
        )
        dest = d / "mixed.pdf"
        res = run_engine(["export-as", str(mixed), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-story", res


def test_ods_annotation_photo_export() -> None:
    """Share dest PDF/Word/Excel on photo ODS whose leftover is office:annotation in a cell."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "Leftover <office:annotation><text:p>Photo in a cell stole" in src
    assert "def _odf_visible_text" in src
    if not pymupdf_available():
        print("SKIP ods annotation -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP ods annotation -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
    png = buf.getvalue()
    ns = (
        'xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" '
        'xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" '
        'xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"'
    )

    def write_ods(path: Path, body: str) -> None:
        content = (
            '<?xml version="1.0"?>'
            f"<office:document-content {ns}>"
            f"<office:body><office:spreadsheet>{body}</office:spreadsheet>"
            "</office:body></office:document-content>"
        )
        with zipfile.ZipFile(path, "w") as zf:
            zf.writestr("mimetype", "application/vnd.oasis.opendocument.spreadsheet")
            zf.writestr("content.xml", content)
            zf.writestr("Pictures/image1.png", png)

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        ods = d / "photo.ods"
        write_ods(
            ods,
            "<table:table><table:table-row><table:table-cell>"
            "<office:annotation><text:p>Photo</text:p></office:annotation>"
            "</table:table-cell></table:table-row></table:table>",
        )
        pdf = d / "photo.pdf"
        res = run_engine(["export-as", str(ods), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-office-images", res
        import pymupdf
        doc = pymupdf.open(pdf)
        try:
            pix = doc[0].get_pixmap()
            i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
            r, g = pix.samples[i], pix.samples[i + 1]
            assert g >= 160 and r < 40, (r, g)
        finally:
            doc.close()
        dest = d / "photo.docx"
        res = run_engine(["export-as", str(ods), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        xlsx = d / "photo.xlsx"
        res = run_engine(["export-as", str(ods), str(xlsx)])
        assert res.get("success"), res
        assert res.get("tool") == "xlsx-zip-images", res
        with zipfile.ZipFile(xlsx) as zf:
            media = [n for n in zf.namelist() if n.startswith("xl/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        mixed = d / "mixed.ods"
        write_ods(
            mixed,
            "<table:table><table:table-row><table:table-cell>"
            "<text:p>Hello</text:p>"
            "<office:annotation><text:p>Photo</text:p></office:annotation>"
            "</table:table-cell></table:table-row></table:table>",
        )
        dest2 = d / "mixed.pdf"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-story", res


def test_odf_changelog_photo_export() -> None:
    """Share dest PDF/Word on photo ODT whose leftover is change-log insertion."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "Leftover <text:insertion> / <text:format-change> inside <text:changed-region> stole dest" in src
    assert 'local in {"changed-region", "tracked-changes"}' in src
    if not pymupdf_available():
        print("SKIP odf changelog -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP odf changelog -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
    png = buf.getvalue()
    ns = (
        'xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" '
        'xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"'
    )

    def write_odt(path: Path, body: str) -> None:
        content = (
            '<?xml version="1.0"?>'
            f"<office:document-content {ns}>"
            f"<office:body><office:text>{body}</office:text></office:body>"
            "</office:document-content>"
        )
        with zipfile.ZipFile(path, "w") as zf:
            zf.writestr("mimetype", "application/vnd.oasis.opendocument.text")
            zf.writestr("content.xml", content)
            zf.writestr("Pictures/image1.png", png)

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        for leftover in (
            "<text:tracked-changes><text:changed-region><text:insertion>"
            "<text:p>Photo</text:p></text:insertion></text:changed-region>"
            "</text:tracked-changes>",
            "<text:tracked-changes><text:changed-region><text:format-change>"
            "<text:p>Photo</text:p></text:format-change></text:changed-region>"
            "</text:tracked-changes>",
        ):
            odt = d / "photo.odt"
            write_odt(odt, leftover)
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(odt), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-office-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (leftover, r, g)
            finally:
                doc.close()
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(odt), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
        mixed = d / "mixed.odt"
        write_odt(
            mixed,
            "<text:tracked-changes><text:changed-region><text:insertion>"
            "<text:p>Photo</text:p></text:insertion></text:changed-region>"
            "</text:tracked-changes><text:p>Hello</text:p>",
        )
        dest2 = d / "mixed.pdf"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-story", res
        ins = d / "ins.odt"
        write_odt(ins, "<text:insertion><text:p>Hello</text:p></text:insertion>")
        dest3 = d / "ins.pdf"
        res = run_engine(["export-as", str(ins), str(dest3)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-story", res


def test_html_unclosed_comment_photo_export() -> None:
    """Share dest Word / PDF on photo HTML / MD whose leftover is unclosed <!-- Photo."""
    src = ENGINE.read_text(encoding="utf-8")
    assert r'(?s)<!--.*$' in src
    if not pymupdf_available():
        print("SKIP html unclosed comment -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP html unclosed comment -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        (d / "pic.png").write_bytes(buf.getvalue())
        for leftover in (
            '<!-- Photo',
            '<!--[if IE]>Photo',
            '<!-- Photo<img src="pic.png">',
        ):
            html = d / "photo.html"
            if leftover.endswith(">"):
                html.write_text("<html><body>" + leftover, encoding="utf-8")
            else:
                html.write_text(
                    '<html><body><img src="pic.png">' + leftover,
                    encoding="utf-8",
                )
            dest = d / "photo.docx"
            res = run_engine(["export-as", str(html), str(dest)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "docx-zip-images", (leftover, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (leftover, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (leftover, px)
                finally:
                    im.close()
            pdf = d / "photo.pdf"
            res = run_engine(["export-as", str(html), str(pdf)])
            assert res.get("success"), (leftover, res)
            assert res.get("tool") == "pymupdf-html-images", (leftover, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (leftover, r, g)
            finally:
                doc.close()
        md = d / "photo.md"
        md.write_text("![](pic.png)\n<!-- Photo", encoding="utf-8")
        dest = d / "md.docx"
        res = run_engine(["export-as", str(md), str(dest)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(dest) as zf:
            media = [n for n in zf.namelist() if n.startswith("word/media/")]
            assert media, zf.namelist()
            im = Image.open(io.BytesIO(zf.read(media[0])))
            try:
                px = im.convert("RGB").getpixel((8, 8))
                assert px[1] >= 160 and px[0] < 40, px
            finally:
                im.close()
        mixed = d / "mixed.html"
        mixed.write_text(
            '<html><body><img src="pic.png">Hello<!-- Photo',
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_md_frontmatter_photo_export() -> None:
    """Share dest Word / PDF on photo Markdown whose leftover is YAML / TOML frontmatter."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "YAML / TOML frontmatter is not page copy" in src
    if not pymupdf_available():
        print("SKIP md frontmatter -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP md frontmatter -- Pillow missing")
        return
    import zipfile
    import io

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (20, 180, 80)).save(buf, format="PNG")
        (d / "pic.png").write_bytes(buf.getvalue())
        md = d / "photo.md"
        for fence, body in (
            ("yaml", "---\ntitle: Photo\n---\n![](pic.png)\n"),
            ("toml", '+++\ntitle = "Photo"\n+++\n![](pic.png)\n'),
        ):
            md.write_text(body, encoding="utf-8")
            dest = d / f"{fence}.docx"
            res = run_engine(["export-as", str(md), str(dest)])
            assert res.get("success"), (fence, res)
            assert res.get("tool") == "docx-zip-images", (fence, res)
            with zipfile.ZipFile(dest) as zf:
                media = [n for n in zf.namelist() if n.startswith("word/media/")]
                assert media, (fence, zf.namelist())
                im = Image.open(io.BytesIO(zf.read(media[0])))
                try:
                    px = im.convert("RGB").getpixel((8, 8))
                    assert px[1] >= 160 and px[0] < 40, (fence, px)
                finally:
                    im.close()
            pdf = d / f"{fence}.pdf"
            res = run_engine(["export-as", str(md), str(pdf)])
            assert res.get("success"), (fence, res)
            assert res.get("tool") == "pymupdf-md-images", (fence, res)
            import pymupdf
            doc = pymupdf.open(pdf)
            try:
                pix = doc[0].get_pixmap()
                i = (pix.height // 2 * pix.width + pix.width // 2) * pix.n
                r, g = pix.samples[i], pix.samples[i + 1]
                assert g >= 160 and r < 40, (fence, r, g)
            finally:
                doc.close()
        mixed = d / "mixed.md"
        mixed.write_text(
            '+++\ntitle = "Photo"\n+++\n![](pic.png)\n\nHello\n',
            encoding="utf-8",
        )
        dest2 = d / "mixed.docx"
        res = run_engine(["export-as", str(mixed), str(dest2)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip", res


def test_svg_ico_drop_merge_lists() -> None:
    """Drop SVG/ICO onto a PDF used IsImagePath (raster-only) so the files were ignored."""
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    merge = (ROOT / "src/NetieSpace/Views/PdfMergeWindow.xaml.cs").read_text(encoding="utf-8")
    organize = (ROOT / "src/NetieSpace/Views/PdfOrganizeWindow.xaml.cs").read_text(encoding="utf-8")
    explorer = (ROOT / "src/NetieSpace/Services/ExplorerFileService.cs").read_text(encoding="utf-8")
    assert '".svg" or ".ico" or ".heic" or ".heif"' in preview
    assert '".svg"' in merge and '".ico"' in merge and '".heic"' in merge and '".heif"' in merge
    # Organize still uses PdfSharp XImage -- do not treat SVG as a raster insert.
    image_path = organize.split("public static bool IsImagePath", 1)[1][:500]
    assert '".svg"' not in image_path
    assert '".webp"' not in image_path
    assert '".tif"' not in image_path
    assert '".svg"' in explorer and '".ico"' in explorer and '".heif"' in explorer
    ico = ROOT / "src/NetieSpace/Assets/netie.ico"
    if ico.is_file() and pymupdf_available():
        with tempfile.TemporaryDirectory() as tmp:
            pdf = Path(tmp) / "icon.pdf"
            res = run_engine(["export-as", str(ico), str(pdf)])
            assert res.get("success"), res
            assert pdf.is_file() and pdf.stat().st_size > 200


def test_webp_tif_drop_uses_engine() -> None:
    """Drop / Share Export as PDF on .webp used XImage / MuPDF insert_image (no webp decoder)."""
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    organize = (ROOT / "src/NetieSpace/Views/PdfOrganizeWindow.xaml.cs").read_text(encoding="utf-8")
    engine = ENGINE.read_text(encoding="utf-8")
    parser = (ROOT / "src/NetieSpace/Services/ContentParserService.cs").read_text(encoding="utf-8")
    # WPF BitmapImage has no WebP decoder; ParseImage catch was ShellInfo and hid Share.
    assert 'extension == ".webp"' in parser
    assert "ParseHtmlPicture" in parser
    assert 'extension == ".svg"' in parser
    assert '".webp" or ".tif" or ".tiff"' in preview
    image_path = organize.split("public static bool IsImagePath", 1)[1][:500]
    assert '".webp"' not in image_path
    assert '".tif"' not in image_path
    assert '".ico", ".webp"' in engine or '".webp"' in engine
    try:
        from PIL import Image
    except Exception:
        print("SKIP webp export -- Pillow missing")
        return
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        webp = d / "photo.webp"
        Image.new("RGB", (32, 24), (20, 80, 180)).save(webp, "WEBP")
        pdf = d / "photo.pdf"
        res = run_engine(["export-as", str(webp), str(pdf)])
        assert res.get("success"), res
        assert pdf.is_file() and pdf.stat().st_size > 200
        tif = d / "photo.tif"
        Image.new("RGB", (32, 24), (20, 80, 180)).save(tif, "TIFF")
        tif_pdf = d / "photo-tif.pdf"
        res = run_engine(["export-as", str(tif), str(tif_pdf)])
        assert res.get("success"), res
        assert tif_pdf.is_file() and tif_pdf.stat().st_size > 200


def test_organize_engine_image_insert() -> None:
    """Organize Add files / drop used IsImagePath so WebP/HEIC/SVG said not supported."""
    organize = (ROOT / "src/NetieSpace/Views/PdfOrganizeWindow.xaml.cs").read_text(encoding="utf-8")
    assert "IsEngineImagePath" in organize
    assert "InsertConvertibleAt" in organize
    image_path = organize.split("public static bool IsImagePath", 1)[1][:500]
    assert '".webp"' not in image_path
    engine_path = organize.split("public static bool IsEngineImagePath", 1)[1][:400]
    assert '".webp"' in engine_path and '".heic"' in engine_path and '".svg"' in engine_path
    filt = organize.split("Add files as pages", 1)[1][:500]
    assert "*.webp" in filt and "*.heic" in filt and "*.svg" in filt
    drop = organize.split("private static bool TryGetImagePaths", 1)[1][:400]
    assert "IsEngineImagePath" in drop
    try:
        from PIL import Image
    except Exception:
        print("SKIP organize webp raster -- Pillow missing")
        return
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        webp = d / "photo.webp"
        Image.new("RGB", (32, 24), (20, 80, 180)).save(webp, "WEBP")
        png = d / "photo.png"
        res = run_engine(["export-as", str(webp), str(png)])
        assert res.get("success"), res
        assert png.is_file() and png.stat().st_size > 50


def test_sign_stamp_engine_raster() -> None:
    """Sign stamp picker hid WebP/HEIC; All files + WebP fed XImage and a fake PNG data URL."""
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    stamp = preview.split("PdfSignStamp_Click", 1)[1].split("PdfSignLibrary_Click", 1)[0]
    assert "RasterizeForXImageAsync" in stamp
    assert "*.webp" in stamp and "*.heic" in stamp and "*.svg" in stamp
    lib = preview.split("PdfSignLibrary_Click", 1)[1].split("PdfCommentHighlight_Click", 1)[0]
    assert "RasterizeForXImageAsync" in lib
    helper = preview.split("private async Task<string?> RasterizeForXImageAsync", 1)[1][:500]
    assert "IsEngineImagePath" in helper and "IsImagePath" in helper
    image_path = (ROOT / "src/NetieSpace/Views/PdfOrganizeWindow.xaml.cs").read_text(
        encoding="utf-8"
    ).split("public static bool IsImagePath", 1)[1][:500]
    assert '".webp"' not in image_path


def test_tiff_export_as_keeps_tiff() -> None:
    """Share Export as on a previewed .tiff defaulted to JPG (lossy). .tif already kept TIFF."""
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    dlg = preview.split("BuildExportAsDialog", 1)[1]
    default_ext = dlg.split("var imageDefaultExt", 1)[1].split("var imageFilterIndex", 1)[0]
    assert '"tif" or "tiff" => "tif"' in default_ext
    assert "TIFF image|*.tif;*.tiff" in dlg
    try:
        from PIL import Image
    except Exception:
        print("SKIP tiff export -- Pillow missing")
        return
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        tiff = d / "scan.tiff"
        Image.new("RGB", (40, 24), (30, 90, 40)).save(tiff, "TIFF")
        tif = d / "scan.tif"
        res = run_engine(["export-as", str(tiff), str(tif)])
        assert res.get("success"), res
        assert tif.is_file() and tif.stat().st_size > 50


def test_pdf_word_image_only() -> None:
    """Share Export as Word on a scan / image-only PDF used to need a text layer."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def pdf_pages_to_docx" in src
    assert "def write_docx_images" in src
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    dlg = preview.split("var imageDefaultExt", 1)[1]
    assert "Word document|*.docx" in dlg.split('".pdf" =>', 1)[1][:350]
    if not pymupdf_available():
        print("SKIP pdf word image-only -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP pdf word image-only -- Pillow missing")
        return
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        png = d / "scan.png"
        Image.new("RGB", (48, 32), (20, 80, 180)).save(png)
        pdf = d / "scan.pdf"
        res = run_engine(["export-as", str(png), str(pdf)])
        assert res.get("success"), res
        docx = d / "scan.docx"
        res = run_engine(["export-as", str(pdf), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        import zipfile
        with zipfile.ZipFile(docx) as zf:
            assert "word/media/image1.png" in zf.namelist()


def test_pdf_html_image_only() -> None:
    """Share Export as HTML on a scan / image-only PDF used to need a text layer."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def pdf_pages_to_html" in src
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    dlg = preview.split("var imageDefaultExt", 1)[1]
    assert "Web page|*.html" in dlg.split('".pdf" =>', 1)[1][:350]
    if not pymupdf_available():
        print("SKIP pdf html image-only -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP pdf html image-only -- Pillow missing")
        return
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        png = d / "scan.png"
        Image.new("RGB", (48, 32), (20, 80, 180)).save(png)
        pdf = d / "scan.pdf"
        res = run_engine(["export-as", str(png), str(pdf)])
        assert res.get("success"), res
        html_out = d / "scan.html"
        res = run_engine(["export-as", str(pdf), str(html_out)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-html-images", res
        html_text = html_out.read_text(encoding="utf-8")
        assert "data:image/png;base64," in html_text
        assert "Page 1" in html_text


def test_pdf_markdown_image_only() -> None:
    """Share Markdown on a scan / image-only PDF used to need a text layer."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def pdf_pages_to_markdown" in src
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    assert "ShareMdRow.Visibility = docFormats" in preview
    assert "ConvertToMarkdownAsync" in preview
    if not pymupdf_available():
        print("SKIP pdf markdown image-only -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP pdf markdown image-only -- Pillow missing")
        return
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        png = d / "scan.png"
        Image.new("RGB", (48, 32), (20, 80, 180)).save(png)
        pdf = d / "scan.pdf"
        res = run_engine(["export-as", str(png), str(pdf)])
        assert res.get("success"), res
        md = d / "scan.md"
        res = run_engine(["to-md", str(pdf), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-md-images", res
        text = md.read_text(encoding="utf-8")
        assert "data:image/png;base64," in text
        assert "Page 1" in text


def test_pptx_photo_export() -> None:
    """Share default PDF / Markdown / Word on a photo PPTX used empty markdown."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def pptx_media_pngs" in src
    assert "def images_to_pdf" in src
    assert "def pptx_pages_to_markdown" in src
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    dlg = preview.split("var imageDefaultExt", 1)[1]
    assert "PDF document|*.pdf" in dlg.split('".pptx"', 1)[1][:280]
    if not pymupdf_available():
        print("SKIP pptx photo export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP pptx photo export -- Pillow missing")
        return
    import io
    import zipfile

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        buf = io.BytesIO()
        Image.new("RGB", (64, 48), (200, 40, 40)).save(buf, format="PNG")
        pptx = d / "photos.pptx"
        with zipfile.ZipFile(pptx, "w") as zf:
            zf.writestr(
                "ppt/slides/slide1.xml",
                '<?xml version="1.0"?>'
                '<s:sld xmlns:s="http://schemas.openxmlformats.org/presentationml/2006/main"'
                ' xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"></s:sld>',
            )
            zf.writestr("ppt/media/image1.png", buf.getvalue())
        pdf = d / "photos.pdf"
        res = run_engine(["export-as", str(pptx), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-pptx-images", res
        assert pdf.is_file() and pdf.stat().st_size > 2000
        md = d / "photos.md"
        res = run_engine(["to-md", str(pptx), str(md)])
        assert res.get("success"), res
        assert res.get("tool") == "pptx-md-images", res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        docx = d / "photos.docx"
        res = run_engine(["export-as", str(pptx), str(docx)])
        assert res.get("success"), res
        assert res.get("tool") == "docx-zip-images", res
        with zipfile.ZipFile(docx) as zf:
            assert "word/media/image1.png" in zf.namelist()


def test_photo_odp_docx_export() -> None:
    """Share default PDF on a photo ODP / image-only DOCX used empty markdown."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "PHOTO_OFFICE_EXTS" in src
    assert "def office_media_pngs" in src
    assert "def write_pptx_images" in src
    preview_html = src.split('@register("preview-html")', 1)[1][:1400]
    assert "office_media_pngs" in preview_html
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    dlg = preview.split("var imageDefaultExt", 1)[1]
    odp_dlg = dlg.split('".odp"', 1)[1][:200]
    assert "PDF document|*.pdf" in odp_dlg
    assert "PowerPoint presentation|*.pptx" in odp_dlg
    assert "PDF document|*.pdf" in dlg.split('".docx"', 1)[1][:200]
    if not pymupdf_available():
        print("SKIP photo odp/docx export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP photo odp/docx export -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (30, 160, 60)).save(buf, format="PNG")
    png = buf.getvalue()
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        odp = d / "photos.odp"
        content = (
            '<?xml version="1.0" encoding="UTF-8"?>'
            '<office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"'
            ' xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"'
            ' xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"'
            ' xmlns:xlink="http://www.w3.org/1999/xlink">'
            "<office:body><office:presentation>"
            '<draw:page><draw:frame><draw:image xlink:href="Pictures/image1.png"/>'
            "</draw:frame></draw:page></office:presentation></office:body>"
            "</office:document-content>"
        )
        with zipfile.ZipFile(odp, "w") as zf:
            zf.writestr("mimetype", "application/vnd.oasis.opendocument.presentation")
            zf.writestr("content.xml", content)
            zf.writestr("Pictures/image1.png", png)
        pdf = d / "odp.pdf"
        res = run_engine(["export-as", str(odp), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-office-images", res
        assert pdf.is_file() and pdf.stat().st_size > 2000
        html_out = d / "odp.html"
        res = run_engine(["export-as", str(odp), str(html_out)])
        assert res.get("success"), res
        assert "data:image/png;base64," in html_out.read_text(encoding="utf-8")
        preview_html = d / "odp-preview.html"
        res = run_engine(["preview-html", str(odp), str(preview_html)])
        assert res.get("success"), res
        assert res.get("tool") == "office-html-images", res
        assert "data:image/png;base64," in preview_html.read_text(encoding="utf-8")
        odt = d / "photos.odt"
        odt_content = (
            '<?xml version="1.0" encoding="UTF-8"?>'
            '<office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"'
            ' xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"'
            ' xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"'
            ' xmlns:xlink="http://www.w3.org/1999/xlink">'
            "<office:body><office:text>"
            '<draw:frame><draw:image xlink:href="Pictures/image1.png"/></draw:frame>'
            "</office:text></office:body></office:document-content>"
        )
        with zipfile.ZipFile(odt, "w") as zf:
            zf.writestr("mimetype", "application/vnd.oasis.opendocument.text")
            zf.writestr("content.xml", odt_content)
            zf.writestr("Pictures/image1.png", png)
        odt_preview = d / "odt-preview.html"
        res = run_engine(["preview-html", str(odt), str(odt_preview)])
        assert res.get("success"), res
        assert res.get("tool") == "office-html-images", res
        assert "data:image/png;base64," in odt_preview.read_text(encoding="utf-8")
        md = d / "odp.md"
        res = run_engine(["to-md", str(odp), str(md)])
        assert res.get("success"), res
        assert "data:image/png;base64," in md.read_text(encoding="utf-8")
        pptx_out = d / "odp.pptx"
        res = run_engine(["export-as", str(odp), str(pptx_out)])
        assert res.get("success"), res
        assert res.get("tool") == "pptx-zip-images", res
        with zipfile.ZipFile(pptx_out) as zf:
            names = zf.namelist()
            assert "ppt/slides/slide1.xml" in names
            media = [n for n in names if n.startswith("ppt/media/") and n.lower().endswith(".png")]
            assert media, names
            assert len(zf.read(media[0])) > 80

        docx = d / "scan.docx"
        with zipfile.ZipFile(docx, "w") as zf:
            zf.writestr(
                "[Content_Types].xml",
                '<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                '<Override PartName="/word/document.xml" '
                'ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'
                "</Types>",
            )
            zf.writestr(
                "word/document.xml",
                '<?xml version="1.0"?><w:document '
                'xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'
                "<w:body><w:p/></w:body></w:document>",
            )
            zf.writestr("word/media/image1.png", png)
        dpdf = d / "docx.pdf"
        res = run_engine(["export-as", str(docx), str(dpdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-office-images", res
        assert dpdf.is_file() and dpdf.stat().st_size > 2000
        dhtml = d / "docx.html"
        res = run_engine(["export-as", str(docx), str(dhtml)])
        assert res.get("success"), res
        assert "data:image/png;base64," in dhtml.read_text(encoding="utf-8")


def test_photo_ods_xlsx_export() -> None:
    """Share default PDF on a photo ODS / image-only XLSX used empty cell text."""
    src = ENGINE.read_text(encoding="utf-8")
    assert '".ods", ".xlsx", ".xlsm"' in src
    assert "def write_xlsx_images" in src
    parser = (ROOT / "src/NetieSpace/Services/ContentParserService.cs").read_text(encoding="utf-8")
    xlsx_block = parser.split('extension is ".xlsx" or ".xlsm"', 1)[1][:1200]
    assert "PreviewHtmlAsync" in xlsx_block
    assert "office-html-images" in xlsx_block
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    dlg = preview.split("var imageDefaultExt", 1)[1]
    assert "PDF document|*.pdf" in dlg.split('".ods"', 1)[1][:220]
    if not pymupdf_available():
        print("SKIP photo ods/xlsx export -- pymupdf missing")
        return
    try:
        from PIL import Image
    except Exception:
        print("SKIP photo ods/xlsx export -- Pillow missing")
        return
    import io
    import zipfile

    buf = io.BytesIO()
    Image.new("RGB", (64, 48), (10, 80, 200)).save(buf, format="PNG")
    png = buf.getvalue()
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        ods = d / "chart.ods"
        content = (
            '<?xml version="1.0" encoding="UTF-8"?>'
            '<office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"'
            ' xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"'
            ' xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"'
            ' xmlns:xlink="http://www.w3.org/1999/xlink">'
            "<office:body><office:spreadsheet><table:table><table:table-row>"
            '<table:table-cell><draw:frame><draw:image xlink:href="Pictures/image1.png"/>'
            "</draw:frame></table:table-cell></table:table-row></table:table>"
            "</office:spreadsheet></office:body></office:document-content>"
        )
        with zipfile.ZipFile(ods, "w") as zf:
            zf.writestr("mimetype", "application/vnd.oasis.opendocument.spreadsheet")
            zf.writestr("content.xml", content)
            zf.writestr("Pictures/image1.png", png)
        pdf = d / "ods.pdf"
        res = run_engine(["export-as", str(ods), str(pdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-office-images", res
        assert pdf.is_file() and pdf.stat().st_size > 2000
        html_out = d / "ods.html"
        res = run_engine(["export-as", str(ods), str(html_out)])
        assert res.get("success"), res
        assert "data:image/png;base64," in html_out.read_text(encoding="utf-8")
        ods_preview = d / "ods-preview.html"
        res = run_engine(["preview-html", str(ods), str(ods_preview)])
        assert res.get("success"), res
        assert res.get("tool") == "office-html-images", res
        assert "data:image/png;base64," in ods_preview.read_text(encoding="utf-8")
        ods_xlsx = d / "ods.xlsx"
        res = run_engine(["export-as", str(ods), str(ods_xlsx)])
        assert res.get("success"), res
        assert res.get("tool") == "xlsx-zip-images", res
        with zipfile.ZipFile(ods_xlsx) as zf:
            assert "xl/media/image1.png" in zf.namelist()
            assert "xl/drawings/drawing1.xml" in zf.namelist()

        xlsx = d / "chart.xlsx"
        with zipfile.ZipFile(xlsx, "w") as zf:
            zf.writestr(
                "xl/worksheets/sheet1.xml",
                '<?xml version="1.0"?><worksheet '
                'xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
                "<sheetData/></worksheet>",
            )
            zf.writestr("xl/media/image1.png", png)
        xpdf = d / "xlsx.pdf"
        res = run_engine(["export-as", str(xlsx), str(xpdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-office-images", res
        assert xpdf.is_file() and xpdf.stat().st_size > 2000
        xlsx_preview = d / "xlsx-preview.html"
        res = run_engine(["preview-html", str(xlsx), str(xlsx_preview)])
        assert res.get("success"), res
        assert res.get("tool") == "office-html-images", res
        assert "data:image/png;base64," in xlsx_preview.read_text(encoding="utf-8")
        xlsm = d / "chart.xlsm"
        xlsm.write_bytes(xlsx.read_bytes())
        xmpdf = d / "xlsm.pdf"
        res = run_engine(["export-as", str(xlsm), str(xmpdf)])
        assert res.get("success"), res
        assert res.get("tool") == "pymupdf-office-images", res
        assert xmpdf.is_file() and xmpdf.stat().st_size > 2000


def test_ole_doc_xls_pdf_not_blank() -> None:
    """Merge / drop .doc/.xls used empty markdown (573-byte PDF). Share already hid those."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "OLE_SOFFICE_EXTS" in src
    assert '".doc", ".xls"' in src
    merge = (ROOT / "src/NetieSpace/Views/PdfMergeWindow.xaml.cs").read_text(encoding="utf-8")
    assert '".doc"' in merge and '".xls"' in merge
    ole = b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1" + b"not-a-real-ole" * 20
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        for name in ("memo.doc", "sheet.xls"):
            src_path = d / name
            src_path.write_bytes(ole)
            dest = d / (src_path.stem + ".pdf")
            res = run_engine(["to-pdf", str(src_path), str(dest)])
            assert not res.get("success"), res
            err = (res.get("error") or "").lower()
            assert "libreoffice" in err or "soffice" in err, res
            assert not dest.is_file() or dest.stat().st_size < 100


def test_ole_ppt_md_not_blank() -> None:
    """Share Markdown on OLE .ppt/.pps wrote a 1-byte file (empty pptx-md-images)."""
    src = ENGINE.read_text(encoding="utf-8")
    assert "def photo_office_to_markdown" in src
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    dlg = preview.split("var imageDefaultExt", 1)[1]
    assert "Markdown|*.md" in dlg
    ole = b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1" + b"not-a-real-ole" * 20
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        for name in ("deck.ppt", "show.pps"):
            src_path = d / name
            src_path.write_bytes(ole)
            dest = d / (src_path.stem + ".md")
            res = run_engine(["to-md", str(src_path), str(dest)])
            assert not res.get("success"), res
            err = (res.get("error") or "").lower()
            assert "libreoffice" in err or "ole" in err or "markdown" in err, res
            assert not dest.is_file() or dest.stat().st_size < 4
            dest2 = d / (src_path.stem + "-export.md")
            res = run_engine(["export-as", str(src_path), str(dest2)])
            assert not res.get("success"), res
            assert not dest2.is_file() or dest2.stat().st_size < 4


def test_silent_video_export_mp3() -> None:
    """Share Export as MP3 on a silent clip failed (no audio stream)."""
    import shutil

    src = ENGINE.read_text(encoding="utf-8")
    assert "anullsrc" in src
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    dlg = preview.split("var imageDefaultExt", 1)[1]
    assert "MP3 audio|*.mp3" in dlg
    if shutil.which("ffmpeg") is None:
        print("SKIP silent video mp3 -- ffmpeg missing")
        return
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        mp4 = d / "silent.mp4"
        proc = subprocess.run(
            [
                "ffmpeg", "-y", "-f", "lavfi", "-i", "color=c=red:s=160x120:d=0.3",
                "-pix_fmt", "yuv420p", str(mp4),
            ],
            capture_output=True,
            text=True,
            timeout=30,
        )
        assert proc.returncode == 0 and mp4.is_file(), (proc.stderr or "")[-300:]
        mp3 = d / "silent.mp3"
        res = run_engine(["export-as", str(mp4), str(mp3)])
        assert res.get("success"), res
        assert mp3.is_file() and mp3.stat().st_size > 200


def test_ico_preview_uses_html() -> None:
    """Space an ICO used ParseImage (WPF) then ShellInfo and hid Share. Chromium paints ICO."""
    parser = (ROOT / "src/NetieSpace/Services/ContentParserService.cs").read_text(encoding="utf-8")
    assert 'extension == ".ico"' in parser
    assert 'ParseHtmlPicture(filePath, fileName, fileInfo.Length, "image/x-icon", "ICO")' in parser
    # Still an Image suffix so Copy / Compress rasterize; do not add a new dest list.
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    assert '".svg" or ".ico" or ".heic" or ".heif" or ".webp"' in preview


def test_heic_preview_uses_engine() -> None:
    """Space a HEIC used ParseImage (WPF) then ShellInfo and hid Share. Engine PNG keeps Image."""
    parser = (ROOT / "src/NetieSpace/Services/ContentParserService.cs").read_text(encoding="utf-8")
    assert 'extension is ".heic" or ".heif"' in parser
    assert "ParseHeicPreviewAsync" in parser
    assert "ExportAsAsync" in parser
    assert "ParseHtmlPicture" in parser


def test_heic_pdf_uses_pillow() -> None:
    """Share / drop HEIC as PDF used MuPDF insert_image (no heic decoder). JPG already used Pillow."""
    engine = ENGINE.read_text(encoding="utf-8")
    assert '".heic", ".heif"' in engine
    assert "pillow_heif" in engine
    try:
        from PIL import Image
        import pillow_heif
        pillow_heif.register_heif_opener()
    except Exception:
        print("SKIP heic pdf -- pillow_heif missing (optional, not a Cursor CI dep)")
        return
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        heic = d / "sample.heic"
        Image.new("RGB", (64, 48), (180, 90, 30)).save(heic)
        pdf = d / "sample.pdf"
        res = run_engine(["export-as", str(heic), str(pdf)])
        assert res.get("success"), res
        assert pdf.is_file() and pdf.stat().st_size > 200


def test_text_markdown_drop_merge_lists() -> None:
    """Drop .text / .markdown onto a PDF used to ignore the file (lists had .txt / no markdown)."""
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    merge = (ROOT / "src/NetieSpace/Views/PdfMergeWindow.xaml.cs").read_text(encoding="utf-8")
    assert '".txt" or ".text"' in preview
    assert '".md" or ".markdown"' in preview
    assert '".text"' in merge and '".markdown"' in merge
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        notes = d / "notes.text"
        notes.write_text("hello space from .text drop\n", encoding="utf-8")
        pdf = d / "notes.pdf"
        res = run_engine(["export-as", str(notes), str(pdf)])
        assert res.get("success"), res
        assert pdf.is_file() and pdf.stat().st_size > 200


def test_code_drop_merge_lists() -> None:
    """Drop previewed .json / .py onto a PDF used to ignore the file (lists stopped at .txt)."""
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    merge = (ROOT / "src/NetieSpace/Views/PdfMergeWindow.xaml.cs").read_text(encoding="utf-8")
    assert '".json"' in preview and '".py"' in preview and '".cs"' in preview
    assert '".json"' in merge and '".py"' in merge and '".log"' in merge
    assert "*.yaml" in merge and "*.kt" in merge and "*.rs" in merge
    explorer = (ROOT / "src/NetieSpace/Services/ExplorerFileService.cs").read_text(encoding="utf-8")
    assert '".py"' in explorer and '".cs"' in explorer and '".yaml"' in explorer
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        js = d / "config.json"
        js.write_text('{"name": "netie"}\n', encoding="utf-8")
        pdf = d / "config.pdf"
        res = run_engine(["export-as", str(js), str(pdf)])
        assert res.get("success"), res
        assert pdf.is_file() and pdf.stat().st_size > 200


def test_export_as_preview_video_gif() -> None:
    """Share Export as GIF on previewed .wmv. VIDEO_EXTS used to omit C# video suffixes."""
    import shutil

    src = ENGINE.read_text(encoding="utf-8")
    assert '".wmv"' in src
    assert '".mpeg"' in src
    parser = (ROOT / "src/NetieSpace/Services/ContentParserService.cs").read_text(encoding="utf-8")
    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    assert '".wmv"' in parser
    # convertOk used to exclude Video so Share Export as never showed (engine GIF already worked).
    convert_ok = preview.split("var convertOk", 1)[1].split(";", 1)[0]
    assert "PreviewKind.Video" not in convert_ok
    assert "ShareMdRow.Visibility = docFormats" in preview
    assert "ShareExportAsRow.Visibility = formats" in preview
    dlg = preview.split("var imageDefaultExt", 1)[1]
    video_dlg = dlg.split("PreviewKind.Video", 1)[1][:280]
    assert "MP4 video|*.mp4" in video_dlg
    if shutil.which("ffmpeg") is None:
        print("SKIP video gif export -- ffmpeg missing")
        return
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        mp4 = d / "clip.mp4"
        proc = subprocess.run(
            [
                "ffmpeg", "-y", "-f", "lavfi", "-i", "color=c=blue:s=160x120:d=0.3",
                "-pix_fmt", "yuv420p", str(mp4),
            ],
            capture_output=True,
            text=True,
            timeout=30,
        )
        assert proc.returncode == 0 and mp4.is_file(), (proc.stderr or "")[-300:]
        before = mp4.read_bytes()
        res = run_engine(["export-as", str(mp4), str(mp4)])
        assert res.get("success"), res
        assert res.get("tool") == "copy", res
        assert mp4.read_bytes() == before
        wmv = d / "clip.wmv"
        wmv.write_bytes(mp4.read_bytes())
        gif = d / "clip.gif"
        res = run_engine(["export-as", str(wmv), str(gif)])
        assert res.get("success"), res
        assert res.get("tool") == "ffmpeg", res
        assert gif.is_file() and gif.stat().st_size > 200


def test_export_as_preview_audio() -> None:
    """Share Export as on previewed .wav / .mp3. ShellInfo hid the row; engine already writes audio."""
    import shutil

    preview = (ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs").read_text(encoding="utf-8")
    engine = ENGINE.read_text(encoding="utf-8")
    assert 'AUDIO_EXTS = {".mp3"' in engine or '".mp3"' in engine
    assert "IsAudioPath" in preview
    assert '".mp3" or ".wav"' in preview
    convert_ok = preview.split("var convertOk", 1)[1].split(";", 1)[0]
    assert "IsAudioPath" in convert_ok
    dlg = preview.split("var imageDefaultExt", 1)[1]
    audio_dlg = dlg.split("IsAudioPath(sourcePath)", 1)[1][:320]
    assert "WAV audio|*.wav" in audio_dlg
    if shutil.which("ffmpeg") is None:
        print("SKIP audio export -- ffmpeg missing")
        return
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        wav = d / "tone.wav"
        proc = subprocess.run(
            [
                "ffmpeg", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=0.3",
                str(wav),
            ],
            capture_output=True,
            text=True,
            timeout=30,
        )
        assert proc.returncode == 0 and wav.is_file(), (proc.stderr or "")[-300:]
        before = wav.read_bytes()
        res = run_engine(["export-as", str(wav), str(wav)])
        assert res.get("success"), res
        assert res.get("tool") == "copy", res
        assert wav.read_bytes() == before
        mp3 = d / "tone.mp3"
        res = run_engine(["export-as", str(wav), str(mp3)])
        assert res.get("success"), res
        assert res.get("tool") == "ffmpeg", res
        assert mp3.is_file() and mp3.stat().st_size > 200


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
    test_export_as_markdown_suffix()
    test_export_as_xlsx_from_csv()
    test_csv_utf16_export()
    test_export_as_rtf_odt_ods()
    test_export_as_odp()
    test_export_as_ppsx()
    test_export_as_pps()
    test_export_as_docx_xlsx_to_pdf()
    test_export_as_preview_text_to_pdf()
    test_html_export_same_file()
    test_html_photo_export()
    test_html_svg_photo_export()
    test_html_linked_css_photo_export()
    test_html_css_utf16_photo_export()
    test_html_css_at_charset_photo_export()
    test_html_css_import_photo_export()
    test_html_srcset_photo_export()
    test_html_src_spaced_eq_photo_export()
    test_html_unquoted_src_photo_export()
    test_html_src_strip_photo_export()
    test_html_imageset_photo_export()
    test_html_amp_img_photo_export()
    test_md_image_title_ref_export()
    test_md_wrap_dest_export()
    test_html_pct_href_photo_export()
    test_html_src_not_data_src_photo_export()
    test_html_num_entity_href_photo_export()
    test_md_num_entity_href_photo_export()
    test_html_heic_sibling_photo_export()
    test_html_data_uri_charset_photo_export()
    test_html_title_visible_photo_export()
    test_html_base_href_photo_export()
    test_html_bom_visible_photo_export()
    test_html_utf16_photo_export()
    test_html_meta_charset_photo_export()
    test_html_xml_encoding_photo_export()
    test_html_cjk_charset_photo_export()
    test_html_iana_charset_photo_export()
    test_html_curly_src_photo_export()
    test_md_curly_dest_export()
    test_md_utf16_photo_export()
    test_export_as_svg()
    test_svg_image_href_export()
    test_svg_use_href_export()
    test_svg_prefixed_href_export()
    test_svg_close_tag_export()
    test_svg_upper_tag_export()
    test_svg_preamble_size_utf16_export()
    test_svg_switch_a_export()
    test_html_svg_desc_title_export()
    test_html_nbsp_template_photo_export()
    test_html_shy_comment_photo_export()
    test_html_amp_nbsp_md_photo_export()
    test_office_svg_media_export()
    test_rtf_pict_photo_export()
    test_rtf_fonttbl_photo_export()
    test_rtf_dest_group_photo_export()
    test_rtf_filetbl_photo_export()
    test_rtf_themedata_photo_export()
    test_rtf_nonshppict_photo_export()
    test_rtf_annotation_photo_export()
    test_rtf_fldinst_photo_export()
    test_odf_object_replacements_export()
    test_md_image_photo_pdf()
    test_html_lrm_wj_photo_export()
    test_html_empty_svg_photo_export()
    test_html_vs16_photo_export()
    test_html_svg_white_raster_photo_export()
    test_html_svg_black_raster_photo_export()
    test_svg_unused_defs_photo_export()
    test_html_delinstr_photo_export()
    test_html_msocomsubj_photo_export()
    test_html_flddata_photo_export()
    test_html_mailmerge_photo_export()
    test_html_msoacetate_photo_export()
    test_html_msoballoon_photo_export()
    test_html_msocomhide_photo_export()
    test_html_unclosed_style_photo_export()
    test_html_unclosed_desc_photo_export()
    test_html_noframes_photo_export()
    test_html_head_xml_photo_export()
    test_html_bindata_photo_export()
    test_xml_bindata_photo_export()
    test_html_deltext_photo_export()
    test_html_aml_comment_photo_export()
    test_html_msodel_photo_export()
    test_html_wdel_photo_export()
    test_docx_del_t_photo_export()
    test_odf_annotation_photo_export()
    test_ods_annotation_photo_export()
    test_odf_changelog_photo_export()
    test_html_unclosed_comment_photo_export()
    test_md_frontmatter_photo_export()
    test_svg_ico_drop_merge_lists()
    test_webp_tif_drop_uses_engine()
    test_organize_engine_image_insert()
    test_sign_stamp_engine_raster()
    test_tiff_export_as_keeps_tiff()
    test_pdf_word_image_only()
    test_pdf_html_image_only()
    test_pdf_markdown_image_only()
    test_pptx_photo_export()
    test_photo_odp_docx_export()
    test_photo_ods_xlsx_export()
    test_ole_doc_xls_pdf_not_blank()
    test_ole_ppt_md_not_blank()
    test_silent_video_export_mp3()
    test_ico_preview_uses_html()
    test_heic_preview_uses_engine()
    test_heic_pdf_uses_pillow()
    test_text_markdown_drop_merge_lists()
    test_code_drop_merge_lists()
    test_export_as_preview_video_gif()
    test_export_as_preview_audio()
    test_pdf_ocr_force_uses_ocrmypdf_force_api()
    test_pdf_ocr_force_live()
    print("PASS test_convert_engine")

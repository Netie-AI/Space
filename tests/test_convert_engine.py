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

    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        md = d / "README.markdown"
        md.write_text("# Hello markdown suffix\n", encoding="utf-8")
        pdf = d / "README.pdf"
        res = run_engine(["export-as", str(md), str(pdf)])
        assert res.get("success"), res
        assert pdf.is_file() and pdf.stat().st_size > 200
        docx = d / "README.docx"
        res = run_engine(["export-as", str(md), str(docx)])
        assert res.get("success"), res


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
    test_export_as_rtf_odt_ods()
    test_export_as_odp()
    test_export_as_ppsx()
    test_export_as_pps()
    test_export_as_docx_xlsx_to_pdf()
    test_export_as_preview_text_to_pdf()
    test_export_as_svg()
    test_svg_ico_drop_merge_lists()
    test_webp_tif_drop_uses_engine()
    test_organize_engine_image_insert()
    test_sign_stamp_engine_raster()
    test_tiff_export_as_keeps_tiff()
    test_pdf_word_image_only()
    test_pdf_html_image_only()
    test_silent_video_export_mp3()
    test_heic_pdf_uses_pillow()
    test_text_markdown_drop_merge_lists()
    test_code_drop_merge_lists()
    test_export_as_preview_video_gif()
    test_export_as_preview_audio()
    test_pdf_ocr_force_uses_ocrmypdf_force_api()
    test_pdf_ocr_force_live()
    print("PASS test_convert_engine")

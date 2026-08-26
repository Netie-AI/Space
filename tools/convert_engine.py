#!/usr/bin/env python3
"""Netie Space conversion engine.

CLI: python convert_engine.py <action> <input> [output] [--flags]

Last stdout line is always JSON:
  {success, output_path, tool, bytes, error}

Never fail stderr-only. Unknown actions and missing deps emit JSON + exit 1.
"""
from __future__ import annotations

import csv
import hashlib
import html
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any, Callable

# ---------------------------------------------------------------------------
# Contract helpers
# ---------------------------------------------------------------------------

ACTIONS: dict[str, Callable[[list[str]], dict]] = {}


def register(name: str) -> Callable[[Callable[[list[str]], dict]], Callable[[list[str]], dict]]:
    def deco(fn: Callable[[list[str]], dict]) -> Callable[[list[str]], dict]:
        ACTIONS[name] = fn
        return fn

    return deco


def emit(
    success: bool,
    output_path: str | None = None,
    tool: str | None = None,
    bytes: int = 0,
    error: str | None = None,
) -> dict:
    payload = {
        "success": bool(success),
        "output_path": output_path,
        "tool": tool,
        "bytes": int(bytes or 0),
        "error": error,
    }
    sys.stdout.write(json.dumps(payload, ensure_ascii=True) + "\n")
    sys.stdout.flush()
    return payload


def ok(path: Path | str, tool: str) -> dict:
    p = Path(path)
    size = p.stat().st_size if p.is_file() else 0
    return emit(True, str(p), tool, size, None)


def fail(error: str, tool: str | None = None) -> dict:
    return emit(False, None, tool, 0, error)


# ---------------------------------------------------------------------------
# Arg / path helpers
# ---------------------------------------------------------------------------

IMAGE_EXTS = {
    ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff",
    ".ico", ".heic", ".heif",
}
VIDEO_EXTS = {".mp4", ".mov", ".mkv", ".webm", ".avi", ".m4v"}
AUDIO_EXTS = {".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg"}
OFFICE_EXTS = {
    ".docx", ".doc", ".odt", ".rtf", ".pptx", ".ppt", ".odp",
    ".xlsx", ".xls", ".ods", ".csv",
}
MD_EXTS = {".md", ".markdown"}
HTML_EXTS = {".html", ".htm"}
TEXT_EXTS = {".txt", ".text", ".log", ".csv"}

PIL_FORMAT = {
    ".jpg": "JPEG", ".jpeg": "JPEG", ".png": "PNG", ".webp": "WEBP",
    ".gif": "GIF", ".bmp": "BMP", ".tif": "TIFF", ".tiff": "TIFF", ".ico": "ICO",
}


def parse_tail(argv: list[str]) -> tuple[list[str], dict[str, Any]]:
    """Split positionals from --flags. --force / --tile are bool; others take a value."""
    positionals: list[str] = []
    flags: dict[str, Any] = {}
    i = 0
    bool_flags = {"force", "tile"}
    while i < len(argv):
        a = argv[i]
        if a.startswith("--") and len(a) > 2:
            key = a[2:]
            if key in bool_flags or i + 1 >= len(argv) or str(argv[i + 1]).startswith("--"):
                flags[key] = True
                i += 1
            else:
                flags[key] = argv[i + 1]
                i += 2
        else:
            positionals.append(a)
            i += 1
    return positionals, flags


def require_in(positionals: list[str]) -> Path | dict:
    if not positionals:
        return fail("missing input path")
    src = Path(positionals[0])
    if not src.is_file():
        return fail(f"input not found: {src}")
    return src


def sibling(src: Path, ext: str, tag: str | None = None) -> Path:
    ext = ext if ext.startswith(".") else f".{ext}"
    name = f"{src.stem}_{tag}{ext}" if tag else f"{src.stem}{ext}"
    dest = src.with_name(name)
    if dest.resolve() == src.resolve():
        dest = src.with_name(f"{src.stem}_converted{ext}")
    return dest


def parse_page_spec(spec: str, page_count: int) -> list[int]:
    """1-based page numbers. Mirrors C# PdfEditService.ParsePageSpec. 'all' -> every page."""
    raw = (spec or "all").strip()
    if not raw or raw.lower() == "all":
        return list(range(1, page_count + 1))
    result: list[int] = []
    for part in raw.split(","):
        part = part.strip()
        if not part:
            continue
        if "-" in part:
            bits = part.split("-", 1)
            a, b = int(bits[0]), int(bits[1])
            if a > b:
                a, b = b, a
            result.extend(range(a, b + 1))
        else:
            result.append(int(part))
    seen = sorted({n for n in result if 1 <= n <= page_count})
    return seen or list(range(1, page_count + 1))


def preview_cache_root() -> Path:
    if os.name == "nt":
        base = Path(os.environ.get("LOCALAPPDATA") or (Path.home() / "AppData" / "Local"))
        return base / "NetieSpace" / "preview-cache"
    xdg = os.environ.get("XDG_CACHE_HOME")
    root = Path(xdg) if xdg else (Path.home() / ".cache")
    return root / "NetieSpace" / "preview-cache"


def preview_key(src: Path) -> str:
    st = src.stat()
    raw = f"{src}|{st.st_size}|{st.st_mtime_ns}"
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()[:32]


def preview_bundle(src: Path) -> Path:
    dest = preview_cache_root() / preview_key(src)
    dest.mkdir(parents=True, exist_ok=True)
    return dest


def find_ffmpeg() -> str | None:
    here = Path(__file__).resolve().parent
    names = ["ffmpeg.exe", "ffmpeg"] if sys.platform == "win32" else ["ffmpeg", "ffmpeg.exe"]
    for name in names:
        cand = here / "ffmpeg" / name
        if cand.is_file():
            return str(cand)
    return shutil.which("ffmpeg")


def try_import(name: str):
    try:
        return __import__(name)
    except Exception:
        return None


# ---------------------------------------------------------------------------
# Markdown / HTML / PDF text conversion
# ---------------------------------------------------------------------------

def via_markitdown(src: Path) -> tuple[str, str] | None:
    try:
        from markitdown import MarkItDown
    except Exception:
        return None
    md = MarkItDown()
    result = md.convert(str(src))
    text = (getattr(result, "text_content", None) or "").strip()
    if not text:
        return None
    return text, "markitdown"


def via_pdfmd(src: Path) -> tuple[str, str] | None:
    pdfmd = try_import("pdfmd")
    if pdfmd is None:
        return None
    for attr in ("convert", "pdf_to_markdown", "to_markdown"):
        fn = getattr(pdfmd, attr, None)
        if callable(fn):
            try:
                out = fn(str(src))
                text = out if isinstance(out, str) else str(out)
                if text.strip():
                    return text, "pdfmd"
            except Exception:
                continue
    return None


def via_pymupdf4llm(src: Path) -> tuple[str, str] | None:
    try:
        import pymupdf4llm
    except Exception:
        return None
    try:
        text = pymupdf4llm.to_markdown(str(src))
    except Exception:
        return None
    if text and str(text).strip():
        return str(text), "pymupdf4llm"
    return None


def via_pymupdf_text(src: Path) -> tuple[str, str] | None:
    try:
        import pymupdf
    except Exception:
        return None
    doc = pymupdf.open(str(src))
    try:
        parts = [page.get_text() for page in doc]
    finally:
        doc.close()
    text = "\n\n".join(p.strip() for p in parts if p and p.strip())
    if not text:
        return None
    return text, "pymupdf"


def via_pptx2md(src: Path) -> tuple[str, str] | None:
    try:
        import pptx2md
    except Exception:
        pptx2md = None  # type: ignore
    if pptx2md is not None:
        for attr in ("convert", "to_markdown"):
            fn = getattr(pptx2md, attr, None)
            if callable(fn):
                try:
                    out = fn(str(src))
                    text = out if isinstance(out, str) else str(out)
                    if text.strip():
                        return text, "pptx2md"
                except Exception:
                    continue
    slides = pptx_slide_texts(src)
    if slides:
        chunks = [f"## Slide {i}\n\n{t}" for i, t in enumerate(slides, 1) if t.strip()]
        return "\n\n".join(chunks), "pptx-zip"
    return None


def pptx_slide_texts(src: Path) -> list[str]:
    texts: list[str] = []
    try:
        with zipfile.ZipFile(src) as zf:
            names = sorted(
                n for n in zf.namelist()
                if n.startswith("ppt/slides/slide") and n.endswith(".xml")
                and "/_rels/" not in n
            )
            for name in names:
                root = ET.fromstring(zf.read(name))
                bits = [
                    (el.text or "")
                    for el in root.iter()
                    if el.tag.endswith("}t") and el.text
                ]
                texts.append("\n".join(bits).strip())
    except Exception:
        return []
    return texts


def pptx_extract_media(src: Path, dest_dir: Path) -> list[Path]:
    dest_dir.mkdir(parents=True, exist_ok=True)
    saved: list[Path] = []
    try:
        with zipfile.ZipFile(src) as zf:
            media = [n for n in zf.namelist() if n.startswith("ppt/media/")]
            for name in media:
                out = dest_dir / Path(name).name
                out.write_bytes(zf.read(name))
                saved.append(out)
    except Exception:
        return []
    return saved


def docx_plain_html(src: Path) -> str | None:
    try:
        with zipfile.ZipFile(src) as zf:
            raw = zf.read("word/document.xml")
    except Exception:
        return None
    root = ET.fromstring(raw)
    paras: list[str] = []
    for p in root.iter():
        if not p.tag.endswith("}p"):
            continue
        bits = [(el.text or "") for el in p.iter() if el.tag.endswith("}t")]
        line = "".join(bits).strip()
        if line:
            paras.append(f"<p>{html.escape(line)}</p>")
    if not paras:
        return None
    return "\n".join(paras)


def file_to_markdown(src: Path) -> tuple[str, str]:
    ext = src.suffix.lower()
    if ext in MD_EXTS or ext in TEXT_EXTS:
        return src.read_text(encoding="utf-8", errors="replace"), "copy"

    if ext == ".pdf":
        for fn in (via_pdfmd, via_pymupdf4llm, via_markitdown, via_pymupdf_text):
            got = fn(src)
            if got:
                return got
        raise RuntimeError("no PDF-to-markdown backend (pdfmd / pymupdf4llm / markitdown / pymupdf)")

    if ext in {".pptx", ".ppt", ".odp"}:
        got = via_pptx2md(src) or via_markitdown(src)
        if got:
            return got
        raise RuntimeError("no PPTX-to-markdown backend (pptx2md / markitdown)")

    if ext in IMAGE_EXTS:
        got = via_markitdown(src)
        if got:
            return got
        return f"![{src.name}]({src.name})\n", "image-stub"

    if ext == ".rtf":
        return rtf_to_text(src), "rtf-text"
    if ext == ".odt":
        return odt_to_text(src), "odt-zip"
    if ext == ".ods":
        rows = ods_to_rows(src)
        return "\n".join(" | ".join(r) for r in rows) + "\n", "ods-zip"

    got = via_markitdown(src)
    if got:
        return got
    if ext in {".html", ".htm"}:
        text = src.read_text(encoding="utf-8", errors="replace")
        text = re.sub(r"(?is)<script.*?>.*?</script>", "", text)
        text = re.sub(r"(?is)<style.*?>.*?</style>", "", text)
        text = re.sub(r"(?s)<[^>]+>", " ", text)
        return re.sub(r"\s+", " ", text).strip() + "\n", "html-strip"
    raise RuntimeError(f"no markdown converter for {ext or 'unknown'} (need markitdown)")


def markdown_to_html(text: str) -> str:
    mdlib = try_import("markdown")
    if mdlib is not None and hasattr(mdlib, "markdown"):
        try:
            return mdlib.markdown(text, extensions=["extra", "tables", "fenced_code"])
        except Exception:
            return mdlib.markdown(text)
    return "<pre>" + html.escape(text) + "</pre>"


def wrap_html(body: str, title: str = "Preview") -> str:
    return (
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\">"
        f"<title>{html.escape(title)}</title>"
        "<style>body{font-family:system-ui,sans-serif;margin:24px;color:#222}"
        "article.office-body{max-width:900px;margin:0 auto}"
        "img{max-width:100%;height:auto}"
        "section.slide{margin:0 0 2em;padding:0 0 1em;border-bottom:1px solid #ddd}"
        "</style></head><body>"
        f"<article class=\"office-body\">{body}</article>"
        "</body></html>\n"
    )


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


_XML_ILLEGAL = re.compile(r"[\x00-\x08\x0b\x0c\x0e-\x1f]")


def write_docx(dest: Path, text: str) -> str:
    """Minimal Office Open XML .docx (stdlib zip). Share Export as Word used this."""
    dest.parent.mkdir(parents=True, exist_ok=True)
    raw = _XML_ILLEGAL.sub("", (text or "").replace("\r\n", "\n").replace("\r", "\n"))
    paragraphs = [p.strip() for p in raw.split("\n") if p.strip()]
    if not paragraphs:
        paragraphs = [""]

    def esc(s: str) -> str:
        return html.escape(s, quote=False)

    body = "".join(
        f'<w:p><w:r><w:t xml:space="preserve">{esc(p)}</w:t></w:r></w:p>'
        for p in paragraphs
    )
    document_xml = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'
        f"<w:body>{body}<w:sectPr/></w:body></w:document>"
    )
    content_types = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
        '<Default Extension="xml" ContentType="application/xml"/>'
        '<Override PartName="/word/document.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'
        "</Types>"
    )
    rels = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" '
        'Target="word/document.xml"/>'
        "</Relationships>"
    )
    word_rels = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>'
    )
    with zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("[Content_Types].xml", content_types)
        zf.writestr("_rels/.rels", rels)
        zf.writestr("word/document.xml", document_xml)
        zf.writestr("word/_rels/document.xml.rels", word_rels)
    return "docx-zip"


def file_to_docx(src: Path, dest: Path) -> str:
    if src.suffix.lower() == ".docx":
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dest)
        return "copy"
    try:
        text, _tool = file_to_markdown(src)
        return write_docx(dest, text)
    except RuntimeError:
        tool = via_soffice(src, dest)
        if tool:
            return tool
        raise


def _xlsx_col(index: int) -> str:
    n = index + 1
    out = ""
    while n:
        n, rem = divmod(n - 1, 26)
        out = chr(65 + rem) + out
    return out


def write_xlsx(dest: Path, rows: list[list[str]]) -> str:
    """Minimal Office Open XML .xlsx (stdlib zip). Share Export as Excel used this."""
    dest.parent.mkdir(parents=True, exist_ok=True)
    if not rows:
        rows = [[""]]

    def esc(s: str) -> str:
        return html.escape(_XML_ILLEGAL.sub("", s), quote=False)

    sheet_rows = []
    for r_i, row in enumerate(rows, start=1):
        cells = []
        for c_i, value in enumerate(row):
            ref = f"{_xlsx_col(c_i)}{r_i}"
            cells.append(
                f'<c r="{ref}" t="inlineStr"><is><t xml:space="preserve">{esc(str(value))}</t></is></c>'
            )
        sheet_rows.append(f'<row r="{r_i}">{"".join(cells)}</row>')
    sheet_xml = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
        f'<sheetData>{"".join(sheet_rows)}</sheetData></worksheet>'
    )
    workbook_xml = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
        'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
        '<sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>'
    )
    workbook_rels = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" '
        'Target="worksheets/sheet1.xml"/>'
        "</Relationships>"
    )
    root_rels = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" '
        'Target="xl/workbook.xml"/>'
        "</Relationships>"
    )
    content_types = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
        '<Default Extension="xml" ContentType="application/xml"/>'
        '<Override PartName="/xl/workbook.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
        '<Override PartName="/xl/worksheets/sheet1.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
        "</Types>"
    )
    with zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("[Content_Types].xml", content_types)
        zf.writestr("_rels/.rels", root_rels)
        zf.writestr("xl/workbook.xml", workbook_xml)
        zf.writestr("xl/_rels/workbook.xml.rels", workbook_rels)
        zf.writestr("xl/worksheets/sheet1.xml", sheet_xml)
    return "xlsx-zip"


def rtf_to_text(src: Path) -> str:
    """Best-effort RTF -> plain text. Share Export as PDF/Word on .rtf used markitdown."""
    s = src.read_bytes().decode("latin-1", errors="replace")
    s = re.sub(
        r"\\'([0-9a-fA-F]{2})",
        lambda m: bytes.fromhex(m.group(1)).decode("latin-1"),
        s,
    )
    s = re.sub(r"\\par[d]?\b", "\n", s)
    s = re.sub(r"\\line\b", "\n", s)
    s = re.sub(r"\\tab\b", "\t", s)
    for _ in range(8):
        n = re.sub(r"\{\\\*[^{}]*\}", "", s)
        if n == s:
            break
        s = n
    s = re.sub(r"\\[a-zA-Z]+-?\d* ?", "", s)
    s = re.sub(r"[{}]", "", s)
    s = s.replace("\\", "")
    return re.sub(r"[ \t]+\n", "\n", s).strip() + "\n"


def _odf_content_root(src: Path) -> ET.Element:
    try:
        with zipfile.ZipFile(src) as zf:
            xml = zf.read("content.xml")
    except (zipfile.BadZipFile, KeyError, OSError) as exc:
        raise RuntimeError(f"not a valid OpenDocument file: {exc}") from exc
    return ET.fromstring(xml)


def odt_to_text(src: Path) -> str:
    root = _odf_content_root(src)
    paras = [
        "".join(p.itertext()).strip()
        for p in root.iter("{urn:oasis:names:tc:opendocument:xmlns:text:1.0}p")
    ]
    return "\n".join(t for t in paras if t) + ("\n" if any(paras) else "")


def ods_to_rows(src: Path) -> list[list[str]]:
    root = _odf_content_root(src)
    ns = "{urn:oasis:names:tc:opendocument:xmlns:table:1.0}"
    rows: list[list[str]] = []
    for row in root.iter(f"{ns}table-row"):
        cells: list[str] = []
        for cell in row.findall(f"{ns}table-cell"):
            raw_rep = cell.attrib.get(f"{ns}number-columns-repeated", "1")
            try:
                repeat = int(raw_rep)
            except ValueError:
                repeat = 1
            repeat = min(max(repeat, 1), 32)
            text = "".join(cell.itertext()).strip()
            if not text and repeat > 8:
                continue
            cells.extend([text] * repeat)
        if any(cells):
            rows.append(cells)
    return rows or [[""]]


def csv_to_rows(src: Path) -> list[list[str]]:
    text = src.read_text(encoding="utf-8", errors="replace")
    sample = text[:4096]
    try:
        dialect = csv.Sniffer().sniff(sample, delimiters=",\t;|")
    except csv.Error:
        dialect = csv.excel
    return [list(row) for row in csv.reader(text.splitlines(), dialect)]


def find_soffice() -> str | None:
    for name in ("soffice", "libreoffice"):
        found = shutil.which(name)
        if found:
            return found
    for c in (
        r"C:\Program Files\LibreOffice\program\soffice.exe",
        r"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
    ):
        if Path(c).is_file():
            return c
    return None


def via_soffice(src: Path, dest: Path) -> str | None:
    exe = find_soffice()
    if not exe:
        return None
    dest.parent.mkdir(parents=True, exist_ok=True)
    ext = dest.suffix.lower().lstrip(".")
    if not ext:
        return None
    with tempfile.TemporaryDirectory() as td:
        proc = subprocess.run(
            [exe, "--headless", "--norestore", "--convert-to", ext, "--outdir", td, str(src)],
            capture_output=True,
            text=True,
            timeout=180,
        )
        produced = list(Path(td).glob(f"*.{ext}"))
        if proc.returncode != 0 or not produced:
            return None
        shutil.copy2(produced[0], dest)
        return "libreoffice"


def file_to_xlsx(src: Path, dest: Path) -> str:
    ext = src.suffix.lower()
    if ext == ".xlsx":
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dest)
        return "copy"
    if ext == ".csv":
        return write_xlsx(dest, csv_to_rows(src))
    if ext == ".ods":
        return write_xlsx(dest, ods_to_rows(src))
    tool = via_soffice(src, dest)
    if tool:
        return tool
    raise RuntimeError(
        "LibreOffice required to export this file as .xlsx (install soffice, or export CSV)"
    )


def file_to_pptx(src: Path, dest: Path) -> str:
    if src.suffix.lower() == ".pptx":
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dest)
        return "copy"
    tool = via_soffice(src, dest)
    if tool:
        return tool
    raise RuntimeError("LibreOffice required to export this file as .pptx")


# ---------------------------------------------------------------------------
# PDF write helpers (pymupdf)
# ---------------------------------------------------------------------------

def require_pymupdf():
    try:
        import pymupdf
        return pymupdf
    except Exception as e:
        raise RuntimeError(f"pymupdf missing: {e}") from e


def markdown_to_pdf(text: str, dest: Path) -> str:
    pymupdf = require_pymupdf()
    dest.parent.mkdir(parents=True, exist_ok=True)
    body = markdown_to_html(text)
    story_html = (
        "<html><body style=\"font-family:sans-serif;font-size:11pt\">"
        f"{body}</body></html>"
    )
    if hasattr(pymupdf, "Story") and hasattr(pymupdf, "DocumentWriter"):
        try:
            mediabox = pymupdf.paper_rect("letter")
            story = pymupdf.Story(story_html)
            writer = pymupdf.DocumentWriter(str(dest))
            more = True
            while more:
                device = writer.begin_page(mediabox)
                more, _ = story.place(mediabox + (36, 36, -36, -36))
                story.draw(device)
                writer.end_page()
            writer.close()
            if dest.is_file() and dest.stat().st_size > 0:
                return "pymupdf-story"
        except Exception:
            if dest.exists():
                try:
                    dest.unlink()
                except OSError:
                    pass
    doc = pymupdf.open()
    margin, fontsize, leading = 36, 11, 14
    words = text.replace("\r\n", "\n").split("\n")
    page = doc.new_page(width=612, height=792)
    y = margin + fontsize
    for line in words:
        chunk = line if line else " "
        while chunk:
            if y > 792 - margin:
                page = doc.new_page(width=612, height=792)
                y = margin + fontsize
            take = chunk[:110]
            page.insert_text((margin, y), take, fontsize=fontsize)
            chunk = chunk[110:]
            y += leading
    doc.save(str(dest))
    doc.close()
    return "pymupdf"


def image_to_pdf(src: Path, dest: Path) -> str:
    pymupdf = require_pymupdf()
    dest.parent.mkdir(parents=True, exist_ok=True)
    doc = pymupdf.open()
    img = pymupdf.open(str(src))
    try:
        rect = img[0].rect
        page = doc.new_page(width=rect.width, height=rect.height)
        page.insert_image(page.rect, filename=str(src))
    finally:
        img.close()
    doc.save(str(dest))
    doc.close()
    return "pymupdf"


def pdf_copy(src: Path, dest: Path) -> str:
    dest.parent.mkdir(parents=True, exist_ok=True)
    if src.resolve() != dest.resolve():
        shutil.copy2(src, dest)
    return "copy"


def file_to_pdf(src: Path, dest: Path) -> str:
    ext = src.suffix.lower()
    if ext == ".pdf":
        return pdf_copy(src, dest)
    if ext in IMAGE_EXTS:
        return image_to_pdf(src, dest)
    if ext in MD_EXTS or ext in TEXT_EXTS or ext in HTML_EXTS:
        if ext in HTML_EXTS:
            text = src.read_text(encoding="utf-8", errors="replace")
            # Story can consume HTML directly
            return markdown_to_pdf(text, dest)
        text = src.read_text(encoding="utf-8", errors="replace")
        return markdown_to_pdf(text, dest)
    text, _tool = file_to_markdown(src)
    return markdown_to_pdf(text, dest)


# ---------------------------------------------------------------------------
# Image / media export-as
# ---------------------------------------------------------------------------

def load_pil_image(src: Path):
    try:
        from PIL import Image
    except Exception as e:
        raise RuntimeError(f"Pillow missing: {e}") from e
    if src.suffix.lower() in {".heic", ".heif"}:
        try:
            from pillow_heif import register_heif_opener
            register_heif_opener()
        except Exception:
            pass
    return Image.open(str(src))


def save_pil(im, dest: Path, fmt: str) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    frames = []
    n = getattr(im, "n_frames", 1) or 1
    try:
        for i in range(n):
            im.seek(i)
            frames.append(im.convert("RGBA").copy())
    except Exception:
        frames = [im.convert("RGBA")]
    if fmt == "JPEG":
        converted = []
        for fr in frames:
            bg = fr.convert("RGB")
            if fr.mode == "RGBA":
                canvas = __import__("PIL").Image.new("RGB", fr.size, (255, 255, 255))
                canvas.paste(fr, mask=fr.split()[-1])
                bg = canvas
            converted.append(bg)
        frames = converted
    if fmt == "ICO":
        fr = frames[0]
        if fr.width > 256 or fr.height > 256:
            fr = fr.copy()
            fr.thumbnail((256, 256))
        fr.save(str(dest), format="ICO")
        return
    first = frames[0]
    save_kw: dict[str, Any] = {"format": fmt}
    if fmt == "JPEG":
        save_kw["quality"] = 90
    if len(frames) > 1 and fmt in {"WEBP", "GIF", "TIFF", "PNG"}:
        save_kw["save_all"] = True
        save_kw["append_images"] = frames[1:]
        save_kw["loop"] = 0
        durs = []
        try:
            im.seek(0)
            for i in range(len(frames)):
                im.seek(i)
                durs.append(im.info.get("duration", 120))
        except Exception:
            durs = [120] * len(frames)
        save_kw["duration"] = durs
    first.save(str(dest), **save_kw)


def convert_image(src: Path, dest: Path) -> str:
    fmt = PIL_FORMAT.get(dest.suffix.lower())
    if not fmt:
        raise RuntimeError(f"unsupported image target: {dest.suffix}")
    im = load_pil_image(src)
    try:
        save_pil(im, dest, fmt)
    finally:
        im.close()
    return "pillow"


def pdf_to_images(src: Path, dest: Path) -> str:
    pymupdf = require_pymupdf()
    dest.parent.mkdir(parents=True, exist_ok=True)
    doc = pymupdf.open(str(src))
    try:
        if doc.page_count < 1:
            raise RuntimeError("PDF has no pages")
        for i, page in enumerate(doc):
            pix = page.get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False)
            if i == 0:
                out = dest
            else:
                out = dest.with_name(f"{dest.stem}_{i + 1}{dest.suffix}")
            # pixmap.save infers from suffix
            pix.save(str(out))
    finally:
        doc.close()
    return "pymupdf"


def export_media(src: Path, dest: Path) -> str:
    ffmpeg = find_ffmpeg()
    if not ffmpeg:
        raise RuntimeError("ffmpeg missing (tools/ffmpeg/ffmpeg or PATH)")
    dest.parent.mkdir(parents=True, exist_ok=True)
    ext = dest.suffix.lower()
    cmd = [ffmpeg, "-y", "-i", str(src)]
    if ext == ".gif":
        cmd += ["-vf", "fps=12,scale=480:-1:flags=lanczos", "-an", str(dest)]
    elif ext == ".webm":
        cmd += ["-c:v", "libvpx-vp9", "-c:a", "libopus", "-b:v", "1M", str(dest)]
    elif ext == ".mp3":
        cmd += ["-vn", "-acodec", "libmp3lame", "-q:a", "4", str(dest)]
    elif ext == ".wav":
        cmd += ["-vn", "-acodec", "pcm_s16le", str(dest)]
    elif ext == ".mkv":
        cmd += ["-c", "copy", str(dest)]
    else:
        cmd += [str(dest)]
    proc = subprocess.run(cmd, capture_output=True, text=True, timeout=600)
    if proc.returncode != 0 or not dest.is_file():
        # copy-codec mkv can fail; retry with encode
        if ext == ".mkv":
            proc = subprocess.run(
                [ffmpeg, "-y", "-i", str(src), str(dest)],
                capture_output=True, text=True, timeout=600,
            )
        if proc.returncode != 0 or not dest.is_file():
            err = (proc.stderr or proc.stdout or "ffmpeg failed")[-400:]
            raise RuntimeError(err.strip() or "ffmpeg failed")
    return "ffmpeg"


# ---------------------------------------------------------------------------
# PDF ops
# ---------------------------------------------------------------------------

def pdf_watermark(src: Path, dest: Path, text: str, opacity: float, angle: int, pages: str) -> str:
    pymupdf = require_pymupdf()
    if not text:
        raise RuntimeError("watermark --text is required")
    dest.parent.mkdir(parents=True, exist_ok=True)
    doc = pymupdf.open(str(src))
    try:
        selected = parse_page_spec(pages, doc.page_count)
        for num in selected:
            page = doc[num - 1]
            rect = page.rect
            # insert_text/insert_textbox rotate must be 0/90/180/270.
            rot = int(angle) if int(angle) in (0, 90, 180, 270) else 0
            fontsize = max(24, int(min(rect.width, rect.height) / 6))
            # Text layer (stress test asserts get_text contains the string).
            page.insert_textbox(
                rect,
                text,
                fontsize=fontsize,
                fontname="helv",
                color=(0.55, 0.55, 0.55),
                align=getattr(pymupdf, "TEXT_ALIGN_CENTER", 1),
                rotate=rot,
                overlay=True,
            )
            # Optional diagonal stamp when --angle is not a right angle.
            if int(angle) not in (0, 90, 180, 270):
                try:
                    center = rect.center
                    mat = pymupdf.Matrix(int(angle))
                    kw = dict(
                        fontsize=max(18, int(min(rect.width, rect.height) / 8)),
                        fontname="helv",
                        color=(0.55, 0.55, 0.55),
                        overlay=True,
                        morph=(center, mat),
                    )
                    try:
                        page.insert_text(center, text, fill_opacity=float(opacity), **kw)
                    except TypeError:
                        page.insert_text(center, text, **kw)
                except Exception:
                    pass
        doc.save(str(dest))
    finally:
        doc.close()
    return "pymupdf"


def pdf_protect(src: Path, dest: Path, password: str, owner: str | None, perm: str | None) -> str:
    pymupdf = require_pymupdf()
    if not password:
        raise RuntimeError("protect --password is required")
    dest.parent.mkdir(parents=True, exist_ok=True)
    perm_map = {
        "print": getattr(pymupdf, "PDF_PERM_PRINT", 4),
        "modify": getattr(pymupdf, "PDF_PERM_MODIFY", 8),
        "copy": getattr(pymupdf, "PDF_PERM_COPY", 16),
        "annotate": getattr(pymupdf, "PDF_PERM_ANNOTATE", 32),
    }
    flags = 0
    if perm:
        for part in perm.split(","):
            key = part.strip().lower()
            if key in perm_map:
                flags |= perm_map[key]
    else:
        flags = perm_map["print"] | perm_map["copy"]
    enc = getattr(pymupdf, "PDF_ENCRYPT_AES_256", 5)
    doc = pymupdf.open(str(src))
    try:
        doc.save(
            str(dest),
            encryption=enc,
            user_pw=password,
            owner_pw=owner or password,
            permissions=flags,
        )
    finally:
        doc.close()
    return "pymupdf"


def pdf_unlock(src: Path, dest: Path, password: str) -> dict | str:
    pymupdf = require_pymupdf()
    if not password:
        return fail("unlock --password is required", "pymupdf")
    dest.parent.mkdir(parents=True, exist_ok=True)
    doc = pymupdf.open(str(src))
    try:
        if doc.needs_pass:
            if not doc.authenticate(password):
                doc.close()
                return fail("bad password", "pymupdf")
        enc_none = getattr(pymupdf, "PDF_ENCRYPT_NONE", 0)
        try:
            doc.save(str(dest), encryption=enc_none)
        except Exception:
            doc.save(str(dest))
    finally:
        try:
            doc.close()
        except Exception:
            pass
    return "pymupdf"


def pdf_has_text(src: Path) -> bool:
    try:
        pymupdf = require_pymupdf()
    except RuntimeError:
        return False
    doc = pymupdf.open(str(src))
    try:
        return any((page.get_text() or "").strip() for page in doc)
    finally:
        doc.close()


def pdf_ocr(src: Path, dest: Path, lang: str, force: bool) -> str:
    dest.parent.mkdir(parents=True, exist_ok=True)
    if pdf_has_text(src) and not force:
        shutil.copy2(src, dest)
        return "skip-text"
    try:
        import ocrmypdf
    except Exception:
        raise RuntimeError("ocrmypdf missing; install ocrmypdf and system Tesseract")
    if shutil.which("tesseract") is None:
        raise RuntimeError("tesseract missing; install Tesseract-OCR and add it to PATH")
    # ocrmypdf 17: skip_text=False is the default (abort if text exists),
    # not "force OCR". --force must send force_ocr / mode=force.
    if force:
        ocrmypdf.ocr(
            str(src),
            str(dest),
            language=lang or "eng",
            force_ocr=True,
            deskew=True,
        )
    else:
        ocrmypdf.ocr(
            str(src),
            str(dest),
            language=lang or "eng",
            skip_text=True,
            deskew=True,
        )
    if not dest.is_file():
        raise RuntimeError("ocrmypdf produced no output")
    return "ocrmypdf"


# ---------------------------------------------------------------------------
# Preview HTML (mammoth / pptx)
# ---------------------------------------------------------------------------

def mammoth_to_html(src: Path, dest_html: Path) -> str:
    try:
        import mammoth
    except Exception:
        body = docx_plain_html(src)
        if body is None:
            raise RuntimeError("mammoth missing and DOCX zip parse failed")
        write_text(dest_html, wrap_html(body, src.name))
        return "docx-zip"
    media_dir = dest_html.parent / "media"
    media_dir.mkdir(parents=True, exist_ok=True)
    counter = {"n": 0}

    def convert_image(image):
        counter["n"] += 1
        ext = ".png"
        try:
            ct = (image.content_type or "").lower()
            if "jpeg" in ct or "jpg" in ct:
                ext = ".jpg"
            elif "gif" in ct:
                ext = ".gif"
            elif "webp" in ct:
                ext = ".webp"
        except Exception:
            pass
        name = f"image{counter['n']}{ext}"
        out = media_dir / name
        with image.open() as blob:
            data = blob.read()
        out.write_bytes(data)
        return {"src": f"media/{name}"}

    with open(src, "rb") as fh:
        result = mammoth.convert_to_html(
            fh,
            convert_image=mammoth.images.img_element(convert_image),
        )
    body = result.value or "<p></p>"
    write_text(dest_html, wrap_html(body, src.name))
    return "mammoth"


def pptx_to_html(src: Path, dest_html: Path) -> str:
    assets = dest_html.parent / "assets"
    images = pptx_extract_media(src, assets)
    slides = pptx_slide_texts(src)
    parts: list[str] = []
    if not slides and not images:
        got = via_pptx2md(src) or via_markitdown(src)
        if got:
            body = markdown_to_html(got[0])
            write_text(dest_html, wrap_html(body, src.name))
            return got[1]
        raise RuntimeError("could not read PPTX slides")
    n = max(len(slides), 1)
    for i in range(n):
        text = slides[i] if i < len(slides) else ""
        paras = "".join(f"<p>{html.escape(line)}</p>" for line in text.splitlines() if line.strip())
        img_html = ""
        if i < len(images):
            img_html = f'<p><img src="assets/{images[i].name}" alt="slide {i + 1}"></p>'
        parts.append(f'<section class="slide"><h2>Slide {i + 1}</h2>{paras}{img_html}</section>')
    extra = images[n:] if len(images) > n else []
    for img in extra:
        parts.append(f'<p><img src="assets/{img.name}" alt="{html.escape(img.name)}"></p>')
    write_text(dest_html, wrap_html("\n".join(parts), src.name))
    return "pptx-zip"


# ---------------------------------------------------------------------------
# Actions
# ---------------------------------------------------------------------------

@register("to-md")
def action_to_md(argv: list[str]) -> dict:
    positionals, _flags = parse_tail(argv)
    src = require_in(positionals)
    if isinstance(src, dict):
        return src
    dest = Path(positionals[1]) if len(positionals) > 1 else sibling(src, ".md")
    text, tool = file_to_markdown(src)
    write_text(dest, text if text.endswith("\n") else text + "\n")
    return ok(dest, tool)


@register("to-pdf")
def action_to_pdf(argv: list[str]) -> dict:
    positionals, _flags = parse_tail(argv)
    src = require_in(positionals)
    if isinstance(src, dict):
        return src
    dest = Path(positionals[1]) if len(positionals) > 1 else sibling(src, ".pdf")
    tool = file_to_pdf(src, dest)
    return ok(dest, tool)


@register("preview")
def action_preview(argv: list[str]) -> dict:
    positionals, _flags = parse_tail(argv)
    src = require_in(positionals)
    if isinstance(src, dict):
        return src
    if len(positionals) > 1:
        dest = Path(positionals[1])
    else:
        dest = preview_bundle(src) / "preview.md"
    text, tool = file_to_markdown(src)
    write_text(dest, text if text.endswith("\n") else text + "\n")
    return ok(dest, tool)


@register("preview-html")
def action_preview_html(argv: list[str]) -> dict:
    positionals, _flags = parse_tail(argv)
    src = require_in(positionals)
    if isinstance(src, dict):
        return src
    dest = Path(positionals[1]) if len(positionals) > 1 else (preview_bundle(src) / "preview.html")
    dest.parent.mkdir(parents=True, exist_ok=True)
    ext = src.suffix.lower()
    if ext in {".docx", ".doc"}:
        tool = mammoth_to_html(src, dest)
        return ok(dest, tool)
    if ext in HTML_EXTS:
        shutil.copy2(src, dest)
        return ok(dest, "copy")
    text, tool = file_to_markdown(src)
    write_text(dest, wrap_html(markdown_to_html(text), src.name))
    return ok(dest, tool)


@register("preview-pptx-html")
def action_preview_pptx_html(argv: list[str]) -> dict:
    positionals, _flags = parse_tail(argv)
    src = require_in(positionals)
    if isinstance(src, dict):
        return src
    dest = Path(positionals[1]) if len(positionals) > 1 else (preview_bundle(src) / "preview.html")
    dest.parent.mkdir(parents=True, exist_ok=True)
    tool = pptx_to_html(src, dest)
    return ok(dest, tool)


@register("export-as")
def action_export_as(argv: list[str]) -> dict:
    positionals, _flags = parse_tail(argv)
    src = require_in(positionals)
    if isinstance(src, dict):
        return src
    if len(positionals) < 2:
        return fail("export-as requires <input> <output>")
    dest = Path(positionals[1])
    src_ext = src.suffix.lower()
    dest_ext = dest.suffix.lower()
    if dest_ext in {"", ".xyz"} or dest_ext not in (
        IMAGE_EXTS | VIDEO_EXTS | AUDIO_EXTS | {".pdf", ".gif"} | MD_EXTS | HTML_EXTS | OFFICE_EXTS | TEXT_EXTS
    ):
        # .gif is in IMAGE_EXTS; keep the unsupported-target guard for junk suffixes.
        if dest_ext not in (IMAGE_EXTS | VIDEO_EXTS | AUDIO_EXTS | {".pdf"} | MD_EXTS | HTML_EXTS | {".txt"}):
            return fail(f"unsupported target: {dest_ext or '(none)'}")
    if dest_ext in IMAGE_EXTS:
        if src_ext == ".pdf":
            return ok(dest, pdf_to_images(src, dest))
        if src_ext in IMAGE_EXTS:
            return ok(dest, convert_image(src, dest))
        if dest_ext == ".gif" and src_ext in VIDEO_EXTS:
            return ok(dest, export_media(src, dest))
        return fail(f"cannot export {src_ext or 'file'} as {dest_ext}")
    if dest_ext == ".pdf":
        return ok(dest, file_to_pdf(src, dest))
    if dest_ext in MD_EXTS:
        text, tool = file_to_markdown(src)
        write_text(dest, text if text.endswith("\n") else text + "\n")
        return ok(dest, tool)
    if dest_ext in HTML_EXTS:
        if src_ext in {".docx", ".doc"}:
            return ok(dest, mammoth_to_html(src, dest))
        if src_ext in {".pptx", ".ppt", ".odp"}:
            return ok(dest, pptx_to_html(src, dest))
        if src_ext in HTML_EXTS:
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dest)
            return ok(dest, "copy")
        text, tool = file_to_markdown(src)
        write_text(dest, wrap_html(markdown_to_html(text), src.name))
        return ok(dest, tool)
    if dest_ext in VIDEO_EXTS or dest_ext in AUDIO_EXTS or (
        dest_ext == ".gif" and src_ext in VIDEO_EXTS
    ):
        return ok(dest, export_media(src, dest))
    if dest_ext in OFFICE_EXTS:
        if dest_ext == src_ext:
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dest)
            return ok(dest, "copy")
        if dest_ext == ".docx":
            return ok(dest, file_to_docx(src, dest))
        if dest_ext == ".xlsx":
            return ok(dest, file_to_xlsx(src, dest))
        if dest_ext == ".pptx":
            return ok(dest, file_to_pptx(src, dest))
        return fail(f"unsupported target: {dest_ext}")
    return fail(f"unsupported target: {dest_ext or '(none)'}")


@register("pdf-watermark")
def action_pdf_watermark(argv: list[str]) -> dict:
    positionals, flags = parse_tail(argv)
    src = require_in(positionals)
    if isinstance(src, dict):
        return src
    if len(positionals) < 2:
        return fail("pdf-watermark requires <input> <output>")
    dest = Path(positionals[1])
    text = str(flags.get("text") or "")
    try:
        opacity = float(flags.get("opacity", 0.25))
    except (TypeError, ValueError):
        opacity = 0.25
    try:
        angle = int(flags.get("angle", 45))
    except (TypeError, ValueError):
        angle = 45
    pages = str(flags.get("pages", "all"))
    tool = pdf_watermark(src, dest, text, opacity, angle, pages)
    return ok(dest, tool)


@register("pdf-protect")
def action_pdf_protect(argv: list[str]) -> dict:
    positionals, flags = parse_tail(argv)
    src = require_in(positionals)
    if isinstance(src, dict):
        return src
    if len(positionals) < 2:
        return fail("pdf-protect requires <input> <output>")
    dest = Path(positionals[1])
    password = str(flags.get("password") or "")
    owner = flags.get("owner")
    owner_s = str(owner) if owner else None
    perm = flags.get("perm")
    perm_s = str(perm) if perm else None
    tool = pdf_protect(src, dest, password, owner_s, perm_s)
    return ok(dest, tool)


@register("pdf-unlock")
def action_pdf_unlock(argv: list[str]) -> dict:
    positionals, flags = parse_tail(argv)
    src = require_in(positionals)
    if isinstance(src, dict):
        return src
    if len(positionals) < 2:
        return fail("pdf-unlock requires <input> <output>")
    dest = Path(positionals[1])
    password = str(flags.get("password") or "")
    tool = pdf_unlock(src, dest, password)
    if isinstance(tool, dict):
        return tool
    return ok(dest, tool)


@register("pdf-ocr")
def action_pdf_ocr(argv: list[str]) -> dict:
    positionals, flags = parse_tail(argv)
    src = require_in(positionals)
    if isinstance(src, dict):
        return src
    if len(positionals) < 2:
        return fail("pdf-ocr requires <input> <output>")
    dest = Path(positionals[1])
    lang = str(flags.get("lang") or "eng")
    force = bool(flags.get("force"))
    tool = pdf_ocr(src, dest, lang, force)
    return ok(dest, tool)


def main(argv: list[str] | None = None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    try:
        if not argv or argv[0] in {"-h", "--help"}:
            fail(
                "usage: convert_engine.py <action> <input> [output] [--flags]; "
                "actions: " + ", ".join(sorted(ACTIONS))
            )
            return 1
        action = argv[0]
        if action not in ACTIONS:
            fail(f"unknown action: {action}")
            return 1
        result = ACTIONS[action](argv[1:])
        return 0 if result.get("success") else 1
    except Exception as exc:
        fail(f"{type(exc).__name__}: {exc}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

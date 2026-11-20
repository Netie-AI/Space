#!/usr/bin/env python3
"""Netie Space conversion engine.

CLI: python convert_engine.py <action> <input> [output] [--flags]

Last stdout line is always JSON:
  {success, output_path, tool, bytes, error}

Never fail stderr-only. Unknown actions and missing deps emit JSON + exit 1.
"""
from __future__ import annotations

import codecs
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
import unicodedata
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any, Callable
from urllib.parse import unquote

# Sibling module: the only place PDF libraries (pypdfium2, pikepdf, reportlab, svglib) are imported.
_TOOLS_DIR = str(Path(__file__).resolve().parent)
if _TOOLS_DIR not in sys.path:
    sys.path.insert(0, _TOOLS_DIR)
import pdf_backend  # noqa: E402

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
    ".ico", ".heic", ".heif", ".svg",
}
# Matches ContentParserService.VideoExtensions. GIF export used a short list.
VIDEO_EXTS = {
    ".mp4", ".webm", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".mpeg", ".mpg",
    ".hevc", ".h265", ".h264", ".3gp", ".flv", ".ts", ".m2ts", ".mts", ".ogv", ".vob",
}
AUDIO_EXTS = {".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg"}
OFFICE_EXTS = {
    ".docx", ".doc", ".odt", ".rtf", ".pptx", ".ppsx", ".ppt", ".pps", ".odp",
    ".xlsx", ".xlsm", ".xls", ".ods", ".csv",
}
PPTX_EXTS = {".pptx", ".ppsx", ".ppt", ".pps"}
# Share default PDF is blank when these have pictures and no text.
PHOTO_OFFICE_EXTS = PPTX_EXTS | {".odp", ".odt", ".docx", ".ods", ".xlsx", ".xlsm"}
# OLE binaries. Share hides these. Merge / drop to-pdf used empty markdown
# (573-byte blank PDF) when soffice is missing.
OLE_SOFFICE_EXTS = {".doc", ".xls"}
MD_EXTS = {".md", ".markdown"}
HTML_EXTS = {".html", ".htm"}
# Matches ContentParserService.TextExtensions minus .html/.md (own paths).
TEXT_EXTS = {
    ".txt", ".text", ".log", ".csv",
    ".json", ".xml", ".cs", ".js", ".ts", ".tsx", ".jsx",
    ".py", ".java", ".cpp", ".c", ".h", ".hpp", ".rs", ".go", ".rb", ".php",
    ".yaml", ".yml", ".toml", ".ini", ".cfg", ".sql", ".sh", ".ps1", ".bat",
    ".css", ".scss", ".vue", ".swift", ".kt",
}

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
    try:
        result = md.convert(str(src))
    except Exception:
        # UnsupportedFormat / MissingDependency: not a result, let the next backend try.
        return None
    text = (getattr(result, "text_content", None) or "").strip()
    if not text:
        return None
    # markitdown >= 0.1 dumps the raw parts of an Office zip it cannot parse
    # (no [Content_Types].xml). That is not a conversion; photo-only decks need
    # their pictures, not the XML.
    if text.startswith("Content from the zip file") and src.suffix.lower() in PHOTO_OFFICE_EXTS:
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


def via_pdfium_text(src: Path) -> tuple[str, str] | None:
    """Plain text layer of every page (pypdfium2). None when the PDF has no text layer."""
    pages = pdf_backend.page_texts(src)
    text = "\n\n".join(p.strip() for p in pages if p and p.strip())
    if not text:
        return None
    return text, "pdfium"


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
        if chunks:
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


def zip_media_pngs(src: Path, prefixes: tuple[str, ...], cap: int = 24) -> list[bytes]:
    """Office zip pictures as PNG bytes. Share PDF used empty markdown."""
    pngs: list[bytes] = []
    try:
        with zipfile.ZipFile(src) as zf:
            names = sorted(
                n for n in zf.namelist() if any(n.startswith(p) for p in prefixes)
            )[:cap]
            for name in names:
                raw = zf.read(name)
                # Pillow cannot open SVG. Photo ODT dest PDF raised
                # "no PDF converter" when Pictures/ was icon.svg.
                png = _bytes_to_png(raw)
                if png:
                    pngs.append(png)
    except Exception:
        return []
    return pngs


def pptx_media_pngs(src: Path, cap: int = 24) -> list[bytes]:
    return zip_media_pngs(src, ("ppt/media/",), cap)


def office_media_pngs(src: Path, cap: int = 24) -> list[bytes]:
    ext = src.suffix.lower()
    if ext in PPTX_EXTS:
        return pptx_media_pngs(src, cap)
    if ext in {".odp", ".odt", ".ods"}:
        return zip_media_pngs(
            src, ("Pictures/", "media/", "ObjectReplacements/"), cap
        )
    if ext == ".docx":
        return zip_media_pngs(src, ("word/media/",), cap)
    if ext in {".xlsx", ".xlsm"}:
        return zip_media_pngs(src, ("xl/media/",), cap)
    return []


def pngs_to_markdown(pngs: list[bytes], label: str, tool: str) -> tuple[str, str]:
    import base64

    chunks: list[str] = []
    for i, blob in enumerate(pngs, 1):
        b64 = base64.b64encode(blob).decode("ascii")
        chunks.append(f"## {label} {i}\n\n![{label} {i}](data:image/png;base64,{b64})\n")
    if not chunks:
        chunks.append("\n")
    return "\n".join(chunks), tool


def pptx_pages_to_markdown(src: Path) -> tuple[str, str]:
    """Photo / image-only PPTX -> Markdown. Share Markdown used to write a blank file."""
    return pngs_to_markdown(pptx_media_pngs(src), "Slide", "pptx-md-images")


def office_pages_to_markdown(src: Path) -> tuple[str, str]:
    ext = src.suffix.lower()
    if ext in PPTX_EXTS:
        return pptx_pages_to_markdown(src)
    label = "Slide" if ext == ".odp" else "Page"
    return pngs_to_markdown(office_media_pngs(src), label, "office-md-images")


def photo_office_to_markdown(src: Path) -> tuple[str, str]:
    """Share Markdown on photo office. Empty media used to write a 1-byte file."""
    text, tool = office_pages_to_markdown(src)
    if not (text or "").strip():
        raise RuntimeError(
            f"no markdown converter for {src.suffix.lower() or 'unknown'} (need LibreOffice for OLE)"
        )
    return text, tool


def pngs_to_html(pngs: list[bytes], dest: Path, title: str) -> str:
    import base64

    parts: list[str] = []
    for i, blob in enumerate(pngs, 1):
        b64 = base64.b64encode(blob).decode("ascii")
        parts.append(
            f'<section class="slide"><h2>Page {i}</h2>'
            f'<p><img src="data:image/png;base64,{b64}" alt="Page {i}"></p></section>'
        )
    if not parts:
        parts.append("<p></p>")
    write_text(dest, wrap_html("\n".join(parts), title))
    return "office-html-images"


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


def _docx_t_in_leftover(el: ET.Element, parent: dict[ET.Element, ET.Element]) -> bool:
    """<w:t> inside <w:del> / <w:moveFrom> is not page copy (same as delText)."""
    cur: ET.Element | None = el
    while cur is not None:
        local = cur.tag.rsplit("}", 1)[-1]
        if local in {"del", "moveFrom"}:
            return True
        cur = parent.get(cur)
    return False


def docx_plain_html(src: Path) -> str | None:
    try:
        with zipfile.ZipFile(src) as zf:
            raw = zf.read("word/document.xml")
    except Exception:
        return None
    root = ET.fromstring(raw)
    parent = {c: p for p in root.iter() for c in list(p)}
    paras: list[str] = []
    for p in root.iter():
        if not p.tag.endswith("}p"):
            continue
        # Leftover <w:t>Photo</w:t> inside <w:del> stole dest PDF (Story)
        # on photo DOCX that already had word/media/. Skip those runs.
        # <w:ins> / <w:moveTo> stay page copy. <w:sdt> stays parked.
        bits = [
            (el.text or "")
            for el in p.iter()
            if el.tag.endswith("}t") and not _docx_t_in_leftover(el, parent)
        ]
        line = "".join(bits).strip()
        if line:
            paras.append(f"<p>{html.escape(line)}</p>")
    if not paras:
        return None
    return "\n".join(paras)


def docx_to_text(src: Path) -> str:
    """Share default Export as PDF on .docx used markitdown."""
    body = docx_plain_html(src)
    if body is None:
        raise RuntimeError("not a valid .docx")
    text = re.sub(r"</p>", "\n", body)
    text = re.sub(r"<[^>]+>", "", text)
    return html.unescape(text).strip() + "\n"


def _xlsx_col_index(ref: str) -> int:
    letters = "".join(c for c in ref if c.isalpha())
    n = 0
    for c in letters.upper():
        n = n * 26 + (ord(c) - 64)
    return max(0, n - 1)


def xlsx_to_rows(src: Path) -> list[list[str]]:
    """Read first worksheet (inlineStr + shared strings + numbers)."""
    try:
        with zipfile.ZipFile(src) as zf:
            shared: list[str] = []
            if "xl/sharedStrings.xml" in zf.namelist():
                sroot = ET.fromstring(zf.read("xl/sharedStrings.xml"))
                for si in sroot:
                    if si.tag.endswith("}si"):
                        shared.append("".join(si.itertext()))
            sheets = sorted(
                n
                for n in zf.namelist()
                if n.startswith("xl/worksheets/sheet") and n.endswith(".xml") and "/_rels/" not in n
            )
            if not sheets:
                raise RuntimeError("xlsx has no worksheet")
            root = ET.fromstring(zf.read(sheets[0]))
    except (zipfile.BadZipFile, KeyError, OSError, ET.ParseError) as exc:
        raise RuntimeError(f"not a valid .xlsx: {exc}") from exc

    rows: list[list[str]] = []
    for row in root.iter():
        if not row.tag.endswith("}row"):
            continue
        cells: dict[int, str] = {}
        for c in row:
            if not c.tag.endswith("}c"):
                continue
            ref = c.attrib.get("r") or ""
            kind = c.attrib.get("t")
            val = ""
            if kind == "inlineStr":
                val = "".join(c.itertext())
            else:
                v_el = next((el for el in c if el.tag.endswith("}v")), None)
                raw = (v_el.text or "") if v_el is not None else ""
                if kind == "s":
                    try:
                        val = shared[int(raw)]
                    except (ValueError, IndexError):
                        val = raw
                else:
                    val = raw
            idx = _xlsx_col_index(ref) if ref else len(cells)
            cells[idx] = val
        if not cells:
            continue
        width = max(cells) + 1
        rows.append([cells.get(i, "") for i in range(width)])
    return rows or [[""]]


def file_to_markdown(src: Path) -> tuple[str, str]:
    ext = src.suffix.lower()
    if ext == ".xml":
        pngs = xml_photo_pngs(src)
        if pngs:
            return pngs_to_markdown(pngs, "Page", "xml-md-images")
        return _read_markup(src), "copy"
    if ext in MD_EXTS or ext in TEXT_EXTS:
        return _read_markup(src), "copy"

    if ext == ".pdf":
        for fn in (via_pdfmd, via_markitdown, via_pdfium_text):
            got = fn(src)
            if got:
                return got
        raise RuntimeError("no PDF-to-markdown backend (pdfmd / markitdown / pypdfium2)")

    if ext == ".odp":
        return odp_to_text(src), "odp-zip"

    if ext in {".pptx", ".ppsx", ".ppt", ".pps"}:
        got = via_pptx2md(src) or via_markitdown(src)
        if got:
            return got
        got = via_soffice_markdown(src)
        if got:
            return got
        raise RuntimeError("no PPTX-to-markdown backend (pptx2md / markitdown / LibreOffice)")

    if ext in IMAGE_EXTS:
        got = via_markitdown(src)
        if got:
            return got
        return f"![{src.name}]({src.name})\n", "image-stub"

    if ext == ".rtf":
        pngs = rtf_media_pngs(src)
        text = rtf_to_text(src)
        if pngs and not text.strip():
            return pngs_to_markdown(pngs, "Page", "rtf-md-images")
        return text, "rtf-text"
    if ext == ".odt":
        return odt_to_text(src), "odt-zip"
    if ext == ".ods":
        rows = ods_to_rows(src)
        return "\n".join(" | ".join(r) for r in rows) + "\n", "ods-zip"
    if ext == ".docx":
        return docx_to_text(src), "docx-zip"
    if ext in {".xlsx", ".xlsm"}:
        rows = xlsx_to_rows(src)
        return "\n".join(" | ".join(r) for r in rows) + "\n", "xlsx-zip"

    if ext in {".html", ".htm"}:
        # Photo-only HTML first: markitdown >= 0.1 keeps "![alt]()" with the
        # data URI stripped, which is non-empty and hid the real picture.
        raw = _read_markup(src)
        pngs = html_string_media_pngs(raw, src.parent)
        if pngs and not html_visible_text(raw):
            return pngs_to_markdown(pngs, "Page", "html-md-images")
    got = via_markitdown(src)
    if got:
        return got
    got = via_soffice_markdown(src)
    if got:
        return got
    if ext in {".html", ".htm"}:
        raw = _read_markup(src)
        text = re.sub(r"(?is)<script.*?>.*?</script>", "", raw)
        text = re.sub(r"(?is)<style.*?>.*?</style>", "", text)
        text = re.sub(r"(?s)<[^>]+>", " ", text)
        return re.sub(r"\s+", " ", text).strip() + "\n", "html-strip"
    raise RuntimeError(f"no markdown converter for {ext or 'unknown'} (need markitdown)")


_MD_LINK_DEF = re.compile(
    r"""^[ \t]{0,3}\[([^\]]+)\]:[ \t]*(?:<([^>]+)>|(\S+))"""
    r"""(?:[ \t]+(?:"([^"]*)"|'([^']*)'|\(([^)]*)\)))?[ \t]*$"""
)
_MD_INLINE_IMG = re.compile(
    r"""!\[([^\]]*)\]\(\s*(?:<([^>]*)>|([^\s)]+))"""
    r"""(?:\s+(?:"([^"]*)"|'([^']*)'|\(([^)]*)\)))?\s*\)"""
)
_MD_REF_IMG = re.compile(r"""!\[([^\]]*)\]\[([^\]]*)\]""")
_MD_SHORTCUT_IMG = re.compile(r"""!\[([^\]]+)\](?!\(|\[)""")
_MD_WRAP_IMG = re.compile(
    r"!\[([^\]]*)\]\(\s*\n[ \t]*(<[^>\n]+>|[^\s)\n]+)"
    r"(?:[ \t]+(?:\"[^\"]*\"|'[^']*'|\([^)]*\)))?[ \t]*\n[ \t]*\)"
)
_MD_WRAP_DEF = re.compile(
    r"^([ \t]{0,3}\[[^\]]+\]:)[ \t]*\n[ \t]+(<[^>]+>|\S+)",
    re.M,
)


def _md_unwrap_dests(text: str) -> str:
    """CommonMark allows a newline in ![alt](dest) / [id]: dest. Line-split missed it."""
    text = _MD_WRAP_IMG.sub(r"![\1](\2)", text)
    return _MD_WRAP_DEF.sub(r"\1 \2", text)


def _md_label(label: str) -> str:
    return re.sub(r"\s+", " ", (label or "").strip()).lower()


def _md_dest(href: str) -> str:
    """Word smart quotes around ![x](dest) used empty Word / dumped source."""
    href = (href or "").strip()
    while (
        len(href) >= 2
        and href[0] in "\"'\u201c\u2018\u201e"
        and href[-1] in "\"'\u201d\u2019\u201c"
    ):
        href = href[1:-1].strip()
    return href


def _md_link_defs(text: str) -> dict[str, str]:
    """`[id]: pic.png` / `[id]: <pic.png> \"t\"`. First label wins."""
    defs: dict[str, str] = {}
    for raw in text.replace("\r\n", "\n").split("\n"):
        m = _MD_LINK_DEF.fullmatch(raw.rstrip())
        if not m:
            continue
        key = _md_label(m.group(1))
        href = _md_dest(m.group(2) or m.group(3) or "")
        if key and href and key not in defs:
            defs[key] = href
    return defs


def _md_inline_html(text: str, defs: dict[str, str]) -> str:
    """Images with title / <dest> / [ref]. Bare `![alt](url)` already worked."""
    out: list[str] = []
    i = 0
    n = len(text)
    while i < n:
        bang = text.find("!", i)
        if bang < 0:
            out.append(html.escape(text[i:]))
            break
        if bang > i:
            out.append(html.escape(text[i:bang]))
        m = _MD_INLINE_IMG.match(text, bang)
        href = ""
        alt = ""
        end = -1
        if m:
            alt = m.group(1)
            href = _md_dest(m.group(2) or m.group(3) or "")
            end = m.end()
        else:
            m = _MD_REF_IMG.match(text, bang)
            if m:
                alt = m.group(1)
                href = defs.get(_md_label(m.group(2) or alt), "")
                if href:
                    end = m.end()
            if end < 0:
                m = _MD_SHORTCUT_IMG.match(text, bang)
                if m:
                    alt = m.group(1)
                    href = defs.get(_md_label(alt), "")
                    if href:
                        end = m.end()
        if end >= 0 and href:
            out.append(
                f'<img src="{html.escape(href, quote=True)}" '
                f'alt="{html.escape(alt, quote=True)}">'
            )
            i = end
            continue
        out.append("!")
        i = bang + 1
    return "".join(out)


def _markdown_to_html_stdlib(text: str) -> str:
    """Share dest 1 PDF on .md. The markdown package is not a Cursor CI dep."""
    text = _md_unwrap_dests(text)
    defs = _md_link_defs(text)
    lines = text.replace("\r\n", "\n").split("\n")
    parts: list[str] = []
    para: list[str] = []

    def flush() -> None:
        if para:
            parts.append("<p>" + " ".join(para) + "</p>")
            para.clear()

    for line in lines:
        raw = line.rstrip()
        if not raw.strip():
            flush()
            continue
        if _MD_LINK_DEF.fullmatch(raw):
            continue
        if raw.startswith("### "):
            flush()
            parts.append(f"<h3>{_md_inline_html(raw[4:], defs)}</h3>")
            continue
        if raw.startswith("## "):
            flush()
            parts.append(f"<h2>{_md_inline_html(raw[3:], defs)}</h2>")
            continue
        if raw.startswith("# "):
            flush()
            parts.append(f"<h1>{_md_inline_html(raw[2:], defs)}</h1>")
            continue
        para.append(_md_inline_html(raw, defs))
    flush()
    return "\n".join(parts) or "<p></p>"


def markdown_to_html(text: str) -> str:
    # YAML / TOML frontmatter is not page copy. Leftover "title: Photo"
    # stole dest Word / dest PDF (Story white) on photo Markdown.
    # Require a key: / key = line so a leading --- / +++ rule stays.
    text = re.sub(
        r"(?s)\A---\r?\n(?=[\w.-]+:\s).*?\r?\n---\r?\n",
        "",
        text,
    )
    text = re.sub(
        r"(?s)\A\+\+\+\r?\n(?=[\w.-]+\s*=).*?\r?\n\+\+\+\r?\n",
        "",
        text,
    )
    mdlib = try_import("markdown")
    if mdlib is not None and hasattr(mdlib, "markdown"):
        try:
            html_out = mdlib.markdown(text, extensions=["extra", "tables", "fenced_code"])
        except Exception:
            html_out = mdlib.markdown(text)
        # python-markdown keeps Word smart quotes around a picture dest
        # (![x](\u201cpic.png\u201d)); the stdlib path and Word both strip them.
        return re.sub(
            r'(?i)(<img\b[^>]*?\bsrc=")([^"]*)(")',
            lambda m: m.group(1) + _md_dest(html.unescape(m.group(2))).replace("&", "&amp;") + m.group(3),
            html_out,
        )
    return _markdown_to_html_stdlib(text)


def _image_rest_with_size(rest: str, png: bytes) -> str:
    """MuPDF skips <image> without width/height. Dest PNG was a white page."""
    rest = rest.strip().strip("/")
    if (
        (not _html_attr(rest, "width") or not _html_attr(rest, "height"))
        and png[:8] == b"\x89PNG\r\n\x1a\n"
        and len(png) >= 24
    ):
        w = int.from_bytes(png[16:20], "big")
        h = int.from_bytes(png[20:24], "big")
        if not _html_attr(rest, "width"):
            rest += f' width="{w}"'
        if not _html_attr(rest, "height"):
            rest += f' height="{h}"'
    return rest.rstrip().rstrip("/")


def _inline_svg_sibling_images(text: str, root: Path, depth: int = 0) -> str:
    """<image href> / <svg:image> / <use href> dest PNG used a white page."""
    if depth > 3 or not text:
        return text
    root = root.resolve()
    # MuPDF skips <image> inside <a> / <switch>. Dest PNG / Word were white.
    for _ in range(4):
        nxt = re.sub(
            r"(?is)<((?:[\w.-]+:)?(?:a|switch))\b[^>]*>(.*?)</\1\s*>",
            r"\2",
            text,
        )
        if nxt == text:
            break
        text = nxt

    def repl(m: re.Match[str]) -> str:
        attrs = m.group(1)
        href = _html_attr(attrs, "href") or _html_attr(attrs, "xlink:href")
        if not href:
            return m.group(0)
        href = _local_href(href)
        if not href or href.startswith(("data:", "http:", "https:", "//")):
            return m.group(0)
        try:
            path = (root / href).resolve()
            path.relative_to(root)
        except (OSError, ValueError):
            return m.group(0)
        if not path.is_file():
            return m.group(0)
        png = _path_to_png(path, depth + 1)
        if not png:
            return m.group(0)
        rest = re.sub(
            r"(?i)\s*(?:xlink:)?href\s*=\s*(?:(['\"]).*?\1|[^\s>]+)",
            "",
            attrs,
        )
        rest = _image_rest_with_size(rest, png)
        return f'<image href="{_png_data_uri(png)}"{rest}/>'

    text = re.sub(
        r"(?is)<(?:[\w.-]+:)?image\b([^>]*)(?:/>|>\s*(?:</(?:[\w.-]+:)?image\s*>)?)",
        repl,
        text,
    )

    def repl_use(m: re.Match[str]) -> str:
        attrs = m.group(1)
        href = _html_attr(attrs, "href") or _html_attr(attrs, "xlink:href")
        if not href or href.strip().startswith("#"):
            return m.group(0)
        file_part, _, frag = href.partition("#")
        file_part = _local_href(file_part)
        if not file_part or file_part.startswith(("data:", "http:", "https:", "//")):
            return m.group(0)
        try:
            path = (root / file_part).resolve()
            path.relative_to(root)
        except (OSError, ValueError):
            return m.group(0)
        if not path.is_file():
            return m.group(0)
        png = None
        if frag and path.suffix.lower() == ".svg":
            sibling = _read_markup(path)
            sibling = _inline_svg_sibling_images(sibling, path.parent, depth + 1)
            sibling = re.sub(r"(?is)<\?xml[^?]*\?>", "", sibling)
            wrap = (
                '<svg xmlns="http://www.w3.org/2000/svg">'
                f"{sibling}<use href=\"#{frag}\"/></svg>"
            )
            png = _svg_bytes_to_png(wrap.encode("utf-8"), None, depth + 1)
        else:
            png = _path_to_png(path, depth + 1)
        if not png:
            return m.group(0)
        rest = re.sub(
            r"(?i)\s*(?:xlink:)?href\s*=\s*(?:(['\"]).*?\1|[^\s>]+)",
            "",
            attrs,
        )
        rest = _image_rest_with_size(rest, png)
        return f'<image href="{_png_data_uri(png)}"{rest}/>'

    return re.sub(
        r"(?is)<(?:[\w.-]+:)?use\b([^>]*)(?:/>|>\s*(?:</(?:[\w.-]+:)?use\s*>)?)",
        repl_use,
        text,
    )


def _svg_lower_root(text: str) -> str:
    """MuPDF rejects <SVG>. Word HTML dest Word / dest PNG used empty / raise."""
    text = re.sub(
        r"(?is)<((?:[\w.-]+:)?svg)\b",
        lambda m: "<" + m.group(1).lower(),
        text,
    )
    return re.sub(
        r"(?is)</((?:[\w.-]+:)?svg)(\s*)>",
        lambda m: "</" + m.group(1).lower() + m.group(2) + ">",
        text,
    )


def _svg_bytes_to_png(raw: bytes, root: Path | None = None, depth: int = 0) -> bytes | None:
    """Pillow cannot open SVG. Photo HTML dest Word/MD used empty / 1-byte files."""
    try:
        if raw.startswith((b"\xff\xfe", b"\xfe\xff")):
            text = raw.decode("utf-16")
        elif raw.startswith(b"\xef\xbb\xbf"):
            text = raw.decode("utf-8-sig")
        else:
            text = raw.decode("utf-8", errors="replace")
        # Illustrator / Inkscape comments sit past 240 bytes. Dest PNG raised.
        if not re.search(r"(?i)<(?:[\w.-]+:)?svg\b", text):
            return None
        if root is not None:
            text = _inline_svg_sibling_images(text, root, depth)
        # Leftover empty <svg> / whitespace-only <text> rasterize to a
        # letter-size white page and steal dest Word (first word/media).
        if not re.search(
            r"(?is)<(?:[\w.-]+:)?(?:image|use|rect|path|circle|polygon|ellipse|line|polyline)\b",
            text,
        ):
            return _svg_unused_container_image(text.encode("utf-8"))
        raw = _svg_lower_root(text).encode("utf-8")
        png = pdf_backend.svg_to_png(raw)
        # Leftover <use href="#missing"/> / empty <path> still have
        # paint tags so the skip above misses them. The rasterizer writes
        # a letter-size white page that steals dest Word first.
        # Leftover <pattern> / feImage is solid black (same steal).
        from PIL import Image
        import io as _io
        with Image.open(_io.BytesIO(png)) as im:
            lo_hi = im.convert("RGB").getextrema()
        if all(lo >= 250 for lo, _hi in lo_hi) or all(hi <= 5 for _lo, hi in lo_hi):
            return _svg_unused_container_image(raw)
        return png
    except pdf_backend.PdfBackendUnavailable:
        raise
    except Exception:
        return _svg_unused_container_image(raw)


def _svg_unused_container_image(raw: bytes) -> bytes | None:
    """First <image> in unused <defs>/<symbol> when nothing paints.

    `<svg><defs><image href="pic.png"/></defs></svg>` has an <image>
    so the paint-tag skip misses it, but MuPDF does not paint unused
    defs -- dest Word stays empty, dest PDF Story is white, and
    standalone SVG dest PNG raises. Same steal as a sibling <img>
    the rasterizer never reached. Do not flatten <pattern> /
    clipPath / mask / marker / filter (parked) -- those have
    rendered geometry or a named container this must not unwrap.
    """
    import base64

    if re.search(
        rb"<(?:[\w.-]+:)?(?:use|rect|path|circle|polygon|ellipse|"
        rb"line|polyline|pattern|clipPath|mask|marker|filter|feImage)\b",
        raw,
        flags=re.I,
    ):
        return None
    m = re.search(
        rb"<(?:[\w.-]+:)?image\b[^>]*?(?:xlink:)?href=["
        rb"\"'](data:image/(?:png|jpe?g|gif|webp);base64,[A-Za-z0-9+/=]+)["
        rb"\"']",
        raw,
        flags=re.I,
    )
    if not m:
        return None
    try:
        blob = base64.b64decode(m.group(1).split(b",", 1)[-1], validate=False)
    except Exception:
        return None
    return _bytes_to_png(blob)


def _path_to_png(path: Path, depth: int = 0) -> bytes | None:
    if path.suffix.lower() == ".svg":
        # Notepad Unicode sibling SVG dest Word used empty / no pictures.
        return _svg_bytes_to_png(_read_markup(path).encode("utf-8"), path.parent, depth)
    return _bytes_to_png(path.read_bytes())


def _bytes_to_png(raw: bytes) -> bytes | None:
    import io

    def _open():
        from PIL import Image

        return Image.open(io.BytesIO(raw))

    try:
        try:
            im = _open()
        except Exception:
            # Sibling .heic in photo HTML. load_pil_image already registers
            # for file dests; Image.open here never saw pillow_heif.
            try:
                from pillow_heif import register_heif_opener

                register_heif_opener()
                im = _open()
            except Exception:
                raise
        try:
            buf = io.BytesIO()
            im.convert("RGBA").save(buf, format="PNG")
            return buf.getvalue()
        finally:
            im.close()
    except Exception:
        if raw[:8] == b"\x89PNG\r\n\x1a\n":
            return raw
    return _svg_bytes_to_png(raw)


def _png_data_uri(png: bytes) -> str:
    import base64

    return "data:image/png;base64," + base64.b64encode(png).decode("ascii")


def _svg_data_uri_to_png(href: str) -> bytes | None:
    import base64
    from urllib.parse import unquote_to_bytes

    m = re.match(
        r"data:image/svg\+xml(?:;[^,]*)*;base64,([A-Za-z0-9+/=\s]+)$",
        href,
        re.I,
    )
    if m:
        try:
            raw = base64.b64decode(re.sub(r"\s+", "", m.group(1)))
        except Exception:
            return None
        return _bytes_to_png(raw)
    # ;utf8 / ;charset=utf-8 / raw SVG inside single-quoted src.
    m = re.match(r"data:image/svg\+xml(?:;[^,]*)*,(.*)$", href, re.I)
    if not m:
        return None
    try:
        raw = unquote_to_bytes(m.group(1))
    except Exception:
        return None
    return _bytes_to_png(raw)


def inline_sibling_images(markup: str, base: Path, cap: int = 24) -> str:
    """Story cannot load file:// or relative imgs. Share dest 2 HTML / dest 1 MD."""
    n = 0
    root = base.resolve()

    def repl(m: re.Match[str]) -> str:
        nonlocal n
        quote, href = m.group(1), m.group(2)
        if n >= cap:
            return m.group(0)
        if href.startswith("data:"):
            png = _svg_data_uri_to_png(href)
            if not png:
                return m.group(0)
            n += 1
            return f"src={quote}{_png_data_uri(png)}{quote}"
        href = _local_href(href)
        if not href:
            return m.group(0)
        path = (base / href).resolve()
        try:
            path.relative_to(root)
        except ValueError:
            return m.group(0)
        if not path.is_file():
            return m.group(0)
        png = _path_to_png(path)
        if not png:
            return m.group(0)
        n += 1
        return f"src={quote}{_png_data_uri(png)}{quote}"

    # Same-quote close so raw SVG data URIs can keep xmlns="..." inside src='...'.
    # Pretty-printed `src = "..."` used empty Word / 1-byte MD.
    markup = re.sub(r'src\s*=\s*(["\'])(.*?)\1', repl, markup, flags=re.I | re.S)

    def repl_unquoted(m: re.Match[str]) -> str:
        nonlocal n
        href = _local_href(m.group(1))
        if n >= cap or not href or href.startswith("data:"):
            return m.group(0)
        path = (base / href).resolve()
        try:
            path.relative_to(root)
        except ValueError:
            return m.group(0)
        if not path.is_file():
            return m.group(0)
        png = _path_to_png(path)
        if not png:
            return m.group(0)
        n += 1
        return f'src="{_png_data_uri(png)}"'

    return re.sub(r'src\s*=\s*(?!["\'])([^\s>]+)', repl_unquoted, markup, flags=re.I)


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


def write_docx_images(dest: Path, pngs: list[bytes]) -> str:
    """Scan / image-only PDF -> Word. Share Export as Word used to need a text layer."""
    dest.parent.mkdir(parents=True, exist_ok=True)
    if not pngs:
        return write_docx(dest, "")
    drawings: list[str] = []
    rels = [
        '<Relationship Id="rId1" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" '
        'Target="word/document.xml"/>'
    ]
    word_rels = []
    media: list[tuple[str, bytes]] = []
    for i, blob in enumerate(pngs, 1):
        rid = f"rId{i}"
        name = f"image{i}.png"
        media.append((name, blob))
        word_rels.append(
            f'<Relationship Id="{rid}" '
            'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" '
            f'Target="media/{name}"/>'
        )
        # 96dpi EMU; cap width at 6.5in so the page fits Word.
        w_px, h_px = 400, 300
        try:
            from PIL import Image
            import io
            im = Image.open(io.BytesIO(blob))
            w_px, h_px = im.size
            im.close()
        except Exception:
            pass
        emu_w = min(int(w_px * 9525), 5940000)
        emu_h = max(1, int(h_px * (emu_w / max(w_px, 1))))
        drawings.append(
            "<w:p><w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">"
            f"<wp:extent cx=\"{emu_w}\" cy=\"{emu_h}\"/>"
            f"<wp:docPr id=\"{i}\" name=\"Page {i}\"/>"
            '<a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">'
            '<a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">'
            '<pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">'
            f'<pic:nvPicPr><pic:cNvPr id="{i}" name="Page {i}"/><pic:cNvPicPr/></pic:nvPicPr>'
            f'<pic:blipFill><a:blip r:embed="{rid}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>'
            "<pic:spPr><a:xfrm>"
            f'<a:off x="0" y="0"/><a:ext cx="{emu_w}" cy="{emu_h}"/>'
            "</a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>"
            "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>"
        )
    document_xml = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" '
        'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" '
        'xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing">'
        f"<w:body>{''.join(drawings)}<w:sectPr/></w:body></w:document>"
    )
    content_types = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
        '<Default Extension="xml" ContentType="application/xml"/>'
        '<Default Extension="png" ContentType="image/png"/>'
        '<Override PartName="/word/document.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'
        "</Types>"
    )
    rels_xml = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        + "".join(rels) +
        "</Relationships>"
    )
    word_rels_xml = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        + "".join(word_rels) +
        "</Relationships>"
    )
    with zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("[Content_Types].xml", content_types)
        zf.writestr("_rels/.rels", rels_xml)
        zf.writestr("word/document.xml", document_xml)
        zf.writestr("word/_rels/document.xml.rels", word_rels_xml)
        for name, blob in media:
            zf.writestr(f"word/media/{name}", blob)
    return "docx-zip-images"


def pdf_page_pngs(src: Path, cap: int = 24) -> list[bytes]:
    return [pdf_backend.png_bytes(im) for im in pdf_backend.render_pages(src, 1.0, cap)]


def pdf_pages_to_docx(src: Path, dest: Path) -> str:
    return write_docx_images(dest, pdf_page_pngs(src))


def pdf_pages_to_html(src: Path, dest: Path) -> str:
    """Scan / image-only PDF -> HTML. Share Export as HTML used to need a text layer."""
    import base64

    pngs = pdf_page_pngs(src)
    parts: list[str] = []
    for i, blob in enumerate(pngs, 1):
        b64 = base64.b64encode(blob).decode("ascii")
        parts.append(
            f'<section class="slide"><h2>Page {i}</h2>'
            f'<p><img src="data:image/png;base64,{b64}" alt="Page {i}"></p></section>'
        )
    if not parts:
        parts.append("<p></p>")
    write_text(dest, wrap_html("\n".join(parts), src.name))
    return "pdfium-html-images"


def pdf_pages_to_markdown(src: Path) -> tuple[str, str]:
    """Scan / image-only PDF -> Markdown. Share Markdown used to need a text layer."""
    import base64

    pngs = pdf_page_pngs(src)
    chunks: list[str] = []
    for i, blob in enumerate(pngs, 1):
        b64 = base64.b64encode(blob).decode("ascii")
        chunks.append(f"## Page {i}\n\n![Page {i}](data:image/png;base64,{b64})\n")
    if not chunks:
        chunks.append("\n")
    return "\n".join(chunks), "pdfium-md-images"


def file_to_docx(src: Path, dest: Path) -> str:
    if src.suffix.lower() == ".docx":
        return copy_file(src, dest)
    if src.suffix.lower() in HTML_EXTS:
        raw = _read_markup(src)
        pngs = html_media_pngs(src)
        visible = html_visible_text(raw)
        if pngs and not visible:
            return write_docx_images(dest, pngs)
        return write_docx(dest, visible)
    if src.suffix.lower() == ".rtf":
        pngs = rtf_media_pngs(src)
        text = rtf_to_text(src)
        if pngs and not text.strip():
            return write_docx_images(dest, pngs)
        return write_docx(dest, text)
    if src.suffix.lower() == ".pdf":
        got = via_pdfium_text(src) or via_pdfmd(src) or via_markitdown(src)
        if got:
            return write_docx(dest, got[0])
        try:
            return pdf_pages_to_docx(src, dest)
        except Exception:
            tool = via_soffice(src, dest)
            if tool:
                return tool
            raise
    try:
        text, _tool = file_to_markdown(src)
        if text.strip() or src.suffix.lower() not in PHOTO_OFFICE_EXTS:
            if src.suffix.lower() in MD_EXTS:
                html = inline_sibling_images(markdown_to_html(text), src.parent)
                pngs = html_string_media_pngs(html, src.parent)
                # Naive tag strip left &nbsp; / comments as "page copy".
                visible = html_visible_text(html)
                if not visible and pngs:
                    return write_docx_images(dest, pngs)
                return write_docx(dest, visible or text)
            if not text.strip() and src.suffix.lower() in HTML_EXTS:
                pngs = html_media_pngs(src)
                if pngs:
                    return write_docx_images(dest, pngs)
            if src.suffix.lower() == ".xml":
                pngs = xml_photo_pngs(src)
                if pngs:
                    return write_docx_images(dest, pngs)
            return write_docx(dest, text)
    except RuntimeError:
        text = None
    if src.suffix.lower() in HTML_EXTS:
        pngs = html_media_pngs(src)
        if pngs:
            return write_docx_images(dest, pngs)
    if src.suffix.lower() in PHOTO_OFFICE_EXTS:
        pngs = office_media_pngs(src)
        if pngs:
            return write_docx_images(dest, pngs)
    if text is not None:
        return write_docx(dest, text)
    tool = via_soffice(src, dest)
    if tool:
        return tool
    raise RuntimeError("LibreOffice required to export this file as .docx")


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


def write_xlsx_images(dest: Path, pngs: list[bytes]) -> str:
    """Photo ODS -> Excel. Share Export as Excel used empty cell text (no pictures)."""
    dest.parent.mkdir(parents=True, exist_ok=True)
    if not pngs:
        return write_xlsx(dest, [[""]])
    anchors: list[str] = []
    drawing_rels: list[str] = []
    media: list[tuple[str, bytes]] = []
    row = 0
    for i, blob in enumerate(pngs, 1):
        name = f"image{i}.png"
        media.append((name, blob))
        rid = f"rId{i}"
        drawing_rels.append(
            f'<Relationship Id="{rid}" '
            'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" '
            f'Target="../media/{name}"/>'
        )
        w_px, h_px = 400, 300
        try:
            from PIL import Image
            import io

            im = Image.open(io.BytesIO(blob))
            w_px, h_px = im.size
            im.close()
        except Exception:
            pass
        emu_w = min(int(w_px * 9525), 9144000)
        emu_h = max(1, int(h_px * (emu_w / max(w_px, 1))))
        anchors.append(
            "<xdr:oneCellAnchor>"
            f"<xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff>"
            f"<xdr:row>{row}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>"
            f'<xdr:ext cx="{emu_w}" cy="{emu_h}"/>'
            "<xdr:pic><xdr:nvPicPr>"
            f'<xdr:cNvPr id="{i}" name="Picture {i}"/><xdr:cNvPicPr/>'
            "</xdr:nvPicPr><xdr:blipFill>"
            f'<a:blip r:embed="{rid}"/><a:stretch><a:fillRect/></a:stretch>'
            "</xdr:blipFill><xdr:spPr><a:xfrm>"
            f'<a:off x="0" y="0"/><a:ext cx="{emu_w}" cy="{emu_h}"/>'
            '</a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr>'
            "</xdr:pic><xdr:clientData/></xdr:oneCellAnchor>"
        )
        row += max(8, min(40, int(h_px / 15) + 2))
    drawing_xml = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" '
        'xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" '
        'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
        f"{''.join(anchors)}</xdr:wsDr>"
    )
    sheet_xml = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
        'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
        "<sheetData/>"
        '<drawing r:id="rId1"/>'
        "</worksheet>"
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
    sheet_rels = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing" '
        'Target="../drawings/drawing1.xml"/>'
        "</Relationships>"
    )
    drawing_rels_xml = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        f"{''.join(drawing_rels)}</Relationships>"
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
        '<Default Extension="png" ContentType="image/png"/>'
        '<Override PartName="/xl/workbook.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
        '<Override PartName="/xl/worksheets/sheet1.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
        '<Override PartName="/xl/drawings/drawing1.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.drawing+xml"/>'
        "</Types>"
    )
    with zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("[Content_Types].xml", content_types)
        zf.writestr("_rels/.rels", root_rels)
        zf.writestr("xl/workbook.xml", workbook_xml)
        zf.writestr("xl/_rels/workbook.xml.rels", workbook_rels)
        zf.writestr("xl/worksheets/sheet1.xml", sheet_xml)
        zf.writestr("xl/worksheets/_rels/sheet1.xml.rels", sheet_rels)
        zf.writestr("xl/drawings/drawing1.xml", drawing_xml)
        zf.writestr("xl/drawings/_rels/drawing1.xml.rels", drawing_rels_xml)
        for name, blob in media:
            zf.writestr(f"xl/media/{name}", blob)
    return "xlsx-zip-images"


def rtf_media_pngs(src: Path, cap: int = 24) -> list[bytes]:
    """WordPad / Word photo RTF dest Word used hex dump / no pictures."""
    raw = src.read_bytes().decode("latin-1", errors="replace")
    pngs: list[bytes] = []
    for m in re.finditer(r"\\pict\b([^}]+)", raw, re.I | re.S):
        if len(pngs) >= cap:
            break
        body = m.group(1)
        if not re.search(r"\\(?:pngblip|jpegblip)\b", body, re.I):
            continue
        hexes = re.sub(r"\\[a-zA-Z]+\-?\d*\s?", "", body)
        hexes = re.sub(r"[^0-9a-fA-F]", "", hexes)
        if len(hexes) < 32:
            continue
        if len(hexes) % 2:
            hexes = hexes[:-1]
        try:
            blob = bytes.fromhex(hexes)
        except ValueError:
            continue
        png = _bytes_to_png(blob)
        if png:
            pngs.append(png)
    return pngs


def _rtf_end_group(s: str, start: int) -> int:
    """Index after the '}' that closes s[start] == '{'."""
    depth = 0
    i = start
    n = len(s)
    while i < n:
        ch = s[i]
        if ch == "\\":
            i += 2 if i + 1 < n else 1
            continue
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    return n


def _rtf_strip_dest_groups(s: str) -> str:
    """{\\*...} / fonttbl / info / filetbl are not page copy. Nested braces used to leak."""
    out: list[str] = []
    i = 0
    n = len(s)
    while i < n:
        if s[i] == "{" and i + 2 < n and s[i + 1] == "\\":
            rest = s[i + 2 :]
            if rest.startswith("*") or re.match(
                r"(?i)(?:fonttbl|colortbl|stylesheet|info|filetbl|"
                r"listoverridetable|listtable|revtbl|rsidtbl|xmlnstbl|generator|"
                r"themedata|colorschememapping|passwordhash|datastore|mmathPr|"
                r"latentstyles|pgdsctbl|fchars|lchars|protusertbl|userdata|"
                r"oldcprops|oldpprops|wgrffmtfilter|nonshppict|shppict|pnseclvl|"
                r"annotation|atnauthor|atrfstart|atrfend|atnid|atnref|atnicn|atntime|atndate|"
                r"fldinst|datafield|userprops|docvar|"
                r"xmlopen|expandedcolortbl|nesttableprops|nonesttables|defchp|defpap|"
                r"bkmkstart|bkmkend|xe|tc|rxe)\b",
                rest,
            ):
                i = _rtf_end_group(s, i)
                continue
        out.append(s[i])
        i += 1
    return "".join(out)


def rtf_to_text(src: Path) -> str:
    """Best-effort RTF -> plain text. Share Export as PDF/Word on .rtf used markitdown."""
    s = src.read_bytes().decode("latin-1", errors="replace")
    s = re.sub(r"\{\\pict\b[^{}]*\}", "", s)
    # Word photo RTF always writes nested {\\*listtable} / {\\*latentstyles}
    # / {\\*fonttbl{\\f0 {\\*panose}Calibri;}}. Leftover "Calibri" / "Photo"
    # stole dest Word (docx-zip) and dest PDF (Story). Unstarred \\filetbl /
    # \\listtable / \\generator stole the same way. Unstarred \\themedata /
    # \\datastore / \\latentstyles stole dest Word the same way.
    # Unstarred \\nonshppict / \\shppict / \\pnseclvl stole dest Word the same way.
    # Unstarred \\annotation / \\atnauthor / \\atrfstart stole dest Word the same way.
    # Unstarred \\fldinst / \\datafield stole dest Word the same way.
    # Unstarred \\userprops / \\docvar stole dest Word the same way.
    # Unstarred \\xmlopen / \\expandedcolortbl stole dest Word the same way.
    # Unstarred \\bkmkstart / \\xe / \\tc stole dest Word the same way.
    # Header / footer stay. Do not strip \\upr (fallback is page copy).
    # Do not strip \\fldrslt (field result is page copy).
    s = _rtf_strip_dest_groups(s)
    s = re.sub(
        r"\\'([0-9a-fA-F]{2})",
        lambda m: bytes.fromhex(m.group(1)).decode("latin-1"),
        s,
    )
    s = re.sub(r"\\par[d]?\b", "\n", s)
    s = re.sub(r"\\line\b", "\n", s)
    s = re.sub(r"\\tab\b", "\t", s)
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


def _odf_p_in_leftover(el: ET.Element, parent: dict[ET.Element, ET.Element]) -> bool:
    """text:p inside office:annotation / text:deletion is not page copy."""
    cur: ET.Element | None = el
    while cur is not None:
        local = cur.tag.rsplit("}", 1)[-1]
        if local in {"annotation", "deletion"}:
            return True
        # Change-log copies are not page copy. Leftover <text:insertion> / <text:format-change> inside <text:changed-region> stole dest
        # PDF / Word on photo ODT (Pictures/ already there). Body
        # text:insertion stays page copy (not under tracked-changes).
        if local in {"changed-region", "tracked-changes"}:
            return True
        cur = parent.get(cur)
    return False


def _odf_visible_text(el: ET.Element) -> str:
    """Skip office:annotation / text:deletion subtrees (same leftover as ODT)."""
    parts = [el.text or ""]
    for child in list(el):
        local = child.tag.rsplit("}", 1)[-1]
        if local in {"annotation", "deletion"}:
            parts.append(child.tail or "")
            continue
        if local in {"changed-region", "tracked-changes"}:
            parts.append(child.tail or "")
            continue
        parts.append(_odf_visible_text(child))
        parts.append(child.tail or "")
    return "".join(parts)


def odt_to_text(src: Path) -> str:
    root = _odf_content_root(src)
    parent = {c: p for p in root.iter() for c in list(p)}
    paras: list[str] = []
    # Leftover <office:annotation><text:p>Photo stole dest PDF (Story)
    # on photo ODT / ODP that already had Pictures/. Same class as
    # DOCX w:del / w:t. text:insertion stays page copy.
    for p in root.iter("{urn:oasis:names:tc:opendocument:xmlns:text:1.0}p"):
        if _odf_p_in_leftover(p, parent):
            continue
        t = "".join(p.itertext()).strip()
        if t:
            paras.append(t)
    return "\n".join(paras) + ("\n" if paras else "")


def odp_to_text(src: Path) -> str:
    """Same ODF text:p walk as ODT. Share Export as PDF on .odp used pptx2md."""
    return odt_to_text(src)


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
            # Leftover <office:annotation><text:p>Photo in a cell stole
            # dest PDF / Word / Excel on photo ODS (Pictures/ already there).
            text = _odf_visible_text(cell).strip()
            if not text and repeat > 8:
                continue
            cells.extend([text] * repeat)
        if any(cells):
            rows.append(cells)
    return rows or [[""]]


def csv_to_rows(src: Path) -> list[list[str]]:
    text = _read_markup(src)
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


def via_soffice_markdown(src: Path) -> tuple[str, str] | None:
    """OLE .doc/.xls/.ppt (and leftovers) -> text when LibreOffice is installed."""
    with tempfile.TemporaryDirectory() as td:
        out = Path(td) / (src.stem + ".txt")
        if not via_soffice(src, out) or not out.is_file():
            return None
        text = out.read_text(encoding="utf-8", errors="replace").strip()
        if not text:
            return None
        return text + "\n", "libreoffice"


def file_to_xlsx(src: Path, dest: Path) -> str:
    ext = src.suffix.lower()
    if ext in {".xlsx", ".xlsm"}:
        return copy_file(src, dest)
    if ext == ".csv":
        return write_xlsx(dest, csv_to_rows(src))
    if ext == ".ods":
        rows = ods_to_rows(src)
        if any((c or "").strip() for r in rows for c in r):
            return write_xlsx(dest, rows)
        pngs = office_media_pngs(src)
        if pngs:
            return write_xlsx_images(dest, pngs)
        return write_xlsx(dest, rows)
    tool = via_soffice(src, dest)
    if tool:
        return tool
    raise RuntimeError(
        "LibreOffice required to export this file as .xlsx (install soffice, or export CSV)"
    )


def write_pptx_images(dest: Path, pngs: list[bytes]) -> str:
    """Photo ODP -> PowerPoint. Share dest 2 used to raise LibreOffice required."""
    dest.parent.mkdir(parents=True, exist_ok=True)
    slide_w, slide_h = 9144000, 6858000
    slides: list[str] = []
    slide_rels: list[str] = []
    media: list[tuple[str, bytes]] = []
    overrides: list[str] = []
    sld_ids: list[str] = []
    pres_slide_rels: list[str] = []
    for i, blob in enumerate(pngs, 1):
        name = f"image{i}.png"
        media.append((name, blob))
        w_px, h_px = 400, 300
        try:
            from PIL import Image
            import io

            im = Image.open(io.BytesIO(blob))
            w_px, h_px = im.size
            im.close()
        except Exception:
            pass
        emu_w = min(int(w_px * 9525), slide_w)
        emu_h = max(1, int(h_px * (emu_w / max(w_px, 1))))
        if emu_h > slide_h:
            emu_h = slide_h
            emu_w = max(1, int(w_px * (emu_h / max(h_px, 1))))
        off_x = max(0, (slide_w - emu_w) // 2)
        off_y = max(0, (slide_h - emu_h) // 2)
        slides.append(
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" '
            'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" '
            'xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">'
            "<p:cSld><p:spTree>"
            '<p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>'
            "<p:grpSpPr><a:xfrm>"
            '<a:off x="0" y="0"/><a:ext cx="0" cy="0"/>'
            '<a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/>'
            "</a:xfrm></p:grpSpPr>"
            "<p:pic><p:nvPicPr>"
            f'<p:cNvPr id="2" name="Picture {i}"/>'
            '<p:cNvPicPr><a:picLocks noChangeAspect="1"/></p:cNvPicPr>'
            "<p:nvPr/></p:nvPicPr>"
            '<p:blipFill><a:blip r:embed="rId2"/>'
            "<a:stretch><a:fillRect/></a:stretch></p:blipFill>"
            "<p:spPr><a:xfrm>"
            f'<a:off x="{off_x}" y="{off_y}"/>'
            f'<a:ext cx="{emu_w}" cy="{emu_h}"/>'
            "</a:xfrm>"
            '<a:prstGeom prst="rect"><a:avLst/></a:prstGeom>'
            "</p:spPr></p:pic>"
            "</p:spTree></p:cSld>"
            "<p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>"
            "</p:sld>"
        )
        slide_rels.append(
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            '<Relationship Id="rId1" '
            'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout" '
            'Target="../slideLayouts/slideLayout1.xml"/>'
            '<Relationship Id="rId2" '
            'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" '
            f'Target="../media/{name}"/>'
            "</Relationships>"
        )
        overrides.append(
            f'<Override PartName="/ppt/slides/slide{i}.xml" '
            'ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>'
        )
        sld_ids.append(f'<p:sldId id="{255 + i}" r:id="rId{i + 1}"/>')
        pres_slide_rels.append(
            f'<Relationship Id="rId{i + 1}" '
            'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" '
            f'Target="slides/slide{i}.xml"/>'
        )
    presentation_xml = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<p:presentation xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" '
        'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" '
        'xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">'
        '<p:sldMasterIdLst><p:sldMasterId id="2147483648" r:id="rId1"/></p:sldMasterIdLst>'
        f'<p:sldIdLst>{"".join(sld_ids)}</p:sldIdLst>'
        f'<p:sldSz cx="{slide_w}" cy="{slide_h}" type="screen4x3"/>'
        '<p:notesSz cx="6858000" cy="9144000"/>'
        "</p:presentation>"
    )
    presentation_rels = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster" '
        'Target="slideMasters/slideMaster1.xml"/>'
        + "".join(pres_slide_rels)
        + "</Relationships>"
    )
    root_rels = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" '
        'Target="ppt/presentation.xml"/>'
        "</Relationships>"
    )
    content_types = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
        '<Default Extension="xml" ContentType="application/xml"/>'
        '<Default Extension="png" ContentType="image/png"/>'
        '<Override PartName="/ppt/presentation.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>'
        '<Override PartName="/ppt/slideMasters/slideMaster1.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml"/>'
        '<Override PartName="/ppt/slideLayouts/slideLayout1.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml"/>'
        '<Override PartName="/ppt/theme/theme1.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.theme+xml"/>'
        + "".join(overrides)
        + "</Types>"
    )
    theme_xml = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Office Theme">'
        "<a:themeElements>"
        '<a:clrScheme name="Office">'
        '<a:dk1><a:sysClr val="windowText" lastClr="000000"/></a:dk1>'
        '<a:lt1><a:sysClr val="window" lastClr="FFFFFF"/></a:lt1>'
        '<a:dk2><a:srgbClr val="44546A"/></a:dk2>'
        '<a:lt2><a:srgbClr val="E7E6E6"/></a:lt2>'
        '<a:accent1><a:srgbClr val="4472C4"/></a:accent1>'
        '<a:accent2><a:srgbClr val="ED7D31"/></a:accent2>'
        '<a:accent3><a:srgbClr val="A5A5A5"/></a:accent3>'
        '<a:accent4><a:srgbClr val="FFC000"/></a:accent4>'
        '<a:accent5><a:srgbClr val="5B9BD5"/></a:accent5>'
        '<a:accent6><a:srgbClr val="70AD47"/></a:accent6>'
        '<a:hlink><a:srgbClr val="0563C1"/></a:hlink>'
        '<a:folHlink><a:srgbClr val="954F72"/></a:folHlink>'
        "</a:clrScheme>"
        '<a:fontScheme name="Office">'
        '<a:majorFont><a:latin typeface="Calibri Light"/><a:ea typeface=""/><a:cs typeface=""/></a:majorFont>'
        '<a:minorFont><a:latin typeface="Calibri"/><a:ea typeface=""/><a:cs typeface=""/></a:minorFont>'
        "</a:fontScheme>"
        '<a:fmtScheme name="Office">'
        "<a:fillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>"
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>"
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:fillStyleLst>"
        "<a:lnStyleLst>"
        '<a:ln w="9525" cap="flat" cmpd="sng" algn="ctr">'
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/></a:ln>"
        '<a:ln w="25400" cap="flat" cmpd="sng" algn="ctr">'
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/></a:ln>"
        '<a:ln w="38100" cap="flat" cmpd="sng" algn="ctr">'
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/></a:ln>"
        "</a:lnStyleLst>"
        "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle>"
        "<a:effectStyle><a:effectLst/></a:effectStyle>"
        "<a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>"
        "<a:bgFillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>"
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>"
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:bgFillStyleLst>"
        "</a:fmtScheme></a:themeElements></a:theme>"
    )
    master_xml = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<p:sldMaster xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" '
        'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" '
        'xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">'
        "<p:cSld><p:spTree>"
        '<p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>'
        "<p:grpSpPr><a:xfrm>"
        '<a:off x="0" y="0"/><a:ext cx="0" cy="0"/>'
        '<a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/>'
        "</a:xfrm></p:grpSpPr>"
        "</p:spTree></p:cSld>"
        '<p:clrMap bg1="lt1" tx1="dk1" bg2="lt2" tx2="dk2" accent1="accent1" '
        'accent2="accent2" accent3="accent3" accent4="accent4" accent5="accent5" '
        'accent6="accent6" hlink="hlink" folHlink="folHlink"/>'
        '<p:sldLayoutIdLst><p:sldLayoutId id="2147483649" r:id="rId1"/></p:sldLayoutIdLst>'
        "</p:sldMaster>"
    )
    master_rels = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout" '
        'Target="../slideLayouts/slideLayout1.xml"/>'
        '<Relationship Id="rId2" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme" '
        'Target="../theme/theme1.xml"/>'
        "</Relationships>"
    )
    layout_xml = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<p:sldLayout xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" '
        'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" '
        'xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" type="blank" preserve="1">'
        '<p:cSld name="Blank"><p:spTree>'
        '<p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>'
        "<p:grpSpPr><a:xfrm>"
        '<a:off x="0" y="0"/><a:ext cx="0" cy="0"/>'
        '<a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/>'
        "</a:xfrm></p:grpSpPr>"
        "</p:spTree></p:cSld>"
        "<p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>"
        "</p:sldLayout>"
    )
    layout_rels = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster" '
        'Target="../slideMasters/slideMaster1.xml"/>'
        "</Relationships>"
    )
    with zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("[Content_Types].xml", content_types)
        zf.writestr("_rels/.rels", root_rels)
        zf.writestr("ppt/presentation.xml", presentation_xml)
        zf.writestr("ppt/_rels/presentation.xml.rels", presentation_rels)
        zf.writestr("ppt/slideMasters/slideMaster1.xml", master_xml)
        zf.writestr("ppt/slideMasters/_rels/slideMaster1.xml.rels", master_rels)
        zf.writestr("ppt/slideLayouts/slideLayout1.xml", layout_xml)
        zf.writestr("ppt/slideLayouts/_rels/slideLayout1.xml.rels", layout_rels)
        zf.writestr("ppt/theme/theme1.xml", theme_xml)
        for i, xml in enumerate(slides, 1):
            zf.writestr(f"ppt/slides/slide{i}.xml", xml)
            zf.writestr(f"ppt/slides/_rels/slide{i}.xml.rels", slide_rels[i - 1])
        for name, blob in media:
            zf.writestr(f"ppt/media/{name}", blob)
    return "pptx-zip-images"


def file_to_pptx(src: Path, dest: Path) -> str:
    if src.suffix.lower() in {".pptx", ".ppsx"} or (
        src.suffix.lower() == ".pps" and zipfile.is_zipfile(src)
    ):
        return copy_file(src, dest)
    if src.suffix.lower() == ".odp" and not (odp_to_text(src) or "").strip():
        pngs = office_media_pngs(src)
        if pngs:
            return write_pptx_images(dest, pngs)
    tool = via_soffice(src, dest)
    if tool:
        return tool
    raise RuntimeError("LibreOffice required to export this file as .pptx")


# ---------------------------------------------------------------------------
# PDF write helpers (tools/pdf_backend.py: pypdfium2 / pikepdf / reportlab / svglib)
# ---------------------------------------------------------------------------

def _local_href(href: str) -> str:
    """Sibling ` pic.png ` / pic.png#x / my%20pic.png / pic&#46;png used empty Word."""
    href = (href or "").strip()
    if not href or href.lower().startswith(("data:", "http:", "https:", "//")):
        return href
    # Unescape before '#' split so &#46; stays a dot. MD dests html.escape
    # &#46; to &amp;#46; -- one unescape still leaves a fragment.
    for _ in range(3):
        nxt = html.unescape(href)
        if nxt == href:
            break
        href = nxt
    href = href.split("#", 1)[0].split("?", 1)[0].strip()
    return unquote(href)


def _html_attr(attrs: str, name: str) -> str | None:
    """Quoted or HTML5 unquoted attribute. Share dests used empty Word on href=page.css."""
    m = re.search(
        rf"""(?i)\b{re.escape(name)}\s*=\s*(?:(['"])(.*?)\1|([^\s>]+))""",
        attrs,
    )
    if not m:
        return None
    val = ((m.group(2) if m.group(1) else m.group(3)) or "").strip()
    return val or None


def _html_base_dir(text: str, html_parent: Path) -> Path:
    """<base href=images/> used empty Word: src=pic.png lives under images/."""
    root = html_parent.resolve()
    m = re.search(r"(?is)<base\b([^>]*)>", text)
    if not m:
        return html_parent
    href = _html_attr(m.group(1), "href")
    if not href:
        return html_parent
    href = _local_href(href)
    if not href or href.startswith(("data:", "http:", "https:", "//", "#")):
        return html_parent
    try:
        cand = (html_parent / href).resolve()
        cand.relative_to(root)
    except (OSError, ValueError):
        return html_parent
    if cand.is_file():
        cand = cand.parent
    return cand


def _stylesheet_hrefs(html: str) -> list[str]:
    """Sibling <link rel=stylesheet href>. Inline / <style> url() already scanned."""
    hrefs: list[str] = []
    seen: set[str] = set()
    for m in re.finditer(r"(?is)<link\b([^>]*)>", html):
        attrs = m.group(1)
        rel = _html_attr(attrs, "rel")
        href = _html_attr(attrs, "href")
        if not rel or not href:
            continue
        if "stylesheet" not in rel.lower().split():
            continue
        if href not in seen:
            seen.add(href)
            hrefs.append(href)
    return hrefs


def _sibling_url_pngs(text: str, resolve_dir: Path, root: Path, cap: int) -> list[bytes]:
    """url() paths resolve against resolve_dir, stay under root (HTML parent)."""
    pngs: list[bytes] = []
    for m in re.finditer(r"url\(\s*['\"]?(?!data:)([^'\")\s]+)['\"]?\s*\)", text, re.I):
        if len(pngs) >= cap:
            break
        href = _local_href(m.group(1))
        if not href or href.startswith("#") or href.lower() in {"none", "inherit"}:
            continue
        path = (resolve_dir / href).resolve()
        try:
            path.relative_to(root)
        except ValueError:
            continue
        if not path.is_file():
            continue
        png = _path_to_png(path)
        if png:
            pngs.append(png)
    return pngs


def _sibling_imageset_pngs(text: str, resolve_dir: Path, root: Path, cap: int) -> list[bytes]:
    """image-set('pic.png' 1x) with no url(). Share dests used empty / 1-byte."""
    pngs: list[bytes] = []
    for m in re.finditer(r"(?is)image-set\s*\((.*?)\)", text):
        body = re.sub(r"(?i)url\s*\([^)]*\)", " ", m.group(1))
        for q in re.finditer(r"""(['"])([^'"]+)\1""", body):
            if len(pngs) >= cap:
                return pngs
            href = _local_href(q.group(2))
            if not href or href.startswith(("data:", "http:", "https:", "//", "#")):
                continue
            if href.lower() in {"none", "inherit"}:
                continue
            suffix = Path(href.replace("\\", "/").split("/")[-1]).suffix.lower()
            if suffix not in IMAGE_EXTS:
                continue
            path = (resolve_dir / href).resolve()
            try:
                path.relative_to(root)
            except ValueError:
                continue
            if not path.is_file():
                continue
            png = _path_to_png(path)
            if png:
                pngs.append(png)
    return pngs


def _css_import_hrefs(css: str) -> list[str]:
    """@import url(page.css) / @import "page.css". Not background url()."""
    hrefs: list[str] = []
    seen: set[str] = set()
    for m in re.finditer(r"""(?i)@import\s+url\(\s*['"]?([^'")\s]+)['"]?\s*\)""", css):
        val = m.group(1).strip()
        if val and val not in seen:
            seen.add(val)
            hrefs.append(val)
    for m in re.finditer(r"""(?i)@import\s+['"]([^'"]+)['"]""", css):
        val = m.group(1).strip()
        if val and val not in seen:
            seen.add(val)
            hrefs.append(val)
    return hrefs


def _load_css_file_pngs(
    css_path: Path, root: Path, cap: int, budget: list[int], seen: set[Path]
) -> list[bytes]:
    if cap <= 0 or budget[0] <= 0:
        return []
    resolved = css_path.resolve()
    if resolved in seen:
        return []
    try:
        css = _read_markup(css_path)
    except OSError:
        return []
    seen.add(resolved)
    budget[0] -= 1
    pngs = html_string_media_pngs(css, None, cap)
    pngs.extend(_sibling_url_pngs(css, css_path.parent, root, cap - len(pngs)))
    pngs.extend(_sibling_imageset_pngs(css, css_path.parent, root, cap - len(pngs)))
    pngs.extend(_imported_css_pngs(css, css_path.parent, root, cap - len(pngs), budget, seen))
    return pngs


def _imported_css_pngs(
    css: str, css_dir: Path, root: Path, cap: int, budget: list[int], seen: set[Path]
) -> list[bytes]:
    """Follow @import .css. Share dests used empty Word / 1-byte MD / 573-byte PDF."""
    pngs: list[bytes] = []
    for href in _css_import_hrefs(css):
        if len(pngs) >= cap or budget[0] <= 0:
            break
        href = _local_href(href)
        if href.startswith(("data:", "http:", "https:", "//", "#")) or not href:
            continue
        try:
            path = (css_dir / href).resolve()
            path.relative_to(root)
        except (OSError, ValueError):
            continue
        if path.suffix.lower() != ".css" or not path.is_file():
            continue
        pngs.extend(_load_css_file_pngs(path, root, cap - len(pngs), budget, seen))
    return pngs


def _srcset_hrefs(attr: str) -> list[str]:
    """img/source srcset='pic.png 1x, other.png 2x' -> sibling paths."""
    hrefs: list[str] = []
    for part in attr.split(","):
        tok = part.strip().split()
        if tok:
            href = _local_href(tok[0])
            if href:
                hrefs.append(href)
    return hrefs


_CURLY_QUOTES = str.maketrans({
    "\u201c": '"',
    "\u201d": '"',
    "\u201e": '"',
    "\u2018": "'",
    "\u2019": "'",
})


def _straight_quotes(text: str) -> str:
    """Word smart quotes around src= used empty Word / html-strip."""
    return text.translate(_CURLY_QUOTES)


def html_string_media_pngs(text: str, base: Path | None = None, cap: int = 24) -> list[bytes]:
    import base64

    text = _straight_quotes(text)
    pngs: list[bytes] = []
    for m in re.finditer(
        r"data:image/(?:png|jpeg|jpg|gif|webp|bmp|svg\+xml)(?:;[^,;]+)*;base64,([A-Za-z0-9+/=\s]+)",
        text,
        re.I,
    ):
        if len(pngs) >= cap:
            break
        try:
            raw = base64.b64decode(re.sub(r"\s+", "", m.group(1)))
        except Exception:
            continue
        png = _bytes_to_png(raw)
        if png:
            pngs.append(png)
    # Word 2003 HTML / WordML leftover <w:binData> hex stole dest Word
    # (base64 as page copy, wordml:// src is not a sibling file).
    for m in re.finditer(
        r"(?is)<(?:[\w.-]+:)?binData\b[^>]*>\s*([A-Za-z0-9+/=\s]+)\s*"
        r"(?:</(?:[\w.-]+:)?binData\s*>|(?=<)|$)",
        text,
    ):
        if len(pngs) >= cap:
            break
        try:
            raw = base64.b64decode(re.sub(r"\s+", "", m.group(1)))
        except Exception:
            continue
        png = _bytes_to_png(raw)
        if png:
            pngs.append(png)
    for m in re.finditer(r'src\s*=\s*(["\'])(data:image/svg\+xml.*?)\1', text, re.I | re.S):
        if len(pngs) >= cap:
            break
        href = m.group(2)
        if ";base64," in href.lower():
            continue
        png = _svg_data_uri_to_png(href)
        if png:
            pngs.append(png)
    # Inline <svg> (no <img>). Share dest Word/MD used empty / 1-byte files.
    for m in re.finditer(r"(?is)<(?:[\w.-]+:)?svg\b[^>]*>.*?</(?:[\w.-]+:)?svg\s*>", text):
        if len(pngs) >= cap:
            break
        prefix = text[max(0, m.start() - 48):m.start()].lower()
        if "data:image/svg" in prefix:
            continue
        png = _svg_bytes_to_png(m.group(0).encode("utf-8"), base)
        if png:
            pngs.append(png)
    if base is None:
        return pngs
    root = base.resolve()
    resolve_dir = _html_base_dir(text, base)
    for m in re.finditer(
        r'<(?:img|amp-img)\b[^>]*(?<![\w-])src\s*=\s*(?:["\'](?!data:)([^"\']+)["\']|(?!["\'])(?!data:)([^\s>]+))',
        text,
        re.I,
    ):
        if len(pngs) >= cap:
            break
        href = _local_href(m.group(1) or m.group(2) or "")
        if not href:
            continue
        path = (resolve_dir / href).resolve()
        try:
            path.relative_to(root)
        except ValueError:
            continue
        if not path.is_file():
            continue
        png = _path_to_png(path)
        if png:
            pngs.append(png)
    # srcset-only <img> / <picture><source> / AMP. Share dests used empty / 1-byte / 573-byte.
    for m in re.finditer(
        r"""(?is)<(?:img|source|amp-img)\b[^>]*(?<![\w-])srcset\s*=\s*(?:(['"])([^'"]+)\1|([^\s>]+))""",
        text,
    ):
        for href in _srcset_hrefs(m.group(2) if m.group(1) else (m.group(3) or "")):
            if len(pngs) >= cap:
                break
            if href.startswith(("data:", "http:", "https:", "//", "#")):
                continue
            path = (resolve_dir / href).resolve()
            try:
                path.relative_to(root)
            except ValueError:
                continue
            if not path.is_file():
                continue
            png = _path_to_png(path)
            if png:
                pngs.append(png)
    # CSS background-image:url(sib.png). Share dest Word/MD used empty / 1-byte.
    pngs.extend(_sibling_url_pngs(text, resolve_dir, root, cap - len(pngs)))
    pngs.extend(_sibling_imageset_pngs(text, resolve_dir, root, cap - len(pngs)))
    budget = [8]
    seen: set[Path] = set()
    for block in re.finditer(r"(?is)<style\b[^>]*>(.*?)</style>", text):
        if len(pngs) >= cap:
            break
        pngs.extend(_imported_css_pngs(block.group(1), resolve_dir, root, cap - len(pngs), budget, seen))
    # Linked .css url() is relative to the CSS file. Inline scan never saw it.
    for href in _stylesheet_hrefs(text):
        if len(pngs) >= cap or budget[0] <= 0:
            break
        href = _local_href(href)
        if href.startswith(("data:", "http:", "https:", "//", "#")) or not href:
            continue
        try:
            css_path = (resolve_dir / href).resolve()
            css_path.relative_to(root)
        except (OSError, ValueError):
            continue
        if css_path.suffix.lower() != ".css" or not css_path.is_file():
            continue
        pngs.extend(_load_css_file_pngs(css_path, root, cap - len(pngs), budget, seen))
    return pngs


def html_visible_text(text: str) -> str:
    # UTF-8 BOM is not page copy. Notepad / Word HTML used to steal dest
    # Word / MD (\\ufeff is not whitespace, so strip() left it).
    text = text.replace("\ufeff", "")
    # IE / Word conditional comments are not page copy. Leftover "Photo"
    # inside <!--[if IE]> stole dest Word / MD.
    text = re.sub(r"(?s)<!--.*?-->", "", text)
    # Unclosed <!-- Photo leftover stole dest Word and dest PDF (Story white).
    text = re.sub(r"(?s)<!--.*$", "", text)
    # WordML <w:binData> leftover base64 stole dest Word.
    text = re.sub(
        r"(?is)<(?:[\w.-]+:)?binData\b[^>]*>.*?(?:</(?:[\w.-]+:)?binData\s*>|(?=<)|$)",
        "",
        text,
    )
    # Word track-change / field / comment leftovers are not page copy.
    # Leftover "Photo" in <w:delText> stole dest Word on photo Word-HTML
    # and photo Word 2003 XML (binData pictures path). Stop at the next
    # tag so mixed Hello stays the text path.
    # <w:delInstrText> is deleted field-code text (same class as
    # instrText). Leftover "Photo" stole dest Word / dest PDF Story.
    # <w:fldData> is private field storage (not page copy). Leftover
    # "Photo" / base64 stole dest Word / dest PDF Story the same way.
    text = re.sub(
        r"(?is)<((?:[\w.-]+:)?(?:delInstrText|delText|instrText|commentText|fldData))\b[^>]*>.*?(?:</\1\s*>|(?=<)|$)",
        "",
        text,
    )
    # Word <w:del> / <w:moveFrom> wrappers hold <w:t>Photo when exporters
    # skip delText. (?=<) would stop at <w:r> and leave Photo. Stop at
    # </w:del> / img / body / p so nested runs strip and a sibling picture
    # stays. Do not strip HTML <del> (page copy) or <w:ins> / <w:moveTo>.
    text = re.sub(
        r"(?is)<((?:[\w.-]+:)(?:del|moveFrom))\b[^>]*>.*?(?:</\1\s*>|(?=<(?:body|img|p|div|h[1-6]|table)\b)|$)",
        "",
        text,
    )
    # Word mail-merge leftovers are not page copy (same class as fldData).
    # Leftover "Photo" in <w:mailMerge> / <w:odso> / <w:recipients> stole dest Word / dest PDF Story.
    # (?=<) would stop at nested <w:odso>. Prefix required.
    # Do not strip HTML form leftovers (parked).
    text = re.sub(
        r"(?is)<((?:[\w.-]+:)(?:mailMerge|odso|recipients))\b[^>]*>.*?(?:</\1\s*>|(?=<(?:body|img|p|div|h[1-6]|table)\b)|$)",
        "",
        text,
    )
    # Word custom document properties are not page copy (same class as
    # RTF \userprops / head <xml> Author).
    # Leftover "Photo" in <o:CustomDocumentProperties> stole dest Word / dest PDF Story.
    # (?=<) would stop at nested <o:Name>. Do not strip xml islands outside head (parked).
    text = re.sub(
        r"(?is)<((?:[\w.-]+:)?CustomDocumentProperties)\b[^>]*>.*?(?:</\1\s*>|(?=<(?:body|img|p|div|h[1-6]|table)\b)|$)",
        "",
        text,
    )
    # Word document variables are not page copy (same class as RTF \docvar).
    # Leftover "Photo" in <w:docVar> / <w:docVars> stole dest Word / dest PDF Story.
    # Attribute-only w:val is already empty. Do not strip HTML <del> (page copy).
    text = re.sub(
        r"(?is)<((?:[\w.-]+:)?(?:docVars|docVar|DocVar))\b[^>]*>.*?(?:</\1\s*>|(?=<(?:body|img|p|div|h[1-6]|table)\b)|$)",
        "",
        text,
    )
    # Word o:DocumentProperties / o:OfficeDocumentSettings are leftover
    # document metadata (same class as CustomDocumentProperties).
    # Leftover "Photo" in <o:DocumentProperties> stole dest Word / dest PDF Story.
    # o:Title was already stripped; o:Author / o:Company were not.
    # TargetScreenSize 800x600 stole dest the same way.
    # Do not strip xml islands outside head (parked).
    text = re.sub(
        r"(?is)<((?:[\w.-]+:)?(?:DocumentProperties|OfficeDocumentSettings))\b[^>]*>.*?(?:</\1\s*>|(?=<(?:body|img|p|div|h[1-6]|table)\b)|$)",
        "",
        text,
    )
    # Word HTML leftover settings are not page copy (same class as
    # OfficeDocumentSettings).
    # Leftover Print / false / 800x600 in <w:View> / <w:SaveIfXMLInvalid> / <w:BrowserLevel> stole dest Word / dest PDF Story.
    # Leftover EN-US / JA in <w:LidThemeOther> / <w:LidThemeAsian> stole dest Word / dest PDF Story.
    # Leftover DontCompress / false in <w:CharacterSpacingControl> / <w:ValidateAgainstSchemas> stole dest Word / dest PDF Story.
    # Prefix required. Do not strip <w:p> (page copy).
    text = re.sub(
        r"(?is)<((?:[\w.-]+:)(?:View|Zoom|SaveIfXMLInvalid|IgnoreMixedContent|AlwaysShowPlaceholderText|BrowserLevel|PixelsPerInch|TargetScreenSize|DrawingGridHorizontalSpacing|DrawingGridVerticalSpacing|DisplayHorizontalDrawingGridEvery|DisplayVerticalDrawingGridEvery|LidThemeOther|LidThemeAsian|LidThemeComplexScript|CharacterSpacingControl|ValidateAgainstSchemas|SaveInvalidXML|RelyOnVML|AllowPNG|DoNotRelyOnCSS|DoNotOptimizeForBrowser|UseFELayout|DoNotUseLongFileNames|DoNotEmbedSystemFonts|ValidateAgainstSchema|SavePreviewPicture|DoNotShadeFormData))\b[^>]*>.*?(?:</\1\s*>|(?=<(?:body|img|p|div|h[1-6]|table)\b)|$)",
        "",
        text,
    )
    # WordML leftover comment paragraphs are the same class as class=MsoCommentText.
    # Leftover "Photo" in pStyle=CommentText / BalloonText stole dest Word / dest PDF Story.
    # pStyle comes first on official leftovers so Hello Normal is not swallowed.
    # Do not strip pStyle=Normal / Header / EndnoteText (page copy).
    text = re.sub(
        r"(?is)<((?:[\w.-]+:)?p)\b[^>]*>\s*<(?:[\w.-]+:)?pPr\b[^>]*>\s*<(?:[\w.-]+:)?pStyle\b[^>]*\b(?:[\w.-]+:)?val\s*=\s*(['\"]?)(?:CommentText|BalloonText|CommentSubject|Balloon Text|annotation text|annotation subject)\2[^>]*>.*?(?:</\1\s*>|(?=<(?:body|img|p|div|h[1-6]|table)\b)|$)",
        "",
        text,
    )
    # WordML leftover comment-reference runs (same class as MsoCommentReference).
    text = re.sub(
        r"(?is)<((?:[\w.-]+:)?r)\b[^>]*>\s*<(?:[\w.-]+:)?rPr\b[^>]*>\s*<(?:[\w.-]+:)?rStyle\b[^>]*\b(?:[\w.-]+:)?val\s*=\s*(['\"]?)(?:CommentReference|annotation reference)\2[^>]*>.*?(?:</\1\s*>|(?=<(?:body|img|p|div|h[1-6]|table)\b)|$)",
        "",
        text,
    )
    # Word 2003 HTML comments / move-from leftovers are not page copy.
    # Leftover "Photo" in <aml:annotation> / <w:comment> / <w:moveFromText>
    # stole dest Word on photo Word-HTML and leftover binData XML.
    # Do not strip MathML <annotation> (parked).
    # (?=<) would stop at nested <aml:content> / <w:p> and leave Photo.
    # Stop at </aml:annotation> / body / img so nested comment runs strip.
    text = re.sub(
        r"(?is)<((?:aml:annotation|w:annotation|(?:[\w.-]+:)?(?:comment|moveFromText)))\b[^>]*>.*?(?:</\1\s*>|(?=<(?:body|img|p|div|h[1-6]|table)\b)|$)",
        "",
        text,
    )
    # Word Save-as-HTML class leftovers are the same as delText / commentText.
    # Leftover "Photo" in class=msoDel / msocomtxt stole dest Word on photo
    # Word-HTML and leftover binData XML. Do not strip msoIns (page copy)
    # or mso-hide (parked CSS visibility). Stop at the next tag so mixed
    # Hello stays the text path.
    # class=MsoCommentSubject is the comment title line (same class as
    # msoCommentText). Leftover "Photo" stole dest Word / dest PDF Story.
    # class=MsoAcetate / MsoCommentReply / MsoCommentList is the comment
    # balloon chrome (Word Save-as-HTML). Leftover "Photo" stole dest
    # Word / dest PDF Story. Do not strip MsoHeader / MsoCaption (page copy)
    # or msoHide (parked CSS visibility).
    # class=MsoBalloonText / MsoAnnotation is the same balloon chrome.
    # Leftover "Photo" stole dest Word / dest PDF Story.
    # class=msocomhide / msocomanch / msocomanchor is Office comment-hide
    # chrome (same family as msocomoff). Leftover "Photo" stole dest Word / dest PDF Story.
    # <a class=msocomoff> is official Word comment-anchor leftover (Save-as-HTML).
    # class=msoChangeDel / msoDeleted is official Word deletion chrome
    # (same class as msoDel). Leftover "Photo" stole dest Word / dest PDF Story.
    # class=MsoCommentTextChar is the official comment character style.
    text = re.sub(
        r"(?is)<((?:[\w.-]+:)?(?:span|div|p|a))\b[^>]*\bclass\s*=\s*(['\"]?)[^'\">]*"
        r"\b(?:msoDel|msocomtxt|msocomoff|msoCommentText|msoCommentReference|msoCommentSubject|msoAcetate|msoCommentReply|msoCommentList|msoBalloonText|msoAnnotation|msocomhide|msocomanch|msocomanchor|msoChangeDel|msoDeleted|msoCommentTextChar|msoCommentSubjectChar|msoBalloonTextChar)\b"
        r"[^>]*>.*?(?:</\1\s*>|(?=<)|$)",
        "",
        text,
    )
    # style=mso-element:comment / comment-list is Office comment storage.
    # Leftover "Photo" stole dest Word / dest PDF Story.
    # Do not strip mso-element:header / footnote (page copy).
    # <a style=mso-comment-reference> / mso-special-character:comment is the
    # same official comment chrome.
    # style=mso-comment-continuation is official Word comment-continuation chrome.
    # Leftover "Photo" in style=mso-comment-continuation stole dest Word / dest PDF Story.
    # Do not strip mso-element:header / footnote (page copy).
    text = re.sub(
        r"(?is)<((?:[\w.-]+:)?(?:span|div|p|a))\b[^>]*\bstyle\s*=\s*(['\"]?)[^'\">]*"
        r"(?:mso-element\s*:\s*comment(?:-list)?|mso-comment-reference|mso-special-character\s*:\s*comment|mso-comment-continuation)\b"
        r"[^>]*>.*?(?:</\1\s*>|(?=<)|$)",
        "",
        text,
    )
    # style=mso-deletion:yes / mso-deleted:yes is official Word deletion chrome
    # (same class as class=msoDel). Leftover "Photo" in style=mso-deletion / class=msoChangeDel stole dest Word / dest PDF Story.
    # Do not strip mso-insertion / msoIns / HTML <del> (page copy).
    text = re.sub(
        r"(?is)<((?:[\w.-]+:)?(?:span|div|p|a))\b[^>]*\bstyle\s*=\s*(['\"]?)[^'\">]*"
        r"(?:mso-deletion|mso-deleted)\s*:\s*yes\b"
        r"[^>]*>.*?(?:</\1\s*>|(?=<)|$)",
        "",
        text,
    )
    # style=mso-style-name:"Comment Text" is official Word comment style chrome
    # (same class as class=MsoCommentText). Leftover "Photo" in style=mso-style-name:"Comment Text" stole dest Word / dest PDF Story.
    # Nested quotes in style='mso-style-name:"Comment Text"' need [^>]* (not [^'">]).
    # Do not strip mso-style-name:Normal / Heading (page copy).
    text = re.sub(
        r"(?is)<((?:[\w.-]+:)?(?:span|div|p|a))\b[^>]*\bstyle\s*=\s*(['\"]?)[^>]*"
        r"mso-style-name\s*:\s*['\"]?(?:Comment Text|Comment Subject|Comment Reference|Balloon Text|annotation text|annotation subject|annotation reference)(?: Char)?\b"
        r"[^>]*>.*?(?:</\1\s*>|(?=<)|$)",
        "",
        text,
    )
    # <title> / <head> are not page copy. Almost every real HTML page has
    # them; leftover title text used to steal photo dest Word / MD.
    # Do not strip <header> (page content). <head\b> does not match it.
    # Unclosed <head> leftover "Photo" stole dest Word. Stop at </head> /
    # body / img so Word <xml> inside a closed head is not page copy, and
    # a sibling <img> after an unclosed head stays the pictures path.
    # (?=<) used to win first and leave <o:Author>Photo</o:Author>.
    text = re.sub(
        r"(?is)<head\b[^>]*>.*?(?:</head>|(?=<(?:body|img)\b)|$)",
        "",
        text,
    )
    # Unclosed <title> leftover "Photo" stole dest Word. Stop at the next
    # tag so mixed Hello after <body> stays the text path.
    text = re.sub(
        r"(?is)<(?:[\w.-]+:)?title\b[^>]*>.*?(?:</(?:[\w.-]+:)?title\s*>|(?=<)|$)",
        "",
        text,
    )
    # SVG <desc> / <metadata> / <svg:title> are not page copy. Leftover
    # "A photo" stole dest Word / MD / Story PDF (no pictures).
    # Unclosed tags stop at the next tag so <desc>A photo<img stays pictures.
    text = re.sub(
        r"(?is)<(?:[\w.-]+:)?desc\b[^>]*>.*?(?:</(?:[\w.-]+:)?desc\s*>|(?=<)|$)",
        "",
        text,
    )
    text = re.sub(
        r"(?is)<(?:[\w.-]+:)?metadata\b[^>]*>.*?(?:</(?:[\w.-]+:)?metadata\s*>|(?=<)|$)",
        "",
        text,
    )
    # Inkscape RDF / inkscape:label sit outside <metadata>. Leftover
    # "Photo" stole dest Word / MD.
    text = re.sub(
        r"(?is)<(?:[\w.-]+:)?RDF\b[^>]*>.*?(?:</(?:[\w.-]+:)?RDF\s*>|(?=<)|$)",
        "",
        text,
    )
    text = re.sub(
        r"(?is)<inkscape:label\b[^>]*>.*?(?:</inkscape:label\s*>|(?=<)|$)",
        "",
        text,
    )
    # Unclosed <style> / <script> leftover CSS / JS stole dest Word.
    text = re.sub(
        r"(?is)<script\b[^>]*>.*?(?:</script>|(?=</?(?:body|div|p|img|h[1-6]|table)\b)|$)",
        "",
        text,
    )
    text = re.sub(
        r"(?is)<style\b[^>]*>.*?(?:</style>|(?=</?(?:body|div|p|img|h[1-6]|table)\b)|$)",
        "",
        text,
    )
    # <template> / <noscript> / <noframes> / <noembed> are not page copy.
    # Leftover "Enable JS" / "Photo" stole dest Word when a sibling <img>
    # was the only picture. Unclosed tags stop at the next tag so mixed
    # Hello stays the text path.
    text = re.sub(
        r"(?is)<((?:[\w.-]+:)?(?:template|noscript|noframes|noembed))\b[^>]*>.*?(?:</\1\s*>|(?=<)|$)",
        "",
        text,
    )
    # Raw SVG data URIs contain '>' inside src='...'. [^>]* used to leave
    # "' alt=''>" and steal the pictures path (empty Word / html-strip).
    text = re.sub(r'(?is)<img\b[^>]*src\s*=\s*(["\'])(?:.*?)\1[^>]*>', "", text)
    text = re.sub(r"(?is)<img[^>]*>", "", text)
    text = re.sub(r"(?s)<[^>]+>", " ", text)
    # Word HTML <p>&nbsp;</p> / &shy; / &amp;nbsp; are not page copy.
    # One unescape left "&nbsp;" and stole dest Word / MD.
    for _ in range(3):
        nxt = html.unescape(text)
        if nxt == text:
            break
        text = nxt
    # MD dest Word html.escape'd <!-- comment --> back into page copy.
    text = re.sub(r"(?s)<!--.*?-->", "", text)
    text = re.sub(r"(?s)<!--.*$", "", text)
    # Format marks are not page copy. Leftover &#xfe0f; (emoji VS16)
    # stole dest Word and wrote dest PDF white (Story of an empty page).
    # Cf covers LRM / ZWSP / SHY / WJ. VS16 / CGJ are Mn.
    text = "".join(
        ch
        for ch in text
        if unicodedata.category(ch) != "Cf"
        and ch != "\u034f"
        and not (0xFE00 <= ord(ch) <= 0xFE0F)
    )
    return re.sub(r"\s+", " ", text).strip()


_META_CHARSET = re.compile(
    r'(?is)<meta\b[^>]*\bcharset\s*=\s*["\']?([a-z0-9_\-]+)',
)
_XML_ENCODING = re.compile(
    r'''(?is)<\?xml\b[^>]*\bencoding\s*=\s*["\']?([a-z0-9_\-]+)''',
)
_AT_CHARSET = re.compile(
    r'''(?is)@charset\s*["']([a-z0-9_\-]+)["']''',
)
_SAFE_CHARSETS = {
    "windows-1252": "cp1252",
    "cp1252": "cp1252",
    "iso-8859-1": "latin-1",
    "iso8859-1": "latin-1",
    "latin-1": "latin-1",
    "latin1": "latin-1",
    "gbk": "gbk",
    "gb2312": "gb2312",
    "shift-jis": "shift_jis",
    "shiftjis": "shift_jis",
    "big5": "big5",
    "macintosh": "mac_roman",
}
# Transform codecs, not text encodings. Declared charset only -- no chardet.
_UNSAFE_CODECS = {
    "idna", "punycode", "rot_13", "rot13",
    "base64", "base64_codec", "hex", "hex_codec",
    "quopri", "quopri_codec", "uu", "uu_codec",
    "zlib", "zlib_codec", "bz2", "bz2_codec",
}


def _declared_codec(label: str) -> str | None:
    key = (label or "").lower().replace("_", "-")
    if not key:
        return None
    mapped = _SAFE_CHARSETS.get(key)
    if mapped:
        return mapped
    for cand in (key, key.replace("-", "_")):
        try:
            name = codecs.lookup(cand).name
        except LookupError:
            continue
        if name in _UNSAFE_CODECS:
            return None
        return name
    return None


def _meta_charset(text: str) -> str | None:
    m = (
        _META_CHARSET.search(text)
        or _XML_ENCODING.search(text)
        or _AT_CHARSET.search(text)
    )
    if not m:
        return None
    return _declared_codec(m.group(1))


def _read_markup(src: Path) -> str:
    """Notepad Unicode HTML/MD/txt is UTF-16. utf-8 replace used empty Word."""
    raw = src.read_bytes()
    if raw.startswith((b"\xff\xfe", b"\xfe\xff")):
        return raw.decode("utf-16")
    if raw.startswith(b"\xef\xbb\xbf"):
        return raw.decode("utf-8-sig")
    text = raw.decode("utf-8", errors="replace")
    # ANSI HTML with a declared charset / xml encoding: src="été.png" used empty Word.
    # No meta / no chardet -- those stay parked.
    if "\ufffd" not in text:
        return text
    enc = _meta_charset(text)
    if not enc:
        return text
    try:
        return raw.decode(enc)
    except (LookupError, UnicodeDecodeError):
        return text


def html_media_pngs(src: Path, cap: int = 24) -> list[bytes]:
    """Photo-only HTML dest Word. html-strip used to write an empty .docx."""
    return html_string_media_pngs(_read_markup(src), src.parent, cap)


def xml_photo_pngs(src: Path, cap: int = 24) -> list[bytes]:
    """Word 2003 XML dest Word dumped source when the only picture is binData."""
    raw = _read_markup(src)
    pngs = html_string_media_pngs(raw, src.parent, cap)
    if pngs and not html_visible_text(raw):
        return pngs
    return []


def html_string_to_pdf(story_html: str, dest: Path) -> str:
    dest.parent.mkdir(parents=True, exist_ok=True)
    if "<html" not in story_html.lower():
        story_html = f"<html><body>{story_html}</body></html>"
    pdf_backend.html_to_pdf(story_html, dest)
    return "reportlab-story-html"


def html_to_pdf(src: Path, dest: Path) -> str:
    """Share dest 2 on HTML. markdown_to_html dumped source tags into the PDF."""
    text = _read_markup(src)
    visible = html_visible_text(text)
    pngs = html_media_pngs(src)
    # CSS background-image only: Story writes a 573-byte blank page.
    if pngs and not visible:
        return images_to_pdf(pngs, dest, tool="reportlab-html-images")
    try:
        return html_string_to_pdf(inline_sibling_images(text, src.parent), dest)
    except RuntimeError:
        stripped, _tool = file_to_markdown(src)
        return markdown_to_pdf(stripped, dest)


def markdown_to_pdf(text: str, dest: Path) -> str:
    dest.parent.mkdir(parents=True, exist_ok=True)
    pdf_backend.html_to_pdf(f"<html><body>{markdown_to_html(text)}</body></html>", dest)
    return "reportlab-story"


def images_to_pdf(pngs: list[bytes], dest: Path, tool: str = "reportlab-pptx-images") -> str:
    """Photo / image-only office -> PDF pages. Share default PDF used empty markdown."""
    pdf_backend.images_to_pdf(pngs, dest)
    return tool


def image_to_pdf(src: Path, dest: Path) -> str:
    dest.parent.mkdir(parents=True, exist_ok=True)
    # SVG is a document, not a raster. Preview already treats .svg as an
    # image; Share Export as PDF used markitdown.
    if src.suffix.lower() == ".svg":
        text = _svg_lower_root(_inline_svg_sibling_images(_read_markup(src), src.parent))
        if "data:image/" not in text:
            dest.write_bytes(pdf_backend.svg_to_pdf_bytes(text.encode("utf-8")))
            return "reportlab"
        # The vector path drops sibling <image href> after data-URI rewrite.
        png = _svg_bytes_to_png(text.encode("utf-8"))
        if not png:
            raise RuntimeError("cannot rasterize SVG")
        pdf_backend.images_to_pdf([png], dest)
        return "reportlab"
    # .ico / .webp / .heic / .tif etc.: Pillow decodes (HEIC needs optional
    # pillow_heif, as JPG export already does); reportlab writes the page.
    im = load_pil_image(src)
    try:
        fd, name = tempfile.mkstemp(suffix=".png")
        os.close(fd)
        tmp = Path(name)
        try:
            save_pil(im, tmp, "PNG")
            pdf_backend.images_to_pdf([tmp.read_bytes()], dest)
        finally:
            tmp.unlink(missing_ok=True)
    finally:
        im.close()
    return "reportlab"


def copy_file(src: Path, dest: Path) -> str:
    """Share dest 1 on HTML (same folder + stem) used to raise SameFileError."""
    dest.parent.mkdir(parents=True, exist_ok=True)
    if src.resolve() != dest.resolve():
        shutil.copy2(src, dest)
    return "copy"


def pdf_copy(src: Path, dest: Path) -> str:
    return copy_file(src, dest)


def file_to_pdf(src: Path, dest: Path) -> str:
    ext = src.suffix.lower()
    if ext == ".pdf":
        return pdf_copy(src, dest)
    if ext in IMAGE_EXTS:
        return image_to_pdf(src, dest)
    if ext == ".rtf":
        pngs = rtf_media_pngs(src)
        text = rtf_to_text(src)
        if pngs and not text.strip():
            return images_to_pdf(pngs, dest, tool="reportlab-rtf-images")
        return markdown_to_pdf(text, dest)
    if ext in MD_EXTS or ext in TEXT_EXTS or ext in HTML_EXTS:
        if ext in HTML_EXTS:
            return html_to_pdf(src, dest)
        text = _read_markup(src)
        if ext == ".xml":
            pngs = xml_photo_pngs(src)
            if pngs:
                return images_to_pdf(pngs, dest, tool="reportlab-xml-images")
        if ext in MD_EXTS:
            html = markdown_to_html(text)
            visible = html_visible_text(html)
            pngs = html_string_media_pngs(html, src.parent)
            # Story drops data-URI <img>. Photo MD dest PDF was a white page
            # while dest Word already embeds the sibling picture.
            if pngs and not visible:
                return images_to_pdf(pngs, dest, tool="reportlab-md-images")
            try:
                return html_string_to_pdf(inline_sibling_images(html, src.parent), dest)
            except RuntimeError:
                return markdown_to_pdf(text, dest)
        return markdown_to_pdf(text, dest)
    try:
        text, _tool = file_to_markdown(src)
        if text.strip() or (ext not in PHOTO_OFFICE_EXTS and ext not in OLE_SOFFICE_EXTS):
            return markdown_to_pdf(text, dest)
    except RuntimeError:
        text = ""
    if ext in PHOTO_OFFICE_EXTS:
        pngs = office_media_pngs(src)
        if pngs:
            name = "reportlab-pptx-images" if ext in PPTX_EXTS else "reportlab-office-images"
            return images_to_pdf(pngs, dest, tool=name)
    if ext in OLE_SOFFICE_EXTS and not (text or "").strip():
        tool = via_soffice(src, dest)
        if tool:
            return tool
        raise RuntimeError("LibreOffice required to export this file as PDF")
    if text.strip() or ext not in PHOTO_OFFICE_EXTS:
        return markdown_to_pdf(text, dest)
    raise RuntimeError(f"no PDF converter for {ext or 'unknown'}")


# ---------------------------------------------------------------------------
# Image / media export-as
# ---------------------------------------------------------------------------

def load_pil_image(src: Path):
    try:
        from PIL import Image
    except Exception as e:
        raise RuntimeError(f"Pillow missing: {e}") from e
    if src.suffix.lower() == ".svg":
        import io
        png = _svg_bytes_to_png(_read_markup(src).encode("utf-8"), src.parent)
        if not png:
            raise RuntimeError("cannot rasterize SVG")
        return Image.open(io.BytesIO(png)).convert("RGBA")
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
    dest.parent.mkdir(parents=True, exist_ok=True)
    for i, im in enumerate(pdf_backend.render_pages(src, 2.0)):
        out = dest if i == 0 else dest.with_name(f"{dest.stem}_{i + 1}{dest.suffix}")
        fmt = PIL_FORMAT.get(out.suffix.lower(), "PNG")
        save_pil(im, out, fmt)
    return "pdfium"


def export_media(src: Path, dest: Path) -> str:
    dest.parent.mkdir(parents=True, exist_ok=True)
    if src.resolve() == dest.resolve():
        return "copy"
    ffmpeg = find_ffmpeg()
    if not ffmpeg:
        raise RuntimeError("ffmpeg missing (tools/ffmpeg/ffmpeg or PATH)")
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
        # Silent / video-only clips have no audio stream. Share Export as MP3
        # still has to write a file (anullsrc = silence). Cap -t so lavfi
        # cannot run forever (-shortest ignores video when -vn).
        if ext == ".mp3" and (proc.returncode != 0 or not dest.is_file()):
            probe = Path(ffmpeg).with_name("ffprobe" + Path(ffmpeg).suffix)
            probe_exe = str(probe) if probe.is_file() else shutil.which("ffprobe")
            dur = 1.0
            if probe_exe:
                pr = subprocess.run(
                    [
                        probe_exe, "-v", "error", "-show_entries", "format=duration",
                        "-of", "csv=p=0", str(src),
                    ],
                    capture_output=True, text=True, timeout=30,
                )
                try:
                    dur = max(0.05, float((pr.stdout or "").strip()))
                except ValueError:
                    pass
            proc = subprocess.run(
                [
                    ffmpeg, "-y",
                    "-f", "lavfi", "-t", f"{dur:.3f}",
                    "-i", "anullsrc=channel_layout=stereo:sample_rate=44100",
                    "-acodec", "libmp3lame", "-q:a", "4",
                    str(dest),
                ],
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
    if not text:
        raise RuntimeError("watermark --text is required")
    selected = parse_page_spec(pages, pdf_backend.page_count(src))
    pdf_backend.watermark(src, dest, text, opacity, angle, selected)
    return "pikepdf"


def pdf_protect(src: Path, dest: Path, password: str, owner: str | None, perm: str | None) -> str:
    if not password:
        raise RuntimeError("protect --password is required")
    known = {"print", "modify", "copy", "annotate"}
    if perm:
        perms = {part.strip().lower() for part in perm.split(",")} & known
    else:
        perms = {"print", "copy"}
    pdf_backend.protect(src, dest, password, owner, perms)
    return "pikepdf"


def pdf_unlock(src: Path, dest: Path, password: str) -> dict | str:
    if not password:
        return fail("unlock --password is required", "pikepdf")
    if not pdf_backend.unlock(src, dest, password):
        return fail("bad password", "pikepdf")
    return "pikepdf"


def pdf_has_text(src: Path) -> bool:
    return pdf_backend.has_text(src)


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
            pngs = office_media_pngs(src)
            if pngs:
                return pngs_to_html(pngs, dest_html, src.name)
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
    # Picture-only Word (media in the zip, none referenced by the body) came out
    # as an empty page. Show the pictures, as the PDF / Markdown exports do.
    if not html_visible_text(body) and "<img" not in body.lower():
        pngs = office_media_pngs(src)
        if pngs:
            return pngs_to_html(pngs, dest_html, src.name)
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
    try:
        text, tool = file_to_markdown(src)
    except RuntimeError:
        text, tool = "", None
    ext = src.suffix.lower()
    if not (text or "").strip() and ext == ".pdf":
        text, tool = pdf_pages_to_markdown(src)
    elif not (text or "").strip() and ext in PHOTO_OFFICE_EXTS:
        text, tool = photo_office_to_markdown(src)
    elif tool is None:
        raise RuntimeError(f"no markdown converter for {ext or 'unknown'}")
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
    # Space ODT/ODP/ODS uses this action. Empty zip text used to write a
    # blank page while Share Export as HTML already embeds Pictures/.
    try:
        text, tool = file_to_markdown(src)
    except RuntimeError:
        text, tool = "", None
    if (text or "").strip() or ext not in {".odp", ".odt", ".ods", ".xlsx", ".xlsm"}:
        write_text(dest, wrap_html(markdown_to_html(text or ""), src.name))
        return ok(dest, tool or "copy")
    pngs = office_media_pngs(src)
    if pngs:
        return ok(dest, pngs_to_html(pngs, dest, src.name))
    write_text(dest, wrap_html(markdown_to_html(text or ""), src.name))
    return ok(dest, tool or "copy")


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
        try:
            text, tool = file_to_markdown(src)
        except RuntimeError:
            text, tool = "", None
        if not (text or "").strip() and src_ext == ".pdf":
            text, tool = pdf_pages_to_markdown(src)
        elif not (text or "").strip() and src_ext in PHOTO_OFFICE_EXTS:
            text, tool = photo_office_to_markdown(src)
        elif tool is None:
            raise RuntimeError(f"no markdown converter for {src_ext or 'unknown'}")
        write_text(dest, text if text.endswith("\n") else text + "\n")
        return ok(dest, tool)
    if dest_ext in HTML_EXTS:
        if src_ext in {".docx", ".doc"}:
            try:
                return ok(dest, mammoth_to_html(src, dest))
            except RuntimeError:
                pngs = office_media_pngs(src)
                if pngs:
                    return ok(dest, pngs_to_html(pngs, dest, src.name))
                raise
        if src_ext in {".pptx", ".ppsx", ".ppt", ".pps"}:
            return ok(dest, pptx_to_html(src, dest))
        if src_ext in HTML_EXTS:
            return ok(dest, copy_file(src, dest))
        try:
            if src_ext == ".xml":
                pngs = xml_photo_pngs(src)
                if pngs:
                    return ok(dest, pngs_to_html(pngs, dest, src.name))
            text, tool = file_to_markdown(src)
            if text.strip() or src_ext not in {".odp", ".odt", ".ods", ".xlsx", ".xlsm", ".pdf"}:
                body = markdown_to_html(text)
                if src_ext in MD_EXTS:
                    body = inline_sibling_images(body, src.parent)
                write_text(dest, wrap_html(body, src.name))
                return ok(dest, tool)
        except RuntimeError:
            text = ""
        if src_ext == ".pdf":
            return ok(dest, pdf_pages_to_html(src, dest))
        if src_ext in {".odp", ".odt", ".ods", ".xlsx", ".xlsm"}:
            pngs = office_media_pngs(src)
            if pngs:
                return ok(dest, pngs_to_html(pngs, dest, src.name))
        if text.strip():
            write_text(dest, wrap_html(markdown_to_html(text), src.name))
            return ok(dest, "copy")
        raise RuntimeError(f"no HTML converter for {src_ext or 'unknown'}")
    if dest_ext in VIDEO_EXTS or dest_ext in AUDIO_EXTS or (
        dest_ext == ".gif" and src_ext in VIDEO_EXTS
    ):
        return ok(dest, export_media(src, dest))
    if dest_ext in OFFICE_EXTS:
        if dest_ext == src_ext:
            return ok(dest, copy_file(src, dest))
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

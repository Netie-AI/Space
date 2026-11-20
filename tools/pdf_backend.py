"""PDF backend for convert_engine.py: the one seam where PDF libraries are imported.

Replaces PyMuPDF (AGPL, never shipped) with permissively licensed engines:

  pypdfium2   BSD-3-Clause / Apache-2.0   render pages, page count and sizes, text layer
  pikepdf     MPL-2.0                     encrypt, decrypt, overlay (watermark), page structure
  reportlab   BSD-3-Clause                draw overlays, build PDFs from pictures / HTML / text
  svglib      LGPL-3.0-or-later           SVG -> reportlab drawing (pure python, dynamically imported)
  Pillow      MIT-CMU                     picture decode / re-encode

Every public function raises PdfBackendUnavailable (a RuntimeError) with a
"not available without <pip name>" message when its wheel is missing. Nothing
here falls back silently and nothing returns an empty result for a missing
engine.
"""
from __future__ import annotations

import base64
import html as _html
import importlib
import io
import re
from html.parser import HTMLParser
from pathlib import Path
from typing import Any


class PdfBackendUnavailable(RuntimeError):
    """A PDF feature needs a wheel that is not installed."""


def _need(module: str, pip_name: str, feature: str):
    try:
        return importlib.import_module(module)
    except Exception as e:  # ImportError or a broken native wheel
        raise PdfBackendUnavailable(
            f"{feature} is not available without {pip_name} ({type(e).__name__}: {e})"
        ) from e


def _pdfium():
    return _need("pypdfium2", "pypdfium2", "PDF rendering and text extraction")


def _pikepdf():
    return _need("pikepdf", "pikepdf", "PDF encryption, decryption and watermarking")


def _rl(sub: str, feature: str = "PDF drawing"):
    return _need(sub, "reportlab", feature)


def _pil():
    return _need("PIL.Image", "Pillow", "picture handling")


def _open(src, password: str | None = None):
    pdfium = _pdfium()
    return pdfium.PdfDocument(str(src), password=password)


# ---------------------------------------------------------------------------
# Read: count, sizes, text, pages as pictures
# ---------------------------------------------------------------------------

def page_count(src) -> int:
    doc = _open(src)
    try:
        return len(doc)
    finally:
        doc.close()


def page_sizes(src) -> list[tuple[float, float]]:
    """Displayed (rotation applied) size in points of every page."""
    doc = _open(src)
    try:
        return [tuple(doc[i].get_size()) for i in range(len(doc))]
    finally:
        doc.close()


def page_texts(src) -> list[str]:
    """Text layer of every page (empty string for a page without one)."""
    doc = _open(src)
    try:
        out = []
        for i in range(len(doc)):
            page = doc[i]
            tp = page.get_textpage()
            out.append((tp.get_text_range() or "").replace("\r\n", "\n"))
            tp.close()
            page.close()
        return out
    finally:
        doc.close()


def has_text(src) -> bool:
    return any(t.strip() for t in page_texts(src))


def render_pages(src, scale: float = 1.0, cap: int | None = None):
    """Yield one PIL RGB image per page (page 1 first), at 72 dpi * scale."""
    doc = _open(src)
    try:
        n = len(doc)
        if n < 1:
            raise RuntimeError("PDF has no pages")
        for i in range(n):
            if cap is not None and i >= cap:
                break
            page = doc[i]
            try:
                img = page.render(scale=scale).to_pil().convert("RGB")
            finally:
                page.close()
            yield img
    finally:
        doc.close()


def png_bytes(img) -> bytes:
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def page_images_count(src, index: int) -> int:
    """Number of embedded pictures drawn on page `index` (0-based)."""
    pdfium = _pdfium()
    doc = _open(src)
    try:
        page = doc[index]
        try:
            return sum(1 for _ in page.get_objects(filter=[pdfium.raw.FPDF_PAGEOBJ_IMAGE]))
        finally:
            page.close()
    finally:
        doc.close()


# ---------------------------------------------------------------------------
# Write: pictures -> PDF
# ---------------------------------------------------------------------------

def _image_reader(blob: bytes):
    """ImageReader for any picture Pillow can decode (JPEG passes through untouched)."""
    Image = _pil()
    rl_utils = _rl("reportlab.lib.utils")
    im = Image.open(io.BytesIO(blob))
    fmt = (im.format or "").upper()
    size = im.size
    if fmt == "JPEG" and im.mode in ("RGB", "L"):
        return rl_utils.ImageReader(io.BytesIO(blob)), size
    im.load()
    if im.mode not in ("RGB", "RGBA", "L"):
        im = im.convert("RGBA" if "A" in im.mode or "transparency" in im.info else "RGB")
    buf = io.BytesIO()
    im.save(buf, format="PNG")
    buf.seek(0)
    return rl_utils.ImageReader(buf), size


def images_to_pdf(blobs: list[bytes], dest: Path) -> None:
    """One page per picture; page size = picture size in pixels (as points)."""
    canvas = _rl("reportlab.pdfgen.canvas")
    dest.parent.mkdir(parents=True, exist_ok=True)
    c = canvas.Canvas(str(dest), pageCompression=1)
    for blob in blobs:
        reader, (w, h) = _image_reader(blob)
        c.setPageSize((float(w), float(h)))
        c.drawImage(reader, 0, 0, width=w, height=h, mask="auto")
        c.showPage()
    c.save()


# ---------------------------------------------------------------------------
# Write: SVG
# ---------------------------------------------------------------------------

def _svg_drawing(raw: bytes):
    svglib = _need("svglib.svglib", "svglib", "SVG rendering")
    drawing = svglib.svg2rlg(io.BytesIO(raw))
    if drawing is None:
        raise RuntimeError("SVG could not be parsed")
    if not drawing.width or not drawing.height:
        # No width/height/viewBox (sprite sheets, <use> wrappers): size to the painted content.
        x0, y0, x1, y1 = drawing.getBounds()
        if x1 > 0 and y1 - y0 > 0:
            drawing.width, drawing.height = float(x1), float(y1 - y0)
            if y0:
                drawing.translate(0, -y0)
        else:
            raise RuntimeError("SVG has no size and paints nothing")
        k = 1.0
    else:
        k = 96.0 / 72.0
    # svglib reads unitless lengths as CSS px and converts to points (x 0.75).
    # Keep one SVG user unit = one PDF point / one raster pixel (what the app showed before).
    drawing.width *= k
    drawing.height *= k
    drawing.scale(k, k)
    return drawing


def svg_to_pdf_bytes(raw: bytes) -> bytes:
    renderPDF = _rl("reportlab.graphics.renderPDF", "SVG rendering")
    drawing = _svg_drawing(raw)
    if not drawing.width or not drawing.height:
        raise RuntimeError("SVG has no size")
    return renderPDF.drawToString(drawing)


def svg_to_png(raw: bytes, scale: float = 1.0) -> bytes:
    """Rasterize SVG: svglib builds the drawing, PDFium paints it."""
    pdf = svg_to_pdf_bytes(raw)
    doc = _pdfium().PdfDocument(pdf)
    try:
        page = doc[0]
        try:
            img = page.render(scale=scale).to_pil().convert("RGB")
        finally:
            page.close()
    finally:
        doc.close()
    return png_bytes(img)


# ---------------------------------------------------------------------------
# Write: text and HTML -> PDF (reportlab platypus)
# ---------------------------------------------------------------------------

_FONT_CANDIDATES = [
    # (regular, bold, italic, bold-italic)
    ("DejaVuSans.ttf", "DejaVuSans-Bold.ttf", "DejaVuSans-Oblique.ttf", "DejaVuSans-BoldOblique.ttf"),
    ("NotoSans-Regular.ttf", "NotoSans-Bold.ttf", "NotoSans-Italic.ttf", "NotoSans-BoldItalic.ttf"),
    ("arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"),
    ("segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"),
]
_CJK_CANDIDATES = [
    "msyh.ttc", "msyh.ttf", "simsun.ttc", "meiryo.ttc", "malgun.ttf", "NotoSansCJK-Regular.ttc",
    "NotoSansCJKsc-Regular.otf", "wqy-microhei.ttc", "WenQuanYi Micro Hei.ttf",
]
_FONT_DIRS = [
    "C:/Windows/Fonts", "/usr/share/fonts", "/usr/local/share/fonts", "/Library/Fonts",
    "/System/Library/Fonts", "~/.fonts", "~/.local/share/fonts",
]
_font_cache: dict[str, Any] = {}


def _find_font_file(name: str) -> Path | None:
    for d in _FONT_DIRS:
        base = Path(d).expanduser()
        if not base.is_dir():
            continue
        direct = base / name
        if direct.is_file():
            return direct
        try:
            for hit in base.rglob(name):
                return hit
        except OSError:
            continue
    return None


def _winansi_ok(text: str) -> bool:
    try:
        text.encode("cp1252")
        return True
    except UnicodeEncodeError:
        return False


def _register_family(key: str, files) -> tuple[str, str, str, str] | None:
    if key in _font_cache:
        return _font_cache[key]
    pdfmetrics = _rl("reportlab.pdfbase.pdfmetrics")
    ttfonts = _rl("reportlab.pdfbase.ttfonts")
    found = [_find_font_file(f) if f else None for f in files]
    if not found[0]:
        _font_cache[key] = None
        return None
    names = []
    for i, path in enumerate(found):
        use = path or found[0]
        fname = f"NS-{key}-{i}"
        try:
            pdfmetrics.registerFont(ttfonts.TTFont(fname, str(use), subfontIndex=0))
        except Exception:
            _font_cache[key] = None
            return None
        names.append(fname)
    pdfmetrics.registerFontFamily(names[0], normal=names[0], bold=names[1], italic=names[2], boldItalic=names[3])
    fam = (names[0], names[1], names[2], names[3])
    _font_cache[key] = fam
    return fam


def _covers(fontname: str, text: str) -> bool:
    pdfmetrics = _rl("reportlab.pdfbase.pdfmetrics")
    face = pdfmetrics.getFont(fontname).face
    cmap = getattr(face, "charToGlyph", None)
    if cmap is None:
        return _winansi_ok(text)
    return all((ord(ch) in cmap) or ch.isspace() for ch in text)


def pick_font(text: str) -> tuple[str, str, str, str]:
    """Regular, bold, italic, bold-italic font names able to show `text`."""
    base = ("Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique")
    if _winansi_ok(text):
        return base
    for cand in _FONT_CANDIDATES:
        fam = _register_family(cand[0], cand)
        if fam and _covers(fam[0], text):
            return fam
    for name in _CJK_CANDIDATES:
        fam = _register_family(name, (name, None, None, None))
        if fam and _covers(fam[0], text):
            return fam
    # no font covers everything: uncovered characters are written as '?' (visible, not silent)
    for cand in _FONT_CANDIDATES:
        fam = _register_family(cand[0], cand)
        if fam:
            return fam
    return base


def _fit_text(text: str, fontname: str) -> str:
    """Replace characters the font cannot draw with '?'."""
    if fontname.startswith("Helvetica"):
        return text.encode("cp1252", errors="replace").decode("cp1252")
    pdfmetrics = _rl("reportlab.pdfbase.pdfmetrics")
    cmap = getattr(pdfmetrics.getFont(fontname).face, "charToGlyph", {})
    return "".join(ch if (ord(ch) in cmap or ch.isspace()) else "?" for ch in text)


_BLOCK = {"p", "div", "section", "article", "header", "footer", "main", "aside", "figure",
          "figcaption", "dd", "dt", "dl", "address", "form", "fieldset", "center", "body", "html"}
_HEAD = {"h1": 20, "h2": 16, "h3": 14, "h4": 12, "h5": 11, "h6": 10}
_SKIP = {"script", "style", "head", "title", "template", "noscript", "svg", "iframe", "object", "audio", "video"}
_INLINE_MAP = {"b": "b", "strong": "b", "i": "i", "em": "i", "cite": "i", "u": "u", "ins": "u",
               "s": "strike", "strike": "strike", "del": "strike", "sup": "super", "sub": "sub"}
_VOID = {"br", "hr", "img", "meta", "link", "input", "col", "area", "base", "source", "wbr"}


class _HtmlFlow(HTMLParser):
    """Small HTML -> platypus flowables converter (text, lists, tables, pre, pictures)."""

    def __init__(self, styles, fonts, width):
        super().__init__(convert_charrefs=True)
        self.s = styles
        self.fonts = fonts
        self.width = width
        self.flow: list = []
        self.stack: list[list] = [self.flow]      # container stack (lists / quotes / cells)
        self.buf: list[str] = []
        self.style_name = "body"
        self.skip = 0
        self.pre = 0
        self.list_stack: list[dict] = []
        self.tables: list[dict] = []

    def _flush(self):
        raw = "".join(self.buf)
        self.buf = []
        P = _rl("reportlab.platypus")
        if self.pre:
            if raw.strip():
                self.stack[-1].append(P.XPreformatted(raw.strip("\n"), self.s["pre"]))
            return
        raw = re.sub(r"\s+", " ", raw).strip()
        if re.sub(r"(<[^>]*>|\s|&nbsp;)+", "", raw):
            self.stack[-1].append(P.Paragraph(raw, self.s[self.style_name]))

    def _text(self, t: str) -> str:
        return _html.escape(_fit_text(t, self.fonts[0]), quote=False)

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if tag in _SKIP:
            self.skip += 1
            return
        if self.skip:
            return
        P = _rl("reportlab.platypus")
        colors = _rl("reportlab.lib.colors")
        if tag == "br":
            self.buf.append("<br/>")
        elif tag in _HEAD:
            self._flush()
            self.style_name = tag
        elif tag in _BLOCK:
            self._flush()
        elif tag == "pre":
            self._flush()
            self.pre += 1
        elif tag in ("ul", "ol"):
            self._flush()
            start = a.get("start") or "1"
            self.list_stack.append({"kind": tag, "items": [], "start": int(start) if start.isdigit() else 1})
            self.stack.append([])
        elif tag == "li":
            self._flush()
            self.stack.append([])
        elif tag == "blockquote":
            self._flush()
            self.stack.append([])
        elif tag == "table":
            self._flush()
            self.tables.append({"rows": [], "row": None})
            self.stack.append([])
        elif tag == "tr":
            self._flush()
            if self.tables:
                self.tables[-1]["row"] = []
        elif tag in ("td", "th"):
            self._flush()
            self.stack.append([])
            if tag == "th":
                self.buf.append("<b>")
        elif tag == "hr":
            self._flush()
            self.stack[-1].append(P.HRFlowable(width="100%", thickness=0.6, color=colors.grey))
        elif tag == "img":
            src = a.get("src") or ""
            m = re.match(r"(?is)data:image/[\w.+-]+;base64,(.*)", src.strip())
            if m:
                self._flush()
                try:
                    blob = base64.b64decode(re.sub(r"\s+", "", m.group(1)))
                    _reader, (w, h) = _image_reader(blob)
                    scale = min(1.0, self.width / float(w), 600.0 / float(h))
                    self.stack[-1].append(P.Image(io.BytesIO(blob), width=w * scale, height=h * scale))
                except Exception:
                    if a.get("alt"):
                        self.buf.append(self._text(a["alt"]))
            elif a.get("alt"):
                self.buf.append(self._text(a["alt"]))
        elif tag in ("code", "tt", "kbd", "samp"):
            self.buf.append('<font face="Courier">')
        elif tag == "a":
            href = (a.get("href") or "").strip()
            if re.match(r"(?i)(https?|mailto):", href):
                self.buf.append(f'<a href="{_html.escape(href, quote=True)}" color="blue">')
            else:
                self.buf.append("<span>")
        elif tag in _INLINE_MAP:
            self.buf.append(f"<{_INLINE_MAP[tag]}>")

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag not in _VOID:
            self.handle_endtag(tag)

    def handle_endtag(self, tag):
        if tag in _SKIP:
            self.skip = max(0, self.skip - 1)
            return
        if self.skip or tag in _VOID:
            return
        P = _rl("reportlab.platypus")
        colors = _rl("reportlab.lib.colors")
        if tag in _HEAD:
            self._flush()
            self.style_name = "body"
        elif tag in _BLOCK:
            self._flush()
        elif tag == "pre":
            self._flush()
            self.pre = max(0, self.pre - 1)
        elif tag == "li":
            self._flush()
            items = self.stack.pop() if len(self.stack) > 1 else []
            if self.list_stack:
                self.list_stack[-1]["items"].append(items or [P.Spacer(1, 1)])
        elif tag in ("ul", "ol"):
            self._flush()
            inner = self.stack.pop() if len(self.stack) > 1 else []
            if self.list_stack:
                lst = self.list_stack.pop()
                items = [P.ListItem(it) for it in lst["items"]]
                if items:
                    if lst["kind"] == "ul":
                        self.stack[-1].append(P.ListFlowable(items, bulletType="bullet", start="\u2022",
                                                             bulletFontName=self.fonts[0], leftIndent=18))
                    else:
                        self.stack[-1].append(P.ListFlowable(items, bulletType="1", start=lst["start"],
                                                             bulletFontName=self.fonts[0], leftIndent=18))
            self.stack[-1].extend(inner)
        elif tag == "blockquote":
            self._flush()
            inner = self.stack.pop() if len(self.stack) > 1 else []
            if inner:
                self.stack[-1].append(P.Table([[inner]], colWidths=[self.width], style=[
                    ("LEFTPADDING", (0, 0), (-1, -1), 12),
                    ("LINEBEFORE", (0, 0), (0, -1), 2, colors.grey)]))
        elif tag in ("td", "th"):
            if tag == "th":
                self.buf.append("</b>")
            self._flush()
            cell = self.stack.pop() if len(self.stack) > 1 else []
            if self.tables and self.tables[-1]["row"] is not None:
                self.tables[-1]["row"].append(cell or [P.Spacer(1, 1)])
        elif tag == "tr":
            if self.tables and self.tables[-1]["row"]:
                self.tables[-1]["rows"].append(self.tables[-1]["row"])
            if self.tables:
                self.tables[-1]["row"] = None
        elif tag == "table":
            self._flush()
            inner = self.stack.pop() if len(self.stack) > 1 else []
            tbl = self.tables.pop() if self.tables else {"rows": []}
            rows = tbl["rows"]
            if rows:
                ncols = max(len(r) for r in rows)
                rows = [r + [[P.Spacer(1, 1)]] * (ncols - len(r)) for r in rows]
                colw = (self.width - 4) / ncols
                self.stack[-1].append(P.Table(rows, colWidths=[colw] * ncols, repeatRows=1, style=[
                    ("GRID", (0, 0), (-1, -1), 0.5, colors.grey), ("VALIGN", (0, 0), (-1, -1), "TOP")]))
                self.stack[-1].append(P.Spacer(1, 6))
            self.stack[-1].extend(inner)
        elif tag in ("code", "tt", "kbd", "samp"):
            self.buf.append("</font>")
        elif tag == "a":
            self.buf.append("</a>" if any("<a " in b for b in self.buf) else "</span>")
        elif tag in _INLINE_MAP:
            self.buf.append(f"</{_INLINE_MAP[tag]}>")

    def handle_data(self, data):
        if self.skip:
            return
        self.buf.append(_html.escape(_fit_text(data, self.fonts[0]), quote=False) if self.pre else self._text(data))

    def finish(self):
        self.close()
        while len(self.stack) > 1:      # unclosed li / ul / td: keep their content
            inner = self.stack.pop()
            self.stack[-1].extend(inner)
        self._flush()


def _styles(fonts):
    ParagraphStyle = _rl("reportlab.lib.styles").ParagraphStyle
    colors = _rl("reportlab.lib.colors")
    s = {"body": ParagraphStyle("body", fontName=fonts[0], fontSize=11, leading=14.5, spaceAfter=6)}
    for tag, size in _HEAD.items():
        s[tag] = ParagraphStyle(tag, parent=s["body"], fontName=fonts[1], fontSize=size, leading=size * 1.25,
                                spaceBefore=8, spaceAfter=6)
    s["pre"] = ParagraphStyle("pre", parent=s["body"], fontName="Courier", fontSize=9.5, leading=12,
                              backColor=colors.Color(0.95, 0.95, 0.95), leftIndent=4, rightIndent=4)
    return s


def html_to_pdf(story_html: str, dest: Path) -> None:
    """Letter pages, 36 pt margins, selectable text. Raises RuntimeError if nothing could be laid out."""
    P = _rl("reportlab.platypus")
    pagesizes = _rl("reportlab.lib.pagesizes")
    dest.parent.mkdir(parents=True, exist_ok=True)
    plain = re.sub(r"(?is)<(script|style|head)\b.*?</\1\s*>", " ", story_html)
    plain = _html.unescape(re.sub(r"<[^>]+>", " ", plain))
    fonts = pick_font(plain)
    width = pagesizes.letter[0] - 72
    parser = _HtmlFlow(_styles(fonts), fonts, width)
    parser.feed(story_html)
    parser.finish()
    flow = parser.flow or [P.Paragraph("&nbsp;", parser.s["body"])]
    doc = P.SimpleDocTemplate(str(dest), pagesize=pagesizes.letter, leftMargin=36, rightMargin=36,
                              topMargin=36, bottomMargin=36)
    try:
        doc.build(flow)
    except Exception as e:
        dest.unlink(missing_ok=True)
        raise RuntimeError(f"reportlab could not lay out the HTML: {e}") from e
    if not dest.is_file() or dest.stat().st_size == 0:
        raise RuntimeError("reportlab wrote no PDF")


def text_to_pdf(text: str, dest: Path) -> None:
    """Plain text on letter pages (line breaks kept)."""
    escaped = _html.escape(text.replace("\r\n", "\n"), quote=False)
    html_to_pdf(f"<pre>{escaped}</pre>", dest)


# ---------------------------------------------------------------------------
# Edit: watermark, protect, unlock
# ---------------------------------------------------------------------------

def _overlay_page_pdf(width: float, height: float, text: str, opacity: float, angle: float) -> bytes:
    canvas = _rl("reportlab.pdfgen.canvas")
    pdfmetrics = _rl("reportlab.pdfbase.pdfmetrics")
    face = pick_font(text)[0]
    text = _fit_text(text, face)
    buf = io.BytesIO()
    c = canvas.Canvas(buf, pagesize=(width, height))
    size = max(18.0, min(width, height) / 6.0)
    # the text run must fit the page along its own direction
    import math
    rad = math.radians(float(angle))
    cos, sin = abs(math.cos(rad)), abs(math.sin(rad))
    avail = 0.9 * min(width / cos if cos > 1e-6 else 1e9, height / sin if sin > 1e-6 else 1e9)
    tw = pdfmetrics.stringWidth(text, face, size)
    if tw > avail > 0:
        size = max(6.0, size * avail / tw)
        tw = pdfmetrics.stringWidth(text, face, size)
    c.saveState()
    c.translate(width / 2.0, height / 2.0)
    c.rotate(float(angle))
    c.setFillAlpha(max(0.0, min(1.0, float(opacity))))
    c.setFillColorRGB(0.55, 0.55, 0.55)
    c.setFont(face, size)
    c.drawString(-tw / 2.0, -size * 0.35, text)
    c.restoreState()
    c.showPage()
    c.save()
    return buf.getvalue()


def watermark(src, dest: Path, text: str, opacity: float, angle: int, selected: list[int]) -> None:
    """Stamp `text` over the chosen 1-based pages; the original page content stays untouched."""
    pikepdf = _pikepdf()
    dest.parent.mkdir(parents=True, exist_ok=True)
    with pikepdf.open(str(src)) as pdf:
        for num in selected:
            page = pdf.pages[num - 1]
            x0, y0, x1, y1 = (float(v) for v in page.cropbox)
            rot = int(page.obj.get("/Rotate", 0)) % 360
            w, h = abs(x1 - x0), abs(y1 - y0)
            dw, dh = (h, w) if rot in (90, 270) else (w, h)
            overlay = pikepdf.open(io.BytesIO(_overlay_page_pdf(dw, dh, text, opacity, angle)))
            page.add_overlay(overlay.pages[0], pikepdf.Rectangle(x0, y0, x1, y1))
        pdf.save(str(dest))


def protect(src, dest: Path, password: str, owner: str | None, perms: set[str]) -> None:
    """AES-256 (R6). `perms` is a subset of print, modify, copy, annotate."""
    pikepdf = _pikepdf()
    dest.parent.mkdir(parents=True, exist_ok=True)
    allow = pikepdf.Permissions(
        print_lowres="print" in perms,
        print_highres="print" in perms,
        extract="copy" in perms,
        accessibility=True,
        modify_annotation="annotate" in perms,
        modify_form="annotate" in perms,
        modify_assembly="modify" in perms,
        modify_other="modify" in perms,
    )
    with pikepdf.open(str(src)) as pdf:
        pdf.save(str(dest), encryption=pikepdf.Encryption(user=password, owner=owner or password, R=6, allow=allow))


def unlock(src, dest: Path, password: str) -> bool:
    """Write an unprotected copy. Returns False when the password is wrong."""
    pikepdf = _pikepdf()
    dest.parent.mkdir(parents=True, exist_ok=True)
    try:
        pdf = pikepdf.open(str(src), password=password)
    except pikepdf.PasswordError:
        return False
    with pdf:
        pdf.save(str(dest), encryption=False)
    return True

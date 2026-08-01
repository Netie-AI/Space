"""Acceptance tests for PDF ops in convert_engine.py (watermark, protect, unlock, OCR).

Run:  D:\\Netie Space\\.venv\\Scripts\\python.exe scripts\\pdf-stress\\test_pdf_ops.py
Exit code 0 = all green. OCR cases SKIP if tesseract or ocrmypdf is missing.
"""
from __future__ import annotations

import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ENGINE = ROOT / "tools" / "convert_engine.py"
PY = ROOT / ".venv" / "Scripts" / "python.exe"
if not PY.exists():
    PY = Path(sys.executable)

PASS, FAIL, SKIP = [], [], []


def run(action_args: list[str]) -> dict:
    proc = subprocess.run(
        [str(PY), str(ENGINE), *action_args],
        capture_output=True,
        text=True,
        timeout=600,
    )
    line = next((ln for ln in reversed(proc.stdout.splitlines()) if ln.strip().startswith("{")), "")
    try:
        return json.loads(line)
    except Exception:
        return {"success": False, "error": proc.stderr[:400] or proc.stdout[:400]}


def check(name: str, cond: bool, detail: str = "") -> None:
    (PASS if cond else FAIL).append(f"{name}  {detail}".rstrip())
    print(("PASS " if cond else "FAIL ") + name + (f"  — {detail}" if detail else ""))


def skip(name: str, reason: str) -> None:
    SKIP.append(f"{name} ({reason})")
    print(f"SKIP {name} — {reason}")


def pdf_text(path: Path, page: int = 0) -> str:
    import pymupdf

    doc = pymupdf.open(str(path))
    try:
        return doc[page].get_text()
    finally:
        doc.close()


def ocr_available() -> bool:
    try:
        import ocrmypdf  # noqa: F401
    except ImportError:
        return False
    return shutil.which("tesseract") is not None


def build_fixtures(d: Path) -> tuple[Path, Path]:
    import pymupdf
    from PIL import Image, ImageDraw, ImageFont

    native = d / "native.pdf"
    doc = pymupdf.open()
    page = doc.new_page(width=400, height=300)
    page.insert_text((40, 120), "Page one", fontsize=22)
    doc.save(str(native))
    doc.close()

    # Raster-only "scan": real pixels with readable text, no PDF text layer.
    scan_png = d / "scan.png"
    im = Image.new("RGB", (400, 300), (255, 255, 255))
    dr = ImageDraw.Draw(im)
    try:
        font = ImageFont.truetype("arial.ttf", 36)
    except Exception:
        font = ImageFont.load_default()
    dr.text((40, 120), "HELLO OCR SCAN", fill=(0, 0, 0), font=font)
    im.save(scan_png)

    scan = d / "scan.pdf"
    doc = pymupdf.open()
    page = doc.new_page(width=400, height=300)
    page.insert_image(page.rect, filename=str(scan_png))
    doc.save(str(scan))
    doc.close()

    return native, scan


def main() -> int:
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        native, scan = build_fixtures(d)

        # pdf-watermark native -> out
        out = d / "watermarked.pdf"
        res = run(["pdf-watermark", str(native), str(out), "--text", "DRAFT"])
        text = pdf_text(out) if out.exists() else ""
        ok = (
            res.get("success")
            and out.exists()
            and out.stat().st_size > 200
            and "DRAFT" in text
        )
        check("pdf-watermark native->out", ok, res.get("error", ""))

        # pdf-protect native -> locked
        locked = d / "protected.pdf"
        res = run(["pdf-protect", str(native), str(locked), "--password", "secret"])
        needs_auth = False
        if locked.exists():
            import pymupdf

            doc = pymupdf.open(str(locked))
            needs_auth = doc.needs_pass
            doc.close()
        ok = res.get("success") and locked.exists() and needs_auth
        check("pdf-protect native->locked", ok, res.get("error", ""))

        # pdf-unlock locked -> clear
        clear = d / "unlocked.pdf"
        res = run(["pdf-unlock", str(locked), str(clear), "--password", "secret"])
        opens_clear = False
        preserved = False
        if clear.exists():
            import pymupdf

            doc = pymupdf.open(str(clear))
            opens_clear = not doc.needs_pass
            preserved = "Page one" in doc[0].get_text()
            doc.close()
        ok = res.get("success") and clear.exists() and opens_clear and preserved
        check("pdf-unlock locked->clear", ok, res.get("error", ""))

        # pdf-unlock wrong password
        bad = d / "bad_unlock.pdf"
        res = run(["pdf-unlock", str(locked), str(bad), "--password", "wrong"])
        check(
            "pdf-unlock wrong-password",
            res.get("success") is False and "error" in res,
            res.get("error", ""),
        )

        # pdf-ocr scan -> searchable
        if not ocr_available():
            skip("pdf-ocr scan->searchable", "tesseract or ocrmypdf missing")
            skip("pdf-ocr native->out", "tesseract or ocrmypdf missing")
        else:
            ocr_out = d / "searchable.pdf"
            res = run(["pdf-ocr", str(scan), str(ocr_out)])
            ocr_text = pdf_text(ocr_out) if ocr_out.exists() else ""
            # Tesseract may drop spaces; accept either form.
            folded = ocr_text.upper().replace(" ", "")
            ok = (
                res.get("success")
                and ocr_out.exists()
                and ("HELLO" in folded and "OCR" in folded and "SCAN" in folded)
            )
            check(
                "pdf-ocr scan->searchable",
                ok,
                res.get("error") or f"text={ocr_text[:80]!r}",
            )

            native_ocr = d / "native_ocr.pdf"
            before = pdf_text(native)
            res = run(["pdf-ocr", str(native), str(native_ocr)])
            after = pdf_text(native_ocr) if native_ocr.exists() else ""
            ok = res.get("success") and native_ocr.exists() and after.strip() == before.strip()
            check("pdf-ocr native->out", ok, res.get("error", ""))

        # bad target / missing input
        res = run(["pdf-watermark", str(d / "missing.pdf"), str(d / "x.pdf"), "--text", "X"])
        check("missing input guarded", res.get("success") is False and "error" in res, res.get("error", ""))

        res = run(["pdf-protect", str(native), str(d / "nope.pdf"), "--password", "x"])
        # Protect to invalid path under non-existent root on Windows may still work in temp;
        # use missing source instead for a second guard case.
        res2 = run(["pdf-unlock", str(d / "missing.pdf"), str(d / "y.pdf"), "--password", "x"])
        check("missing unlock input guarded", res2.get("success") is False and "error" in res2, res2.get("error", ""))

    print(f"\n{len(PASS)} passed, {len(FAIL)} failed, {len(SKIP)} skipped")
    if SKIP:
        print("skipped: " + "; ".join(SKIP))
    if FAIL:
        print("FAILURES:\n  " + "\n  ".join(FAIL))
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

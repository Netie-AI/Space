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


if __name__ == "__main__":
    test_engine_file_exists()
    test_actions_registered()
    test_unknown_action_emits_json()
    test_watermark_protect_smoke()
    print("PASS test_convert_engine")

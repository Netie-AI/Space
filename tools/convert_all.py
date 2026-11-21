"""Batch convert PDFs with MarkItDown and enhance to structured markdown."""
from __future__ import annotations

import sys
from pathlib import Path

from markitdown import MarkItDown

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "tools"))
from markdown_enhance import enhance_markdown  # noqa: E402

OUT_DIR = ROOT / "markdown"
SUPPORTED = {".pdf", ".pptx", ".ppt", ".docx", ".doc", ".xlsx", ".xls"}


def convert_file(path: Path, md: MarkItDown) -> Path:
    out_path = OUT_DIR / f"{path.stem}.md"
    result = md.convert_local(str(path))
    content = enhance_markdown(result.text_content, path.name)
    out_path.write_text(content, encoding="utf-8")
    return out_path


def main() -> None:
    OUT_DIR.mkdir(exist_ok=True)
    md = MarkItDown()
    files = sorted(
        p for p in ROOT.iterdir() if p.is_file() and p.suffix.lower() in SUPPORTED
    )
    if not files:
        print("No supported documents found.")
        return

    print(f"Converting {len(files)} file(s) to {OUT_DIR}/\n")
    for path in files:
        try:
            out = convert_file(path, md)
            size = out.stat().st_size
            print(f"  OK  {path.name} -> {out.name} ({size:,} bytes)")
        except Exception as exc:
            print(f"  FAIL {path.name}: {exc}")

    print("\nDone.")


if __name__ == "__main__":
    main()

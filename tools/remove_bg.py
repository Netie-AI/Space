"""Remove image background via rembg (U2-Net / onnxruntime).

CLI:
  python tools/remove_bg.py check
  python tools/remove_bg.py remove <input> [output.png]

Outputs one JSON object on stdout. Optional — install with:
  .\\.venv\\Scripts\\pip install rembg
See optional/README.md.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path


def _emit(**kwargs) -> None:
    print(json.dumps(kwargs, ensure_ascii=False), flush=True)


def _check() -> int:
    try:
        import rembg  # noqa: F401
        import onnxruntime  # noqa: F401
    except ImportError as ex:
        _emit(
            success=False,
            available=False,
            error=f"rembg not installed: {ex}. Run: .\\.venv\\Scripts\\pip install rembg",
        )
        return 1

    _emit(success=True, available=True, tool="rembg")
    return 0


def _remove(input_path: Path, output_path: Path | None) -> int:
    try:
        from rembg import remove, new_session
    except ImportError as ex:
        _emit(
            success=False,
            available=False,
            error=f"rembg not installed: {ex}. Run: .\\.venv\\Scripts\\pip install rembg",
        )
        return 1

    if not input_path.is_file():
        _emit(success=False, error=f"Input not found: {input_path}")
        return 1

    out = output_path or input_path.with_name(f"{input_path.stem}_nobg.png")
    if out.suffix.lower() != ".png":
        out = out.with_suffix(".png")

    data = input_path.read_bytes()
    # Prefer general models; SAM (click) needs points — try auto models first
    models = ["u2net", "isnet-general-use", "u2net_human_seg"]
    last_err: Exception | None = None
    used = "u2net"
    for model in models:
        try:
            session = new_session(model)
            result = remove(data, session=session)
            out.write_bytes(result)
            used = model
            last_err = None
            break
        except Exception as ex:
            last_err = ex

    if last_err is not None:
        _emit(
            success=False,
            error=f"{last_err}. Tip: pip install rembg onnxruntime. SAM click-refine comes later.",
        )
        return 1

    _emit(
        success=True,
        available=True,
        tool=f"rembg/{used}",
        output_path=str(out.resolve()),
        bytes=out.stat().st_size,
    )
    return 0


def main(argv: list[str]) -> int:
    if len(argv) < 2 or argv[1] in {"-h", "--help", "help"}:
        _emit(
            success=False,
            error="Usage: remove_bg.py check | remove <input> [output.png]",
        )
        return 2

    cmd = argv[1].lower()
    if cmd == "check":
        return _check()

    if cmd == "remove":
        if len(argv) < 3:
            _emit(success=False, error="Usage: remove_bg.py remove <input> [output.png]")
            return 2
        inp = Path(argv[2])
        out = Path(argv[3]) if len(argv) > 3 else None
        return _remove(inp, out)

    _emit(success=False, error=f"Unknown command: {cmd}")
    return 2


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))

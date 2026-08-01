"""Acceptance tests for the Netie Space universal converter (convert_engine.py export-as).

Runs the whole from->to matrix against generated fixtures and asserts each output is real
(exists, non-trivial size, correct decoded format / frame count where it matters).

Run:  D:\\Netie Space\\.venv\\Scripts\\python.exe scripts\\convert-stress\\test_matrix.py
Exit code 0 = all green. Keep it green on any convert_engine.py change.

Notes:
- HEIC round-trips are skipped unless `pillow-heif` is installed (optional dep).
- Video/audio cases are skipped if tools/ffmpeg/ffmpeg.exe (or PATH ffmpeg) is absent.
"""
from __future__ import annotations

import json
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ENGINE = ROOT / "tools" / "convert_engine.py"
PY = ROOT / ".venv" / "Scripts" / "python.exe"
if not PY.exists():
    PY = Path(sys.executable)
FFMPEG = ROOT / "tools" / "ffmpeg" / ("ffmpeg.exe" if sys.platform == "win32" else "ffmpeg")

PASS, FAIL, SKIP = [], [], []


def run_export(src: Path, out: Path) -> dict:
    proc = subprocess.run(
        [str(PY), str(ENGINE), "export-as", str(src), str(out)],
        capture_output=True, text=True, timeout=600,
    )
    line = next((ln for ln in reversed(proc.stdout.splitlines()) if ln.strip().startswith("{")), "")
    try:
        return json.loads(line)
    except Exception:
        return {"success": False, "error": proc.stderr[:400] or proc.stdout[:400]}


def check(name: str, cond: bool, detail: str = "") -> None:
    (PASS if cond else FAIL).append(f"{name}  {detail}".rstrip())
    print(("PASS " if cond else "FAIL ") + name + (f"  — {detail}" if detail else ""))


def image_info(path: Path):
    from PIL import Image
    with Image.open(path) as im:
        return im.format, im.size, getattr(im, "n_frames", 1)


def build_fixtures(d: Path):
    from PIL import Image, ImageDraw
    png = d / "still.png"
    im = Image.new("RGBA", (240, 140), (0, 0, 0, 0))
    dr = ImageDraw.Draw(im)
    dr.rectangle([20, 20, 220, 120], fill=(94, 196, 184, 255))
    dr.text((40, 60), "NETIE", fill=(255, 255, 255, 255))
    im.save(png)

    anim = d / "anim.gif"
    frames = [Image.new("RGB", (90, 90), c) for c in [(220, 60, 60), (60, 220, 60), (60, 60, 220)]]
    frames[0].save(anim, save_all=True, append_images=frames[1:], duration=120, loop=0)

    import pymupdf
    pdf = d / "two.pdf"
    doc = pymupdf.open()
    for t in ["Page one", "Page two"]:
        p = doc.new_page(width=300, height=200)
        p.insert_text((40, 90), t, fontsize=22)
    doc.save(pdf)
    doc.close()

    clip = None
    if FFMPEG.exists():
        clip = d / "clip.mp4"
        r = subprocess.run(
            [str(FFMPEG), "-y", "-f", "lavfi", "-i", "testsrc=duration=1:size=320x240:rate=15",
             "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
             "-c:v", "libx264", "-c:a", "aac", "-shortest", str(clip)],
            capture_output=True, text=True, timeout=120,
        )
        if r.returncode != 0 or not clip.exists():
            clip = None
    return png, anim, pdf, clip


def main() -> int:
    with tempfile.TemporaryDirectory() as tmp:
        d = Path(tmp)
        png, anim, pdf, clip = build_fixtures(d)

        # 1. Image <-> image, type preserved / converted correctly.
        #    Write to a separate dir so png->png is a genuine copy, not src==dst.
        outdir = d / "out"
        outdir.mkdir(exist_ok=True)
        for tgt, want_fmt in [("jpg", "JPEG"), ("png", "PNG"), ("webp", "WEBP"),
                              ("gif", "GIF"), ("bmp", "BMP"), ("tiff", "TIFF"), ("ico", "ICO")]:
            out = outdir / f"still.{tgt}"
            res = run_export(png, out)
            ok = res.get("success") and out.exists() and out.stat().st_size > 50
            if ok:
                try:
                    fmt, _, _ = image_info(out)
                    ok = fmt == want_fmt
                except Exception as e:
                    ok, want_fmt = False, f"decode err {e}"
            check(f"image png->{tgt}", ok, res.get("error", ""))

        # 2. Animated GIF -> animated WEBP keeps > 1 frame (the old flatten bug)
        out = d / "anim.webp"
        res = run_export(anim, out)
        frames = image_info(out)[2] if out.exists() else 0
        check("animated gif->webp keeps frames", res.get("success") and frames > 1, f"frames={frames}")

        # 3. PDF -> image renders every page (page 1 = chosen path, page 2 = sibling)
        out = d / "two.png"
        res = run_export(pdf, out)
        sib = out.with_name("two_2.png")
        check("pdf->png all pages", res.get("success") and out.exists() and sib.exists(),
              f"p1={out.exists()} p2={sib.exists()}")

        # 4. Image -> PDF
        out = d / "still.pdf"
        res = run_export(png, out)
        check("image->pdf", res.get("success") and out.exists() and out.stat().st_size > 200)

        # 5. webp -> jpg (was a silent downgrade path; must be a real convert)
        webp = outdir / "still.webp"
        if webp.exists():
            out = d / "from_webp.jpg"
            res = run_export(webp, out)
            ok = res.get("success") and out.exists() and image_info(out)[0] == "JPEG"
            check("webp->jpg", ok, res.get("error", ""))

        # 6. Media (skipped without ffmpeg)
        if clip is None:
            SKIP.append("video/audio (ffmpeg missing)")
            print("SKIP video/audio — ffmpeg not found")
        else:
            for tgt in ["webm", "mkv", "gif", "mp3", "wav"]:
                out = d / f"clip.{tgt}"
                res = run_export(clip, out)
                check(f"video->{tgt}", res.get("success") and out.exists() and out.stat().st_size > 100,
                      res.get("error", ""))

        # 7. Unsupported target must fail cleanly, not throw
        res = run_export(png, d / "still.xyz")
        check("unsupported target guarded", res.get("success") is False and "error" in res)

    print(f"\n{len(PASS)} passed, {len(FAIL)} failed, {len(SKIP)} skipped")
    if SKIP:
        print("skipped: " + "; ".join(SKIP))
    if FAIL:
        print("FAILURES:\n  " + "\n  ".join(FAIL))
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

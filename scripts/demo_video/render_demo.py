#!/usr/bin/env python3
"""Render a short Netie Space product demo (16.5s, 1920x1080) to MP4."""

from __future__ import annotations

import math
import shutil
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[1]
W, H = 1920, 1080
FPS = 30
DURATION = 16.5
TOTAL = int(DURATION * FPS)

INK = (7, 9, 13, 255)
MINT = (110, 251, 203, 255)
PAPER = (255, 255, 255, 235)
PAPER_DIM = (255, 255, 255, 140)
PAPER_FAINT = (255, 255, 255, 30)

FONT_DIR = ROOT / "fonts"
SYSTEM_FONTS = Path("/usr/share/fonts/truetype")
OUT_DIR = Path("/opt/cursor/artifacts")
FRAMES = Path("/tmp/netie-demo-frames")

_FONT_CANDIDATES = {
    "Inter-Bold.ttf": [
        FONT_DIR / "Inter-Bold.ttf",
        SYSTEM_FONTS / "macos/Inter-Bold.ttf",
    ],
    "Inter-SemiBold.ttf": [
        FONT_DIR / "Inter-SemiBold.ttf",
        SYSTEM_FONTS / "macos/Inter-SemiBold.ttf",
    ],
    "Inter-Regular.ttf": [
        FONT_DIR / "Inter-Regular.ttf",
        SYSTEM_FONTS / "macos/Inter-Regular.ttf",
    ],
    "JetBrainsMono-Regular.ttf": [
        FONT_DIR / "JetBrainsMono-Regular.ttf",
        SYSTEM_FONTS / "jetbrains-mono/JetBrainsMono-Regular.ttf",
        SYSTEM_FONTS / "macos/JetBrainsMono-Regular.ttf",
    ],
    "JetBrainsMono-Bold.ttf": [
        FONT_DIR / "JetBrainsMono-Bold.ttf",
        SYSTEM_FONTS / "jetbrains-mono/JetBrainsMono-Bold.ttf",
        SYSTEM_FONTS / "macos/JetBrainsMono-Bold.ttf",
    ],
}


def font(name: str, size: int) -> ImageFont.FreeTypeFont:
    for path in _FONT_CANDIDATES.get(name, [FONT_DIR / name]):
        if path.exists():
            return ImageFont.truetype(str(path), size=size)
    raise FileNotFoundError(f"Font not found: {name}")


def ease_out_cubic(t: float) -> float:
    t = max(0.0, min(1.0, t))
    return 1 - (1 - t) ** 3


def ease_in_out(t: float) -> float:
    t = max(0.0, min(1.0, t))
    return 3 * t * t - 2 * t * t * t


def lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def scene_at(t: float) -> tuple[int, float]:
    """Return (scene_index, local_time_seconds)."""
    cuts = [0.2, 3.2, 7.8, 12.5, DURATION]
    for i in range(len(cuts) - 1):
        if t < cuts[i + 1]:
            return i, t - cuts[i]
    return 3, t - cuts[-2]


def make_bg() -> Image.Image:
    img = Image.new("RGBA", (W, H), INK)
    overlay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(overlay)
    # soft mint glow bottom
    for i in range(40):
        a = int(18 * (1 - i / 40))
        y0 = H - 80 - i * 14
        d.ellipse((-200, y0, W + 200, H + 400), fill=(110, 251, 203, a))
    for i in range(28):
        a = int(10 * (1 - i / 28))
        d.ellipse((W - 700 + i * 4, -200 + i * 3, W + 200, 420), fill=(56, 189, 248, a))
    img = Image.alpha_composite(img, overlay)
    return img


def rounded_rect(draw, xy, radius, fill, outline=None, width=1):
    draw.rounded_rectangle(xy, radius=radius, fill=fill, outline=outline, width=width)


def draw_text_center(draw, text, font_obj, y, fill, x_center=W // 2):
    bbox = draw.textbbox((0, 0), text, font=font_obj)
    tw = bbox[2] - bbox[0]
    draw.text((x_center - tw / 2, y), text, font=font_obj, fill=fill)


def load_brand() -> Image.Image:
    path = ROOT / "brand.png"
    if not path.exists():
        svg = REPO / "tests/fixtures/preview/Brand_mark.svg"
        try:
            import cairosvg

            cairosvg.svg2png(url=str(svg), write_to=str(path), output_width=512, output_height=512)
        except Exception as exc:  # noqa: BLE001
            raise SystemExit(f"Missing brand mark and could not rasterize SVG: {exc}") from exc
    return Image.open(path).convert("RGBA").resize((148, 148), Image.Resampling.LANCZOS)


def scene_brand(base: Image.Image, local: float, brand: Image.Image) -> Image.Image:
    img = base.copy()
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)

    mark_t = ease_out_cubic((local - 0.1) / 0.75)
    name_t = ease_out_cubic((local - 0.35) / 0.7)
    tag_t = ease_out_cubic((local - 0.55) / 0.7)

    if mark_t > 0:
        scale = lerp(0.7, 1.0, mark_t)
        size = int(148 * scale)
        mark = brand.resize((size, size), Image.Resampling.LANCZOS)
        # glow
        glow = Image.new("RGBA", (size + 80, size + 80), (0, 0, 0, 0))
        gd = ImageDraw.Draw(glow)
        for i in range(20):
            a = int(22 * mark_t * (1 - i / 20))
            gd.ellipse((i, i, size + 80 - i, size + 80 - i), fill=(110, 251, 203, a))
        gx = W // 2 - (size + 80) // 2
        gy = 300 - (size + 80 - size) // 2
        layer.alpha_composite(glow, (gx, gy - int((1 - mark_t) * 12)))
        mx = W // 2 - size // 2
        my = 300 - int((1 - mark_t) * 12)
        # round mask already in asset; paste with alpha
        mark_put = mark.copy()
        mark_put.putalpha(Image.eval(mark.split()[-1], lambda a: int(a * mark_t)))
        layer.alpha_composite(mark_put, (mx, my))

    if name_t > 0:
        f = font("Inter-Bold.ttf", 72)
        y = 490 + int((1 - name_t) * 16)
        a = int(235 * name_t)
        # NETIE
        t1 = "NETIE "
        t2 = "SPACE"
        b1 = d.textbbox((0, 0), t1, font=f)
        b2 = d.textbbox((0, 0), t2, font=f)
        total = (b1[2] - b1[0]) + (b2[2] - b2[0])
        x = W // 2 - total / 2
        d.text((x, y), t1, font=f, fill=(255, 255, 255, a))
        d.text((x + (b1[2] - b1[0]), y), t2, font=f, fill=(110, 251, 203, a))

    if tag_t > 0:
        f = font("JetBrainsMono-Regular.ttf", 16)
        draw_text_center(
            d,
            "PRIVATE · SECURE · EFFECTIVE",
            f,
            580 + int((1 - tag_t) * 12),
            (255, 255, 255, int(140 * tag_t)),
        )

    return Image.alpha_composite(img, layer)


def scene_desk(base: Image.Image, local: float) -> Image.Image:
    img = base.copy()
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)

    copy_t = ease_out_cubic(local / 0.6)
    if copy_t > 0:
        a = int(255 * copy_t)
        ey = font("JetBrainsMono-Regular.ttf", 18)
        h1 = font("Inter-Bold.ttf", 78)
        sub = font("Inter-Regular.ttf", 26)
        y0 = 200 + int((1 - copy_t) * 16)
        d.text((120, y0), "ONE KEY", font=ey, fill=(110, 251, 203, a))
        d.text((120, y0 + 48), "Press Space.", font=h1, fill=(255, 255, 255, a))
        d.text((120, y0 + 140), "See everything.", font=h1, fill=(255, 255, 255, a))
        d.text(
            (120, y0 + 260),
            "Instant Quick Look–style preview for every\nfile on Windows — no app launch, no wait.",
            font=sub,
            fill=(255, 255, 255, int(140 * copy_t)),
            spacing=10,
        )

    # desktop panel
    panel = Image.new("RGBA", (820, 680), (0, 0, 0, 0))
    pd = ImageDraw.Draw(panel)
    rounded_rect(pd, (0, 0, 819, 679), 22, fill=(18, 24, 32, 255), outline=(255, 255, 255, 28), width=1)
    for i, x in enumerate((24, 42, 60)):
        pd.ellipse((x, 22, x + 10, 32), fill=(255, 255, 255, 45))

    files = [
        ("PDF", (255, 138, 128), "Q3_Report.pdf"),
        ("DOC", (30, 136, 229), "Brief.docx"),
        ("PPT", (251, 140, 0), "Deck.pptx"),
        ("XLS", (67, 160, 71), "Budget.xlsx"),
        ("IMG", (142, 36, 170), "Portrait.png"),
        ("PY", (0, 137, 123), "demo_main.py"),
        ("MP4", (94, 53, 177), "Cut.mp4"),
        ("MD", (144, 164, 174), "README.md"),
    ]
    mono = font("JetBrainsMono-Bold.ttf", 13)
    small = font("Inter-Regular.ttf", 13)
    selected = local >= 0.9
    for i, (label, color, name) in enumerate(files):
        ft = ease_out_cubic((local - 0.15 - i * 0.07) / 0.45)
        if ft <= 0:
            continue
        col, row = i % 4, i // 4
        x = 48 + col * 190
        y = 70 + row * 250 + int((1 - ft) * 10)
        icon = Image.new("RGBA", (72, 88), (0, 0, 0, 0))
        idraw = ImageDraw.Draw(icon)
        rounded_rect(idraw, (0, 0, 71, 87), 10, fill=(*color, int(255 * ft)))
        bb = idraw.textbbox((0, 0), label, font=mono)
        idraw.text(((72 - (bb[2] - bb[0])) / 2, 34), label, font=mono, fill=(10, 10, 10, int(255 * ft)))
        if selected and i == 0:
            glow = Image.new("RGBA", (90, 106), (0, 0, 0, 0))
            gd = ImageDraw.Draw(glow)
            gd.rounded_rectangle((0, 0, 89, 105), radius=12, outline=(110, 251, 203, 200), width=3)
            panel.alpha_composite(glow, (x - 9, y - 9))
        panel.alpha_composite(icon, (x, y))
        nb = pd.textbbox((0, 0), name, font=small)
        pd.text((x + 36 - (nb[2] - nb[0]) / 2, y + 98), name, font=small, fill=(255, 255, 255, int(140 * ft)))

    # spacebar
    sb = Image.new("RGBA", (420, 80), (0, 0, 0, 0))
    sd = ImageDraw.Draw(sb)
    pressed = 1.5 <= local <= 1.68
    dy = 8 if pressed else 0
    rounded_rect(sd, (0, 12, 419, 75), 12, fill=(6, 8, 12, 255))
    fill = (28, 36, 48, 255)
    outline = (110, 251, 203, 160) if pressed else (255, 255, 255, 36)
    rounded_rect(sd, (0, dy, 419, 63 + dy), 12, fill=fill, outline=outline, width=2)
    label = "SPACE"
    jf = font("JetBrainsMono-Bold.ttf", 18)
    bb = sd.textbbox((0, 0), label, font=jf)
    color = MINT if pressed else (255, 255, 255, 140)
    sd.text(((420 - (bb[2] - bb[0])) / 2, 18 + dy), label, font=jf, fill=color)

    # compose desktop + spacebar onto layer
    layer.alpha_composite(panel, (1040, 160))
    layer.alpha_composite(sb, (120, 860))
    return Image.alpha_composite(img, layer)


def scene_preview(base: Image.Image, local: float) -> Image.Image:
    img = base.copy()
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))

    shell_t = ease_out_cubic((local - 0.1) / 0.7)
    if shell_t <= 0:
        return img

    pw, ph = 1180, 780
    scale = lerp(0.92, 1.0, shell_t)
    sw, sh = int(pw * scale), int(ph * scale)
    shell = Image.new("RGBA", (pw, ph), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shell)
    rounded_rect(sd, (0, 0, pw - 1, ph - 1), 18, fill=(14, 19, 26, 255), outline=(255, 255, 255, 28), width=1)

    # top bar
    sd.rectangle((0, 0, pw, 56), fill=(255, 255, 255, 8))
    sd.line((0, 56, pw, 56), fill=(255, 255, 255, 16))
    for i, x in enumerate((20, 38, 56)):
        sd.ellipse((x, 23, x + 10, 33), fill=(255, 255, 255, 45))
    mono = font("JetBrainsMono-Regular.ttf", 15)
    sd.text((80, 18), "Q3_Report.pdf", font=mono, fill=(255, 255, 255, 150))
    pill_f = font("JetBrainsMono-Bold.ttf", 12)
    sd.rounded_rectangle((pw - 120, 14, pw - 24, 42), radius=999, outline=(110, 251, 203, 120), width=1)
    sd.text((pw - 100, 20), "AI ON", font=pill_f, fill=MINT)

    # doc pane
    sd.rectangle((0, 56, int(pw * 0.58), ph - 64), fill=(247, 244, 238, 255))
    h2 = font("Inter-Bold.ttf", 34)
    body = font("Inter-Regular.ttf", 16)
    sd.text((40, 100), "Q3 Performance", font=h2, fill=(26, 26, 26, 255))
    paras = [
        "Revenue grew 18% quarter-over-quarter, led by\nenterprise seats and renewals across APAC.",
        "Highlights: faster onboarding, lower support load,\nand a cleaner preview-to-share loop for every file.",
        "Next: expand PDF workshop tools and CapCut-light\nvideo trim in the same Space preview.",
    ]
    y = 170
    for p in paras:
        sd.text((40, y), p, font=body, fill=(58, 58, 58, 255), spacing=6)
        y += 90
        if y == 260:
            sd.line((40, y - 20, 520, y - 20), fill=(0, 0, 0, 30))

    # ai pane
    ax0 = int(pw * 0.58)
    sd.rectangle((ax0, 56, pw, ph - 64), fill=(11, 16, 22, 255))
    sd.line((ax0, 56, ax0, ph - 64), fill=(255, 255, 255, 16))
    label = font("JetBrainsMono-Regular.ttf", 12)
    sd.text((ax0 + 24, 80), "ASK YOUR FILES", font=label, fill=MINT)

    cards = [
        (0.55, "Summary", "Q3 beat plan on revenue (+18%) with strong\nenterprise retention. Focus: onboarding & preview."),
        (0.85, "Continue chat", "What shipped in the PDF workshop this quarter?"),
    ]
    card_f = font("Inter-SemiBold.ttf", 15)
    card_p = font("Inter-Regular.ttf", 14)
    for delay, title, text in cards:
        ct = ease_out_cubic((local - delay) / 0.5)
        if ct <= 0:
            continue
        cy = 120 if delay < 0.7 else 300
        cy += int((1 - ct) * 10)
        card = Image.new("RGBA", (pw - ax0 - 48, 140), (0, 0, 0, 0))
        cd = ImageDraw.Draw(card)
        rounded_rect(cd, (0, 0, card.width - 1, 139), 14, fill=(255, 255, 255, int(14 * ct)), outline=(255, 255, 255, int(24 * ct)))
        cd.text((18, 18), title, font=card_f, fill=(255, 255, 255, int(230 * ct)))
        cd.text((18, 50), text, font=card_p, fill=(255, 255, 255, int(140 * ct)), spacing=5)
        if delay > 0.7 and int(local * 4) % 2 == 0:
            cd.rectangle((18 + cd.textbbox((0, 0), text.split("\n")[0], font=card_p)[2] + 6, 52, 26 + cd.textbbox((0, 0), text.split("\n")[0], font=card_p)[2], 68), fill=MINT)
        shell.alpha_composite(card, (ax0 + 24, cy))

    # bottom bar
    sd.rectangle((0, ph - 64, pw, ph), fill=(255, 255, 255, 8))
    sd.line((0, ph - 64, pw, ph - 64), fill=(255, 255, 255, 16))
    btns = [("↓ MD", True), ("↓ PDF", False), ("PDF ▾", False), ("Continue Chat", False)]
    bx = 20
    bf = font("JetBrainsMono-Bold.ttf", 13)
    for label, primary in btns:
        bb = sd.textbbox((0, 0), label, font=bf)
        bw = (bb[2] - bb[0]) + 28
        if primary:
            rounded_rect(sd, (bx, ph - 48, bx + bw, ph - 16), 10, fill=MINT)
            sd.text((bx + 14, ph - 42), label, font=bf, fill=(6, 36, 28, 255))
        else:
            rounded_rect(sd, (bx, ph - 48, bx + bw, ph - 16), 10, fill=(255, 255, 255, 14), outline=(255, 255, 255, 30))
            sd.text((bx + 14, ph - 42), label, font=bf, fill=(255, 255, 255, 200))
        bx += bw + 12

    # scale & place
    if scale != 1.0:
        shell = shell.resize((sw, sh), Image.Resampling.LANCZOS)
    shell.putalpha(Image.eval(shell.split()[-1], lambda a: int(a * shell_t)))
    x = (W - sw) // 2
    y = (H - sh) // 2
    # soft shadow
    shadow = Image.new("RGBA", (sw + 40, sh + 40), (0, 0, 0, 0))
    sdraw = ImageDraw.Draw(shadow)
    sdraw.rounded_rectangle((20, 24, sw + 20, sh + 28), radius=22, fill=(0, 0, 0, int(90 * shell_t)))
    shadow = shadow.filter(ImageFilter.GaussianBlur(18))
    layer.alpha_composite(shadow, (x - 20, y - 10))
    layer.alpha_composite(shell, (x, y))
    return Image.alpha_composite(img, layer)


def scene_finale(base: Image.Image, local: float) -> Image.Image:
    img = base.copy()
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)

    ey_t = ease_out_cubic(local / 0.5)
    h_t = ease_out_cubic((local - 0.15) / 0.75)
    s_t = ease_out_cubic((local - 0.4) / 0.7)
    c_t = ease_out_cubic((local - 0.7) / 0.6)

    if ey_t > 0:
        f = font("JetBrainsMono-Regular.ttf", 18)
        draw_text_center(d, "NETIE SPACE", f, 250 + int((1 - ey_t) * 10), (110, 251, 203, int(255 * ey_t)))

    if h_t > 0:
        f = font("Inter-Bold.ttf", 100)
        a = int(240 * h_t)
        y = 310 + int((1 - h_t) * 18)
        draw_text_center(d, "One key.", f, y, (255, 255, 255, a))
        draw_text_center(d, "Every file.", f, y + 118, (255, 255, 255, a))

    if s_t > 0:
        f = font("Inter-Regular.ttf", 28)
        draw_text_center(
            d,
            "Preview, ask, convert, and edit — private and local-first.",
            f,
            580 + int((1 - s_t) * 12),
            (255, 255, 255, int(140 * s_t)),
        )

    if c_t > 0:
        label = "SPACE"
        text = "Download for Windows"
        jf = font("JetBrainsMono-Bold.ttf", 14)
        bf = font("Inter-Bold.ttf", 20)
        tmp = ImageDraw.Draw(Image.new("RGBA", (1, 1)))
        lb = tmp.textbbox((0, 0), label, font=jf)
        tb = tmp.textbbox((0, 0), text, font=bf)
        cw = 28 + (lb[2] - lb[0]) + 24 + (tb[2] - tb[0]) + 28
        ch = 60
        scale = lerp(0.96, 1.0, c_t)
        cta = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
        cd = ImageDraw.Draw(cta)
        rounded_rect(cd, (0, 0, cw - 1, ch - 1), 14, fill=(110, 251, 203, int(255 * c_t)))
        kx0, ky0 = 12, 12
        kx1 = kx0 + (lb[2] - lb[0]) + 18
        rounded_rect(cd, (kx0, ky0, kx1, ch - 12), 8, fill=(6, 36, 28, int(255 * c_t)))
        cd.text((kx0 + 9, 20), label, font=jf, fill=(110, 251, 203, int(255 * c_t)))
        cd.text((kx1 + 12, 18), text, font=bf, fill=(6, 36, 28, int(255 * c_t)))
        if scale != 1:
            nw, nh = int(cw * scale), int(ch * scale)
            cta = cta.resize((nw, nh), Image.Resampling.LANCZOS)
            cw, ch = nw, nh
        glow = Image.new("RGBA", (cw + 120, ch + 120), (0, 0, 0, 0))
        gd = ImageDraw.Draw(glow)
        for i in range(24):
            a = int(16 * c_t * (1 - i / 24) ** 2)
            gd.ellipse((i, i + 10, cw + 120 - i, ch + 100 - i), fill=(110, 251, 203, a))
        glow = glow.filter(ImageFilter.GaussianBlur(12))
        layer.alpha_composite(glow, (W // 2 - (cw + 120) // 2, 660))
        layer.alpha_composite(cta, (W // 2 - cw // 2, 690))

    return Image.alpha_composite(img, layer)


def crossfade(a: Image.Image, b: Image.Image, t: float) -> Image.Image:
    t = max(0.0, min(1.0, t))
    return Image.blend(a.convert("RGBA"), b.convert("RGBA"), t)


def render_frame(t: float, brand: Image.Image, bg: Image.Image) -> Image.Image:
    scene, local = scene_at(t)
    # fade between scenes near cuts
    cuts = [3.2, 7.8, 12.5]
    fade = 0.45
    for cut in cuts:
        if cut - fade / 2 <= t <= cut + fade / 2:
            # blend previous and next
            prev_i = cuts.index(cut)
            t0 = local if scene == prev_i else (t - ([0.2, 3.2, 7.8, 12.5][prev_i]))
            # simpler: render both sides
            break
    else:
        if scene == 0:
            return scene_brand(bg, local, brand)
        if scene == 1:
            return scene_desk(bg, local)
        if scene == 2:
            return scene_preview(bg, local)
        return scene_finale(bg, local)

    # handle crossfade
    cut_idx = next(i for i, c in enumerate(cuts) if abs(t - c) <= fade / 2 or (c - fade / 2 <= t <= c + fade / 2))
    cut = cuts[cut_idx]
    u = (t - (cut - fade / 2)) / fade
    starts = [0.2, 3.2, 7.8, 12.5]
    prev_scene = cut_idx
    next_scene = cut_idx + 1
    prev_local = t - starts[prev_scene]
    next_local = t - starts[next_scene]
    renderers = [scene_brand, scene_desk, scene_preview, scene_finale]
    if prev_scene == 0:
        a = scene_brand(bg, prev_local, brand)
    else:
        a = renderers[prev_scene](bg, prev_local)
    if next_scene == 0:
        b = scene_brand(bg, next_local, brand)
    else:
        b = renderers[next_scene](bg, max(0.0, next_local))
    return crossfade(a, b, ease_in_out(u))


def encode(frames_dir: Path, out: Path) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)
    cmd = [
        "ffmpeg", "-y",
        "-framerate", str(FPS),
        "-i", str(frames_dir / "frame_%05d.png"),
        "-c:v", "libx264",
        "-pix_fmt", "yuv420p",
        "-crf", "18",
        "-movflags", "+faststart",
        "-r", str(FPS),
        str(out),
    ]
    subprocess.check_call(cmd)
    # also write a poster frame
    shutil.copy(frames_dir / "frame_00090.png", out.with_suffix(".png"))


def main() -> int:
    brand = load_brand()
    bg = make_bg()
    if FRAMES.exists():
        shutil.rmtree(FRAMES)
    FRAMES.mkdir(parents=True)

    print(f"Rendering {TOTAL} frames @ {FPS}fps…", flush=True)
    for i in range(TOTAL):
        t = i / FPS
        frame = render_frame(t, brand, bg).convert("RGB")
        frame.save(FRAMES / f"frame_{i:05d}.png", optimize=False)
        if i % 30 == 0:
            print(f"  {i}/{TOTAL} ({t:.1f}s)", flush=True)

    out = OUT_DIR / "netie-space-demo.mp4"
    print(f"Encoding {out}…", flush=True)
    encode(FRAMES, out)
    # lightweight copy into repo artifacts note path
    repo_out = ROOT / "netie-space-demo.mp4"
    # don't commit large mp4 by default — leave in /opt/cursor/artifacts
    size = out.stat().st_size
    print(f"Done: {out} ({size / 1e6:.2f} MB)", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())

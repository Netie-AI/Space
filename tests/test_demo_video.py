"""Contract checks for optional Pillar 4 demo-video marketing assets."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RENDER = ROOT / "scripts/demo_video/render_demo.py"
README = ROOT / "scripts/demo_video/README.md"
HTML = ROOT / "scripts/demo_video/index.html"
PARKING = ROOT / "PARKING_LOT.md"


def test_demo_renderer_is_portable():
    py = RENDER.read_text(encoding="utf-8")
    readme = README.read_text(encoding="utf-8")
    html = HTML.read_text(encoding="utf-8")
    assert "/opt/cursor/artifacts" not in py
    assert "NETIE_DEMO_OUT" in py
    assert "NETIE_DEMO_MAX_FRAMES" in py
    assert "scene_brand" in py
    assert "scene_desk" in py
    assert "scene_preview" in py
    assert "scene_finale" in py
    assert "crossfade" in py
    assert "1080p" in readme or "1920x1080" in py
    assert "Netie Space" in html
    assert "libx264" in py


def test_parking_lot_marketing_checked():
    text = PARKING.read_text(encoding="utf-8")
    assert "- [x] Marketing assets (Pillar 4 optional)" in text


if __name__ == "__main__":
    test_demo_renderer_is_portable()
    test_parking_lot_marketing_checked()
    print("PASS test_demo_video")

"""Contract checks for human-gate helper scripts + Manage billing."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CLIPS = ROOT / "scripts/make-pillar1-clips.sh"
PFX = ROOT / "scripts/make-test-pfx.sh"
PILLAR = ROOT / "docs/HUMAN_PILLAR1_TEST.md"
PARKED = ROOT / "docs/HUMAN_PARKED.md"
SETUP_XAML = ROOT / "src/NetieSpace/Views/ApiSetupWindow.xaml"


def test_scripts_and_docs():
    clips = CLIPS.read_text(encoding="utf-8")
    pfx = PFX.read_text(encoding="utf-8")
    pillar = PILLAR.read_text(encoding="utf-8")
    parked = PARKED.read_text(encoding="utf-8")
    xaml = SETUP_XAML.read_text(encoding="utf-8")
    assert "clip_a.mp4" in clips and "clip_b.mp4" in clips
    assert "1.6" in clips and "2.0" in clips
    assert "3.1" in clips
    assert "pass:test" in pfx
    assert "make-pillar1-clips.ps1" in pillar
    assert "3.1s" in pillar
    assert "make-test-pfx.ps1" in parked
    assert 'x:Name="ManageBillingButton"' in xaml
    assert (ROOT / "scripts/make-pillar1-clips.ps1").is_file()
    assert (ROOT / "scripts/make-test-pfx.ps1").is_file()


if __name__ == "__main__":
    test_scripts_and_docs()
    print("PASS test_human_gates")

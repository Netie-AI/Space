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
    check_sh = (ROOT / "scripts/check-pillar1-export.sh").read_text(encoding="utf-8")
    check_ps = (ROOT / "scripts/check-pillar1-export.ps1").read_text(encoding="utf-8")
    assert "2.8" in check_sh and "3.4" in check_sh
    assert "2.8" in check_ps and "3.4" in check_ps
    assert "check-pillar1-export.ps1" in pillar
    export = (ROOT / "src/NetieSpace/Services/BackgroundExportService.cs").read_text(encoding="utf-8")
    toast = (ROOT / "src/NetieSpace/Views/ToastHud.xaml.cs").read_text(encoding="utf-8")
    assert "TryFormatDuration" in export
    assert "wipe expect ~3.1s" in export
    assert "durationNote" in toast
    enc = (ROOT / "scripts/encode-pillar1-wipe.sh").read_text(encoding="utf-8")
    assert "wipeleft" in enc
    assert "offset=1.1" in enc
    assert "price_1U8ckCFV5wcFod2faXiUCsaV" in (ROOT / "docs/HUMAN_STRIPE_CHECKOUT.md").read_text(encoding="utf-8")


if __name__ == "__main__":
    test_scripts_and_docs()
    print("PASS test_human_gates")

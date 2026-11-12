"""Contract: the shipping build cannot carry an AGPL engine.

PowerShell does not run on the Linux gate, so this pins the source shape of
scripts/check-tool-engines.ps1 and its call from scripts/publish.ps1. When pwsh or powershell
is present (CI ubuntu has pwsh) the script's own -SelfTest is executed too; when neither is
present that part prints NOT RUN loudly and the Windows or CI job is the proof.
"""
from __future__ import annotations

import re
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GATE = ROOT / "scripts/check-tool-engines.ps1"
PUBLISH = ROOT / "scripts/publish.ps1"
NOTICES = ROOT / "THIRD-PARTY-NOTICES.txt"
DR = ROOT / "docs/decisions/DR-0002-engine-runtime-and-license-policy.md"

BANNED = {
    "PyMuPDF": ["pymupdf", "fitz"],
    "pymupdf4llm": ["pymupdf4llm"],
    "Ghostscript": ["gswin64c", "gswin32c", "gsdll64", "gsdll32"],
    "OnlyOffice": ["x2t", "onlyoffice", "documentserver"],
}


def gate() -> str:
    return GATE.read_text(encoding="utf-8")


def test_gate_script_is_ascii_and_5_1_compatible():
    text = gate()
    text.encode("ascii")
    assert text.lstrip().startswith("#Requires -Version 5.1")
    assert "param(" in text and "[switch]$SelfTest" in text and "[string]$Dist" in text
    # PowerShell 7-only syntax must not appear.
    for needle in ("??", "?.", " ? ", "&&", "||", "ForEach-Object -Parallel"):
        assert needle not in text, f"PS7-only syntax: {needle!r}"


def test_gate_names_every_banned_engine_and_its_binaries():
    text = gate().lower()
    for engine, names in BANNED.items():
        assert engine.lower() in text, f"engine {engine} not named"
        for name in names:
            assert name in text, f"{engine}: pattern for {name} missing"
    # Matching is by name, never by file contents of arbitrary files (decoy PDFs written by MuPDF).
    assert "get-content" in text and "metadata" in text
    assert "xrefstream-mupdf.pdf" in text


def test_gate_prints_per_hit_and_exits_non_zero_on_a_hit():
    text = gate()
    assert "BANNED $($hit.Engine) $($hit.Path)" in text
    assert re.search(r"if \(\$final\.Passed\) \{ exit 0 \}\s*exit 1", text)
    assert "OK $($result.Scanned) entries scanned, 0 banned" in text
    assert "exit (Invoke-SelfTest)" in text


def test_gate_has_no_override_switch():
    params = re.search(r"param\((.*?)\n\)", gate(), re.S).group(1)
    names = re.findall(r"\$(\w+)", params)
    assert sorted(names) == ["Dist", "SelfTest"], names


def test_gate_enforces_the_ffmpeg_notice_rule():
    text = gate()
    assert "THIRD-PARTY-NOTICES.txt" in text
    assert "'ffmpeg.exe'" in text and "FFmpeg" in text and "GPL" in text


def test_publish_runs_the_gate_after_venv_and_before_the_launcher():
    text = PUBLISH.read_text(encoding="utf-8")
    call = "check-tool-engines.ps1') -Dist $dist"
    assert call in text
    assert text.index("Copying .venv") < text.index(call) < text.index("# Launcher")
    after = text[text.index(call):]
    assert 'if ($LASTEXITCODE -ne 0) { throw "Shipping gate failed' in after.split("# Launcher")[0]
    # The notice is copied before the gate looks for it.
    assert text.index("THIRD-PARTY-NOTICES.txt") < text.index(call)


def test_notices_cover_the_shipped_engines():
    text = NOTICES.read_text(encoding="utf-8")
    text.encode("ascii")
    for needle in ["FFmpeg", "GPLv3", "PdfSharpCore", "PdfPig", "Apache-2.0", "MIT",
                   "SixLabors.ImageSharp 1.0.4", "SixLabors.Fonts", "DocumentFormat.OpenXml",
                   "whisper.cpp", "https://ffmpeg.org/"]:
        assert needle in text, needle
    csproj = (ROOT / "src/NetieSpace/NetieSpace.csproj").read_text(encoding="utf-8")
    for pkg in re.findall(r'PackageReference Include="([^"]+)"', csproj):
        assert pkg in text, f"package {pkg} is in the csproj but not in the notices"


def test_decision_record_is_proposed_and_names_its_confirmation():
    text = DR.read_text(encoding="utf-8")
    text.encode("ascii")
    assert re.search(r"^status: proposed$", text, re.M)
    assert "scripts/check-tool-engines.ps1 -SelfTest" in text
    assert "tests/test_tool_engines_gate.py" in text


def test_name_rules_catch_real_engine_layouts_and_spare_decoys():
    # Evaluate the script's own name patterns (simple anchored regexes, same in Python and PowerShell).
    rules = re.findall(r"Engine = '([^']+)';\s+Pattern = '([^']+)'(; DirOnly = \$true)?", gate())
    assert len(rules) >= 6

    def hit(name: str, is_dir: bool = False) -> bool:
        return any(re.search(pat, name, re.I) for _, pat, dir_only in rules if is_dir or not dir_only)

    for name in ("gs.exe", "gswin64c.exe", "gsdll64.dll", "ghostscript-0.7.dist-info", "libgs.so.10",
                 "pymupdf", "pymupdf4llm", "mupdf.dll", "_mupdf.pyd", "x2t.exe", "OnlyOffice", "DocumentServer"):
        assert hit(name), name
    assert hit("fitz", is_dir=True) and not hit("fitz")
    for name in ("xrefstream-mupdf.pdf", "NetieSpace.exe", "ffmpeg.exe", "gsutil.txt", "gsap.js", "Fixtures"):
        assert not hit(name), name


def test_selftest_runs_when_a_powershell_exists():
    shell = shutil.which("pwsh") or shutil.which("powershell")
    if shell is None:
        print("NOT RUN test_selftest_runs_when_a_powershell_exists: no pwsh or powershell on PATH; "
              "CI ubuntu (pwsh) or Windows must run scripts/check-tool-engines.ps1 -SelfTest")
        return
    proc = subprocess.run(
        [shell, "-NoProfile", "-File", str(GATE), "-SelfTest"],
        capture_output=True, text=True, timeout=300,
    )
    out = proc.stdout + proc.stderr
    assert proc.returncode == 0, out
    for engine in BANNED:
        assert engine in out, f"{engine} not named in -SelfTest output:\n{out}"


if __name__ == "__main__":
    for name, fn in list(globals().items()):
        if name.startswith("test_") and callable(fn):
            fn()
    print("PASS test_tool_engines_gate")

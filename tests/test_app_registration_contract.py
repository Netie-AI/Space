"""Contract for S50-E1-c: icons are reproducible, registration stays additive and HKCU-only.

The COM code, the scripts and real shortcuts need Windows; this pins what can be checked on Linux:
the icon generator is deterministic and the committed icons are its output, the Windows code never
touches UserChoice or HKLM, and the scripts line up with the exe switches.
"""
from __future__ import annotations

import importlib.util
import re
import struct
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ICONS = ROOT / "src/NetieSpace/Assets/apps"
GEN = ROOT / "scripts/make-app-icons.py"
SHELL = ROOT / "src/NetieSpace/Apps/Shell"


def load_generator():
    spec = importlib.util.spec_from_file_location("make_app_icons", GEN)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_generator_is_deterministic_and_matches_the_committed_icons():
    gen = load_generator()
    for app in ("pdf", "image", "video"):
        first = gen.build(app)
        assert first == gen.build(app), f"{app}: two runs differ"
        assert first == (ICONS / f"netie-{app}.ico").read_bytes(), f"{app}: committed icon is stale"


def test_icons_have_the_five_sizes_and_png_frames():
    for app in ("pdf", "image", "video"):
        data = (ICONS / f"netie-{app}.ico").read_bytes()
        reserved, kind, count = struct.unpack_from("<HHH", data, 0)
        assert (reserved, kind, count) == (0, 1, 5)
        sizes = []
        for i in range(count):
            w, h, _, _, planes, bpp, length, offset = struct.unpack_from("<BBBBHHII", data, 6 + 16 * i)
            sizes.append(w or 256)
            assert (w or 256) == (h or 256) and bpp == 32
            assert data[offset:offset + 8] == b"\x89PNG\r\n\x1a\n", "frame is not PNG-compressed"
            iw, ih = struct.unpack(">II", data[offset + 16:offset + 24])
            assert (iw, ih) == (w or 256, h or 256)
            assert offset + length <= len(data)
        assert sizes == [16, 24, 32, 48, 256]


def test_registration_code_never_touches_userchoice_or_hklm():
    for path in list((SHELL / "Windows").glob("*.cs")) + [SHELL / "AppRegistration.cs"]:
        text = re.sub(r"//.*", "", path.read_text(encoding="utf-8"))
        for banned in ("UserChoice", "HKEY_LOCAL_MACHINE", "LocalMachine", "Registry.", "RegistryKey"):
            assert banned not in text, f"{path.name} mentions {banned}"
        assert "pack://" not in text
    reg = (ROOT / "scripts/register-open-with.ps1").read_text(encoding="utf-8")
    assert "HKLM" not in reg and "HKEY_LOCAL_MACHINE" not in reg
    assert "UserChoice" not in reg.split("--register-apps")[-1]
    reader = (ROOT / "scripts/check-app-shortcuts.ps1").read_text(encoding="utf-8")
    for banned in ("UserChoice", "HKLM", "New-Item", "Set-ItemProperty", ".Save(", "Remove-Item"):
        assert banned not in reader, f"the reader must be read-only: {banned}"


def test_scripts_are_ascii_and_wired_to_the_exe_switches():
    for ps in ("register-open-with.ps1", "check-app-shortcuts.ps1", "check-tool-engines.ps1"):
        (ROOT / "scripts" / ps).read_text(encoding="utf-8").encode("ascii")
    reg = (ROOT / "scripts/register-open-with.ps1").read_text(encoding="utf-8")
    assert "--register-apps" in reg and "ExitCode -ne 0" in reg
    entry = (SHELL / "AppEntry.cs").read_text(encoding="utf-8")
    assert "AppRegistration.Parse(args)" in entry and "AppShortcuts.Run(" in entry
    # Registration is handled before the app / Space dispatch (no mutex, no window).
    assert entry.index("AppRegistration.Parse(args)") < entry.index("AppLaunch.Parse(args)")
    reader = (ROOT / "scripts/check-app-shortcuts.ps1").read_text(encoding="utf-8")
    for needle in ("Netie.Pdf", "Netie.Image", "Netie.Video", "--app pdf", "--app image", "--app video",
                   "9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", "exit 1"):
        assert needle in reader, needle


def test_window_applies_relaunch_properties_at_source_initialized():
    win = (SHELL / "AppWindow.xaml.cs").read_text(encoding="utf-8")
    assert "SourceInitialized" in win and "AppRelaunch.Apply(" in win
    relaunch = (SHELL / "Windows/AppRelaunch.cs").read_text(encoding="utf-8")
    for needle in ("SHGetPropertyStoreForWindow", "AppUserModelIdPid", "RelaunchCommandPid",
                   "RelaunchDisplayNameResourcePid", "RelaunchIconResourcePid", "Commit()"):
        assert needle in relaunch or needle in (SHELL / "Windows/AppShortcuts.cs").read_text(encoding="utf-8"), needle
    shortcuts = (SHELL / "Windows/AppShortcuts.cs").read_text(encoding="utf-8")
    # The AUMID is committed before the link is saved or it is lost.
    assert shortcuts.index("store.Commit()") < shortcuts.index("file.Save(")


if __name__ == "__main__":
    test_generator_is_deterministic_and_matches_the_committed_icons()
    test_icons_have_the_five_sizes_and_png_frames()
    test_registration_code_never_touches_userchoice_or_hklm()
    test_scripts_are_ascii_and_wired_to_the_exe_switches()
    test_window_applies_relaunch_properties_at_source_initialized()
    print("PASS test_app_registration_contract")

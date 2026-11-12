"""Contract: a Netie app process (--app pdf|image|video) never becomes Space.

The exe cannot run on the Linux gate, so this pins the source shape S50-E1-a depends on:
the dispatcher runs before the Space single-instance mutex, and the app path starts none
of the Space-only machinery (hook, tray, OpenVault sync, license gate, CLI preview).
"""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src/NetieSpace/App.xaml.cs"
SHELL = ROOT / "src/NetieSpace/Apps/Shell"

SPACE_ONLY = [
    "new Mutex(",
    "SyncOpenVaultOnStartupAsync",
    "GlobalKeyboardHook",
    "new TaskbarIcon",
    "StartShowTrayWatcher",
    "ShowStartupHudIfNeeded",
    "EnsureLicenseGate",
    "TryHandleCliPreview",
]


def method_body(src: str, signature: str) -> str:
    start = src.index(signature)
    brace = src.index("{", start)
    depth = 0
    for i in range(brace, len(src)):
        if src[i] == "{":
            depth += 1
        elif src[i] == "}":
            depth -= 1
            if depth == 0:
                return src[brace : i + 1]
    raise AssertionError(f"unbalanced braces after {signature}")


def test_dispatch_runs_before_the_space_mutex():
    src = APP.read_text(encoding="utf-8")
    body = method_body(src, "protected override void OnStartup(StartupEventArgs e)")
    statements = [ln.strip() for ln in body.splitlines()[1:] if ln.strip() and not ln.strip().startswith("//")]
    assert statements[0] == "if (Apps.Shell.AppEntry.TryHandle(e.Args, this))", statements[0]
    assert body.index("AppEntry.TryHandle") < body.index("new Mutex(true, MutexName")


def test_app_path_starts_no_space_machinery():
    src = APP.read_text(encoding="utf-8")
    body = method_body(src, "private void StartAppProcess(")
    for needle in SPACE_ONLY:
        assert needle not in body, f"app path must not reach {needle}"
    for needed in ["base.OnStartup(e)", "RegisterExceptionGuards(", "CrashRecovery.Register()",
                   "AppResourceBudget.Initialize()", "LoadSettings()", "Shutdown()"]:
        assert needed in body, f"app path lost {needed}"


def test_app_shell_uses_its_own_kernel_names_and_no_pack_uri():
    for cs in SHELL.glob("*.cs"):
        text = cs.read_text(encoding="utf-8")
        assert "NetieSpace.SingleInstance" not in text, cs.name
        assert "NetieSpace.ShowTray" not in text, cs.name
        assert "pack://" not in re.sub(r"//.*", "", text), cs.name
    entry = (SHELL / "AppEntry.cs").read_text(encoding="utf-8")
    assert "TOOL_CLI_UNAVAILABLE" in entry
    assert "ExitToolCliUnavailable = 3" in entry
    assert "ExitInvalidArgs = 2" in entry
    # AUMID is applied before the instance (and so before any window) exists.
    assert entry.index("ApplyAumid(identity)") < entry.index("new AppInstance(identity)")


def test_tfm_is_the_winrt_one_and_tests_never_reference_the_winexe():
    exe = (ROOT / "src/NetieSpace/NetieSpace.csproj").read_text(encoding="utf-8")
    tests = (ROOT / "tests/NetieSpace.Tests/NetieSpace.Tests.csproj").read_text(encoding="utf-8")
    assert "<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>" in exe
    assert "net8.0-windows10.0.19041.0" in tests
    assert "Apps\\Tools\\**\\*.cs" in tests
    assert "<ProjectReference" not in tests


if __name__ == "__main__":
    test_dispatch_runs_before_the_space_mutex()
    test_app_path_starts_no_space_machinery()
    test_app_shell_uses_its_own_kernel_names_and_no_pack_uri()
    test_tfm_is_the_winrt_one_and_tests_never_reference_the_winexe()
    print("PASS test_app_entry")

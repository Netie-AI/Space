"""Contract checks for the local ComfyUI client (no live :8188 required)."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CLIENT = ROOT / "src/NetieSpace/Services/ComfyUiClient.cs"
PREVIEW = ROOT / "src/NetieSpace/Views/PreviewWindow.xaml.cs"
PREVIEW_XAML = ROOT / "src/NetieSpace/Views/PreviewWindow.xaml"
TEST_CS = ROOT / "tests/NetieSpace.Tests/ComfyUiClientTests.cs"
TEST_CSPROJ = ROOT / "tests/NetieSpace.Tests/NetieSpace.Tests.csproj"
PARKING = ROOT / "PARKING_LOT.md"
HUMAN = ROOT / "docs/HUMAN_PARKED.md"
STATUS = ROOT / "STATUS.md"
CHANGELOG = ROOT / "CHANGELOG.md"


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_client_file_exists():
    assert CLIENT.is_file(), "src/NetieSpace/Services/ComfyUiClient.cs"
    text = _read(CLIENT)
    assert "namespace NetieSpace.Services" in text
    assert "class ComfyUiClient" in text
    assert "System.Windows" not in text
    assert "http://127.0.0.1:8188" in text
    assert "8188" in text
    assert "NETIE_COMFY_URL" in text
    assert "/system_stats" in text
    assert "/api/system_stats" in text
    assert "RoutePrefixes" in text
    assert '["/api", ""]' in text
    assert "ProbeTimeoutMs = 1200" in text
    assert "QueueTimeoutMs = 30_000" in text
    assert "/prompt" in text
    assert "Success=false" in text
    assert "/history/" in text
    assert "IsAvailableAsync" in text
    assert "QueuePromptAsync" in text
    assert "Success=false" in text or "Fail(" in text
    assert "rembg / Copy still work" in text
    assert "SidecarSuffix" in text
    assert ".comfy-input.json" in text
    assert "BuildLoadImageWorkflow" in text
    assert "WriteInputSidecar" in text
    assert "/upload/image" in text
    assert "UploadImageAsync" in text
    assert "ImageContentType" in text
    assert "image/png" in text
    assert "application/octet-stream" not in text
    assert "UploadTimeoutMs = 10_000" in text
    assert "/view" in text
    assert "SaveImage" in text
    assert "TryFirstOutputImage" in text
    assert "TryReadHistory" in text
    assert "status.completed with empty outputs is not done" in text
    assert "DownloadFirstOutputAsync" in text


def test_timeout_and_fail_closed():
    text = _read(CLIENT)
    assert "1200" in text
    assert "30_000" in text or "30s" in text
    assert "catch" in text
    assert "return false" in text
    assert "ComfyUiResult.Fail" in text
    assert "OfflineMessage" in text
    assert "Never throw" in text or "Never throws" in text


def test_menu_and_handler_names():
    xaml = _read(PREVIEW_XAML)
    cs = _read(PREVIEW)
    assert 'Content="Send to Comfy..."' in xaml
    assert "SendToComfyButton" in xaml
    assert "SendToComfyButton_Click" in xaml
    assert "SendToComfyButton_Click" in cs
    assert "ComfyUiClient" in cs
    assert "IsAvailableAsync" in cs
    assert "QueuePromptAsync" in cs
    assert "UploadImageAsync" in cs
    assert "DownloadFirstOutputAsync" in cs
    assert "WriteInputSidecar" in cs
    assert "Sidecar only when upload" in cs or "sidecar only" in cs.lower()
    assert cs.find("WriteInputSidecar") > cs.find("QueuePromptAsync")
    assert "ToastHud.Show" in cs
    assert "rembg / Copy still work" in cs
    assert "SendToComfyButton.Visibility = isImage" in cs


def test_xunit_compile_include():
    csproj = _read(TEST_CSPROJ)
    tests = _read(TEST_CS)
    assert "ComfyUiClient.cs" in csproj
    assert "<ProjectReference" not in csproj
    assert "new ComfyUiClient()" in tests
    assert "127.0.0.1:8188" in tests
    assert "Does not talk to a live" in tests
    assert "ImageContentType_uses_extension_not_octet_stream" in tests
    assert "UploadImage_missing_or_down_returns_null" in tests
    assert "TryFirstOutputImage_reads_filename" in tests
    assert "TryReadHistory_completed_without_images_keeps_polling" in tests
    assert "DownloadFirstOutput_down_server_returns_null" in tests
    assert "RoutePrefixes_official_api_then_legacy" in tests


def test_docs_code_ready():
    parking = _read(PARKING)
    human = _read(HUMAN)
    status = _read(STATUS)
    log = _read(CHANGELOG)
    assert "code-ready local client; live GPU still human" in parking
    assert "docs/HUMAN_PARKED.md" in parking
    assert "Send to Comfy..." in human
    assert "127.0.0.1:8188" in human
    assert "rembg / Copy" in human
    assert "code-ready local client" in status
    assert "Send to Comfy..." in log
    assert len(status.splitlines()) <= 60


if __name__ == "__main__":
    test_client_file_exists()
    test_timeout_and_fail_closed()
    test_menu_and_handler_names()
    test_xunit_compile_include()
    test_docs_code_ready()
    print("PASS test_comfyui_client")

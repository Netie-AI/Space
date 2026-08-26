using NetieSpace.Services;

namespace NetieSpace.Tests;

/// <summary>
/// Constructs the Windows AI probe. Does not talk to WinML.
/// </summary>
public class WindowsAiClientTests
{
    [Fact]
    public void Linux_or_missing_runtime_is_unavailable()
    {
        if (!OperatingSystem.IsWindows())
            Assert.False(WindowsAiClient.IsAvailable());
        Assert.Contains("rembg / Copy still work", WindowsAiClient.OfflineMessage());
    }

    [Fact]
    public void WriteInputSidecar_writes_json()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netie-winai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "x.png");
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        try
        {
            var sidecar = WindowsAiClient.WriteInputSidecar(path);
            Assert.True(File.Exists(sidecar));
            Assert.EndsWith(WindowsAiClient.SidecarSuffix, sidecar);
            Assert.Contains("image_path", File.ReadAllText(sidecar));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }
}

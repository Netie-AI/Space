using NetieSpace.Services;

namespace NetieSpace.Tests;

/// <summary>
/// Constructs the local Comfy client. Does not talk to a live :8188 server.
/// </summary>
public class ComfyUiClientTests
{
    [Fact]
    public void Default_url_is_localhost_8188()
    {
        var prev = Environment.GetEnvironmentVariable(ComfyUiClient.UrlEnvName);
        try
        {
            Environment.SetEnvironmentVariable(ComfyUiClient.UrlEnvName, null);
            var client = new ComfyUiClient();
            Assert.Equal("http://127.0.0.1:8188", client.BaseUrl);
            Assert.Equal(ComfyUiClient.DefaultBaseUrl, client.BaseUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ComfyUiClient.UrlEnvName, prev);
        }
    }

    [Fact]
    public async Task UploadImage_missing_or_down_returns_null()
    {
        var client = new ComfyUiClient("http://127.0.0.1:1");
        Assert.Null(await client.UploadImageAsync("/no/such/netie-comfy.png"));

        var dir = Path.Combine(Path.GetTempPath(), "netie-comfy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "x.png");
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        try
        {
            Assert.Null(await client.UploadImageAsync(path));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }
}

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

    [Fact]
    public void ImageContentType_uses_extension_not_octet_stream()
    {
        Assert.Equal("image/png", ComfyUiClient.ImageContentType("shot.png"));
        Assert.Equal("image/jpeg", ComfyUiClient.ImageContentType(@"C:\img.JPG"));
        Assert.Equal("image/webp", ComfyUiClient.ImageContentType("a.webp"));
        Assert.Equal("image/png", ComfyUiClient.ImageContentType("noext"));
    }

    [Fact]
    public void BuildLoadImageWorkflow_includes_save_image()
    {
        var json = ComfyUiClient.BuildLoadImageWorkflow("shot.png");
        Assert.Contains("LoadImage", json);
        Assert.Contains("SaveImage", json);
        Assert.Contains("shot.png", json);
    }

    [Fact]
    public void TryFirstOutputImage_reads_filename()
    {
        const string outputs = """
            {"9":{"images":[{"filename":"netie_00001.png","subfolder":"","type":"output"}]}}
            """;
        Assert.True(ComfyUiClient.TryFirstOutputImage(outputs, out var name, out var sub, out var type));
        Assert.Equal("netie_00001.png", name);
        Assert.Equal("", sub);
        Assert.Equal("output", type);
        Assert.False(ComfyUiClient.TryFirstOutputImage("{}", out _, out _, out _));
        Assert.False(ComfyUiClient.TryFirstOutputImage(null, out _, out _, out _));
    }

    [Fact]
    public async Task DownloadFirstOutput_down_server_returns_null()
    {
        var client = new ComfyUiClient("http://127.0.0.1:1");
        const string outputs = """
            {"9":{"images":[{"filename":"x.png","subfolder":"","type":"output"}]}}
            """;
        Assert.Null(await client.DownloadFirstOutputAsync("/tmp/x.png", outputs));
        Assert.Null(await client.DownloadFirstOutputAsync("/tmp/x.png", "{}"));
    }
}

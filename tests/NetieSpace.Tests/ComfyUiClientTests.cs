using NetieSpace.Services;

namespace NetieSpace.Tests;

/// <summary>
/// Constructs the local Comfy client. Does not talk to a live :8188 server.
/// </summary>
public class ComfyUiClientTests
{
    [Fact]
    public void RoutePrefixes_official_api_then_legacy()
    {
        Assert.Equal(new[] { "/api", "" }, ComfyUiClient.RoutePrefixes);
        Assert.Equal("/api/system_stats", ComfyUiClient.RoutePath("/api", "system_stats"));
        Assert.Equal("/system_stats", ComfyUiClient.RoutePath("", "system_stats"));
        Assert.Equal("/api/prompt", ComfyUiClient.RoutePath("/api", "prompt"));
        Assert.Equal("/prompt", ComfyUiClient.RoutePath("", "prompt"));
    }

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
    public void TryReadHistory_completed_without_images_keeps_polling()
    {
        const string pid = "abc-1";
        const string racing = """
            {"abc-1":{"status":{"completed":true,"status_str":"success"},"outputs":{}}}
            """;
        Assert.False(ComfyUiClient.TryReadHistory(racing, pid, out var empty, out var err));
        Assert.Null(empty);
        Assert.Null(err);

        const string ready = """
            {"abc-1":{"status":{"completed":true},"outputs":{"2":{"images":[{"filename":"netie_00001.png","type":"output"}]}}}}
            """;
        Assert.True(ComfyUiClient.TryReadHistory(ready, pid, out var outputs, out var noErr));
        Assert.Null(noErr);
        Assert.True(ComfyUiClient.TryFirstOutputImage(outputs, out var name, out _, out _));
        Assert.Equal("netie_00001.png", name);

        const string failed = """
            {"abc-1":{"status":{"status_str":"error","completed":true},"outputs":{}}}
            """;
        Assert.True(ComfyUiClient.TryReadHistory(failed, pid, out _, out var histError));
        Assert.Contains("failed", histError);
    }

    [Fact]
    public void PollRoutes_official_jobs_then_history()
    {
        Assert.Equal(new[] { "jobs", "history" }, ComfyUiClient.PollRoutes);
        Assert.Equal("/api/jobs/abc-1", ComfyUiClient.RoutePath("/api", "jobs/abc-1"));
        Assert.Equal("/history/abc-1", ComfyUiClient.RoutePath("", "history/abc-1"));
    }

    [Fact]
    public void TryReadHistory_does_not_read_job_payload()
    {
        const string pid = "abc-1";
        const string job = """
            {"id":"abc-1","status":"completed","outputs":{"2":{"images":[{"filename":"netie_00001.png","type":"output"}]}}}
            """;
        Assert.False(ComfyUiClient.TryReadHistory(job, pid, out var histOut, out var histErr));
        Assert.Null(histOut);
        Assert.Null(histErr);
        Assert.True(ComfyUiClient.TryReadJob(job, out var outputs, out var noErr));
        Assert.Null(noErr);
        Assert.True(ComfyUiClient.TryFirstOutputImage(outputs, out var name, out _, out _));
        Assert.Equal("netie_00001.png", name);
        Assert.True(ComfyUiClient.TryReadPoll(job, pid, out var pollOut, out _));
        Assert.True(ComfyUiClient.TryFirstOutputImage(pollOut, out var pollName, out _, out _));
        Assert.Equal("netie_00001.png", pollName);
    }

    [Fact]
    public void TryReadJob_completed_without_images_keeps_polling()
    {
        const string racing = """
            {"id":"abc-1","status":"completed","outputs":{}}
            """;
        Assert.False(ComfyUiClient.TryReadJob(racing, out var empty, out var err));
        Assert.Null(empty);
        Assert.Null(err);

        const string pending = """
            {"id":"abc-1","status":"in_progress"}
            """;
        Assert.False(ComfyUiClient.TryReadJob(pending, out _, out _));

        const string failed = """
            {"id":"abc-1","status":"error","outputs":{}}
            """;
        Assert.True(ComfyUiClient.TryReadJob(failed, out _, out var jobError));
        Assert.Contains("failed", jobError);
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

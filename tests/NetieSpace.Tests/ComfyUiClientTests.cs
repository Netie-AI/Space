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
}

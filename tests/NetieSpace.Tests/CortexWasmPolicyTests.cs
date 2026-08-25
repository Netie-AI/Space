using NetieSpace.Services;

namespace NetieSpace.Tests;

public class CortexWasmPolicyTests
{
    [Fact]
    public void Payload_denies_wasi_and_caps_fuel()
    {
        var payload = CortexWasmPolicy.ToPayload();
        Assert.Equal(50_000, payload["fuel"]);
        Assert.Equal(4 * 1024 * 1024, payload["memory_bytes"]);
        Assert.Equal(64, payload["memory_pages"]);
        Assert.Equal(2_000, payload["timeout_ms"]);
        Assert.Equal(true, payload["deny_imports"]);
        Assert.Equal(false, payload["wasi_fs"]);
        Assert.Equal(false, payload["wasi_net"]);
        Assert.Equal(!CortexWasmPolicy.IsDisabled, payload["enabled"]);
    }
}

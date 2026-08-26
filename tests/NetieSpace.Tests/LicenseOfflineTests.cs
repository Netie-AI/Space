using System.Text.Json;
using NetieSpace.Services.Licensing;

namespace NetieSpace.Tests;

/// <summary>
/// Offline gate after Activate this laptop. Does not call the Worker.
/// </summary>
public class LicenseOfflineTests
{
    [Fact]
    public void ReadString_prefers_entitlement_over_empty_fallback()
    {
        var json = JsonDocument.Parse("""{"license_key":"NS-TEST-KEY","license_id":"lic_1"}""");
        var map = json.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
        Assert.Equal("NS-TEST-KEY", LicenseOffline.ReadString(map, "license_key", ""));
        Assert.Equal("lic_1", LicenseOffline.ReadString(map, "license_id"));
        Assert.Equal("fallback", LicenseOffline.ReadString(map, "missing", "fallback"));
        Assert.Equal("", LicenseOffline.ReadString(null, "license_key"));
    }

    [Fact]
    public void IsLicensed_accepts_token_when_key_empty()
    {
        Assert.True(LicenseOffline.IsLicensed("abc", "ABC", "", "ent.tok"));
        Assert.True(LicenseOffline.IsLicensed("abc", "abc", "NS-KEY", ""));
        Assert.False(LicenseOffline.IsLicensed("abc", "abc", "", ""));
        Assert.False(LicenseOffline.IsLicensed("abc", "other", "NS-KEY", "ent.tok"));
        Assert.False(LicenseOffline.IsLicensed("", "", "NS-KEY", "ent.tok"));
    }
}

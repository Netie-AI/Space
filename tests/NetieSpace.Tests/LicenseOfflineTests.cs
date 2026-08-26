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

    [Fact]
    public void PreferPersistedBoard_skips_a_later_wmi_unknown()
    {
        Assert.Equal("REAL-UUID", LicenseOffline.PreferPersistedBoard("REAL-UUID"));
        Assert.Equal("REAL-UUID", LicenseOffline.PreferPersistedBoard(" REAL-UUID "));
        Assert.Null(LicenseOffline.PreferPersistedBoard(null));
        Assert.Null(LicenseOffline.PreferPersistedBoard(""));
        Assert.Null(LicenseOffline.PreferPersistedBoard("   "));
    }

    [Fact]
    public void StableVolumeToken_is_deterministic_not_gethashcode()
    {
        Assert.Equal("DESKTOP-1", LicenseOffline.StableVolumeToken("DESKTOP-1"));
        Assert.Equal("DESKTOP-1", LicenseOffline.StableVolumeToken(" DESKTOP-1 "));
        Assert.Equal("00000000", LicenseOffline.StableVolumeToken(""));
        Assert.Equal("00000000", LicenseOffline.StableVolumeToken(null));
        Assert.NotEqual(
            "DESKTOP-1".GetHashCode().ToString("X8"),
            LicenseOffline.StableVolumeToken("DESKTOP-1"));
    }
}

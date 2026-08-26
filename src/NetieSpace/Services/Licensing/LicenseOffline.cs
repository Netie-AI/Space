using System.Text.Json;

namespace NetieSpace.Services.Licensing;

/// <summary>
/// Offline license gate helpers. No WPF. Activate this laptop must persist
/// license_key from the Worker entitlement or the next SkipGate=false launch
/// treats a paid PC as unlicensed.
/// </summary>
public static class LicenseOffline
{
    public static string ReadString(
        IReadOnlyDictionary<string, JsonElement>? entitlement,
        string key,
        string? fallback = null)
    {
        if (entitlement is not null &&
            entitlement.TryGetValue(key, out var el) &&
            el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                return s;
        }
        return fallback ?? "";
    }

    /// <summary>
    /// Device hash must match. License key or entitlement token is enough
    /// (Activate this laptop used to save an empty key).
    /// </summary>
    public static bool IsLicensed(
        string storedHash,
        string currentHash,
        string? licenseKey,
        string? entitlementToken)
    {
        if (string.IsNullOrWhiteSpace(storedHash) ||
            !string.Equals(storedHash, currentHash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(licenseKey)
               || !string.IsNullOrWhiteSpace(entitlementToken);
    }
}

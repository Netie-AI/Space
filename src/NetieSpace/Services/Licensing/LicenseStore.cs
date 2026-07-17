using System.Security.Cryptography;
using System.Text.Json;
using NetieSpace.Helpers;

namespace NetieSpace.Services.Licensing;

/// <summary>
/// Sealed offline entitlement under %LOCALAPPDATA%\NetieSpace\license.
/// After activate, preview/edit works offline; AI and re-login still need network.
/// </summary>
public static class LicenseStore
{
    private static readonly string Dir = IoPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetieSpace", "license");

    private static readonly string EntitlementPath = IoPath.Combine(Dir, "entitlement.dat");
    private static readonly string SessionPath = IoPath.Combine(Dir, "session.dat");

    public static bool IsLicensedOffline()
    {
        try
        {
            var ent = LoadEntitlement();
            if (ent is null) return false;
            var device = DeviceFingerprint.ComputeHash(ent.PepperHint ?? "");
            return string.Equals(ent.DeviceHash, device, StringComparison.OrdinalIgnoreCase)
                   && !string.IsNullOrWhiteSpace(ent.LicenseKey);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"License check failed: {ex.Message}");
            return false;
        }
    }

    public static LicenseEntitlement? LoadEntitlement()
    {
        if (!IoFile.Exists(EntitlementPath))
            return null;
        var bytes = ProtectedData.Unprotect(IoFile.ReadAllBytes(EntitlementPath), null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<LicenseEntitlement>(bytes);
    }

    public static void SaveEntitlement(LicenseEntitlement entitlement)
    {
        IoDirectory.CreateDirectory(Dir);
        var json = JsonSerializer.SerializeToUtf8Bytes(entitlement);
        var sealedBytes = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
        IoFile.WriteAllBytes(EntitlementPath, sealedBytes);
    }

    public static void SaveSession(string token, string email)
    {
        IoDirectory.CreateDirectory(Dir);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new SessionBlob { Token = token, Email = email });
        IoFile.WriteAllBytes(SessionPath, ProtectedData.Protect(payload, null, DataProtectionScope.CurrentUser));
    }

    public static SessionBlob? LoadSession()
    {
        if (!IoFile.Exists(SessionPath)) return null;
        var bytes = ProtectedData.Unprotect(IoFile.ReadAllBytes(SessionPath), null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<SessionBlob>(bytes);
    }

    public static void Clear()
    {
        try
        {
            if (IoFile.Exists(EntitlementPath)) IoFile.Delete(EntitlementPath);
            if (IoFile.Exists(SessionPath)) IoFile.Delete(SessionPath);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"License clear failed: {ex.Message}");
        }
    }
}

public sealed class LicenseEntitlement
{
    public string Email { get; set; } = "";
    public string LicenseKey { get; set; } = "";
    public string LicenseId { get; set; } = "";
    public string DeviceHash { get; set; } = "";
    public string ActivatedAt { get; set; } = "";
    public string EntitlementToken { get; set; } = "";
    /// <summary>Local pepper used when hashing (not secret of server).</summary>
    public string? PepperHint { get; set; }
}

public sealed class SessionBlob
{
    public string Token { get; set; } = "";
    public string Email { get; set; } = "";
}

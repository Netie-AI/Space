using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>Local Fill &amp; Sign profile. DPAPI CurrentUser, never uploaded.</summary>
public static class FillProfileStore
{
    private static string Path => IoPath.Combine(EnvLoader.UserDataDirectory, "fill-profile.bin");

    public sealed record Profile(
        string FullName,
        string Email,
        string Phone,
        string Address,
        string DateOfBirth,
        string Nationality,
        string DateFormat,
        string DateToday);

    public static Profile Load()
    {
        try
        {
            if (!IoFile.Exists(Path))
                return Empty();
            var protectedBytes = IoFile.ReadAllBytes(Path);
            var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            return new Profile(
                Read(r, "FullName"),
                Read(r, "Email"),
                Read(r, "Phone"),
                Read(r, "Address"),
                Read(r, "DateOfBirth"),
                Read(r, "Nationality"),
                Read(r, "DateFormat"),
                DateTime.Now.ToString("dd/MM/yyyy"));
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"fill-profile load: {ex.Message}");
            return Empty();
        }
    }

    public static void Save(Profile profile)
    {
        var json = JsonSerializer.Serialize(new
        {
            FullName = profile.FullName.Trim(),
            Email = profile.Email.Trim(),
            Phone = profile.Phone.Trim(),
            Address = profile.Address.Trim(),
            DateOfBirth = profile.DateOfBirth.Trim(),
            Nationality = profile.Nationality.Trim(),
            DateFormat = string.IsNullOrWhiteSpace(profile.DateFormat) ? "dd-MMM-yyyy" : profile.DateFormat.Trim()
        });
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        IoDirectory.CreateDirectory(EnvLoader.UserDataDirectory);
        IoFile.WriteAllBytes(Path, bytes);
    }

    public static void Save(string fullName, string email, string phone, string address)
    {
        var cur = Load();
        Save(cur with
        {
            FullName = fullName,
            Email = email,
            Phone = phone,
            Address = address
        });
    }

    // No MergeHarvest here on purpose. A save-path merge wrote six PII fields with no
    // prompt; the harvest now goes through FillProfileHarvest.Propose and reaches Save
    // only after the user confirms it. Do not re-add an unconfirmed write.

    public static bool HasName => !string.IsNullOrWhiteSpace(Load().FullName);

    private static Profile Empty() =>
        new("", "", "", "", "", "", "dd-MMM-yyyy", DateTime.Now.ToString("dd/MM/yyyy"));

    private static string Read(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
}

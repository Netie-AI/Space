using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>
/// Local password vault (iCloud-like simplicity) — DPAPI encrypted at rest on this Windows user.
/// Sync between devices is a later Space bridge; this ships the vault core first.
/// </summary>
public sealed class PasswordVaultService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NetieSpace.Vault.v1");
    private readonly string _vaultPath;

    public PasswordVaultService()
    {
        var dir = IoPath.Combine(EnvLoader.UserDataDirectory, "vault");
        IoDirectory.CreateDirectory(dir);
        _vaultPath = IoPath.Combine(dir, "vault.bin");
    }

    public IReadOnlyList<VaultEntry> List()
    {
        var store = Load();
        return store.Entries
            .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Upsert(VaultEntry entry)
    {
        var store = Load();
        var existing = store.Entries.FirstOrDefault(e =>
            string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            entry.Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id;
            entry.CreatedUtc = DateTime.UtcNow;
            entry.UpdatedUtc = entry.CreatedUtc;
            store.Entries.Add(entry);
        }
        else
        {
            existing.Title = entry.Title;
            existing.Username = entry.Username;
            existing.Password = entry.Password;
            existing.Url = entry.Url;
            existing.Notes = entry.Notes;
            existing.UpdatedUtc = DateTime.UtcNow;
        }

        Save(store);
        UsageFootprintStore.Track("vault_upsert");
    }

    public bool Delete(string id)
    {
        var store = Load();
        var n = store.Entries.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
        if (n > 0)
        {
            Save(store);
            UsageFootprintStore.Track("vault_delete");
        }
        return n > 0;
    }

    private VaultStore Load()
    {
        try
        {
            if (!IoFile.Exists(_vaultPath))
                return new VaultStore();
            var protectedBytes = IoFile.ReadAllBytes(_vaultPath);
            var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plain);
            return JsonSerializer.Deserialize<VaultStore>(json) ?? new VaultStore();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Vault load failed: {ex.Message}");
            return new VaultStore();
        }
    }

    private void Save(VaultStore store)
    {
        var json = JsonSerializer.Serialize(store);
        var plain = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        IoFile.WriteAllBytes(_vaultPath, protectedBytes);
    }

    private sealed class VaultStore
    {
        public List<VaultEntry> Entries { get; set; } = [];
    }
}

public sealed class VaultEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Url { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

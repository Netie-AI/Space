using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>
/// File-backed Redis-like KV (GET/SET + TTL) under the proprietary brain folder.
/// Not a network Redis — local only, zero open ports.
/// </summary>
public static class LocalRedisStore
{
    private static readonly ConcurrentDictionary<string, (string Value, DateTime? Expires)> Mem = new();

    private static string Dir => LocalEmbeddingBrain.RedisDir;

    public static bool TryGet(string key, out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (Mem.TryGetValue(key, out var hit))
        {
            if (hit.Expires is null || hit.Expires > DateTime.UtcNow)
            {
                value = hit.Value;
                return true;
            }
            Mem.TryRemove(key, out _);
        }

        try
        {
            var path = PathFor(key);
            if (!IoFile.Exists(path))
                return false;
            var dto = JsonSerializer.Deserialize<RedisEntry>(IoFile.ReadAllText(path));
            if (dto is null)
                return false;
            if (dto.ExpiresUtc is not null && dto.ExpiresUtc < DateTime.UtcNow)
            {
                try { IoFile.Delete(path); } catch { /* ignore */ }
                return false;
            }

            value = dto.Value;
            Mem[key] = (dto.Value, dto.ExpiresUtc);
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    public static void Set(string key, string value, TimeSpan? ttl = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;
        DateTime? exp = ttl is null ? null : DateTime.UtcNow.Add(ttl.Value);
        Mem[key] = (value, exp);
        try
        {
            IoDirectory.CreateDirectory(Dir);
            var path = PathFor(key);
            var dto = new RedisEntry { Value = value, ExpiresUtc = exp };
            IoFile.WriteAllText(path, JsonSerializer.Serialize(dto));
            try
            {
                var fi = new FileInfo(path);
                fi.Attributes |= FileAttributes.Hidden;
            }
            catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"LocalRedis SET: {ex.Message}");
        }
    }

    public static void Delete(string key)
    {
        Mem.TryRemove(key, out _);
        try
        {
            var path = PathFor(key);
            if (IoFile.Exists(path))
                IoFile.Delete(path);
        }
        catch { /* ignore */ }
    }

    public static void ClearMemory() => Mem.Clear();

    private static string PathFor(string key)
    {
        var safe = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..40];
        return IoPath.Combine(Dir, safe + ".rdb.json");
    }

    private sealed class RedisEntry
    {
        public string Value { get; set; } = "";
        public DateTime? ExpiresUtc { get; set; }
    }
}

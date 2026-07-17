using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NetieSpace.Models;

namespace NetieSpace.Services;

/// <summary>
/// In-memory hash-keyed AI summary cache. Key = content fingerprint when text exists,
/// else path|size|mtime — avoids re-calling providers for the same document bytes.
/// </summary>
public sealed class SummaryCache
{
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly bool _enabled;

    public SummaryCache(bool enabled) => _enabled = enabled;

    public bool TryGet(PreviewContent content, out string? summary)
    {
        if (!_enabled)
        {
            summary = null;
            return false;
        }

        return _cache.TryGetValue(ContentFingerprint(content), out summary);
    }

    /// <summary>Legacy path-only lookup (file metadata fingerprint).</summary>
    public bool TryGet(string filePath, out string? summary)
    {
        if (!_enabled)
        {
            summary = null;
            return false;
        }

        return _cache.TryGetValue(FileMetaFingerprint(filePath), out summary);
    }

    public void Set(PreviewContent content, string summary)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(summary))
            return;

        _cache[ContentFingerprint(content)] = summary;
    }

    public void Set(string filePath, string summary)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(summary))
            return;

        _cache[FileMetaFingerprint(filePath)] = summary;
    }

    public void Clear() => _cache.Clear();

    /// <summary>
    /// Stable SHA-256 fingerprint of preview text (head+tail sample) or file metadata.
    /// Shared with chat/lookup memory so the same excerpt is not re-paid for.
    /// </summary>
    public static string ContentFingerprint(PreviewContent content)
    {
        var raw = content.RawText;
        if (!string.IsNullOrEmpty(raw))
        {
            var sample = SampleForHash(raw);
            var payload = $"{content.FilePath}|{raw.Length}|{sample}";
            return ToHexSha256(payload);
        }

        if (content.Kind == PreviewKind.Image && !string.IsNullOrEmpty(content.ImageBase64))
        {
            // Images: path + size + short base64 ends (full base64 would be huge)
            var b64 = content.ImageBase64;
            var tip = b64.Length <= 128 ? b64 : b64[..64] + b64[^64..];
            return ToHexSha256($"{content.FilePath}|img|{b64.Length}|{tip}");
        }

        return FileMetaFingerprint(content.FilePath);
    }

    public static string FileMetaFingerprint(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (info.Exists)
            {
                var payload = $"{filePath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
                return ToHexSha256(payload);
            }
        }
        catch
        {
            // fall through
        }

        return ToHexSha256(filePath);
    }

    private static string SampleForHash(string text)
    {
        if (text.Length <= 8_000)
            return text;

        return string.Concat(text.AsSpan(0, 4_000), text.AsSpan(text.Length - 4_000));
    }

    private static string ToHexSha256(string payload)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }
}

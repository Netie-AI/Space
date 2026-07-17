using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetieSpace.Helpers;
using NetieSpace.Models;

namespace NetieSpace.Services;

/// <summary>
/// Proprietary dual-brain local store: compact embeddings + file graph under a hidden folder.
/// Never uploaded. Requires explicit user consent. Redis-like keys for digests live alongside.
/// </summary>
public sealed class LocalEmbeddingBrain
{
    private static readonly object Gate = new();
    private static LocalEmbeddingBrain? _instance;

    public static LocalEmbeddingBrain Instance
    {
        get
        {
            lock (Gate)
                return _instance ??= new LocalEmbeddingBrain();
        }
    }

    /// <summary>Hidden proprietary root — not under public "cache" name.</summary>
    public static string BrainRoot =>
        IoPath.Combine(EnvLoader.UserDataDirectory, ".netie-brain");

    public static string EmbeddingsDir => IoPath.Combine(BrainRoot, "embeddings");
    public static string GraphPath => IoPath.Combine(BrainRoot, "file-graph.jsonl");
    public static string RedisDir => IoPath.Combine(BrainRoot, "redis");
    public static string ManifestPath => IoPath.Combine(BrainRoot, "MANIFEST.proprietary");

    public bool IsConsented => UserPrefs.EmbeddingConsentAccepted;

    public void EnsureLayout()
    {
        try
        {
            IoDirectory.CreateDirectory(EmbeddingsDir);
            IoDirectory.CreateDirectory(RedisDir);
            TryHideDirectory(BrainRoot);
            if (!IoFile.Exists(ManifestPath))
            {
                IoFile.WriteAllText(ManifestPath,
                    """
                    NETIE SPACE — PROPRIETARY LOCAL DUAL-BRAIN
                    This folder stores compressed local embeddings and a file-level knowledge graph
                    ONLY on this device, for your Space summaries and dual-brain recall.
                    It is not uploaded, not shared, and not part of any public API.
                    Remove this folder or revoke consent in tray → Local memory to wipe.
                    """);
                TryHideFile(ManifestPath);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Brain layout: {ex.Message}");
        }
    }

    /// <summary>
    /// Before AI summary: ensure local embedding exists; return a compact digest for the prompt.
    /// No-op when consent denied.
    /// </summary>
    public async Task<string?> PrepareBeforeSummaryAsync(PreviewContent content, CancellationToken ct = default)
    {
        if (!IsConsented)
            return null;

        EnsureLayout();
        var fp = SummaryCache.ContentFingerprint(content);
        var redisKey = $"digest:{fp}";
        if (LocalRedisStore.TryGet(redisKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
            return cached;

        var text = content.RawText ?? "";
        if (text.Length < 40 && content.Kind != PreviewKind.Image)
        {
            UpsertGraphNode(content, fp, dims: 0);
            return null;
        }

        var emb = await BuildEmbeddingAsync(fp, text, content, ct);
        var digest = BuildDigestLine(content, emb);
        LocalRedisStore.Set(redisKey, digest, TimeSpan.FromDays(30));
        UpsertGraphNode(content, fp, emb.Dimensions);
        LinkSiblingFolder(content, fp);
        return digest;
    }

    public void WipeAll()
    {
        lock (Gate)
        {
            try
            {
                LocalRedisStore.ClearMemory();
                if (IoDirectory.Exists(BrainRoot))
                    IoDirectory.Delete(BrainRoot, recursive: true);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Brain wipe: {ex.Message}");
            }
        }
    }

    private async Task<LocalEmbedding> BuildEmbeddingAsync(
        string fingerprint,
        string text,
        PreviewContent content,
        CancellationToken ct)
    {
        var path = EmbeddingPath(fingerprint);
        if (IoFile.Exists(path))
        {
            try
            {
                var json = await IoFile.ReadAllTextAsync(path, ct);
                var existing = JsonSerializer.Deserialize<LocalEmbedding>(json);
                if (existing is { Dimensions: > 0 })
                    return existing;
            }
            catch { /* rebuild */ }
        }

        var emb = RustEmbedBridge.TryEncode(text) ?? ManagedSparseEmbed.Encode(text, dims: 256);
        emb.Fingerprint = fingerprint;
        emb.FileName = content.FileName;
        emb.Kind = content.Kind.ToString();
        emb.CreatedUtc = DateTime.UtcNow;
        emb.PathHint = HashPathHint(content.FilePath);

        var tmp = path + ".tmp";
        await IoFile.WriteAllTextAsync(tmp, JsonSerializer.Serialize(emb), ct);
        IoFile.Copy(tmp, path, overwrite: true);
        try { IoFile.Delete(tmp); } catch { /* ignore */ }
        TryHideFile(path);
        return emb;
    }

    private static string BuildDigestLine(PreviewContent content, LocalEmbedding emb)
    {
        var top = emb.TopTokens is { Length: > 0 }
            ? string.Join(", ", emb.TopTokens.Take(12))
            : "(compact vector)";
        return
            $"[Local dual-brain · {emb.Dimensions}d · private]\n" +
            $"File: {content.FileName} ({content.Kind})\n" +
            $"Signal tokens: {top}\n" +
            $"Use this local memory; do not invent unseen sections.";
    }

    private void UpsertGraphNode(PreviewContent content, string fp, int dims)
    {
        try
        {
            var node = new
            {
                t = "node",
                id = fp,
                name = content.FileName,
                kind = content.Kind.ToString(),
                dims,
                pathHash = HashPathHint(content.FilePath),
                parentHash = HashPathHint(IoPath.GetDirectoryName(content.FilePath) ?? ""),
                utc = DateTime.UtcNow.ToString("o")
            };
            AppendGraph(JsonSerializer.Serialize(node));
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Graph node: {ex.Message}");
        }
    }

    private void LinkSiblingFolder(PreviewContent content, string fp)
    {
        try
        {
            var dir = IoPath.GetDirectoryName(content.FilePath);
            if (string.IsNullOrWhiteSpace(dir) || !IoDirectory.Exists(dir))
                return;

            foreach (var sibling in IoDirectory.EnumerateFileSystemEntries(dir).Take(40))
            {
                if (string.Equals(sibling, content.FilePath, StringComparison.OrdinalIgnoreCase))
                    continue;
                var edge = new
                {
                    t = "edge",
                    from = fp,
                    to = SummaryCache.FileMetaFingerprint(sibling),
                    rel = "folder_sibling",
                    utc = DateTime.UtcNow.ToString("o")
                };
                AppendGraph(JsonSerializer.Serialize(edge));
            }
        }
        catch { /* ignore */ }
    }

    private static void AppendGraph(string line)
    {
        lock (Gate)
        {
            EnsureParent(GraphPath);
            IoFile.AppendAllText(GraphPath, line + Environment.NewLine);
            TryHideFile(GraphPath);
        }
    }

    private static string EmbeddingPath(string fingerprint) =>
        IoPath.Combine(EmbeddingsDir, fingerprint[..Math.Min(32, fingerprint.Length)] + ".emb.json");

    private static string HashPathHint(string path)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return Convert.ToHexString(hash)[..16];
    }

    private static void EnsureParent(string file)
    {
        var d = IoPath.GetDirectoryName(file);
        if (!string.IsNullOrWhiteSpace(d))
            IoDirectory.CreateDirectory(d);
    }

    private static void TryHideDirectory(string dir)
    {
        try
        {
            var di = new DirectoryInfo(dir);
            if (di.Exists)
                di.Attributes |= FileAttributes.Hidden | FileAttributes.System;
        }
        catch { /* ignore */ }
    }

    private static void TryHideFile(string file)
    {
        try
        {
            var fi = new FileInfo(file);
            if (fi.Exists)
                fi.Attributes |= FileAttributes.Hidden;
        }
        catch { /* ignore */ }
    }
}

public sealed class LocalEmbedding
{
    public string Fingerprint { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Kind { get; set; } = "";
    public string PathHint { get; set; } = "";
    public int Dimensions { get; set; }
    public float[]? Values { get; set; }
    public string[]? TopTokens { get; set; }
    public DateTime CreatedUtc { get; set; }
}

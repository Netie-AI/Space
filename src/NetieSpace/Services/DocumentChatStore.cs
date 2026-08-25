using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetieSpace.Helpers;
using NetieSpace.Models;

namespace NetieSpace.Services;

/// <summary>
/// Per-document chat / notes attached to a file path fingerprint.
/// Survives reopen; one active thread per document (New chat archives the prior thread).
/// </summary>
public sealed class DocumentChatStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _root;

    public DocumentChatStore()
    {
        _root = IoPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetieSpace", "doc-chats");
        IoDirectory.CreateDirectory(_root);
    }

    /// <summary>
    /// Stable IDENTITY key for a document's chat/notes — the normalized absolute
    /// path only. Deliberately NOT <see cref="SummaryCache.FileMetaFingerprint"/>,
    /// which folds in file length + last-write time. Chat/notes belong to the
    /// document, so they must survive edits that change the bytes: Fill &amp; Sign
    /// autosave, compress (overwrite), Organize/Merge, cloud-sync touches, etc.
    /// Metadata keying silently orphaned the thread after the first edit.
    /// </summary>
    public static string KeyForPath(string filePath)
    {
        string full;
        try { full = IoPath.GetFullPath(filePath); }
        catch { full = filePath ?? string.Empty; }
        full = full.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant(); // Windows FS is case-insensitive
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("path:" + full));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Display title for AirGPT / UI — the file name, not the full path.</summary>
    public static string TitleForPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "Notes & chat";
        try
        {
            var name = IoPath.GetFileName(filePath.Trim());
            return string.IsNullOrWhiteSpace(name) ? "Notes & chat" : name;
        }
        catch
        {
            return "Notes & chat";
        }
    }

    public DocumentChatThread Load(string filePath)
    {
        var key = KeyForPath(filePath);
        var path = ThreadPath(key);
        if (!IoFile.Exists(path))
            TryMigrateLegacyThread(filePath, path); // recover pre-fix threads (unedited files)
        if (!IoFile.Exists(path))
            return new DocumentChatThread { DocumentKey = key, FilePath = filePath };

        try
        {
            var json = IoFile.ReadAllText(path);
            var thread = JsonSerializer.Deserialize<DocumentChatThread>(json, JsonOpts)
                         ?? new DocumentChatThread { DocumentKey = key, FilePath = filePath };
            thread.DocumentKey = key;
            thread.FilePath = filePath;
            return thread;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"doc-chat load: {ex.Message}");
            return new DocumentChatThread { DocumentKey = key, FilePath = filePath };
        }
    }

    public void Save(string filePath, DocumentChatThread thread)
    {
        if (string.IsNullOrWhiteSpace(filePath) || thread is null) return;
        thread.DocumentKey = KeyForPath(filePath);
        thread.FilePath = filePath;
        thread.UpdatedUtc = DateTime.UtcNow;
        try
        {
            var path = ThreadPath(thread.DocumentKey);
            var json = JsonSerializer.Serialize(thread, JsonOpts);
            IoFile.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"doc-chat save: {ex.Message}");
        }
    }

    public DocumentChatThread StartNew(string filePath, DocumentChatThread? current)
    {
        if (current is not null && current.Messages.Count > 0)
            Archive(filePath, current);

        return new DocumentChatThread
        {
            DocumentKey = KeyForPath(filePath),
            FilePath = filePath,
            Title = $"Chat {DateTime.Now:MMM d HH:mm}"
        };
    }

    public void Clear(string filePath)
    {
        try
        {
            var path = ThreadPath(KeyForPath(filePath));
            if (IoFile.Exists(path))
                IoFile.Delete(path);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"doc-chat clear: {ex.Message}");
        }
    }

    /// <summary>
    /// Threads saved before this fix were keyed by file metadata (size + mtime),
    /// so any edit orphaned them. If the file is still byte-identical to when the
    /// thread was last saved, its legacy fingerprint still resolves — copy it onto
    /// the stable path key once so the history reappears. Best-effort.
    /// </summary>
    private void TryMigrateLegacyThread(string filePath, string stablePath)
    {
        try
        {
            var legacy = ThreadPath(SummaryCache.FileMetaFingerprint(filePath));
            if (!string.Equals(legacy, stablePath, StringComparison.OrdinalIgnoreCase) &&
                IoFile.Exists(legacy))
                IoFile.Copy(legacy, stablePath);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"doc-chat legacy migrate: {ex.Message}");
        }
    }

    private void Archive(string filePath, DocumentChatThread thread)
    {
        try
        {
            var key = KeyForPath(filePath);
            var dir = IoPath.Combine(_root, key + "_archive");
            IoDirectory.CreateDirectory(dir);
            var name = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{SafeHash(thread.Title)}.json";
            var dest = IoPath.Combine(dir, name);
            IoFile.WriteAllText(dest, JsonSerializer.Serialize(thread, JsonOpts));
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"doc-chat archive: {ex.Message}");
        }
    }

    /// <summary>Prior threads created by New chat — newest first.</summary>
    public IReadOnlyList<ArchivedChatInfo> ListArchives(string filePath)
    {
        var list = new List<ArchivedChatInfo>();
        try
        {
            var dir = IoPath.Combine(_root, KeyForPath(filePath) + "_archive");
            if (!IoDirectory.Exists(dir))
                return list;

            foreach (var path in IoDirectory.GetFiles(dir, "*.json")
                         .OrderByDescending(IoFile.GetLastWriteTimeUtc))
            {
                try
                {
                    var thread = JsonSerializer.Deserialize<DocumentChatThread>(
                        IoFile.ReadAllText(path), JsonOpts);
                    list.Add(new ArchivedChatInfo
                    {
                        ArchivePath = path,
                        Title = string.IsNullOrWhiteSpace(thread?.Title)
                            ? IoPath.GetFileNameWithoutExtension(path)
                            : thread!.Title,
                        UpdatedUtc = thread?.UpdatedUtc ?? IoFile.GetLastWriteTimeUtc(path),
                        MessageCount = thread?.Messages?.Count ?? 0
                    });
                }
                catch
                {
                    list.Add(new ArchivedChatInfo
                    {
                        ArchivePath = path,
                        Title = IoPath.GetFileNameWithoutExtension(path),
                        UpdatedUtc = IoFile.GetLastWriteTimeUtc(path),
                        MessageCount = 0
                    });
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"doc-chat list archive: {ex.Message}");
        }

        return list;
    }

    public DocumentChatThread? LoadArchiveFile(string archivePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(archivePath) || !IoFile.Exists(archivePath))
                return null;
            return JsonSerializer.Deserialize<DocumentChatThread>(
                IoFile.ReadAllText(archivePath), JsonOpts);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"doc-chat load archive: {ex.Message}");
            return null;
        }
    }

    private string ThreadPath(string key) => IoPath.Combine(_root, key + ".json");

    private static string SafeHash(string? title)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(title ?? "chat"));
        return Convert.ToHexString(bytes)[..8];
    }
}

public sealed class ArchivedChatInfo
{
    public string ArchivePath { get; init; } = "";
    public string Title { get; init; } = "";
    public DateTime UpdatedUtc { get; init; }
    public int MessageCount { get; init; }
}

public sealed class DocumentChatThread
{
    public string DocumentKey { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Title { get; set; } = "Notes & chat";
    /// <summary>normal | cite | first-principles | mindmap — restored with the document.</summary>
    public string ChatMode { get; set; } = "normal";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<ChatMessage> Messages { get; set; } = [];
}

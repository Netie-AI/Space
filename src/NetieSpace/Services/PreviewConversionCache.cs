using System.Collections.Concurrent;

namespace NetieSpace.Services;

/// <summary>
/// Caches converted preview markdown/html by path + size + mtime.
/// </summary>
public sealed class PreviewConversionCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string filePath, long size, DateTime lastWriteUtc, out string markdown, out string? tool)
    {
        markdown = string.Empty;
        tool = null;

        if (!_entries.TryGetValue(filePath, out var entry))
            return false;

        if (entry.Size != size || entry.LastWriteUtc != lastWriteUtc)
            return false;

        markdown = entry.Markdown;
        tool = entry.Tool;
        return true;
    }

    public void Set(string filePath, long size, DateTime lastWriteUtc, string markdown, string? tool)
    {
        _entries[filePath] = new Entry(size, lastWriteUtc, markdown, tool);
    }

    private sealed record Entry(long Size, DateTime LastWriteUtc, string Markdown, string? Tool);
}

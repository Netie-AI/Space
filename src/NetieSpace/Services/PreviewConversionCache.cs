using System.Collections.Concurrent;

namespace NetieSpace.Services;

/// <summary>
/// Caches converted preview markdown/html by path + size + mtime.
/// Bounded — each entry can be multi-MB (PPTX/DOCX HTML embeds base64 slide images), so an
/// unbounded dictionary grew without limit across a session. Oldest-inserted entries are evicted.
/// </summary>
public sealed class PreviewConversionCache
{
    private const int MaxEntries = 8;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _order = new();
    private readonly LinkedList<string> _insertionOrder = new();

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

        lock (_order)
        {
            _insertionOrder.Remove(filePath);
            _insertionOrder.AddLast(filePath);
            while (_insertionOrder.Count > MaxEntries)
            {
                var oldest = _insertionOrder.First!.Value;
                _insertionOrder.RemoveFirst();
                _entries.TryRemove(oldest, out _);
            }
        }
    }

    private sealed record Entry(long Size, DateTime LastWriteUtc, string Markdown, string? Tool);
}

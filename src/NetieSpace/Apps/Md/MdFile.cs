using System.IO;
using System.Text;

namespace NetieSpace.Apps.Md;

/// <summary>Length and last write of a file: enough to tell whether it changed.</summary>
public readonly record struct MdFileStamp(long Length, long WriteTicks)
{
    public static MdFileStamp? Of(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new MdFileStamp(info.Length, info.LastWriteTimeUtc.Ticks) : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Result of reading a Markdown file. Exactly one of Text and Error is set.</summary>
public sealed record MdRead(string? Text, string? Error, bool Unstable, MdFileStamp? Stamp);

/// <summary>Reads a Markdown file for display. Never throws; a limit, a binary file or a file that keeps changing is reported.</summary>
public static class MdFile
{
    public const long MaxBytes = 1_500_000;
    public const int Attempts = 3;

    /// <param name="afterRead">Test hook: runs after the bytes are read and before the file is checked again.</param>
    public static MdRead Read(string path, Action<int>? afterRead = null)
    {
        try
        {
            string? text = null;
            MdFileStamp? stamp = null;
            for (var attempt = 1; attempt <= Attempts; attempt++)
            {
                var before = MdFileStamp.Of(path);
                if (before is null)
                    return new MdRead(null, "File not found", false, null);
                if (before.Value.Length > MaxBytes)
                    return new MdRead(null, "File too large to show", false, before);

                byte[] bytes;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var buffer = new byte[MaxBytes + 1];
                    var read = 0;
                    int n;
                    while (read < buffer.Length && (n = stream.Read(buffer, read, buffer.Length - read)) > 0)
                        read += n;
                    if (read > MaxBytes)
                        return new MdRead(null, "File too large to show", false, before);
                    bytes = buffer[..read];
                }

                afterRead?.Invoke(attempt);
                var after = MdFileStamp.Of(path);
                if (IsBinary(bytes))
                    return new MdRead(null, "Not a text file", false, after);
                text = Decode(bytes);
                stamp = after;
                if (after is not null && after.Value.Equals(before.Value))
                    return new MdRead(text, null, false, after);
            }
            // Still changing after every attempt: show what was read and let the caller read again.
            return new MdRead(text, null, true, stamp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new MdRead(null, "File could not be read", false, null);
        }
    }

    private static bool IsBinary(byte[] bytes)
    {
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            return false; // UTF-16 with a byte order mark
        var limit = Math.Min(bytes.Length, 8192);
        for (var i = 0; i < limit; i++)
            if (bytes[i] == 0)
                return true;
        return false;
    }

    private static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// Coalesces the burst of change events a save produces into one reload: the reload is due once
/// events stop for <c>quiet</c>, or after <c>maxWait</c> since the first event of a burst that never stops.
/// </summary>
public sealed class MdChangeGate
{
    private readonly TimeSpan _quiet;
    private readonly TimeSpan _maxWait;
    private DateTime? _first;
    private DateTime _last;

    public MdChangeGate(TimeSpan quiet, TimeSpan maxWait)
    {
        _quiet = quiet;
        _maxWait = maxWait;
    }

    public bool Pending => _first is not null;

    public void Note(DateTime nowUtc)
    {
        _first ??= nowUtc;
        _last = nowUtc;
    }

    /// <summary>True once, when the reload is due; the gate is then empty again.</summary>
    public bool TryTake(DateTime nowUtc)
    {
        if (_first is null)
            return false;
        if (nowUtc - _last < _quiet && nowUtc - _first.Value < _maxWait)
            return false;
        _first = null;
        return true;
    }

    public void Clear() => _first = null;
}

/// <summary>Zoom steps of the viewer.</summary>
public static class MdZoom
{
    public static readonly double[] Levels = { 0.5, 0.67, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0 };
    public const double Min = 0.5;
    public const double Max = 3.0;

    public static double Clamp(double zoom) => double.IsNaN(zoom) ? 1.0 : Math.Min(Max, Math.Max(Min, zoom));

    /// <summary>The next level above (direction &gt; 0) or below (direction &lt; 0) the current zoom.</summary>
    public static double Step(double current, int direction)
    {
        current = Clamp(current);
        if (direction > 0)
            return Levels.FirstOrDefault(l => l > current + 0.001, Max);
        if (direction < 0)
            return Levels.LastOrDefault(l => l < current - 0.001, Min);
        return current;
    }

    public static string Label(double zoom) => ((int)Math.Round(Clamp(zoom) * 100)).ToString() + "%";
}

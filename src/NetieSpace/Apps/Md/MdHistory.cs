namespace NetieSpace.Apps.Md;

/// <summary>One stop of the history: a file, the heading asked for, and where the page was scrolled when it was left.</summary>
public sealed record MdPlace(string Path, string Fragment, double ScrollY);

/// <summary>
/// Back and Forward for documents opened in one window. Pure: the view feeds it the file it shows and
/// the scroll position it leaves, and asks where to go. Opening a new file clears Forward.
/// </summary>
public sealed class MdHistory
{
    public const int Limit = 100;
    private readonly List<MdPlace> _back = new();
    private readonly List<MdPlace> _forward = new();

    public MdPlace? Current { get; private set; }
    public bool CanBack => _back.Count > 0;
    public bool CanForward => _forward.Count > 0;
    public int BackCount => _back.Count;
    public int ForwardCount => _forward.Count;

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>A new document was opened (a link, the Open dialog, a drop). The same file again only updates the heading.</summary>
    public void Visit(string path, string fragment)
    {
        if (Current is not null && Same(Current.Path, path))
        {
            Current = Current with { Fragment = fragment };
            return;
        }
        if (Current is not null)
        {
            _back.Add(Current);
            if (_back.Count > Limit)
                _back.RemoveAt(0);
        }
        _forward.Clear();
        Current = new MdPlace(path, fragment, 0);
    }

    /// <summary>Remembers how far the current page is scrolled.</summary>
    public void SaveScroll(double scrollY)
    {
        if (Current is not null && scrollY >= 0 && scrollY < 1e9)
            Current = Current with { ScrollY = scrollY };
    }

    /// <summary>Steps back; the place to show, or null at the start.</summary>
    public MdPlace? GoBack()
    {
        if (_back.Count == 0 || Current is null)
            return null;
        _forward.Add(Current);
        Current = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        return Current;
    }

    public MdPlace? GoForward()
    {
        if (_forward.Count == 0 || Current is null)
            return null;
        _back.Add(Current);
        Current = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        return Current;
    }
}

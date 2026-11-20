using System.IO;
namespace NetieSpace.Apps.Md;

public enum MdLinkKind
{
    /// <summary>"#heading": scroll inside the page.</summary>
    Anchor,
    /// <summary>http, https or mailto: handed to the default app.</summary>
    External,
    /// <summary>A relative link to another Markdown file: opened in the same window.</summary>
    Markdown,
    /// <summary>Anything else: not followed.</summary>
    Blocked,
}

public sealed record MdLink(MdLinkKind Kind, string Target, string Fragment)
{
    public static MdLink Blocked { get; } = new(MdLinkKind.Blocked, "", "");
}

/// <summary>What a click on a link may do. Relative links reach only other Markdown files.</summary>
public static class MdLinks
{
    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".mdown", ".mkd",
    };

    public static bool IsMarkdownFile(string path) => MarkdownExtensions.Contains(IoPath.GetExtension(path));

    /// <param name="href">The href as written (already HTML-decoded).</param>
    public static MdLink Classify(string href, string documentFolder)
    {
        href = href.Trim();
        if (href.Length == 0)
            return MdLink.Blocked;
        if (href[0] == '#')
            return new MdLink(MdLinkKind.Anchor, "", href[1..]);

        var scheme = MdPaths.SchemeOf(href);
        if (scheme is "http" or "https")
            return Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Host.Length > 0
                ? new MdLink(MdLinkKind.External, uri.AbsoluteUri, "")
                : MdLink.Blocked;
        if (scheme == "mailto")
            return Uri.TryCreate(href, UriKind.Absolute, out var mail)
                ? new MdLink(MdLinkKind.External, mail.AbsoluteUri, "")
                : MdLink.Blocked;
        if (scheme is not null || href.StartsWith("//", StringComparison.Ordinal) || href.StartsWith("\\\\", StringComparison.Ordinal))
            return MdLink.Blocked;

        var path = MdPaths.SplitTail(href, out var fragment);
        var segments = MdPaths.Segments(path, allowClimb: true);
        if (segments is null || !IsMarkdownFile(segments[^1]))
            return MdLink.Blocked;

        var full = IoPath.GetFullPath(IoPath.Combine(new[] { documentFolder }.Concat(segments).ToArray()));
        return new MdLink(MdLinkKind.Markdown, full, fragment);
    }
}

using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace NetieSpace.Apps.Md;

/// <summary>One heading of the document. Depth is the nesting in the outline (0 = top).</summary>
public sealed record MdHeading(string Id, string Title, int Level, int Depth);

public sealed record MdBody(string Html, IReadOnlyList<MdHeading> Outline);

/// <summary>
/// Post-processes the HTML an engine produced (raw HTML already disabled by the engine):
/// gives every heading a unique id and collects the outline, replaces pictures by the local-only
/// rule and decides what each link may do. Only tags the engine itself writes are matched, and every
/// attribute that is not needed is dropped.
/// </summary>
public static class MdHtml
{
    private const string Attrs = @"((?:[^>""]|""[^""]*"")*)";
    private static readonly Regex Heading = new(@"<h([1-6])" + Attrs + @">(.*?)</h\1>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex Image = new(@"<img\b" + Attrs + @">", RegexOptions.CultureInvariant);
    private static readonly Regex Anchor = new(@"<a\b" + Attrs + @">", RegexOptions.CultureInvariant);
    private static readonly Regex Attribute = new(@"([A-Za-z_:][-A-Za-z0-9_:.]*)\s*=\s*""([^""]*)""", RegexOptions.CultureInvariant);
    private static readonly Regex Tag = new(@"<[^>]*>", RegexOptions.CultureInvariant);
    private static readonly Regex SafeId = new(@"^[A-Za-z0-9:_-]{1,64}$", RegexOptions.CultureInvariant);
    private static readonly Regex Space = new(@"\s+", RegexOptions.CultureInvariant);

    public static MdBody Process(string engineHtml, string documentFolder, Func<string, bool> fileExists)
    {
        var withImages = Image.Replace(engineHtml, m => RewriteImage(m.Groups[1].Value, documentFolder, fileExists));
        var withLinks = Anchor.Replace(withImages, m => RewriteAnchor(m.Groups[1].Value, documentFolder));
        var outline = new List<MdHeading>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var levels = new List<int>();
        var nextSuffix = new Dictionary<string, int>(StringComparer.Ordinal);
        var html = Heading.Replace(withLinks, m =>
        {
            var level = m.Groups[1].Value[0] - '0';
            var inner = m.Groups[3].Value;
            var title = TitleOf(inner);
            var id = UniqueId(Slug(title), used, nextSuffix);
            while (levels.Count > 0 && levels[^1] >= level)
                levels.RemoveAt(levels.Count - 1);
            outline.Add(new MdHeading(id, title, level, levels.Count));
            levels.Add(level);
            return $"<h{level} id=\"{id}\">{inner}</h{level}>";
        });
        return new MdBody(html, outline);
    }

    public static string TitleOf(string headingInnerHtml)
    {
        var text = WebUtility.HtmlDecode(Tag.Replace(headingInnerHtml, ""));
        text = Space.Replace(text, " ").Trim();
        return text.Length == 0 ? "Untitled heading" : text;
    }

    /// <summary>Letters and digits lower-cased, runs of anything else become one "-". Never empty.</summary>
    public static string Slug(string title)
    {
        var sb = new StringBuilder();
        var dash = false;
        foreach (var c in title)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (dash && sb.Length > 0)
                    sb.Append('-');
                dash = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                dash = true;
            }
        }
        return sb.Length == 0 ? "section" : sb.ToString();
    }

    public static string UniqueId(string slug, HashSet<string> used) => UniqueId(slug, used, new Dictionary<string, int>());

    /// <summary>The next suffix per slug is remembered, so thousands of equal headings stay linear.</summary>
    public static string UniqueId(string slug, HashSet<string> used, Dictionary<string, int> next)
    {
        var id = slug;
        var n = next.GetValueOrDefault(slug, 1);
        while (!used.Add(id))
            id = slug + "-" + n++;
        next[slug] = n;
        return id;
    }

    private static string RewriteImage(string attrs, string documentFolder, Func<string, bool> fileExists)
    {
        var a = Parse(attrs);
        var alt = a.GetValueOrDefault("alt", "");
        var picture = MdPictures.Resolve(a.GetValueOrDefault("src", ""), documentFolder, fileExists);
        var altHtml = WebUtility.HtmlEncode(alt);
        switch (picture.Kind)
        {
            case MdPictureKind.Local:
            case MdPictureKind.Inline:
                var title = a.TryGetValue("title", out var t) ? $" title=\"{WebUtility.HtmlEncode(t)}\"" : "";
                return $"<img src=\"{WebUtility.HtmlEncode(picture.Src)}\" alt=\"{altHtml}\"{title}>";
            case MdPictureKind.Remote:
                return Placeholder("md-remote", altHtml, "remote picture");
            case MdPictureKind.Missing:
                return Placeholder("md-missing", altHtml, "picture not found");
            default:
                return Placeholder("md-blocked", altHtml, "picture blocked");
        }
    }

    private static string Placeholder(string cls, string altHtml, string label)
    {
        var name = altHtml.Length == 0 ? "" : $"<span class=\"md-alt\">{altHtml}</span> ";
        return $"<span class=\"md-pic {cls}\">{name}<span class=\"md-tag\">{label}</span></span>";
    }

    private static string RewriteAnchor(string attrs, string documentFolder)
    {
        var a = Parse(attrs);
        var title = a.TryGetValue("title", out var t) ? $" title=\"{WebUtility.HtmlEncode(t)}\"" : "";
        // Footnote back links need the id the engine put on the anchor; only a plain id survives.
        if (a.TryGetValue("id", out var anchorId) && SafeId.IsMatch(anchorId))
            title = $" id=\"{anchorId}\"" + title;
        var link = MdLinks.Classify(a.GetValueOrDefault("href", ""), documentFolder);
        switch (link.Kind)
        {
            case MdLinkKind.Anchor:
                return $"<a href=\"#{WebUtility.HtmlEncode(link.Fragment)}\"{title}>";
            case MdLinkKind.External:
                return $"<a href=\"{WebUtility.HtmlEncode(link.Target)}\" data-md=\"ext\"{title}>";
            case MdLinkKind.Markdown:
                var href = a.GetValueOrDefault("href", "");
                return $"<a href=\"{WebUtility.HtmlEncode(href)}\" data-md=\"md\"{title}>";
            default:
                return "<a class=\"md-blocked-link\" title=\"Link not followed\">";
        }
    }

    private static Dictionary<string, string> Parse(string attrs)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Attribute.Matches(attrs))
            map.TryAdd(m.Groups[1].Value, WebUtility.HtmlDecode(m.Groups[2].Value));
        return map;
    }
}

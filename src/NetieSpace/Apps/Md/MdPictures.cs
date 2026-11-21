using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace NetieSpace.Apps.Md;

public enum MdPictureKind
{
    /// <summary>A file inside the document folder, shown through the picture host.</summary>
    Local,
    /// <summary>A data:image URI carried by the document itself.</summary>
    Inline,
    /// <summary>http(s) or protocol-relative: never fetched.</summary>
    Remote,
    /// <summary>A local reference whose file does not exist.</summary>
    Missing,
    /// <summary>Absolute, outside the folder, unsafe name, wrong type or another scheme.</summary>
    Blocked,
}

public sealed record MdPicture(MdPictureKind Kind, string Src, string FullPath)
{
    public static MdPicture Of(MdPictureKind kind) => new(kind, "", "");
}

/// <summary>
/// The local-only rule for pictures. A picture is shown only when it is a file inside the folder of
/// the .md (subfolders included, no way out through "..", drive letters, UNC or links). Nothing remote
/// is ever requested: the page's policy and the host's request filter say the same.
/// </summary>
public static class MdPictures
{
    public const string Host = "netie-md-doc.local";
    public const string HostOrigin = "https://" + Host + "/";
    private const int MaxInlineChars = 8_000_000;

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".svg",
    };

    private static readonly Regex InlineImage = new(
        @"^data:image/(png|jpeg|jpg|gif|webp|bmp);base64,[A-Za-z0-9+/=\s]+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <param name="reference">The src as written (already HTML-decoded).</param>
    /// <param name="documentFolder">Folder of the .md file.</param>
    /// <param name="fileExists">Probe for a full path; injected so the rule is testable.</param>
    public static MdPicture Resolve(string reference, string documentFolder, Func<string, bool> fileExists)
    {
        reference = reference.Trim();
        if (reference.Length == 0)
            return MdPicture.Of(MdPictureKind.Blocked);

        var scheme = MdPaths.SchemeOf(reference);
        if (scheme is "http" or "https")
            return MdPicture.Of(MdPictureKind.Remote);
        if (reference.StartsWith("//", StringComparison.Ordinal) || reference.StartsWith("\\\\", StringComparison.Ordinal))
            return MdPicture.Of(MdPictureKind.Remote); // protocol-relative or UNC: never touched
        if (scheme == "data")
            return reference.Length <= MaxInlineChars && InlineImage.IsMatch(reference)
                ? new MdPicture(MdPictureKind.Inline, reference, "")
                : MdPicture.Of(MdPictureKind.Blocked);
        if (scheme is not null)
            return MdPicture.Of(MdPictureKind.Blocked); // file:, javascript:, drive letters, anything else

        var path = MdPaths.SplitTail(reference, out _);
        var segments = MdPaths.Segments(path, allowClimb: false);
        if (segments is null)
            return MdPicture.Of(MdPictureKind.Blocked);
        if (!Extensions.Contains(IoPath.GetExtension(segments[^1])))
            return MdPicture.Of(MdPictureKind.Blocked);

        var full = IoPath.Combine(new[] { documentFolder }.Concat(segments).ToArray());
        if (!fileExists(full))
            return new MdPicture(MdPictureKind.Missing, "", full);
        return new MdPicture(MdPictureKind.Local, HostOrigin + MdPaths.UrlPath(segments), full);
    }

    /// <summary>True when a file path has no link or junction between the folder and the file.
    /// Used by the host's probe so a link inside the folder cannot lead out of it.</summary>
    public static bool HasNoLinkBelow(string documentFolder, string fullPath)
    {
        try
        {
            var root = IoPath.GetFullPath(documentFolder).TrimEnd(IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar);
            var current = IoPath.GetFullPath(fullPath);
            if (!current.StartsWith(root + IoPath.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;
            while (current.Length > root.Length)
            {
                var info = new FileInfo(current);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null)
                    return false;
                current = IoPath.GetDirectoryName(current) ?? root;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string Encode(string value) => WebUtility.HtmlEncode(value);
}

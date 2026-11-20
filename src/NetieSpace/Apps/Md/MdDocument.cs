using System.IO;
namespace NetieSpace.Apps.Md;

/// <summary>A document ready to show: the full page, its outline and where its pictures live.</summary>
public sealed record MdDocument(
    string Path, string Folder, string Title, string Page, IReadOnlyList<MdHeading> Outline,
    string? Error, bool Unstable, MdFileStamp? Stamp)
{
    /// <summary>NavigateToString takes about 2 MB; stay well under it.</summary>
    public const int MaxPageChars = 1_800_000;

    /// <summary>Reads, renders and wraps the file. Never throws: a failure becomes a notice page with Error set.</summary>
    public static MdDocument Load(string path, IMdEngine engine, string? nonce = null, Func<string, bool>? fileExists = null)
    {
        nonce ??= MdShell.NewNonce();
        var full = IoPath.GetFullPath(path);
        var folder = IoPath.GetDirectoryName(full) ?? "";
        var title = IoPath.GetFileName(full);
        var read = MdFile.Read(full);
        if (read.Text is null)
        {
            var why = read.Error ?? "File could not be read";
            return new MdDocument(full, folder, title, MdShell.Notice(title, why, nonce), Array.Empty<MdHeading>(), why, false, read.Stamp);
        }

        try
        {
            var probe = fileExists ?? (p => IoFile.Exists(p) && MdPictures.HasNoLinkBelow(folder, p));
            var body = MdHtml.Process(engine.ToHtml(read.Text), folder, probe);
            var page = MdShell.Page(title, body.Html, nonce);
            if (page.Length > MaxPageChars)
            {
                const string big = "Document too large to show";
                return new MdDocument(full, folder, title, MdShell.Notice(title, big, nonce), Array.Empty<MdHeading>(), big, false, read.Stamp);
            }
            return new MdDocument(full, folder, title, page, body.Outline, null, read.Unstable, read.Stamp);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            const string failed = "Document could not be rendered";
            return new MdDocument(full, folder, title, MdShell.Notice(title, failed + ": " + ex.GetType().Name, nonce),
                Array.Empty<MdHeading>(), failed, false, read.Stamp);
        }
    }
}

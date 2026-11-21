namespace NetieSpace.Apps.Md;

/// <summary>
/// <c>--app md [--open FILE] [--snapshot OUT.png] [--pdf OUT.pdf]</c>: what the headless path of Netie MD reads.
/// Pure argv reading; SnapshotArgs belongs to the tool apps and is not reused.
/// </summary>
public sealed record MdArgs(bool Headless, string? OpenPath, string? PngPath, string? PdfPath, string? Error)
{
    public const int ExitInvalid = 2;

    public static MdArgs Parse(string[] args)
    {
        var snap = IndexOf(args, "--snapshot");
        var pdf = IndexOf(args, "--pdf");
        var open = IndexOf(args, "--open");
        var openPath = open >= 0 ? Value(args, open) : null;
        if (snap < 0 && pdf < 0)
            return new MdArgs(false, openPath, null, null, null);

        var png = snap >= 0 ? Value(args, snap) : null;
        var pdfPath = pdf >= 0 ? Value(args, pdf) : null;
        if (snap >= 0 && png is null)
            return new MdArgs(true, openPath, null, null, "--snapshot needs an output path");
        if (pdf >= 0 && pdfPath is null)
            return new MdArgs(true, openPath, null, null, "--pdf needs an output path");
        if (openPath is null)
            return new MdArgs(true, null, png, pdfPath, "--snapshot and --pdf need --open <file.md>");
        return new MdArgs(true, openPath, png, pdfPath, null);
    }

    private static string? Value(string[] args, int flagIndex)
    {
        var i = flagIndex + 1;
        if (i >= args.Length)
            return null;
        var v = args[i].Trim().Trim('"');
        return v.Length == 0 || v.StartsWith("--", StringComparison.Ordinal) ? null : v;
    }

    private static int IndexOf(string[] args, string flag) =>
        Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
}

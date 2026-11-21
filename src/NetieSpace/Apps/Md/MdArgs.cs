namespace NetieSpace.Apps.Md;

/// <summary>
/// <c>--app md [--open FILE] [--snapshot OUT.png] [--pdf OUT.pdf] [--theme light|dark|system] [--steps FILE]</c>:
/// what the headless path of Netie MD reads. <c>--steps</c> runs a script of UI actions (see MdDriver) and
/// prints one line per step. Pure argv reading; SnapshotArgs belongs to the tool apps and is not reused.
/// </summary>
public sealed record MdArgs(
    bool Headless, string? OpenPath, string? PngPath, string? PdfPath, string? Error,
    MdColorMode Theme = MdColorMode.System, string? StepsPath = null)
{
    public const int ExitInvalid = 2;

    public static MdArgs Parse(string[] args)
    {
        var snap = IndexOf(args, "--snapshot");
        var pdf = IndexOf(args, "--pdf");
        var open = IndexOf(args, "--open");
        var steps = IndexOf(args, "--steps");
        var themeAt = IndexOf(args, "--theme");
        var openPath = open >= 0 ? Value(args, open) : null;

        var theme = MdColorMode.System;
        if (themeAt >= 0 && !MdColorModes.TryParse(Value(args, themeAt), out theme))
            return new MdArgs(true, openPath, null, null, "--theme needs light, dark or system", MdColorMode.System);

        if (snap < 0 && pdf < 0 && steps < 0)
            return new MdArgs(false, openPath, null, null, null, theme);

        var png = snap >= 0 ? Value(args, snap) : null;
        var pdfPath = pdf >= 0 ? Value(args, pdf) : null;
        var stepsPath = steps >= 0 ? Value(args, steps) : null;
        if (snap >= 0 && png is null)
            return new MdArgs(true, openPath, null, null, "--snapshot needs an output path", theme);
        if (pdf >= 0 && pdfPath is null)
            return new MdArgs(true, openPath, null, null, "--pdf needs an output path", theme);
        if (steps >= 0 && stepsPath is null)
            return new MdArgs(true, openPath, null, null, "--steps needs a script file", theme);
        if (openPath is null)
            return new MdArgs(true, null, png, pdfPath, "--snapshot, --pdf and --steps need --open <file.md>", theme, stepsPath);
        return new MdArgs(true, openPath, png, pdfPath, null, theme, stepsPath);
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

using NetieSpace.Apps.Tools;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// <c>--snapshot &lt;png&gt; [--view &lt;tool-id&gt;] [--files a.pdf|b.pdf] [--state text]</c>. Pure argv reading: the WPF side only renders.
/// <c>--files</c> (separated by '|', which no Windows file name holds) puts files on the tool page's panel first, and
/// <c>--state</c> is the panel's own text for what a person's clicks would have done (select, rotate, delete ...).
/// </summary>
public sealed record SnapshotArgs(bool Requested, string? OutPath, string? ToolId, string? Error,
    IReadOnlyList<string>? Files = null, string? State = null)
{
    public const int ExitInvalid = 2;

    public static readonly SnapshotArgs None = new(false, null, null, null);

    public static SnapshotArgs Parse(string[] args)
    {
        var snap = IndexOf(args, "--snapshot");
        var view = IndexOf(args, "--view");
        if (snap < 0 && view < 0)
            return None;
        if (snap < 0)
            return new SnapshotArgs(false, null, null, "--view needs --snapshot <png>");

        var outPath = Value(args, snap);
        if (outPath is null)
            return new SnapshotArgs(true, null, null, "--snapshot needs an output path");

        string? toolId = null;
        if (view >= 0)
        {
            toolId = Value(args, view);
            if (toolId is null)
                return new SnapshotArgs(true, outPath, null, "--view needs a tool id");
        }
        IReadOnlyList<string>? files = null;
        var filesAt = IndexOf(args, "--files");
        if (filesAt >= 0)
        {
            var raw = Value(args, filesAt);
            if (raw is null)
                return new SnapshotArgs(true, outPath, toolId, "--files needs file paths separated by |");
            if (toolId is null)
                return new SnapshotArgs(true, outPath, toolId, "--files needs --view <tool-id>");
            files = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        string? state = null;
        var stateAt = IndexOf(args, "--state");
        if (stateAt >= 0)
        {
            state = Value(args, stateAt);
            if (state is null)
                return new SnapshotArgs(true, outPath, toolId, "--state needs a text");
        }
        return new SnapshotArgs(true, outPath, toolId, null, files, state);
    }

    /// <summary>Null when the id is empty or belongs to this app; otherwise the text naming the id.</summary>
    public static string? CheckView(string? toolId, AppIdentity app, IEnumerable<ToolEntry> entries)
    {
        if (toolId is null)
            return null;
        var toolApp = HomeModel.ToolAppOf(app);
        return entries.Any(e => e.App == toolApp && string.Equals(e.Id, toolId, StringComparison.Ordinal))
            ? null
            : $"Unknown tool for {app.DisplayName}: {toolId}";
    }

    private static string? Value(string[] args, int flagIndex)
    {
        var i = flagIndex + 1;
        if (i >= args.Length) return null;
        var v = args[i].Trim().Trim('"');
        return v.Length == 0 || v.StartsWith("--", StringComparison.Ordinal) ? null : v;
    }

    private static int IndexOf(string[] args, string flag) =>
        Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
}

using System.Windows.Controls;
using NetieSpace.Apps.Tools;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell.Panels.Video;

/// <summary>
/// The whole surface ToolPageView touches (W6-4). The page gives the hook its two host controls and the file list; the hook
/// decides whether this tool gets the filmstrip, builds it, and hands back the session that runs it. Pages of other tools,
/// and a page with no file yet, are left exactly as they were.
/// </summary>
internal static class VideoFlowHook
{
    /// <summary>False only for <c>--snapshot</c>: nothing may open a media pipeline in a window nobody sees.</summary>
    public static bool Live { get; set; } = true;

    /// <summary>
    /// Called whenever the file list changes. Returns the session to run instead of the page's own tool, or null.
    /// <paramref name="activeChanged"/> is told whether the filmstrip stands in for the page's option boxes.
    /// </summary>
    public static VideoFlowSession? Sync(ContentControl host, ToolPageModel model, IReadOnlyList<string> files, Action<bool> activeChanged)
    {
        if (!FlowPages.Applies(model.ToolId) || !model.Available || files.Count == 0)
        {
            Clear(host);
            activeChanged(false);
            return null;
        }

        var file = files[0];
        if (host.Content is VideoFlowPanel same && same.Matches(model.ToolId, file) && !same.Failed)
        {
            activeChanged(true);
            return (VideoFlowSession)same.Tag;
        }

        Clear(host);
        var panel = new VideoFlowPanel(model.ToolId, file, model.Tool.Availability, Live);
        panel.Tag = new VideoFlowSession(panel);
        panel.ActiveChanged += activeChanged;
        host.Content = panel;
        panel.Load();
        activeChanged(true);
        return (VideoFlowSession)panel.Tag;
    }

    private static void Clear(ContentControl host)
    {
        if (host.Content is VideoFlowPanel old) old.Dispose();
        host.Content = null;
    }

    /// <summary>Puts a player for the finished video (or the first frame of a GIF) under the result card; clears it otherwise.</summary>
    public static void ShowResult(ContentControl host, string? primary)
    {
        if (host.Content is ResultPlayer old) old.Stop();
        host.Content = primary is not null && ResultPlayer.Plays(primary) ? new ResultPlayer(primary) : null;
    }
}

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NetieSpace.Apps.Shell.Panels.Video;

/// <summary>
/// <c>--app video --snapshot out.png --view vid-trim --flow-file clip.mp4 --flow-state handles|presets|gif</c> (W6-4).
/// The headless render opens the clip on the tool page, waits until the filmstrip and the poster frame are really drawn, puts
/// the handles where the state says and scrolls the part of the page that state is about into the picture. A page that never
/// loads fails the snapshot (exit 1) instead of rendering an empty box.
/// </summary>
internal sealed class VideoFlowSnapshot
{
    public static readonly IReadOnlyList<string> States = new[] { "handles", "presets", "gif" };

    public string File { get; }
    public string State { get; }

    private VideoFlowSnapshot(string file, string state) { File = file; State = state; }

    /// <summary>Null when the command line has no <c>--flow-file</c>. Also turns the media player off for the render.</summary>
    public static VideoFlowSnapshot? FromCommandLine(string[]? args = null)
    {
        args ??= Environment.GetCommandLineArgs();
        string? file = null, state = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--flow-file", StringComparison.OrdinalIgnoreCase)) file = args[i + 1].Trim('"');
            if (string.Equals(args[i], "--flow-state", StringComparison.OrdinalIgnoreCase)) state = args[i + 1].Trim('"');
        }
        if (file is null) return null;
        VideoFlowHook.Live = false;
        return new VideoFlowSnapshot(file, state ?? "handles");
    }

    public void Apply(AppShell shell)
    {
        var host = shell.ToolPage.FlowHost;
        var panel = host.Content as VideoFlowPanel
            ?? throw new InvalidOperationException("--flow-file was given but this page has no filmstrip (use --view vid-trim and a video file)");
        Pump(() => panel.Ready || panel.Failed, TimeSpan.FromSeconds(90), "the filmstrip to load");
        if (panel.Failed)
            throw new InvalidOperationException("the filmstrip could not be built: " + panel.FailMessage);
        if (!panel.ApplySnapshotState(State))
            throw new InvalidOperationException("unknown --flow-state '" + State + "' (use " + string.Join(", ", States) + ")");
        Pump(() => panel.Settled, TimeSpan.FromSeconds(60), "the poster frame");

        // Lay the page out as the render will, then scroll the part this state is about into the picture.
        shell.Width = AppSnapshot.Width;
        shell.Height = AppSnapshot.Height;
        shell.Measure(new Size(AppSnapshot.Width, AppSnapshot.Height));
        shell.Arrange(new Rect(0, 0, AppSnapshot.Width, AppSnapshot.Height));
        shell.UpdateLayout();
        if (shell.ToolPage.Content is ScrollViewer sv)
        {
            FrameworkElement? anchor = State == "presets" ? panel.PresetsAnchor : panel;
            if (anchor is not null)
            {
                var y = anchor.TransformToAncestor(sv).Transform(new Point(0, 0)).Y + sv.VerticalOffset;
                var keep = State == "presets" ? 40 : 70;
                sv.ScrollToVerticalOffset(Math.Max(0, y - keep));
                sv.UpdateLayout();
            }
        }
    }

    private static void Pump(Func<bool> done, TimeSpan timeout, string what)
    {
        if (done()) return;
        var clock = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            if (done() || clock.Elapsed > timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        if (!done())
            throw new TimeoutException("snapshot gave up after " + (int)timeout.TotalSeconds + " s waiting for " + what);
    }
}

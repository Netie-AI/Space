using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NetieSpace.Apps.Shell.Panels.Video;
using NetieSpace.Apps.Tools;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// Headless render of the app window: <c>--app pdf --snapshot out.png [--view tool-id]</c>.
/// Never shows a window or a dialog. The shell root is measured and arranged at 1280x800 and
/// drawn into a 96 DPI RenderTargetBitmap, which is the only way an unshown element yields pixels.
/// </summary>
public static class AppSnapshot
{
    public const int Width = 1280;
    public const int Height = 800;
    public const int ExitFailed = 1;

    public static int Run(AppIdentity identity, SnapshotArgs args)
    {
        try
        {
            var tools = ToolRegistry.Default.All();
            var bad = SnapshotArgs.CheckView(args.ToolId, identity, tools.Select(t => t.Entry));
            if (bad is not null)
            {
                Console.Error.WriteLine(bad);
                return SnapshotArgs.ExitInvalid;
            }

            // Before the shell exists: an element outside a window does not repaint when a resource appears later.
            ShellTheme.Force(args.Theme);
            AppAccents.Apply(identity);
            var flow = VideoFlowSnapshot.FromCommandLine(); // W6-4 video flow hook: --flow-file / --flow-state
            var shell = new AppShell();
            RecentFiles recent;
            RecentTools? recentTools = null;
            if (args.SeedRecents is not null)
                (recent, recentTools) = SeedRecents(identity, args.SeedRecents, tools);
            else
                recent = new RecentFiles(RecentFiles.DefaultPath(identity));
            shell.Initialize(identity, recent, args.WithFile ?? flow?.File, null, recentTools);
            shell.SetTools(tools);
            if (args.ToolId is not null)
                shell.ShowTool(args.ToolId);
            switch (args.Overlay)
            {
                case "palette": shell.OpenPalette(args.Query ?? ""); break;
                case "help": shell.ShowHelp(); break;
                case "drop": shell.ShowDropPreview(); break;
            }
            flow?.Apply(shell); // W6-4 video flow hook

            Render(shell, args.OutPath!);
            if (args.AuditPath is not null)
                WriteAudit(shell, args.AuditPath);
            return 0;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"[app:{identity.Key}] snapshot");
            Console.Error.WriteLine("SNAPSHOT_FAILED " + ex.GetType().Name + ": " + ex.Message);
            return ExitFailed;
        }
    }

    /// <summary>
    /// Recent files and tools for a picture: a throw-away folder, files dated 5 minutes, 1 day and 3 days ago, and the first
    /// available tools of the app as recently used. Nothing is read from or written to this PC's own history.
    /// </summary>
    private static (RecentFiles, RecentTools) SeedRecents(AppIdentity identity, string paths, IReadOnlyList<RegisteredTool> tools)
    {
        var dir = IoPath.Combine(IoPath.GetTempPath(), "netie-snapshot-" + Guid.NewGuid().ToString("N"));
        IoDirectory.CreateDirectory(dir);
        var now = DateTime.UtcNow;
        var ages = new[] { TimeSpan.FromMinutes(5), TimeSpan.FromDays(1), TimeSpan.FromDays(3), TimeSpan.FromDays(9) };
        var files = paths.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => IoFile.Exists(p)).ToList();

        var at = now;
        var recent = new RecentFiles(IoPath.Combine(dir, "recent.json"), clock: () => at);
        for (var i = files.Count - 1; i >= 0; i--)
        {
            at = now - ages[Math.Min(i, ages.Length - 1)];
            recent.Add(files[i]);
        }

        var recentTools = new RecentTools(IoPath.Combine(dir, "recent-tools.json"), clock: () => at);
        var mine = HomeModel.ToolAppOf(identity);
        // "none" (or no file that exists) is a clean first run: no recent files and no recent tools.
        var picks = files.Count == 0
            ? new List<string>()
            : tools.Where(t => t.Entry.App == mine && t.Availability.Available).Take(3).Select(t => t.Entry.Id).Reverse().ToList();
        for (var i = 0; i < picks.Count; i++)
        {
            at = now - TimeSpan.FromMinutes(10 + 30 * (picks.Count - i));
            recentTools.Add(picks[i]);
        }
        return (recent, recentTools);
    }

    /// <summary>
    /// The names UI Automation reads for the controls on screen, one line each, then a total. A button, box or list without a
    /// name is "UNNAMED": a screen reader would say only "button". Parts of a control's own template are not counted.
    /// The visible text is listed too, so a check can see that a sentence is on screen and a raw engine message is not.
    /// </summary>
    private static void WriteAudit(FrameworkElement root, string path)
    {
        var lines = new List<string>();
        var unnamed = 0;
        var total = 0;
        void Walk(DependencyObject node)
        {
            if (node is UIElement ui && ui.Visibility != Visibility.Visible)
                return;
            if (node is System.Windows.Controls.TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
                lines.Add("text \"" + tb.Text.Replace("\r", " ").Replace("\n", " ") + "\"");
            if (node is System.Windows.Controls.Control c && IsAudited(c))
            {
                var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement((UIElement)c);
                var name = (peer?.GetName() ?? "").Trim();
                total++;
                if (name.Length == 0)
                    unnamed++;
                lines.Add($"{(name.Length == 0 ? "UNNAMED" : "ok     ")} {c.GetType().Name} {c.Name} \"{name}\"");
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                Walk(VisualTreeHelper.GetChild(node, i));
        }
        Walk(root);
        lines.Add($"controls={total} unnamed={unnamed}");
        var dir = IoPath.GetDirectoryName(IoPath.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            IoDirectory.CreateDirectory(dir);
        IoFile.WriteAllLines(path, lines);
        Console.WriteLine($"AUDIT controls={total} unnamed={unnamed}");
    }

    private static bool IsAudited(System.Windows.Controls.Control c) =>
        c is System.Windows.Controls.ProgressBar
        // a control that cannot take focus is a part of another control's template (a combo box's drop-down arrow)
        || (c.Focusable
            && (c is System.Windows.Controls.Button
                || c is System.Windows.Controls.Primitives.ToggleButton
                || c is System.Windows.Controls.TextBox
                || c is System.Windows.Controls.ComboBox
                || c is System.Windows.Controls.CheckBox
                || c is System.Windows.Controls.ListBox
                || c is System.Windows.Controls.ListBoxItem));

    private static void Render(FrameworkElement root, string outPath)
    {
        root.Width = Width;
        root.Height = Height;
        root.Measure(new Size(Width, Height));
        root.Arrange(new Rect(0, 0, Width, Height));
        root.UpdateLayout();
        // Let Loaded-priority work (bindings, templates) finish before pixels are read.
        root.Dispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var dir = IoPath.GetDirectoryName(IoPath.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(dir))
            IoDirectory.CreateDirectory(dir);
        using var stream = IoFile.Create(outPath);
        encoder.Save(stream);
    }
}

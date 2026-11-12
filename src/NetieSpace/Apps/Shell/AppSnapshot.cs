using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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

            var shell = new AppShell();
            shell.Initialize(identity, new RecentFiles(RecentFiles.DefaultPath(identity)), null);
            shell.SetTools(tools);
            if (args.ToolId is not null)
                shell.ShowTool(args.ToolId);

            Render(shell, args.OutPath!);
            return 0;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"[app:{identity.Key}] snapshot");
            Console.Error.WriteLine("SNAPSHOT_FAILED " + ex.GetType().Name + ": " + ex.Message);
            return ExitFailed;
        }
    }

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

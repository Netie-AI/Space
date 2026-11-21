using System.Windows;
using System.Windows.Threading;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Md.WinRt;

/// <summary>
/// <c>--app md --open FILE [--snapshot OUT.png] [--pdf OUT.pdf] [--theme light|dark] [--steps FILE]</c>: loads the file in an off-screen
/// window on its own STA thread and dispatcher, saves CoreWebView2.CapturePreviewAsync as a PNG
/// (and the PDF when asked) and returns the exit code. No dialog is ever shown.
/// </summary>
public static class MdHeadless
{
    public const int ExitFailed = 1;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(90);

    public static int Run(MdArgs args, IMdEngine engine)
    {
        var code = ExitFailed;
        string? failure = null;

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    failure = await RunAsync(args, engine);
                    if (failure is null)
                        code = 0;
                }
                catch (Exception ex)
                {
                    AppLogger.Error(ex, "[app:md] snapshot");
                    failure = ex.GetType().Name + ": " + ex.Message;
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!thread.Join(Limit))
        {
            Console.Error.WriteLine("SNAPSHOT_FAILED timed out");
            return ExitFailed;
        }
        if (failure is not null)
            Console.Error.WriteLine("SNAPSHOT_FAILED " + failure);
        return code;
    }

    /// <returns>null on success, otherwise what went wrong.</returns>
    private static async Task<string?> RunAsync(MdArgs args, IMdEngine engine)
    {
        var window = new Window
        {
            Width = 1280,
            Height = 800,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -12000,
            Top = 0,
        };
        var view = new MdView(engine, MdTheme.Load(), args.Theme);
        window.Content = view;
        window.Show();
        try
        {
            await view.OpenAsync(args.OpenPath!);
            if (view.Document is not { Error: null })
                return view.Document?.Error ?? "document did not load";
            if (!await view.WaitRenderedAsync())
                return "document did not render";

            var steps = true;
            if (args.StepsPath is not null)
            {
                if (!System.IO.File.Exists(args.StepsPath))
                    return "steps file not found";
                steps = await new MdDriver(view, Console.Out).RunAsync(System.IO.File.ReadAllLines(args.StepsPath));
                if (!steps)
                    return "a step failed";
            }

            // The window is wider than the page column: capture the web view as it is.
            if (args.PngPath is not null)
            {
                await view.CapturePngAsync(args.PngPath);
                var size = new System.IO.FileInfo(args.PngPath).Length;
                Console.WriteLine($"SNAPSHOT_OK {args.PngPath} {size}");
            }
            if (args.PdfPath is not null)
            {
                if (!await view.PrintPdfAsync(args.PdfPath))
                    return "print to PDF failed";
                Console.WriteLine($"PDF_OK {args.PdfPath} {new System.IO.FileInfo(args.PdfPath).Length}");
            }
            return null;
        }
        finally
        {
            view.Shutdown();
            window.Close();
        }
    }
}

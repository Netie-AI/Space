using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace NetieSpace.Apps.Pdf.WinRt;

/// <summary>
/// Runs one local HTML file in a headless WebView2 on its own STA thread (the tool CLI calls runners on a
/// pool thread that has no dispatcher). Everything that is not file:// is refused and counted. The
/// temporary user-data folder is deleted afterwards. Compiled only for the Windows TFM.
/// </summary>
internal static class WebView2Host
{
    public const int TimeoutSeconds = 60;

    /// <summary>Requests refused in the most recent run; read by tests and by the progress message.</summary>
    internal static int LastBlockedCount;

    /// <summary>User-data folder of the most recent run (already deleted when a run returns).</summary>
    internal static string? LastUserDataFolder;

    public sealed class Session
    {
        private int _blocked;

        internal Session(CoreWebView2Environment env, CoreWebView2Controller controller, IntPtr hwnd)
        {
            Environment = env;
            Controller = controller;
            Hwnd = hwnd;
        }

        public CoreWebView2Environment Environment { get; }
        public CoreWebView2Controller Controller { get; }
        public CoreWebView2 Core => Controller.CoreWebView2;
        internal IntPtr Hwnd { get; }
        public int Blocked => Volatile.Read(ref _blocked);
        internal void CountBlocked() => Interlocked.Increment(ref _blocked);

        /// <summary>Sizes the host window and the view in pixels, then lets layout settle.</summary>
        public async Task ResizeAsync(int width, int height)
        {
            NativeMethods.SetWindowPos(Hwnd, IntPtr.Zero, -32000, -32000, width, height,
                NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
            Controller.Bounds = new System.Drawing.Rectangle(0, 0, width, height);
            await Task.Delay(300);
        }
    }

    /// <summary>
    /// Opens <paramref name="htmlPath"/> in a view of the given pixel size and runs <paramref name="work"/>
    /// once it has loaded. Throws a message naming the cause when the runtime is missing, the page cannot
    /// load, or <paramref name="timeoutSeconds"/> pass.
    /// </summary>
    public static async Task<T> RunAsync<T>(
        string htmlPath, int width, int height, Func<Session, Task<T>> work,
        CancellationToken ct, int timeoutSeconds = TimeoutSeconds)
    {
        EnsureRuntime();
        ct.ThrowIfCancellationRequested();

        var userData = IoPath.Combine(IoPath.GetTempPath(), "netie-webview2-" + Guid.NewGuid().ToString("N"));
        LastUserDataFolder = userData;
        LastBlockedCount = 0;
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    tcs.TrySetResult(await BodyAsync(htmlPath, width, height, userData, work));
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "netie-webview2" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        using var reg2 = timeout.Token.Register(() => tcs.TrySetException(new TimeoutException(
            "the web view did not finish within " + timeoutSeconds + " s for " + htmlPath)));
        try
        {
            return await tcs.Task;
        }
        finally
        {
            // A timed-out or cancelled run leaves the thread parked in WebView2: stop its message loop.
            Dispatcher.FromThread(thread)?.BeginInvokeShutdown(DispatcherPriority.Send);
            await DeleteFolderAsync(userData);
        }
    }

    private static void EnsureRuntime()
    {
        string? version = null;
        try { version = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (Exception) { /* treated as missing below */ }
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException(
                "the Microsoft Edge WebView2 Runtime is not installed on this PC, so HTML cannot be rendered");
    }

    private static async Task<T> BodyAsync<T>(
        string htmlPath, int width, int height, string userData, Func<Session, Task<T>> work)
    {
        var hwnd = IntPtr.Zero;
        CoreWebView2Controller? controller = null;
        try
        {
            hwnd = NativeMethods.CreateWindowExW(
                NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate, "STATIC", "netie-html",
                NativeMethods.WsPopup, -32000, -32000, width, height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException("cannot create the host window for the web view (error "
                    + Marshal.GetLastWin32Error() + ")");
            NativeMethods.ShowWindow(hwnd, NativeMethods.SwShowNoActivate);

            // Nothing may leave this PC: names do not resolve and the proxy is a dead port. The request
            // filter below is what counts the refusals.
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--host-resolver-rules=\"MAP * ~NOTFOUND\" --proxy-server=127.0.0.1:1",
            };
            CoreWebView2Environment env;
            try
            {
                env = await CoreWebView2Environment.CreateAsync(null, userData, options);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                throw new InvalidOperationException(
                    "the Microsoft Edge WebView2 Runtime is not installed on this PC, so HTML cannot be rendered");
            }
            controller = await env.CreateCoreWebView2ControllerAsync(hwnd);
            controller.ShouldDetectMonitorScaleChanges = false;
            controller.RasterizationScale = 1.0;
            controller.Bounds = new System.Drawing.Rectangle(0, 0, width, height);
            controller.IsVisible = true;

            var session = new Session(env, controller, hwnd);
            var core = controller.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (IsLocal(e.Request.Uri)) return;
                session.CountBlocked();
                e.Response = env.CreateWebResourceResponse(null, 403, "Blocked", "");
            };

            var started = new System.Collections.Generic.HashSet<ulong>();
            var loaded = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationStarting += (_, e) =>
            {
                if (e.Uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) started.Add(e.NavigationId);
            };
            core.NavigationCompleted += (_, e) =>
            {
                if (started.Contains(e.NavigationId)) loaded.TrySetResult(e);
            };
            core.Navigate(new Uri(htmlPath).AbsoluteUri);
            var done = await loaded.Task;
            if (!done.IsSuccess)
                throw new InvalidOperationException("cannot load " + htmlPath + ": " + done.WebErrorStatus);
            await Task.Delay(400);

            try
            {
                return await work(session);
            }
            finally
            {
                LastBlockedCount = session.Blocked;
            }
        }
        finally
        {
            try { controller?.Close(); } catch { /* already gone */ }
            if (hwnd != IntPtr.Zero) NativeMethods.DestroyWindow(hwnd);
        }
    }

    /// <summary>file: is the page itself; data:, blob: and about: never reach a network.</summary>
    internal static bool IsLocal(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return false;
        return u.Scheme is "file" or "data" or "blob" or "about";
    }

    private static async Task DeleteFolderAsync(string folder)
    {
        // The browser process lets go of its profile a moment after the controller closes.
        for (var i = 0; i < 40; i++)
        {
            try
            {
                if (!IoDirectory.Exists(folder)) return;
                IoDirectory.Delete(folder, true);
                return;
            }
            catch (Exception)
            {
                await Task.Delay(250);
            }
        }
    }

    private static class NativeMethods
    {
        public const uint WsPopup = 0x80000000;
        public const uint WsExToolWindow = 0x00000080;
        public const uint WsExNoActivate = 0x08000000;
        public const int SwShowNoActivate = 4;
        public const uint SwpNoZOrder = 0x0004;
        public const uint SwpNoActivate = 0x0010;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowExW(
            uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hwnd, int cmd);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}

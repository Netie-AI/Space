using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using NetieSpace.Apps.Md.WinRt;
using NetieSpace.Apps.Tools;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// The window of one Netie app process (Netie PDF / Image / Video).
/// Hosts the app shell (sidebar, home, tool page). No Space-key hook, no tray.
/// </summary>
public partial class AppWindow : Window
{
    private readonly AppIdentity _identity;
    private readonly MdView? _md;
    private readonly WindowPlacementStore _placement;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public AppWindow(AppIdentity identity, string? openPath)
    {
        _identity = identity;
        AppAccents.Apply(identity);
        InitializeComponent();
        Title = identity.DisplayName;
        ApplyIcon();
        _placement = new WindowPlacementStore(WindowPlacementStore.DefaultPath(identity));
        RestorePlacement();
        Closing += (_, _) => SavePlacement();
        ShellTheme.Watch();
        ShellTheme.Changed += ApplyTitleBarTheme;
        // Before the window is shown, so a taskbar pin relaunches this app and not Netie Space.
        SourceInitialized += (_, _) =>
        {
            AppRelaunch.Apply(new System.Windows.Interop.WindowInteropHelper(this).Handle, _identity);
            ApplyTitleBarTheme();
        };
        if (identity.IsMarkdown)
        {
            // Netie MD hosts a web page instead of the tool shell.
            _md = new MdView(new MdMarkdigEngine(), MdTheme.Load());
            Content = _md;
            _md.DocumentChanged += name => Title = name is null ? identity.DisplayName : name + " - " + identity.DisplayName;
            Loaded += async (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(openPath))
                    await _md.OpenAsync(openPath);
            };
            Closed += (_, _) => _md.Shutdown();
            return;
        }

        PreviewKeyDown += (_, e) => Shell.HandleKey(e);
        Shell.Initialize(identity, new RecentFiles(RecentFiles.DefaultPath(identity)), openPath, null,
            new RecentTools(RecentTools.DefaultPath(identity)));
        Loaded += async (_, _) => await LoadToolsAsync();
    }

    public string? SelectedFile => _md is not null ? _md.CurrentPath : Shell.SelectedFile;

    public void SelectFile(string? path)
    {
        if (_md is not null)
        {
            if (!string.IsNullOrWhiteSpace(path))
                _ = _md.OpenAsync(path);
            return;
        }
        Shell.SelectFile(path);
    }

    /// <summary>The size and place this app's window had last time, pulled back on screen if the desktop changed since.</summary>
    private void RestorePlacement()
    {
        var desktop = new WindowPlacement.Area(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var fit = WindowPlacement.Fit(_placement.Load(), desktop, MinWidth, MinHeight);
        if (fit is null)
            return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = fit.Left;
        Top = fit.Top;
        Width = fit.Width;
        Height = fit.Height;
        if (fit.Maximized)
            WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        // A maximised or minimised window remembers the size it will return to.
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (r.Width > 0 && r.Height > 0)
            _placement.Save(new WindowPlacement(r.Left, r.Top, r.Width, r.Height, WindowState == WindowState.Maximized));
    }

    /// <summary>A dark client area under a light title bar looks broken: ask Windows for the matching caption.</summary>
    private void ApplyTitleBarTheme()
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;
            var dark = ShellTheme.IsDark ? 1 : 0;
            const int UseImmersiveDarkMode = 20; // 19 before Windows 10 20H1
            if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[app:{_identity.Key}] title bar theme: {ex.Message}");
        }
    }

    /// <summary>Availability probes can be slow: build the registry view off the UI thread.</summary>
    private async Task LoadToolsAsync()
    {
        try
        {
            var tools = await Task.Run(() => ToolRegistry.Default.All());
            Shell.SetTools(tools);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"[app:{_identity.Key}] tool registry");
        }
    }

    /// <summary>A second launch of the same app lands here: come forward, show its file.</summary>
    public void ActivateFromSecondLaunch(string? path)
    {
        AppLogger.Info($"[app:{_identity.Key}] activate {path ?? "(no file)"}");
        if (!string.IsNullOrWhiteSpace(path))
            SelectFile(path);
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        Activate();
        // Windows refuses a plain Activate from a background process; a topmost blink does not.
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ApplyIcon()
    {
        var path = _identity.IconPath;
        if (!IoFile.Exists(path))
        {
            AppLogger.Warn($"[app:{_identity.Key}] app icon missing at {path}; using netie.ico");
            path = AppIconHelper.IconPath;
        }

        try
        {
            if (IoFile.Exists(path))
                Icon = BitmapFrame.Create(new Uri(path, UriKind.Absolute));
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[app:{_identity.Key}] window icon load failed ({path}): {ex.Message}");
        }
    }
}

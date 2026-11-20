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

    public AppWindow(AppIdentity identity, string? openPath)
    {
        _identity = identity;
        AppAccents.Apply(identity);
        InitializeComponent();
        Title = identity.DisplayName;
        ApplyIcon();
        // Before the window is shown, so a taskbar pin relaunches this app and not Netie Space.
        SourceInitialized += (_, _) =>
            AppRelaunch.Apply(new System.Windows.Interop.WindowInteropHelper(this).Handle, _identity);
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

        Shell.Initialize(identity, new RecentFiles(RecentFiles.DefaultPath(identity)), openPath);
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

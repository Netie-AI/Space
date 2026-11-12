using System.Windows;
using System.Windows.Media.Imaging;
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

    public AppWindow(AppIdentity identity, string? openPath)
    {
        _identity = identity;
        InitializeComponent();
        Title = identity.DisplayName;
        ApplyIcon();
        Shell.Initialize(identity, new RecentFiles(RecentFiles.DefaultPath(identity)), openPath);
        Loaded += async (_, _) => await LoadToolsAsync();
    }

    public string? SelectedFile => Shell.SelectedFile;

    public void SelectFile(string? path) => Shell.SelectFile(path);

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

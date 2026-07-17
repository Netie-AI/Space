using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace NetieSpace.Helpers;

/// <summary>
/// Lazily creates and caches a WebView2 environment; warms CoreWebView2 on idle UI thread.
/// </summary>
public static class WebViewWarmup
{
    private static CoreWebView2Environment? _environment;
    private static Task<CoreWebView2Environment?>? _initTask;
    private static readonly object Lock = new();
    private static bool _controlWarmed;

    public static CoreWebView2Environment? Environment => _environment;

    /// <summary>
    /// Kick off env create + hidden control EnsureCoreWebView2 on the UI dispatcher after tray is up.
    /// </summary>
    public static void StartWarmup()
    {
        AppLogger.Info("WebView2 warmup scheduled (idle UI)");
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        _ = dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await Task.Delay(400); // let tray/license settle
                var env = await GetEnvironmentAsync();
                if (env is null || _controlWarmed)
                    return;

                // Hidden control so first Space skips cold EnsureCoreWebView2
                var host = new Window
                {
                    Width = 1,
                    Height = 1,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    Opacity = 0,
                    AllowsTransparency = true,
                    ShowActivated = false,
                    Left = -32000,
                    Top = -32000
                };
                var wv = new WebView2 { Width = 1, Height = 1 };
                host.Content = wv;
                host.Show();
                await wv.EnsureCoreWebView2Async(env);
                _controlWarmed = true;
                AppLogger.Info("WebView2 control warmed");
                host.Close();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"WebView2 warmup failed: {ex.Message}");
            }
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    public static Task<CoreWebView2Environment?> GetEnvironmentAsync()
    {
        lock (Lock)
        {
            if (_environment is not null)
                return Task.FromResult<CoreWebView2Environment?>(_environment);

            _initTask ??= CreateAsync();
            return _initTask;
        }
    }

    private static async Task<CoreWebView2Environment?> CreateAsync()
    {
        try
        {
            var userData = IoPath.Combine(EnvLoader.UserDataDirectory, "WebView2");
            IoDirectory.CreateDirectory(userData);
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData).ConfigureAwait(true);
            _environment = env;
            AppLogger.Info("WebView2 environment ready");
            return env;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"WebView2 environment create failed: {ex.Message}");
            lock (Lock)
                _initTask = null;
            return null;
        }
    }
}

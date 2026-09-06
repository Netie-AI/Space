using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Configuration;
using NetieSpace.Helpers;
using NetieSpace.Models;
using NetieSpace.Services;
using NetieSpace.Views;

namespace NetieSpace;

public partial class App : Application
{
    private const string MutexName = "Global\\NetieSpace.SingleInstance.v1";
    private const string ShowTrayEventName = "Global\\NetieSpace.ShowTray.v1";
    private static Mutex? _mutex;
    private static bool _ownsMutex;
    private EventWaitHandle? _showTrayEvent;
    private Thread? _showTrayWatcher;

    private static string PendingPreviewPath =>
        IoPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetieSpace", "pending-preview.txt");

    private TaskbarIcon? _trayIcon;
    private GlobalKeyboardHook? _keyboardHook;
    private ExplorerFileService? _explorerService;
    private ContentParserService? _contentParser;
    private ConversionService? _conversionService;
    private AiService? _aiService;
    private SummaryCache? _summaryCache;
    private ConversationMemoryCache? _conversationMemory;
    private AppSettings? _settings;
    private PreviewWindow? _previewWindow;
    private ApiSetupWindow? _apiSetupWindow;
    private readonly object _previewLock = new();
    private DateTime _lastUiErrorPopupUtc = DateTime.MinValue;
    private string? _lastUiErrorMessage;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out var isNew);
        _ownsMutex = isNew;
        var headlessStamp = e.Args.Any(a => string.Equals(a, "--stamp-test", StringComparison.OrdinalIgnoreCase));
        if (!isNew && !headlessStamp)
        {
            // Desktop "Netie" stack button / second launch — no MessageBox spam.
            // Forward --preview to the live instance via a pending file + tray ping.
            try
            {
                var previewPath = TryExtractPreviewPath(e.Args);
                if (!string.IsNullOrWhiteSpace(previewPath))
                {
                    IoDirectory.CreateDirectory(IoPath.GetDirectoryName(PendingPreviewPath)!);
                    IoFile.WriteAllText(PendingPreviewPath, previewPath);
                }

                using var ping = EventWaitHandle.OpenExisting(ShowTrayEventName);
                ping.Set();
            }
            catch
            {
                /* first instance may be mid-startup */
            }

            try { _mutex.Dispose(); } catch { /* ignore */ }
            _mutex = null;
            _ownsMutex = false;
            Shutdown();
            return;
        }

        base.OnStartup(e);
        AppLogger.Info($"Starting NetieSpace v1.0 from {AppContext.BaseDirectory}");
        AppLogger.Info($"Log file: {AppLogger.LogPath}");

        // Headless stamp smoke (scripts/stress-sign.ps1) — no tray/UI
        if (TryHandleCliStampTest(e.Args))
            return;

        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Error(args.Exception, "UI unhandled");
            args.Handled = true; // keep process alive; layout may recover after resource fix

            // Never MessageBox-storm on layout retries (was: unlimited popups on Video Edit)
            var msg = args.Exception.GetBaseException().Message;
            var now = DateTime.UtcNow;
            if (msg == _lastUiErrorMessage && (now - _lastUiErrorPopupUtc).TotalSeconds < 12)
                return;
            _lastUiErrorPopupUtc = now;
            _lastUiErrorMessage = msg;

            // Resource/layout errors are logged only — MessageBox during Measure causes more layout
            if (msg.Contains("StaticResource", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("GlassScrollViewer", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("TypeConverter", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("stack", StringComparison.OrdinalIgnoreCase) ||
                args.Exception is System.Windows.Markup.XamlParseException ||
                args.Exception is OutOfMemoryException)
                return;

            try
            {
                MessageBox.Show(
                    $"Something went wrong:\n\n{msg}\n\nDetails: {AppLogger.LogPath}",
                    "Netie Space",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch { /* ignore */ }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                AppLogger.Error(ex, "AppDomain unhandled");
            else
                AppLogger.Error($"AppDomain unhandled: {args.ExceptionObject}");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Error(args.Exception, "Unobserved task");
            args.SetObserved();
        };

        try
        {
            EnvLoader.LoadFromProjectRoot();
            CrashRecovery.Register();
            AppResourceBudget.Initialize();
            if (UserPrefs.EmbeddingConsentAccepted)
                LocalEmbeddingBrain.Instance.EnsureLayout();
            _settings = LoadSettings();
            Services.Licensing.LicenseActivation.Configure(
                _settings?.License.ApiBaseUrl,
                _settings?.License.DevicePepper);
            _summaryCache = new SummaryCache(_settings.Preview.CacheSummaries);
            _conversationMemory = new ConversationMemoryCache();
            _aiService = new AiService(_settings.Ai, _summaryCache, _conversationMemory);
            _conversionService = new ConversionService();
            _contentParser = new ContentParserService(_conversionService);
            _explorerService = new ExplorerFileService();

            // Soft-connect OpenVault key SoT (offline cache stays in user.env)
            _ = SyncOpenVaultOnStartupAsync();

            WebViewWarmup.StartWarmup();

            _keyboardHook = new GlobalKeyboardHook
            {
                SpacePressed = HandleSpacePressed,
                EscapePressed = HandleEscapePressed,
                ArrowPressed = HandleArrowPressed,
                HomeEndPressed = HandleHomeEndPressed
            };
            _keyboardHook.Start();
            AppLogger.Info("Global Space/Esc/Arrow/HomeEnd hooks registered");

            _trayIcon = new TaskbarIcon
            {
                Icon = AppIconHelper.LoadTrayIcon(),
                ToolTipText = "NETIE SPACE — Space in Explorer/Desktop to preview",
                Visibility = Visibility.Visible
            };
            _trayIcon.TrayMouseDoubleClick += (_, _) =>
            {
                // Double-click tray: do not re-spam HUD — open tip only if never seen, else noop focus
                if (!UserPrefs.StartupHudSeen)
                    ShowStartupHudIfNeeded(force: true);
            };
            _trayIcon.ContextMenu = BuildTrayMenu();
            AppLogger.Info(
                AppIconHelper.ExistsOnDisk()
                    ? $"Tray icon loaded from {AppIconHelper.IconPath}"
                    : "Tray icon using fallback (netie.ico not on disk)");

            StartShowTrayWatcher();

            ShowStartupHudIfNeeded(force: false);
            AppLogger.Info($"Ready. AI provider: {_aiService.ActiveProvider}. Conversion: {_conversionService.IsAvailable}");

            EnsureLicenseGate();

            // AirGPT / CLI: NetieSpace.exe --preview "C:\path\to\file"
            TryHandleCliPreview(e.Args);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Startup failed");
            MessageBox.Show(
                $"Netie Space failed to start:\n\n{ex.Message}\n\nLog: {AppLogger.LogPath}",
                "Netie Space Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    /// <summary>
    /// Swallow Space/Esc/arrows only when Explorer/Desktop or our preview owns focus.
    /// Chrome/Word/etc. keep their keys (Peek/QL: float visible, do not steal).
    /// </summary>
    private bool ShouldSwallowPreviewKeys()
    {
        if (GlobalKeyboardHook.IsExplorerOrDesktopForeground())
            return true;
        return Win32Api.IsOurProcessForeground();
    }

    /// <summary>
    /// Runs on the hook thread. Must not call Shell COM here (RPC_E_CANTCALLOUT_ININPUTSYNCCALL).
    /// Returns true to swallow Space.
    /// </summary>
    private bool HandleSpacePressed()
    {
        // Explorer F2 rename / in-place label edit — Space must insert a space character.
        if (Win32Api.IsExplorerLabelEdit())
            return false;

        // Never close / open while typing in chat, signature, API keys, etc.
        if (IsTypingContext())
        {
            AppLogger.Info("Space ignored — typing context (chat/dialog focus)");
            return false;
        }

        var previewOpen = false;
        lock (_previewLock)
            previewOpen = _previewWindow is { IsLoaded: true };

        if (previewOpen)
        {
            if (!ShouldSwallowPreviewKeys())
                return false;

            Dispatcher.BeginInvoke(() =>
            {
                if (IsTypingContext())
                    return;
                if (!ShouldSwallowPreviewKeys())
                    return;
                lock (_previewLock)
                {
                    if (_previewWindow is { IsLoaded: true } win)
                    {
                        if (win.TryPopPreviewStack())
                        {
                            AppLogger.Info("Space toggle — popped nested preview");
                            return;
                        }

                        AppLogger.Info("Space toggle — save-then-close preview");
                        _ = win.RequestCloseAsync();
                    }
                }
            });
            return true;
        }

        if (!GlobalKeyboardHook.IsExplorerOrDesktopForeground())
        {
            var fg = Win32Api.GetForegroundWindow();
            var cur = Win32Api.GetWindowUnderCursor();
            AppLogger.Info(
                $"Space ignored — not Explorer/Desktop " +
                $"(fg=0x{fg.ToInt64():X}/{Win32Api.GetWindowClass(fg)} " +
                $"cursor=0x{cur.ToInt64():X}/{Win32Api.GetWindowClass(cur)})");
            return false;
        }

        // Capture context HWND now (focus control, not delayed GetForegroundWindow — that often becomes Progman).
        var contextHwnd = Win32Api.GetExplorerContextHwnd();
        if (contextHwnd == IntPtr.Zero)
            contextHwnd = Win32Api.GetForegroundWindow();
        var cursorHwnd = Win32Api.GetWindowUnderCursor();

        AppLogger.Info(
            $"Space context hwnd=0x{contextHwnd.ToInt64():X} class={Win32Api.GetWindowClass(contextHwnd)} " +
            $"cursor=0x{cursorHwnd.ToInt64():X} class={Win32Api.GetWindowClass(cursorHwnd)} " +
            $"fg=0x{Win32Api.GetForegroundWindow().ToInt64():X}");

        _ = Task.Run(async () =>
        {
            await Task.Delay(40);
            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    if (IsTypingContext())
                        return;

                    var path = _explorerService?.GetSelectedFilePath(contextHwnd, cursorHwnd);
                    if (string.IsNullOrWhiteSpace(path) || !ExplorerFileService.PathExists(path))
                    {
                        AppLogger.Warn("Space pressed but no file selected in Explorer");
                        ShowBalloon("Select a file first", "Click a file in Explorer or Desktop, then press Space.");
                        return;
                    }

                    var ownerHwnd = PreferOwnerHwnd(cursorHwnd, contextHwnd);
                    await OpenPreviewAsync(path, ownerHwnd);
                }
                catch (Exception ex)
                {
                    AppLogger.Error(ex, "Space handler failed");
                    ShowBalloon("Preview failed", ex.GetBaseException().Message);
                }
            });
        });
        return true;
    }

    /// <summary>True when focus is in a text field or a modal editor dialog — Space must type a space.</summary>
    private bool IsTypingContext()
    {
        try
        {
            // Hook thread: marshal briefly to UI
            if (!Dispatcher.CheckAccess())
            {
                var typing = false;
                try
                {
                    Dispatcher.Invoke(
                        () => typing = IsTypingContextOnUi(),
                        DispatcherPriority.Send,
                        CancellationToken.None,
                        TimeSpan.FromMilliseconds(120));
                }
                catch (Exception ex)
                {
                    // Timeout / busy — do NOT treat as typing (that silently killed Space).
                    // Preview open is the product path; real text fields rarely hang the dispatcher.
                    AppLogger.Warn($"IsTypingContext marshal failed ({ex.GetType().Name}) — assuming not typing");
                    return false;
                }
                return typing;
            }

            return IsTypingContextOnUi();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"IsTypingContext error: {ex.Message} — assuming not typing");
            return false;
        }
    }

    private bool IsTypingContextOnUi()
    {
        foreach (Window w in Windows)
        {
            if (w is PreviewWindow or StartupHud)
                continue;
            if (w.IsVisible && w.IsKeyboardFocusWithin)
                return true;
        }

        PreviewWindow? preview;
        lock (_previewLock)
            preview = _previewWindow is { IsLoaded: true } ? _previewWindow : null;

        if (preview is null)
            return false;

        return preview.IsTypingOrEditCapture;
    }

    /// <summary>
    /// Esc closes preview (WebView2-safe via global hook), but never while typing in chat / inputs / edit mode.
    /// </summary>
    private bool HandleEscapePressed()
    {
        if (IsTypingContext())
            return false;

        PreviewWindow? window;
        lock (_previewLock)
            window = _previewWindow is { IsLoaded: true } ? _previewWindow : null;

        if (window is null)
            return false;

        if (!ShouldSwallowPreviewKeys())
            return false;

        Dispatcher.BeginInvoke(() =>
        {
            if (IsTypingContext())
                return;
            if (!ShouldSwallowPreviewKeys())
                return;
            lock (_previewLock)
            {
                if (_previewWindow is { IsLoaded: true } win)
                {
                    if (win.TryPopPreviewStack())
                    {
                        AppLogger.Info("Esc — popped nested preview");
                        return;
                    }

                    AppLogger.Info("Esc — save-then-close preview");
                    _ = win.RequestCloseAsync();
                }
            }
        });
        return true;
    }

    /// <summary>
    /// Mac Quick Look–style: while preview is open, Up/Down/Left/Right travel siblings.
    /// Hook thread — no Shell COM here; dispatch to UI. Skipped while typing / edit mode.
    /// </summary>
    private bool HandleArrowPressed(uint vk)
    {
        if (IsTypingContext())
            return false;

        PreviewWindow? window;
        lock (_previewLock)
            window = _previewWindow is { IsLoaded: true } ? _previewWindow : null;

        if (window is null)
            return false;

        if (!ShouldSwallowPreviewKeys())
            return false;

        var delta = vk is Win32Api.VkDown or Win32Api.VkRight ? 1 : -1;
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (IsTypingContext())
                    return;
                lock (_previewLock)
                {
                    if (_previewWindow is { IsLoaded: true })
                        _ = _previewWindow.NavigateAdjacentAsync(delta, vk);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Arrow nav failed");
            }
        });
        return true;
    }

    private bool HandleHomeEndPressed(uint vk)
    {
        if (IsTypingContext())
            return false;

        PreviewWindow? window;
        lock (_previewLock)
            window = _previewWindow is { IsLoaded: true } ? _previewWindow : null;

        if (window is null)
            return false;

        if (!ShouldSwallowPreviewKeys())
            return false;

        var toEnd = vk == Win32Api.VkEnd;
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (IsTypingContext())
                    return;
                lock (_previewLock)
                {
                    if (_previewWindow is { IsLoaded: true })
                        _ = _previewWindow.NavigateExtremeAsync(toEnd);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Home/End nav failed");
            }
        });
        return true;
    }

    private static IntPtr PreferOwnerHwnd(IntPtr cursorHwnd, IntPtr contextHwnd)
    {
        if (cursorHwnd != IntPtr.Zero &&
            Win32Api.TryFindExplorerShellWindow(cursorHwnd, out var shell, out var cls) &&
            Win32Api.IsExplorerShellClass(cls))
            return shell != IntPtr.Zero ? shell : cursorHwnd;

        if (contextHwnd != IntPtr.Zero &&
            Win32Api.TryFindExplorerShellWindow(contextHwnd, out var shell2, out _))
            return shell2 != IntPtr.Zero ? shell2 : contextHwnd;

        return contextHwnd;
    }

    private bool TryHandleCliStampTest(string[] args)
    {
        string? path = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--stamp-test", StringComparison.OrdinalIgnoreCase))
            {
                path = args[i + 1].Trim('"');
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(path) || !IoFile.Exists(path))
            return false;

        try
        {
            EnvLoader.LoadFromProjectRoot();
            var ext = IoPath.GetExtension(path).ToLowerInvariant();
            if (ext != ".pdf")
            {
                AppLogger.Warn($"--stamp-test requires a PDF, got {ext}");
                Environment.Exit(2);
                return true;
            }

            var edit = new PdfEditService();
            var fields = PdfEditService.GetFormFieldRects(path);
            var page = fields.Count > 0 ? fields[0].Page : 1;
            double x = 72, y = 72, w = 120, h = 40;
            if (fields.Count > 0)
            {
                var f = fields[0];
                x = f.X;
                y = f.Y;
                w = Math.Max(40, f.Width);
                h = Math.Max(20, f.Height);
                AppLogger.Info($"Stamp test snap target: {f.Name ?? "field"} page {f.Page}");
            }

            var tempPng = IoPath.Combine(IoPath.GetTempPath(), $"netie_stamp_test_{Guid.NewGuid():N}.png");
            PdfEditService.RenderTypedSignaturePng("Netie Test", "Segoe Script", tempPng);
            var output = edit.StampTypedSignature(
                path, "Netie Test", "Segoe Script", null, page, x, y, w, h);

            if (!IoFile.Exists(output))
            {
                AppLogger.Error("Stamp test failed — output missing");
                Environment.Exit(3);
                return true;
            }

            var grew = new FileInfo(output).Length >= new FileInfo(path).Length;
            AppLogger.Info($"Stamp test OK → {output} (fields={fields.Count}, grew={grew})");
            Console.WriteLine($"STAMP_OK {output}");
            Environment.Exit(0);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Stamp test failed");
            Console.Error.WriteLine($"STAMP_FAIL {ex.Message}");
            Environment.Exit(4);
            return true;
        }
    }

    private static string? TryExtractPreviewPath(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--preview", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length)
            {
                var p = args[i + 1].Trim('"');
                return IoFile.Exists(p) ? p : null;
            }

            if (i == 0 && !args[i].StartsWith('-') && IoFile.Exists(args[i]))
                return args[i];
        }

        return null;
    }

    private void TryHandleCliPreview(string[] args)
    {
        if (args.Length == 0)
            return;

        if (args.Any(a => string.Equals(a, "--recover", StringComparison.OrdinalIgnoreCase)))
        {
            var last = CrashRecovery.LoadLastPreview();
            if (!string.IsNullOrWhiteSpace(last))
            {
                AppLogger.Info($"Recovering last preview: {last}");
                ShowBalloon("Netie Space recovered", IoPath.GetFileName(last));
                Dispatcher.BeginInvoke(async () => await OpenPreviewAsync(last!));
            }
            return;
        }

        var path = TryExtractPreviewPath(args);
        if (string.IsNullOrWhiteSpace(path))
            return;

        var file = path!;
        Dispatcher.BeginInvoke(async () =>
        {
            AppLogger.Info($"CLI preview: {file}");
            await OpenPreviewAsync(file);
        });
    }

    private async Task OpenPreviewAsync(string path, IntPtr ownerHwnd = default)
    {
        try
        {
            PreviewWindow? existing = null;
            lock (_previewLock)
            {
                if (_previewWindow is { IsLoaded: true })
                    existing = _previewWindow;
            }

            // Already open → reload path (CLI / second Space path) instead of close-and-bail
            if (existing is not null)
            {
                AppLogger.Info($"Preview reload: {path}");
                try
                {
                    long? size = null;
                    try { size = new FileInfo(path).Length; } catch { /* ignore */ }
                    UsageFootprintStore.Track("preview", path, size);
                }
                catch { /* ignore */ }
                CrashRecovery.RememberPreview(path);
                await existing.LoadFileAsync(path);
                return;
            }

            AppLogger.Info($"Preview: {path}");
            try
            {
                long? size = null;
                try { size = new FileInfo(path).Length; } catch { /* ignore */ }
                UsageFootprintStore.Track("preview", path, size);
            }
            catch { /* ignore */ }
            CrashRecovery.RememberPreview(path);
            PreviewWindow window;
            lock (_previewLock)
            {
                window = new PreviewWindow(
                    _settings!.Preview.AnimationDurationMs,
                    _contentParser!,
                    _conversionService!,
                    _aiService!,
                    _explorerService!,
                    ownerHwnd,
                    () => ShowApiSetup());

                window.Closed += (_, _) =>
                {
                    lock (_previewLock)
                        _previewWindow = null;
                };

                _previewWindow = window;
                window.PositionOnOwnerMonitor(ownerHwnd);
                window.Show();
            }

            await window.LoadFileAsync(path);
        }
        catch (Exception ex)
        {
            var detail = ex.ToString();
            for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
                detail += "\n--- INNER ---\n" + inner;
            AppLogger.Error(ex, "Preview failed: " + ex.GetBaseException().Message);
            AppLogger.Info(detail);
        }
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();

        var showHud = new MenuItem { Header = "Show tip" };
        showHud.Click += (_, _) => ShowStartupHudIfNeeded(force: true);
        menu.Items.Add(showHud);

        var logFolder = new MenuItem { Header = "Open log folder" };
        logFolder.Click += (_, _) => OpenLogFolder();
        menu.Items.Add(logFolder);

        menu.Items.Add(new Separator());

        var status = new MenuItem
        {
            Header = Services.Licensing.LicenseStore.IsLicensedOffline()
                ? $"License OK · AI: {_aiService?.ActiveProvider ?? "none"}"
                : $"License pending · AI: {_aiService?.ActiveProvider ?? "none"}",
            IsEnabled = false
        };
        menu.Items.Add(status);

        var account = new MenuItem { Header = "Account / Sign in…" };
        account.Click += (_, _) =>
        {
            var api = Services.Licensing.LicenseActivation.ResolveApiBase();
            var pepper = Services.Licensing.LicenseActivation.ResolvePepper();
            var login = new LoginWindow(api, pepper);
            login.ShowDialog();
            if (_trayIcon?.ContextMenu is not null)
                _trayIcon.ContextMenu = BuildTrayMenu();
        };
        menu.Items.Add(account);

        var signOut = new MenuItem { Header = "Sign out (clear license)" };
        signOut.Click += (_, _) =>
        {
            Services.Licensing.LicenseStore.Clear();
            MessageBox.Show("Local license cleared. Sign in again to activate.", "Netie Space");
            if (_trayIcon?.ContextMenu is not null)
                _trayIcon.ContextMenu = BuildTrayMenu();
        };
        menu.Items.Add(signOut);

        var apiKeys = new MenuItem { Header = "API Keys…" };
        apiKeys.Click += (_, _) => ShowApiSetup();
        menu.Items.Add(apiKeys);

        var myDetails = new MenuItem { Header = "My details…" };
        myDetails.Click += (_, _) => FillProfileWindow.ShowStandalone();
        menu.Items.Add(myDetails);

        var memory = new MenuItem
        {
            Header = UserPrefs.EmbeddingConsentAccepted
                ? "Local memory ✓ (dual-brain on)"
                : "Local memory… (dual-brain)"
        };
        memory.Click += (_, _) =>
        {
            if (UserPrefs.EmbeddingConsentAccepted)
            {
                var wipe = MessageBox.Show(
                    "Local dual-brain is ON (private folder on this PC only).\n\n" +
                    "Yes = wipe all embeddings & revoke consent\n" +
                    "No = keep memory\n" +
                    "Cancel = close",
                    "Netie Space — Local memory",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                if (wipe == MessageBoxResult.Yes)
                {
                    LocalEmbeddingBrain.Instance.WipeAll();
                    UserPrefs.EmbeddingConsentAccepted = false;
                    UserPrefs.EmbeddingConsentPrompted = true;
                    MessageBox.Show("Local memory wiped. Summaries will not use dual-brain until you Allow again.",
                        "Netie Space");
                }
            }
            else
            {
                EmbeddingConsentWindow.EnsurePrompted();
            }

            if (_trayIcon?.ContextMenu is not null)
                _trayIcon.ContextMenu = BuildTrayMenu();
        };
        menu.Items.Add(memory);

        var vault = new MenuItem { Header = "Password Vault…" };
        vault.Click += (_, _) => PasswordVaultWindow.ShowVault();
        menu.Items.Add(vault);

        var footprint = new MenuItem { Header = "Usage footprint…" };
        footprint.Click += (_, _) =>
        {
            var (n, bytes) = UsageFootprintStore.SnapshotStats();
            var export = UsageFootprintStore.ExportEncryptedLearningBlob();
            MessageBox.Show(
                $"Local events: {n:N0}\nLog size: {bytes:N0} bytes\n\n" +
                "Tracks extensions + actions only (no file paths/contents).\n" +
                (export is null
                    ? "No export yet."
                    : $"Encrypted learning blob:\n{export}"),
                "Netie Space · footprint");
        };
        menu.Items.Add(footprint);

        var provider = new MenuItem { Header = $"Switch AI ({_aiService?.ActiveProvider ?? "none"})" };
        provider.Click += (_, _) =>
        {
            var next = _aiService?.SwitchToNextProvider() ?? "none";
            ShowBalloon("AI provider", $"Now using: {next}");
            if (_trayIcon?.ContextMenu is not null)
                _trayIcon.ContextMenu = BuildTrayMenu();
        };
        menu.Items.Add(provider);

        var about = new MenuItem { Header = "About" };
        about.Click += (_, _) => ShowAbout();
        menu.Items.Add(about);

        menu.Items.Add(new Separator());

        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => Shutdown();
        menu.Items.Add(quit);

        return menu;
    }

    private static void OpenLogFolder()
    {
        try
        {
            var dir = IoPath.GetDirectoryName(AppLogger.LogPath)!;
            IoDirectory.CreateDirectory(dir);
            if (!IoFile.Exists(AppLogger.LogPath))
                IoFile.WriteAllText(AppLogger.LogPath, "");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot open log folder: {ex.Message}", "Netie Space");
        }
    }

    private void EnsureLicenseGate()
    {
        var skipEnv = Environment.GetEnvironmentVariable("NETIE_SKIP_LICENSE");
        var skip = _settings?.License.SkipGate == true
                   || string.Equals(skipEnv, "1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skipEnv, "true", StringComparison.OrdinalIgnoreCase);

        if (skip)
        {
            AppLogger.Info("License gate skipped (dev / SkipGate)");
            return;
        }

        if (Services.Licensing.LicenseStore.IsLicensedOffline())
        {
            AppLogger.Info("Offline license OK");
            return;
        }

        try
        {
            var api = Services.Licensing.LicenseActivation.ResolveApiBase();
            var pepper = Services.Licensing.LicenseActivation.ResolvePepper();
            var login = new LoginWindow(api, pepper);
            var ok = login.ShowDialog() == true && login.Entitled;
            if (!ok)
            {
                AppLogger.Warn("License gate cancelled — exiting");
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "License gate");
            Shutdown();
        }
    }

    private void ShowApiSetup()
    {
        if (_apiSetupWindow is { IsLoaded: true })
        {
            _apiSetupWindow.Activate();
            return;
        }

        _apiSetupWindow = new ApiSetupWindow(_aiService!, () =>
        {
            if (_trayIcon?.ContextMenu is not null)
                _trayIcon.ContextMenu = BuildTrayMenu();
        });
        _apiSetupWindow.Closed += (_, _) => _apiSetupWindow = null;
        _apiSetupWindow.Show();
    }

    private async Task SyncOpenVaultOnStartupAsync()
    {
        try
        {
            if (_aiService is null) return;
            var result = await OpenVaultKeySync.SyncApiKeysAsync().ConfigureAwait(false);
            if (result.Synced)
            {
                _aiService.ReloadProviders();
                AppLogger.Info($"OpenVault startup sync: {result.Message}");
            }
            else
            {
                AppLogger.Info($"OpenVault startup: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"OpenVault startup sync: {ex.Message}");
        }
    }

    private void ShowBalloon(string title, string text)
    {
        try
        {
            _trayIcon?.ShowBalloonTip(title, text, BalloonIcon.Info);
        }
        catch
        {
            // balloons optional
        }
    }

    private void ShowStartupHudIfNeeded(bool force)
    {
        var args = Environment.GetCommandLineArgs();
        var setup = args.Any(a => a.Equals("--setup", StringComparison.OrdinalIgnoreCase) ||
                                  a.Equals("--ci", StringComparison.OrdinalIgnoreCase) ||
                                  a.Equals("--show-hud", StringComparison.OrdinalIgnoreCase));
        if (!force && !setup && UserPrefs.StartupHudSeen)
            return;

        ShowStartupHud();
        if (!force || setup || !UserPrefs.StartupHudSeen)
            UserPrefs.StartupHudSeen = true;
    }

    private void ShowStartupHud()
    {
        var hud = new StartupHud(_aiService?.ActiveProvider ?? "none");
        hud.Show();
    }

    private static AppSettings LoadSettings()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        var settings = new AppSettings();
        config.Bind(settings);

        // Primary settings.Ai slot is Groq-shaped; other providers are wired from env in AiService.
        if (string.IsNullOrWhiteSpace(settings.Ai.ApiKey))
        {
            settings.Ai.ApiKey =
                Environment.GetEnvironmentVariable("GROQ_API_KEY") ??
                Environment.GetEnvironmentVariable("NETIESPACE_API_KEY") ??
                string.Empty;
        }

        if (string.IsNullOrWhiteSpace(settings.Ai.SummaryModel))
            settings.Ai.SummaryModel = "llama-3.1-8b-instant";

        if (string.IsNullOrWhiteSpace(settings.Ai.VisionModel) ||
            settings.Ai.VisionModel.Contains("90b-vision", StringComparison.OrdinalIgnoreCase))
            settings.Ai.VisionModel = "meta-llama/llama-4-scout-17b-16e-instruct";

        var openRouterKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (!string.IsNullOrWhiteSpace(settings.Ai.ApiKey) &&
            settings.Ai.ApiKey == openRouterKey)
        {
            settings.Ai.BaseUrl = "https://openrouter.ai/api/v1";
            settings.Ai.Model = "openai/gpt-4o-mini";
            settings.Ai.VisionModel = "openai/gpt-4o-mini";
            settings.Ai.SummaryModel = "openai/gpt-4o-mini";
        }

        return settings;
    }

    private void ShowAbout()
    {
        var provider = _aiService?.ActiveProvider ?? "none";
        var root = EnvLoader.FindProjectRoot();
        MessageBox.Show(
            "Netie Space is running in the background.\n\n" +
            "• Select a file in Explorer or Desktop → Space to preview\n" +
            "• Space again closes the preview\n" +
            "• ↓ MD / ↓ PDF — saves next to original file\n" +
            "• Tray → API Keys… for AI summary / chat\n\n" +
            $"AI: {provider}\n" +
            $"Data: {root}\n" +
            $"Log: {AppLogger.LogPath}",
            "Netie Space",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void StartShowTrayWatcher()
    {
        try
        {
            _showTrayEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowTrayEventName);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"ShowTray event: {ex.Message}");
            return;
        }

        _showTrayWatcher = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    if (_showTrayEvent is null) return;
                    _showTrayEvent.WaitOne();
                    Dispatcher.BeginInvoke(async () =>
                    {
                        try
                        {
                            if (IoFile.Exists(PendingPreviewPath))
                            {
                                var pending = IoFile.ReadAllText(PendingPreviewPath).Trim().Trim('"');
                                try { IoFile.Delete(PendingPreviewPath); } catch { /* ignore */ }
                                if (!string.IsNullOrWhiteSpace(pending) && IoFile.Exists(pending))
                                {
                                    AppLogger.Info($"Pending preview from second instance: {pending}");
                                    await OpenPreviewAsync(pending);
                                    return;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"Pending preview: {ex.Message}");
                        }

                        ShowBalloon(
                            "Netie Space is ready",
                            "Already running in the tray — select a file in Explorer or Desktop, then press Space.");
                    });
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"ShowTray watcher: {ex.Message}");
                }
            }
        })
        {
            IsBackground = true,
            Name = "NetieShowTrayWatcher"
        };
        _showTrayWatcher.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("Shutting down");
        try { _showTrayEvent?.Set(); } catch { /* wake watcher */ }
        try { _showTrayEvent?.Dispose(); } catch { /* ignore */ }
        _showTrayEvent = null;
        _keyboardHook?.Dispose();
        _trayIcon?.Dispose();
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch { /* ignore */ }
        }
        try { _mutex?.Dispose(); } catch { /* ignore */ }
        _mutex = null;
        _ownsMutex = false;
        base.OnExit(e);
    }
}

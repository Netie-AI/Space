using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Md.WinRt;

/// <summary>
/// The body of Netie MD: toolbar, outline pane and the WebView2 that shows the rendered page.
/// Built in code so the same class runs in the app window and in the headless snapshot. The page is
/// loaded with NavigateToString (no file URL), pictures come through one virtual host mapped to the
/// document folder, and every other request is answered 403.
/// </summary>
public sealed class MdView : Grid
{
    private static Task<CoreWebView2Environment>? _environment;

    private readonly IMdEngine _engine;
    private readonly WebView2 _web = new();
    private readonly StackPanel _outline = new();
    private readonly TextBlock _title = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _zoomLabel = new();
    private readonly Button _edit = new();
    private readonly Button _print = new();
    private readonly Button _back = new();
    private readonly Button _forward = new();
    private readonly Button _find = new();
    private readonly Button _themeButton = new();
    private readonly Button _zoomOut = new();
    private readonly Button _zoomIn = new();
    private readonly MdHistory _history = new();
    private readonly Dictionary<string, Button> _sectionButtons = new(StringComparer.Ordinal);
    private readonly List<Button> _chromeButtons = new();
    private readonly SolidColorBrush _buttonFill = new(Colors.White);
    private readonly SolidColorBrush _sectionFill = new(Colors.LightGray);
    private readonly string? _settingsPath;
    private MdColorMode _colorMode;
    private string? _currentSection;
    private static string? _highlightJs;
    private static bool _highlightTried;
    private readonly Border _empty = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly MdChangeGate _gate = new(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(2));
    private FileSystemWatcher? _watcher;
    private TaskCompletionSource<bool>? _navigated;
    private bool _awaitStart;
    private ulong _wantedNavigation;
    private string? _mappedFolder;
    private int _openVersion;
    private bool _shutdown;

    /// <param name="mode">A fixed colour mode (headless runs, tests); null reads the remembered choice and remembers the toggle.</param>
    public MdView(IMdEngine engine, ResourceDictionary? theme, MdColorMode? mode = null)
    {
        _engine = engine;
        if (theme is not null)
            Resources.MergedDictionaries.Add(theme);
        if (mode is null)
        {
            _settingsPath = MdSettings.DefaultPath();
            _colorMode = MdSettings.LoadColorMode(_settingsPath);
        }
        else
        {
            _colorMode = mode.Value;
        }
        Build();
        ApplyChrome();
        _timer.Tick += (_, _) => OnTick();
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnDrop;
        InputBindings.Add(new KeyBinding(new MdCommand(() => _ = BackAsync()), Key.Left, ModifierKeys.Alt));
        InputBindings.Add(new KeyBinding(new MdCommand(() => _ = ForwardAsync()), Key.Right, ModifierKeys.Alt));
        InputBindings.Add(new KeyBinding(new MdCommand(() => _ = FindAsync()), Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new MdCommand(OpenClicked), Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new MdCommand(() => Zoom(1)), Key.OemPlus, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new MdCommand(() => Zoom(-1)), Key.OemMinus, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new MdCommand(() => Zoom(0)), Key.D0, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new MdCommand(() => _ = ReloadAsync()), Key.F5, ModifierKeys.None));
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
    }

    /// <summary>Opens the picker; returns the chosen file or null. Replaced in tests, where no dialog may appear.</summary>
    public Func<string?> PickFile { get; set; } = DefaultPick;

    /// <summary>Asks whether an external link may open in the browser. Replaced in tests.</summary>
    public Func<string, bool> ConfirmExternal { get; set; } = DefaultConfirm;

    /// <summary>Starts a process (the browser for an external link, the editor for Edit). Replaced in tests.</summary>
    public Func<ProcessStartInfo, bool> Launcher { get; set; } = DefaultLaunch;

    public MdColorMode ColorMode => _colorMode;
    public string? CurrentSection => _currentSection;
    public bool CanGoBack => _history.CanBack;
    public bool CanGoForward => _history.CanForward;
    public double ZoomFactor => _web.ZoomFactor;
    public string StatusText => _status.Text;
    public bool HighlightLoaded => LoadHighlight() is not null;

    private static string? DefaultPick()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open",
            Filter = "Markdown|*.md;*.markdown;*.mdown;*.mkd|All files|*.*",
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static bool DefaultConfirm(string url) =>
        MessageBox.Show("Open this link in your browser?\n\n" + url, "Netie MD", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
        == MessageBoxResult.Yes;

    private static bool DefaultLaunch(ProcessStartInfo info)
    {
        Process.Start(info);
        return true;
    }

    private sealed class MdCommand : ICommand
    {
        private readonly Action _run;
        public MdCommand(Action run) => _run = run;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _run();
    }

    /// <summary>The bundled highlight.js (BSD-3, shipped for the JSON viewer too), or null when it is not beside the exe.</summary>
    private static string? LoadHighlight()
    {
        if (_highlightTried)
            return _highlightJs;
        _highlightTried = true;
        var candidates = new[]
        {
            IoPath.Combine(AppContext.BaseDirectory, "Assets", "code-viewer", "highlight.min.js"),
            IoPath.Combine(NetieSpace.Helpers.EnvLoader.FindProjectRoot(), "src", "NetieSpace", "Assets", "code-viewer", "highlight.min.js"),
        };
        foreach (var path in candidates)
        {
            try
            {
                if (IoFile.Exists(path))
                    return _highlightJs = IoFile.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLogger.Warn($"[app:md] highlighter not read: {ex.Message}");
            }
        }
        AppLogger.Warn("[app:md] highlight.min.js not found: code blocks are shown without colours");
        return null;
    }

    private MdPageOptions PageOptions() => new(_colorMode, LoadHighlight());

    public MdDocument? Document { get; private set; }

    public string? CurrentPath => Document?.Path;

    /// <summary>Raised with the file name when a document is shown (null: none), so the window can title itself.</summary>
    public event Action<string?>? DocumentChanged;

    public CoreWebView2? Core => _web.CoreWebView2;

    // ---- layout ----

    private void Build()
    {
        Background = MdTheme.Brush(this, "ShellPaper", "#FFFFFF");
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var bar = new DockPanel { Margin = new Thickness(14, 10, 14, 10), LastChildFill = true };
        Configure(_back, "Back", "md-back", "Back to the previous document (Alt+Left)", () => _ = BackAsync());
        Configure(_forward, "Forward", "md-forward", "Forward (Alt+Right)", () => _ = ForwardAsync());
        var open = Action("Open", "md-open", "Open a Markdown file (Ctrl+O)", OpenClicked);
        Configure(_edit, "Edit", "md-edit", "Edit this file in Netie Space", EditClicked);
        Configure(_print, "Print to PDF", "md-print", "Save the document as a PDF", () => _ = PrintClickedAsync());
        Configure(_find, "Find", "md-find", "Find in the document (Ctrl+F)", () => _ = FindAsync());
        Configure(_themeButton, "", "md-theme", "Theme: Auto follows Windows, or choose Light or Dark", ToggleTheme);
        Configure(_zoomOut, "-", "md-zoom-out", "Zoom out (Ctrl+-)", () => Zoom(-1));
        Configure(_zoomIn, "+", "md-zoom-in", "Zoom in (Ctrl++)", () => Zoom(1));
        _back.IsEnabled = _forward.IsEnabled = false;
        _edit.IsEnabled = false;
        _print.IsEnabled = false;
        _find.IsEnabled = false;
        _zoomOut.MinWidth = _zoomIn.MinWidth = 32;
        _zoomLabel.Text = MdZoom.Label(1);
        _zoomLabel.Width = 48;
        _zoomLabel.TextAlignment = TextAlignment.Center;
        _zoomLabel.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(_zoomLabel, "md-zoom-label");
        UpdateThemeButton();

        var left = new WrapPanel { Orientation = Orientation.Horizontal }; // wraps to a second row in a narrow window instead of overlapping
        foreach (var b in new[] { _back, _forward, open, _edit, _print, _find })
        {
            b.Margin = new Thickness(0, 0, 8, 4);
            left.Children.Add(b);
        }
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        _themeButton.Margin = new Thickness(0, 0, 12, 0);
        right.Children.Add(_themeButton);
        right.Children.Add(_zoomOut);
        right.Children.Add(_zoomLabel);
        right.Children.Add(_zoomIn);
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(left);
        bar.Children.Add(right);

        _title.FontWeight = FontWeights.SemiBold;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.Margin = new Thickness(8, 0, 12, 0);
        _status.Foreground = MdTheme.Brush(this, "ShellMuted", "#6E6E6E");
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        AutomationProperties.SetAutomationId(_status, "md-status");
        AutomationProperties.SetAutomationId(_title, "md-title");
        var middle = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_status, Dock.Right);
        middle.Children.Add(_status);
        middle.Children.Add(_title);
        bar.Children.Add(middle);

        var barBorder = new Border
        {
            Child = bar,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = MdTheme.Brush(this, "ShellLine", "#D6D6D6"),
        };
        SetRow(barBorder, 0);
        Children.Add(barBorder);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var side = new Border
        {
            Background = MdTheme.Brush(this, "ShellSidebar", "#F4F5F7"),
            BorderBrush = MdTheme.Brush(this, "ShellLine", "#D6D6D6"),
            BorderThickness = new Thickness(0, 0, 1, 0),
        };
        var sideDock = new DockPanel { Margin = new Thickness(10, 14, 10, 10) };
        var head = new TextBlock { Text = "Outline", FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 0, 8) };
        DockPanel.SetDock(head, Dock.Top);
        sideDock.Children.Add(head);
        sideDock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _outline });
        side.Child = sideDock;
        SetColumn(side, 0);
        body.Children.Add(side);

        _web.Visibility = Visibility.Collapsed;
        _web.AllowExternalDrop = false;
        SetColumn(_web, 1);
        body.Children.Add(_web);

        var emptyText = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        emptyText.Children.Add(new TextBlock { Text = "Netie MD", FontSize = 24, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        emptyText.Children.Add(new TextBlock
        {
            Text = "Drop a Markdown file here, or open one.",
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = MdTheme.Brush(this, "ShellMuted", "#6E6E6E"),
        });
        var openBig = Action("Open a Markdown file", "md-open-big", "Open a Markdown file (Ctrl+O)", OpenClicked);
        openBig.Margin = new Thickness(0, 14, 0, 0);
        emptyText.Children.Add(openBig);
        _empty.Child = emptyText;
        SetColumn(_empty, 1);
        body.Children.Add(_empty);

        SetRow(body, 1);
        Children.Add(body);
    }

    private Button Action(string label, string id, string tip, Action click)
    {
        var button = new Button();
        Configure(button, label, id, tip, click);
        return button;
    }

    private void Configure(Button button, string label, string id, string tip, Action click)
    {
        button.Content = label;
        button.ToolTip = tip;
        if (TryFindResource("ShellButton") is Style style)
            button.Style = style;
        button.Background = _buttonFill;
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, label.Length == 0 ? id : label);
        button.Click += (_, _) => click();
        _chromeButtons.Add(button);
    }

    // ---- colours ----

    private static readonly (string Key, string Light, string Dark)[] ChromeColors =
    {
        ("ShellPaper", "#FFFFFF", "#16181C"),
        ("ShellSidebar", "#F4F5F7", "#1F2227"),
        ("ShellInk", "#2C2C2C", "#D9DCE1"),
        ("ShellMuted", "#6E6E6E", "#9AA0AA"),
        ("ShellLine", "#D6D6D6", "#3A3F47"),
        ("ShellHover", "#EEEEEE", "#2A2E35"),
        ("ShellPress", "#E2E2E2", "#343942"),
    };

    private static bool SystemIsDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Paints the toolbar and outline for the current mode by recolouring the theme brushes this view owns.</summary>
    private void ApplyChrome()
    {
        var dark = MdColorModes.IsDark(_colorMode, SystemIsDark());
        foreach (var (key, light, darkHex) in ChromeColors)
        {
            var color = (Color)ColorConverter.ConvertFromString(dark ? darkHex : light);
            if (TryFindResource(key) is SolidColorBrush { IsFrozen: false } brush)
                brush.Color = color;
        }
        _buttonFill.Color = (Color)ColorConverter.ConvertFromString(dark ? "#23272D" : "#FFFFFF");
        _sectionFill.Color = (Color)ColorConverter.ConvertFromString(dark ? "#2E3846" : "#EBEEF2");
    }

    private void OnSystemPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (_colorMode == MdColorMode.System && !_shutdown)
            Dispatcher.BeginInvoke(ApplyChrome);
    }

    private void UpdateThemeButton() => _themeButton.Content = "Theme: " + MdColorModes.Label(_colorMode);

    /// <summary>Cycles Auto, Light, Dark; the page changes at once, without a reload.</summary>
    public void ToggleTheme() => SetColorMode(MdColorModes.Next(_colorMode));

    public void SetColorMode(MdColorMode mode)
    {
        _colorMode = mode;
        if (_settingsPath is not null && !MdSettings.SaveColorMode(_settingsPath, mode))
            AppLogger.Warn("[app:md] colour choice not saved");
        ApplyChrome();
        UpdateThemeButton();
        if (_web.CoreWebView2 is { } core)
        {
            var attr = MdColorModes.Attribute(mode);
            var script = attr is null
                ? "document.documentElement.removeAttribute('data-theme')"
                : "document.documentElement.setAttribute('data-theme','" + attr + "')";
            _ = core.ExecuteScriptAsync(script);
        }
    }

    // ---- web view ----

    private static Task<CoreWebView2Environment> Environment() => _environment ??= CreateEnvironmentAsync();

    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        var folder = IoPath.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "NetieSpace", "apps", "md", "webview2");
        IoDirectory.CreateDirectory(folder);
        return await CoreWebView2Environment.CreateAsync(userDataFolder: folder);
    }

    private async Task<CoreWebView2> EnsureWebAsync()
    {
        if (_web.CoreWebView2 is not null)
            return _web.CoreWebView2;

        var env = await Environment();
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsPinchZoomEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;

        // Nothing but the page itself and the document's pictures is ever requested.
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (!MdShell.IsAllowedRequest(e.Request.Uri))
            {
                AppLogger.Warn($"[app:md] blocked request {e.Request.Method} {Truncate(e.Request.Uri)}");
                e.Response = env.CreateWebResourceResponse(null, 403, "Blocked", "");
            }
        };
        core.NavigationStarting += (_, e) =>
        {
            if (!MdShell.IsAllowedRequest(e.Uri))
            {
                e.Cancel = true;
                return;
            }
            if (_awaitStart)
            {
                _awaitStart = false;
                _wantedNavigation = e.NavigationId;
            }
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.NavigationCompleted += (_, e) =>
        {
            // Only the navigation this open started counts; a superseded one finishing late is ignored.
            if (e.NavigationId == _wantedNavigation)
                _navigated?.TrySetResult(e.IsSuccess);
        };
        core.WebMessageReceived += (_, e) => OnWebMessage(e.TryGetWebMessageAsString());
        _web.ZoomFactorChanged += (_, _) => _zoomLabel.Text = MdZoom.Label(_web.ZoomFactor);
        return core;
    }

    private static string Truncate(string text) => text.Length <= 120 ? text : text[..120];

    private void MapFolder(CoreWebView2 core, string folder)
    {
        if (string.Equals(_mappedFolder, folder, StringComparison.OrdinalIgnoreCase))
            return;
        core.ClearVirtualHostNameToFolderMapping(MdPictures.Host);
        core.SetVirtualHostNameToFolderMapping(MdPictures.Host, folder, CoreWebView2HostResourceAccessKind.DenyCors);
        _mappedFolder = folder;
    }

    // ---- opening ----

    /// <summary>
    /// Reads, renders and shows <paramref name="path"/> as a new stop of the history (Open, a dropped file, a second launch).
    /// Never throws; a failure is shown in the page and the status.
    /// </summary>
    public Task OpenAsync(string path, string? fragment = null, bool keepScroll = false) =>
        OpenCoreAsync(path, fragment, keepScroll, record: !keepScroll, restoreScroll: null);

    private async Task OpenCoreAsync(string path, string? fragment, bool keepScroll, bool record, double? restoreScroll)
    {
        var version = ++_openVersion;
        try
        {
            var core = await EnsureWebAsync();
            var options = PageOptions();
            var doc = await Task.Run(() => MdDocument.Load(path, _engine, options: options));
            if (version != _openVersion || _shutdown)
                return;

            var scroll = "0";
            var sameFile = Document is not null && string.Equals(Document.Path, doc.Path, StringComparison.OrdinalIgnoreCase);
            if (Document is not null && (keepScroll && sameFile || record && !sameFile))
            {
                var y = await ScrollYAsync(core);
                if (keepScroll && sameFile)
                    scroll = y.ToString(CultureInfo.InvariantCulture);
                else
                    _history.SaveScroll(y);
            }

            MapFolder(core, doc.Folder);
            _navigated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _awaitStart = true;
            core.NavigateToString(doc.Page);
            var done = await Task.WhenAny(_navigated.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            if (version != _openVersion || _shutdown)
                return;
            if (done != _navigated.Task || !_navigated.Task.Result)
                doc = doc with { Error = doc.Error ?? "Page did not load" };

            Document = doc;
            if (record)
                _history.Visit(doc.Path, fragment ?? "");
            _web.Visibility = Visibility.Visible;
            _empty.Visibility = Visibility.Collapsed;
            _title.Text = doc.Title;
            _status.Text = doc.Error ?? "";
            _edit.IsEnabled = doc.Error is null;
            _print.IsEnabled = doc.Error is null;
            _find.IsEnabled = doc.Error is null;
            FillOutline(doc.Outline);
            UpdateHistoryButtons();
            Watch(doc.Path);
            DocumentChanged?.Invoke(doc.Title);

            if (scroll != "0")
                await core.ExecuteScriptAsync("window.scrollTo(0," + scroll + ")");
            else if (restoreScroll is > 0)
                await core.ExecuteScriptAsync("window.scrollTo(0," + ((long)restoreScroll.Value).ToString(CultureInfo.InvariantCulture) + ")");
            else if (!string.IsNullOrEmpty(fragment))
                await ScrollToAsync(Uri.UnescapeDataString(fragment));
            await WaitImagesAsync(core);

            if (doc.Unstable)
                _gate.Note(DateTime.UtcNow); // still being written: read again once it settles
        }
        catch (Exception ex) when (!_shutdown)
        {
            AppLogger.Error(ex, "[app:md] open");
            _status.Text = "Could not open the file";
        }
    }

    private static async Task<long> ScrollYAsync(CoreWebView2 core)
    {
        var raw = await core.ExecuteScriptAsync("Math.round(window.scrollY)");
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && y >= 0 && y < 1e9 ? (long)y : 0;
    }

    private void UpdateHistoryButtons()
    {
        _back.IsEnabled = _history.CanBack;
        _forward.IsEnabled = _history.CanForward;
    }

    /// <summary>Back to the previous document, at the place it was left. False when there is none.</summary>
    public async Task<bool> BackAsync()
    {
        if (!_history.CanBack || _web.CoreWebView2 is null)
            return false;
        await SaveScrollAsync();
        var place = _history.GoBack();
        if (place is null)
            return false;
        await OpenCoreAsync(place.Path, place.Fragment, keepScroll: false, record: false, restoreScroll: place.ScrollY);
        return true;
    }

    public async Task<bool> ForwardAsync()
    {
        if (!_history.CanForward || _web.CoreWebView2 is null)
            return false;
        await SaveScrollAsync();
        var place = _history.GoForward();
        if (place is null)
            return false;
        await OpenCoreAsync(place.Path, place.Fragment, keepScroll: false, record: false, restoreScroll: place.ScrollY);
        return true;
    }

    private async Task SaveScrollAsync()
    {
        if (_web.CoreWebView2 is { } core && Document is not null)
            _history.SaveScroll(await ScrollYAsync(core));
    }

    /// <summary>Reads the file again now, keeping the scroll position.</summary>
    public Task ReloadAsync() => Document is null ? Task.CompletedTask : OpenAsync(Document.Path, keepScroll: true);

    /// <summary>Opens the find bar of the page (Ctrl+F does the same from inside it).</summary>
    public async Task FindAsync()
    {
        if (_web.CoreWebView2 is { } core && Document?.Error is null)
        {
            await core.ExecuteScriptAsync("window.netieMd&&window.netieMd.find()");
            _web.Focus();
        }
    }

    /// <summary>Runs a script in the page and returns its JSON result. For the self-check driver.</summary>
    public async Task<string> EvalAsync(string script)
    {
        var core = _web.CoreWebView2 ?? throw new InvalidOperationException("web view not ready");
        return await core.ExecuteScriptAsync(script);
    }

    /// <summary>True when the document is shown and its pictures have finished loading (or failed).</summary>
    public async Task<bool> WaitRenderedAsync()
    {
        var core = _web.CoreWebView2;
        if (core is null || Document is null || Document.Error is not null)
            return false;
        await WaitImagesAsync(core);
        await Task.Delay(200); // one paint after the last picture
        return true;
    }

    private static async Task WaitImagesAsync(CoreWebView2 core)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < until)
        {
            var done = await core.ExecuteScriptAsync("Array.prototype.every.call(document.images,function(i){return i.complete})");
            if (done == "true")
                return;
            await Task.Delay(60);
        }
    }

    private Task<string> ScrollToAsync(string id) =>
        _web.CoreWebView2.ExecuteScriptAsync(
            "(function(){var e=document.getElementById(" + JsonSerializer.Serialize(id) + ");if(e)e.scrollIntoView()})()");

    private void FillOutline(IReadOnlyList<MdHeading> outline)
    {
        _outline.Children.Clear();
        _sectionButtons.Clear();
        _currentSection = null;
        if (outline.Count == 0)
        {
            _outline.Children.Add(new TextBlock
            {
                Text = "No headings",
                Margin = new Thickness(6, 0, 0, 0),
                Foreground = MdTheme.Brush(this, "ShellMuted", "#6E6E6E"),
            });
            return;
        }
        foreach (var h in outline)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = h.Title, TextTrimming = TextTrimming.CharacterEllipsis },
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(6 + 14 * h.Depth, 4, 6, 4),
                MinHeight = 26,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                ToolTip = h.Title,
                FontWeight = h.Level == 1 ? FontWeights.SemiBold : FontWeights.Normal,
                Tag = h.Level,
            };
            if (TryFindResource("ShellNavItem") is Style style)
                button.Style = style;
            AutomationProperties.SetAutomationId(button, "md-outline-" + h.Id);
            AutomationProperties.SetName(button, h.Title);
            var id = h.Id;
            _sectionButtons[id] = button;
            button.Click += async (_, _) =>
            {
                try
                {
                    await ScrollToAsync(id);
                    MarkSection(id);
                }
                catch (Exception ex) { AppLogger.Warn($"[app:md] scroll failed: {ex.Message}"); }
            };
            _outline.Children.Add(button);
        }
    }

    /// <summary>The outline entry of the section the reader is in is highlighted and kept in view.</summary>
    private void MarkSection(string id)
    {
        if (_currentSection is not null && _sectionButtons.TryGetValue(_currentSection, out var old))
        {
            old.Background = Brushes.Transparent;
            old.FontWeight = old.Tag is int level && level == 1 ? FontWeights.SemiBold : FontWeights.Normal;
        }
        _currentSection = id.Length == 0 ? null : id;
        if (_currentSection is not null && _sectionButtons.TryGetValue(_currentSection, out var now))
        {
            now.Background = _sectionFill;
            now.FontWeight = FontWeights.SemiBold;
            now.BringIntoView();
        }
    }

    // ---- messages from the page ----

    private void OnWebMessage(string? message)
    {
        if (message is null || Document is null)
            return;
        var colon = message.IndexOf(':');
        if (colon <= 0)
            return;
        var kind = message[..colon];
        var value = message[(colon + 1)..];
        switch (kind)
        {
            case "link":
                OnLink(value);
                break;
            case "section":
                MarkSection(value);
                break;
            case "copy":
                CopyToClipboard(value);
                break;
            case "nav":
                _ = value == "back" ? BackAsync() : value == "forward" ? ForwardAsync() : Task.CompletedTask;
                break;
            case "zoom":
                Zoom(value == "in" ? 1 : value == "out" ? -1 : 0);
                break;
            case "ui":
                if (value == "open") OpenClicked();
                else if (value == "print") _ = PrintClickedAsync();
                else if (value == "reload") _ = ReloadAsync();
                break;
        }
    }

    private void OnLink(string href)
    {
        var link = MdLinks.Classify(href, Document!.Folder);
        switch (link.Kind)
        {
            case MdLinkKind.External:
                // A document must not open the browser behind the reader's back: ask, showing the address.
                if (!ConfirmExternal(link.Target))
                {
                    _status.Text = "Link not opened";
                    break;
                }
                try
                {
                    Launcher(new ProcessStartInfo(link.Target) { UseShellExecute = true });
                    _status.Text = "Opened in your browser";
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"[app:md] link not opened: {ex.Message}");
                    _status.Text = "Could not open the link";
                }
                break;
            case MdLinkKind.Markdown:
                if (string.Equals(link.Target, Document.Path, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(link.Fragment))
                        _ = ScrollToAsync(Uri.UnescapeDataString(link.Fragment));
                }
                else
                {
                    _ = OpenAsync(link.Target, link.Fragment);
                }
                break;
        }
    }

    private void CopyToClipboard(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                _status.Text = "Copied";
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                System.Threading.Thread.Sleep(40); // another program holds the clipboard for a moment
            }
        }
        AppLogger.Warn("[app:md] clipboard busy");
        _status.Text = "Could not copy";
    }

    // ---- toolbar ----

    private void OpenClicked()
    {
        var picked = PickFile();
        if (!string.IsNullOrEmpty(picked))
            _ = OpenAsync(picked);
    }

    /// <summary>The existing Space editor opens the file; Netie MD edits nothing itself.</summary>
    private void EditClicked()
    {
        if (Document is null || System.Environment.ProcessPath is not { } exe)
            return;
        try
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false };
            info.ArgumentList.Add("--edit");
            info.ArgumentList.Add(Document.Path);
            Launcher(info);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[app:md] edit not started: {ex.Message}");
            _status.Text = "Could not start the editor";
        }
    }

    private async Task PrintClickedAsync()
    {
        if (Document is null || _web.CoreWebView2 is null)
            return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Print to PDF",
            Filter = "PDF|*.pdf",
            FileName = IoPath.GetFileNameWithoutExtension(Document.Path) + ".pdf",
            InitialDirectory = Document.Folder,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog() != true)
            return;
        _status.Text = await PrintPdfAsync(dialog.FileName) ? "Saved " + IoPath.GetFileName(dialog.FileName) : "Print to PDF failed";
    }

    /// <summary>Writes the shown document to a PDF through WebView2.</summary>
    public async Task<bool> PrintPdfAsync(string path)
    {
        var core = _web.CoreWebView2;
        if (core is null || Document is null)
            return false;
        try
        {
            var settings = core.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            settings.MarginTop = settings.MarginBottom = 0.6;
            settings.MarginLeft = settings.MarginRight = 0.6;
            return await core.PrintToPdfAsync(path, settings);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[app:md] print failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Saves what the web view shows as a PNG.</summary>
    public async Task CapturePngAsync(string path)
    {
        var core = _web.CoreWebView2 ?? throw new InvalidOperationException("web view not ready");
        using var memory = new MemoryStream();
        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, memory);
        var dir = IoPath.GetDirectoryName(IoPath.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            IoDirectory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(path, memory.ToArray());
    }

    /// <summary>+1 and -1 step the zoom, 0 resets it to 100%.</summary>
    public void Zoom(int direction)
    {
        if (_web.CoreWebView2 is null)
            return;
        _web.ZoomFactor = direction == 0 ? 1.0 : MdZoom.Step(_web.ZoomFactor, direction);
        _zoomLabel.Text = MdZoom.Label(_web.ZoomFactor);
    }

    /// <summary>Whether a toolbar button is enabled; null when there is no such button.</summary>
    public bool? ButtonEnabled(string automationId) => FindButton(this, automationId)?.IsEnabled;

    /// <summary>The same as pressing a toolbar button with Windows UI Automation (what a screen reader or a test tool does).</summary>
    public bool InvokeButton(string automationId)
    {
        var button = FindButton(this, automationId);
        if (button is null || !button.IsEnabled)
            return false;
        (UIElementAutomationPeer.CreatePeerForElement(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider)?.Invoke();
        return true;
    }

    private static Button? FindButton(DependencyObject root, string automationId)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button b && AutomationProperties.GetAutomationId(b) == automationId)
                return b;
            if (FindButton(child, automationId) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>Saves the whole window (toolbar, outline and page) as a PNG: the chrome is drawn by WPF, the page is the web view's capture.</summary>
    public async Task CaptureWindowPngAsync(string path)
    {
        var core = _web.CoreWebView2 ?? throw new InvalidOperationException("web view not ready");
        using var memory = new MemoryStream();
        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, memory);
        memory.Position = 0;
        var page = new System.Windows.Media.Imaging.BitmapImage();
        page.BeginInit();
        page.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        page.StreamSource = memory;
        page.EndInit();
        page.Freeze();

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var chrome = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        chrome.Render(this);
        var origin = _web.TranslatePoint(new Point(0, 0), this);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(chrome, new Rect(0, 0, ActualWidth, ActualHeight));
            dc.DrawImage(page, new Rect(origin.X, origin.Y, _web.ActualWidth, _web.ActualHeight));
        }
        var all = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        all.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(all));
        var dir = IoPath.GetDirectoryName(IoPath.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            IoDirectory.CreateDirectory(dir);
        await using var file = File.Create(path);
        encoder.Save(file);
    }

    // ---- drag and drop ----

    private static string? DroppedMarkdown(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] files
            ? files.FirstOrDefault(MdLinks.IsMarkdownFile)
            : null;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedMarkdown(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        OpenDropped(e.Data.GetData(DataFormats.FileDrop) as string[]);
    }

    /// <summary>What a drop does: the first Markdown file is opened, anything else says so in the status.</summary>
    public void OpenDropped(string[]? files)
    {
        var file = files?.FirstOrDefault(MdLinks.IsMarkdownFile);
        if (file is null)
        {
            _status.Text = "Not a Markdown file";
            return;
        }
        _ = OpenAsync(file);
    }

    // ---- reload on change ----

    private void Watch(string path)
    {
        if (_watcher is not null && string.Equals(_watchedPath, path, StringComparison.OrdinalIgnoreCase))
            return;
        _watcher?.Dispose();
        _watcher = null;
        _watchedPath = path;
        try
        {
            var name = IoPath.GetFileName(path);
            var watcher = new FileSystemWatcher(IoPath.GetDirectoryName(path)!)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            void Touch(string? changed)
            {
                if (string.Equals(changed, name, StringComparison.OrdinalIgnoreCase))
                    Dispatcher.BeginInvoke(() => _gate.Note(DateTime.UtcNow));
            }
            watcher.Changed += (_, e) => Touch(e.Name);
            watcher.Created += (_, e) => Touch(e.Name);
            watcher.Deleted += (_, e) => Touch(e.Name);
            watcher.Renamed += (_, e) => { Touch(e.Name); Touch(e.OldName); };
            _watcher = watcher;
            _timer.Start();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[app:md] watch failed: {ex.Message}");
        }
    }

    private string? _watchedPath;

    private void OnTick()
    {
        if (!_gate.TryTake(DateTime.UtcNow) || Document is null || _shutdown)
            return;
        var now = MdFileStamp.Of(Document.Path);
        if (now is null)
        {
            _status.Text = "File not found";
            return;
        }
        if (now.Equals(Document.Stamp) && !Document.Unstable)
            return;
        _ = OpenAsync(Document.Path, keepScroll: true);
    }

    /// <summary>Stops the watcher and the timer and releases the web view. Call when the window closes.</summary>
    public void Shutdown()
    {
        _shutdown = true;
        _timer.Stop();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        _watcher?.Dispose();
        _watcher = null;
        try { _web.Dispose(); } catch { /* already gone */ }
    }
}

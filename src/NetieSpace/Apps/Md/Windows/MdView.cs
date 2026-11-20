using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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

    public MdView(IMdEngine engine, ResourceDictionary? theme)
    {
        _engine = engine;
        if (theme is not null)
            Resources.MergedDictionaries.Add(theme);
        Build();
        _timer.Tick += (_, _) => OnTick();
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

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
        var open = Action("Open", OpenClicked);
        _edit.Content = "Edit";
        _edit.Click += (_, _) => EditClicked();
        _print.Content = "Print to PDF";
        _print.Click += async (_, _) => await PrintClickedAsync();
        _edit.IsEnabled = false;
        _print.IsEnabled = false;
        var minus = Action("−", () => Zoom(-1));
        var plus = Action("+", () => Zoom(1));
        minus.MinWidth = plus.MinWidth = 32;
        _zoomLabel.Text = MdZoom.Label(1);
        _zoomLabel.Width = 48;
        _zoomLabel.TextAlignment = TextAlignment.Center;
        _zoomLabel.VerticalAlignment = VerticalAlignment.Center;

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var b in new[] { open, _edit, _print })
        {
            b.Margin = new Thickness(0, 0, 8, 0);
            left.Children.Add(b);
        }
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(minus);
        right.Children.Add(_zoomLabel);
        right.Children.Add(plus);
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
        var openBig = Action("Open a Markdown file", OpenClicked);
        openBig.Margin = new Thickness(0, 14, 0, 0);
        emptyText.Children.Add(openBig);
        _empty.Child = emptyText;
        SetColumn(_empty, 1);
        body.Children.Add(_empty);

        SetRow(body, 1);
        Children.Add(body);
    }

    private Button Action(string label, Action click)
    {
        var button = new Button { Content = label };
        if (TryFindResource("ShellButton") is Style style)
            button.Style = style;
        button.Click += (_, _) => click();
        return button;
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

    /// <summary>Reads, renders and shows <paramref name="path"/>. Never throws; a failure is shown in the page and the status.</summary>
    public async Task OpenAsync(string path, string? fragment = null, bool keepScroll = false)
    {
        var version = ++_openVersion;
        try
        {
            var core = await EnsureWebAsync();
            var doc = await Task.Run(() => MdDocument.Load(path, _engine));
            if (version != _openVersion || _shutdown)
                return;

            var scroll = "0";
            if (keepScroll && Document is not null && Document.Path == doc.Path)
            {
                var raw = await core.ExecuteScriptAsync("Math.round(window.scrollY)");
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && y >= 0 && y < 1e9)
                    scroll = ((long)y).ToString(CultureInfo.InvariantCulture);
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
            _web.Visibility = Visibility.Visible;
            _empty.Visibility = Visibility.Collapsed;
            _title.Text = doc.Title;
            _status.Text = doc.Error ?? "";
            _edit.IsEnabled = doc.Error is null;
            _print.IsEnabled = doc.Error is null;
            FillOutline(doc.Outline);
            Watch(doc.Path);
            DocumentChanged?.Invoke(doc.Title);

            if (scroll != "0")
                await core.ExecuteScriptAsync("window.scrollTo(0," + scroll + ")");
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
                Content = new TextBlock { Text = h.Title, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = h.Level == 1 ? FontWeights.SemiBold : FontWeights.Normal },
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(6 + 14 * h.Depth, 4, 6, 4),
                MinHeight = 26,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                ToolTip = h.Title,
            };
            if (TryFindResource("ShellNavItem") is Style style)
                button.Style = style;
            var id = h.Id;
            button.Click += async (_, _) =>
            {
                try { await ScrollToAsync(id); }
                catch (Exception ex) { AppLogger.Warn($"[app:md] scroll failed: {ex.Message}"); }
            };
            _outline.Children.Add(button);
        }
    }

    // ---- links from the page ----

    private void OnWebMessage(string? message)
    {
        if (message is null || !message.StartsWith("link:", StringComparison.Ordinal) || Document is null)
            return;
        var link = MdLinks.Classify(message[5..], Document.Folder);
        switch (link.Kind)
        {
            case MdLinkKind.External:
                try { Process.Start(new ProcessStartInfo(link.Target) { UseShellExecute = true }); }
                catch (Exception ex) { AppLogger.Warn($"[app:md] link not opened: {ex.Message}"); }
                break;
            case MdLinkKind.Markdown:
                _ = OpenAsync(link.Target, link.Fragment);
                break;
        }
    }

    // ---- toolbar ----

    private void OpenClicked()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open",
            Filter = "Markdown|*.md;*.markdown;*.mdown;*.mkd|All files|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true)
            _ = OpenAsync(dialog.FileName);
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
            Process.Start(info);
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
            return await core.PrintToPdfAsync(path);
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

    private void Zoom(int direction)
    {
        if (_web.CoreWebView2 is null)
            return;
        _web.ZoomFactor = MdZoom.Step(_web.ZoomFactor, direction);
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
        var file = DroppedMarkdown(e);
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
        _watcher?.Dispose();
        _watcher = null;
        try { _web.Dispose(); } catch { /* already gone */ }
    }
}

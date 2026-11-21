using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NetieSpace.Apps.Tools;
using NetieSpace.Apps.Video;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell.Panels.Video;

/// <summary>A text box for one time: shows mm:ss.f, takes seconds or clock text, and says what it could not read.</summary>
internal sealed class TimeBox : TextBox
{
    private readonly Func<double> _get;
    private readonly Action<double> _set;
    private readonly Action<string> _say;

    public TimeBox(string name, Func<double> get, Action<double> set, Action<string> say)
    {
        _get = get;
        _set = set;
        _say = say;
        Width = 96;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetName(this, name);
        ToolTip = "mm:ss.f, or seconds";
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Sync(); e.Handled = true; }
        };
        LostFocus += (_, _) => Commit();
        Sync();
    }

    public void Sync() => Text = FlowTime.Format(_get());

    private void Commit()
    {
        if (FlowTime.TryParse(Text, out var t))
        {
            _say("");
            _set(t);
        }
        else
        {
            _say("That is not a time. Use mm:ss.f, for example 01:05.5, or plain seconds.");
        }
        Sync();
    }
}

/// <summary>
/// The quick edit that replaces the time boxes of the video tools (W6-4): a picture of the clip, the filmstrip with
/// its handles, the sections, the caption, the export presets and the size estimate. It owns a <see cref="FlowState"/>;
/// the page's own Export button runs <see cref="VideoFlowSession"/> with it.
/// </summary>
internal sealed class VideoFlowPanel : StackPanel
{
    private const int StripCount = 10;
    private const int StripHeight = 128;

    private readonly string _toolId;
    private readonly string _file;
    private readonly bool _live;
    private readonly CancellationTokenSource _cts = new();
    private TextBlock _status = new();
    private readonly StackPanel _body = new();
    private TextBox? _captionBox;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly DispatcherTimer _posterDebounce = new() { Interval = TimeSpan.FromMilliseconds(220) };

    private FlowState? _state;
    private FlowSource? _src;
    private FilmstripControl? _strip;
    private System.Windows.Controls.Image? _poster;
    private MediaElement? _media;
    private bool _mediaShowing;
    private bool _playing;
    private Button? _playButton;
    private TextBlock? _selectionLine, _timeLine, _estimate, _hint;
    private TimeBox? _inBox, _outBox, _muteFrom, _muteTo, _speedFrom, _speedTo;
    private CheckBox? _muteCheck, _speedCheck;
    private StackPanel? _muteRow, _speedRow;
    private ComboBox? _speedCombo;
    private readonly List<(FlowPreset Preset, Border Card, TextBlock Size)> _cards = new();
    private int _posterSeq;
    private int _pending;
    private double _posterAt = -1;

    public VideoFlowPanel(string toolId, string file, ToolAvailability availability, bool live)
    {
        _toolId = toolId;
        _file = file;
        _live = live;
        Availability = availability;
        Margin = new Thickness(0, 24, 0, 0);
        _tick.Tick += (_, _) => PlayTick();
        _posterDebounce.Tick += (_, _) =>
        {
            _posterDebounce.Stop();
            if (_state is not null) _ = ShowPosterAsync(_state.Playhead);
        };
        Unloaded += (_, _) => StopPlayback();
    }

    public ToolAvailability Availability { get; }
    public string ToolId => _toolId;
    public string File => _file;
    public FlowState? State => _state;
    public FlowSource? Source => _src;

    /// <summary>The clip was read, the strip is drawn. False after <see cref="Failed"/>.</summary>
    public bool Ready { get; private set; }  // set on the UI thread, read by the snapshot pump on the same thread
    public bool Failed { get; private set; }
    public string? FailMessage { get; private set; }

    /// <summary>No picture is still being cut: safe to take a snapshot.</summary>
    public bool Settled => _pending == 0 && !_posterDebounce.IsEnabled;

    /// <summary>The place the preset cards start; the snapshot scrolls here.</summary>
    public FrameworkElement? PresetsAnchor { get; private set; }
    public FrameworkElement? StripAnchor => _strip;

    /// <summary>Raised with false when the clip could not be read, so the page can show its own form again.</summary>
    public event Action<bool>? ActiveChanged;

    public bool Matches(string toolId, string file) =>
        string.Equals(_toolId, toolId, StringComparison.Ordinal) && string.Equals(_file, file, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _cts.Cancel();
        StopPlayback();
        _posterDebounce.Stop();
        if (_media is not null)
        {
            try { _media.Close(); } catch { /* the pipeline may already be gone */ }
            _media = null;
        }
    }

    private TextBlock Heading(string text)
    {
        var h = new TextBlock { Text = text, Style = (Style)FindResource("ShellSection") };
        return h;
    }

    // ---------------------------------------------------------------- loading

    /// <summary>Resources are looked up through the page, so the panel must already be in it.</summary>
    private void Init()
    {
        _status = new TextBlock { Text = "Reading the clip...", Style = S("ShellMutedText"), Margin = new Thickness(0, 8, 0, 0) };
        Children.Add(Heading("Edit"));
        Children.Add(_status);
        Children.Add(_body);
    }

    /// <summary>After an await the code may be on a pool thread (no dispatcher context at startup): all screen work goes through here.</summary>
    private void OnUi(Action a)
    {
        if (Dispatcher.CheckAccess()) a();
        else Dispatcher.Invoke(a);
    }

    public async void Load()
    {
        try
        {
            Init();
            var ct = _cts.Token;
            var (info, strip) = await Task.Run(async () =>
            {
                var i = VideoFfmpeg.RequireVideo(_file);
                var st = await VideoFilmstrip.BuildAsync(_file, StripCount, StripHeight, ct, null, i).ConfigureAwait(false);
                return (i, st);
            }, ct);
            if (ct.IsCancellationRequested) return;
            OnUi(() =>
            {
                var frames = strip.Frames.Select(f => (ImageSource?)LoadImage(f)).ToList();
                var bytes = new System.IO.FileInfo(_file).Length;
                _src = new FlowSource(info.Width, info.Height, info.Duration, bytes, info.Fps, info.HasAudio);
                _state = FlowPages.Start(_toolId, info.Duration, info.HasAudio);
                _state.SetPlayhead(_state.In);
                BuildBody(info, frames);
                _status.Visibility = Visibility.Collapsed;
                // A picture of the clip at once: the thumbnail nearest the start, sharpened by the poster below.
                if (frames.Count > 0 && frames[0] is { } first && _poster is not null) _poster.Source = first;
            });
            await ShowPosterAsync(_state!.Playhead);
            OnUi(() => Ready = true);
            AppLogger.Info($"[video] flow ready file='{IoPath.GetFileName(_file)}' tool={_toolId} frames={strip.Frames.Count} cached={strip.FromCache} dur={info.Duration:0.0}");
        }
        catch (OperationCanceledException)
        {
            // the page moved to another file
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[video] flow could not read '{_file}': {ex.Message}");
            OnUi(() =>
            {
                Failed = true;
                FailMessage = ex.Message;
                _status.Text = "This video could not be read for editing (" + FirstLine(ex.Message) + "). The plain options are shown instead.";
                _status.Foreground = (Brush)FindResource("ShellDanger");
                ActiveChanged?.Invoke(false);
            });
        }
    }

    private static string FirstLine(string s)
    {
        var line = s.Split('\n')[0].Trim();
        return line.Length > 160 ? line[..160] + "..." : line;
    }

    internal static BitmapImage? LoadImage(string path)
    {
        try
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            b.UriSource = new Uri(path);
            b.EndInit();
            b.Freeze();
            return b;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[video] picture not loaded ({path}): {ex.Message}");
            return null;
        }
    }

    // ---------------------------------------------------------------- layout

    private Style S(string key) => (Style)FindResource(key);
    private Brush Br(string key) => (Brush)FindResource(key);

    private void BuildBody(VideoInfo info, IReadOnlyList<ImageSource?> frames)
    {
        var s = _state!;
        _body.Children.Clear();

        var infoLine = $"{FlowTime.Format(info.Duration)} - {info.Width}x{info.Height} - {ToolPageModel.FormatSize(_src!.Bytes)} - "
            + (info.HasAudio ? "with sound" : "no sound");
        _body.Children.Add(new TextBlock { Text = infoLine, Style = S("ShellMutedText"), Margin = new Thickness(0, 2, 0, 10) });

        // Picture: the poster frame first, the player on top once it really has a picture.
        var box = new Grid { Height = 250, Background = Brushes.Black };
        _poster = new System.Windows.Controls.Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(_poster, BitmapScalingMode.HighQuality);
        box.Children.Add(_poster);
        if (_live) AddPlayer(box);
        var frame = new Border { CornerRadius = (CornerRadius)FindResource("ShellRadiusControl"), ClipToBounds = true, Child = box };
        AutomationProperties.SetName(frame, "Preview");
        _body.Children.Add(frame);

        // Transport line.
        _playButton = new Button { Content = "Play selection", MinHeight = 30, Padding = new Thickness(14, 4, 14, 4), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(_playButton, "Play selection");
        _playButton.Click += (_, _) => TogglePlay();
        _timeLine = new TextBlock { Style = S("ShellMutedText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        var transport = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 10) };
        transport.Children.Add(_playButton);
        transport.Children.Add(_timeLine);
        _body.Children.Add(transport);

        // Filmstrip.
        _selectionLine = new TextBlock { Style = S("ShellMutedText"), Margin = new Thickness(0, 0, 0, 6) };
        _body.Children.Add(_selectionLine);
        _strip = new FilmstripControl();
        _strip.Bind(s, frames);
        _strip.Edited += OnStripEdited;
        _body.Children.Add(_strip);

        // In / Out.
        _hint = new TextBlock { Style = S("ShellCaption"), Foreground = Br("ShellDanger"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        _inBox = new TimeBox("Start time", () => s.In, t => { s.SetIn(t); Changed(true); }, Say);
        _outBox = new TimeBox("End time", () => s.Out, t => { s.SetOut(t); Changed(true); }, Say);
        var times = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        times.Children.Add(Label("Start"));
        times.Children.Add(_inBox);
        times.Children.Add(Label("End", 18));
        times.Children.Add(_outBox);
        _body.Children.Add(times);
        _body.Children.Add(_hint);

        // Sections.
        _muteCheck = new CheckBox { Content = info.HasAudio ? "Mute a section" : "Mute a section (this clip has no sound)", IsEnabled = info.HasAudio, Margin = new Thickness(0, 16, 0, 0), IsChecked = s.MuteOn };
        _muteCheck.Checked += (_, _) => { s.SetMuteOn(true); Changed(true); };
        _muteCheck.Unchecked += (_, _) => { s.SetMuteOn(false); Changed(true); };
        _body.Children.Add(_muteCheck);
        _muteFrom = new TimeBox("Mute from", () => s.MuteFrom, t => { s.SetMuteFrom(t); Changed(true); }, Say);
        _muteTo = new TimeBox("Mute to", () => s.MuteTo, t => { s.SetMuteTo(t); Changed(true); }, Say);
        _muteRow = Row(Label("from"), _muteFrom, Label("to", 12), _muteTo);
        _body.Children.Add(_muteRow);

        _speedCheck = new CheckBox { Content = "Speed up or slow down a section", Margin = new Thickness(0, 10, 0, 0), IsChecked = s.SpeedOn };
        _speedCheck.Checked += (_, _) => { s.SetSpeedOn(true); Changed(true); };
        _speedCheck.Unchecked += (_, _) => { s.SetSpeedOn(false); Changed(true); };
        _body.Children.Add(_speedCheck);
        _speedFrom = new TimeBox("Speed section from", () => s.SpeedFrom, t => { s.SetSpeedFrom(t); Changed(true); }, Say);
        _speedTo = new TimeBox("Speed section to", () => s.SpeedTo, t => { s.SetSpeedTo(t); Changed(true); }, Say);
        _speedCombo = new ComboBox { MinWidth = 150, Margin = new Thickness(12, 0, 0, 0) };
        AutomationProperties.SetName(_speedCombo, "Speed of the section");
        foreach (var (pct, label) in new[] { (25, "25% (4x slower)"), (50, "50% (half speed)"), (150, "150%"), (200, "200% (2x)"), (300, "300%"), (400, "400% (4x)") })
            _speedCombo.Items.Add(new ComboBoxItem { Content = label, Tag = pct });
        SelectSpeed(s.SpeedPercent);
        _speedCombo.SelectionChanged += (_, _) =>
        {
            if (_speedCombo.SelectedItem is ComboBoxItem { Tag: int pct }) { s.SpeedPercent = pct; Changed(true); }
        };
        _speedRow = Row(Label("from"), _speedFrom, Label("to", 12), _speedTo, _speedCombo);
        _body.Children.Add(_speedRow);

        // Caption.
        _body.Children.Add(new TextBlock { Text = "Caption", Style = S("ShellMutedText"), Margin = new Thickness(0, 16, 0, 6) });
        var caption = new TextBox { MaxLength = 300, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 320, Text = s.Caption };
        _captionBox = caption;
        AutomationProperties.SetName(caption, "Caption");
        caption.TextChanged += (_, _) => { s.Caption = caption.Text; };
        var capNote = new TextBlock
        {
            Text = "Shown at the bottom of the whole video. Leave empty for none.", Style = S("ShellCaption"), Margin = new Thickness(0, 4, 0, 0),
        };
        _body.Children.Add(caption);
        _body.Children.Add(capNote);

        // Presets.
        var presetsTitle = new TextBlock { Text = "Export for", Style = S("ShellSection"), Margin = new Thickness(0, 22, 0, 8) };
        PresetsAnchor = presetsTitle;
        _body.Children.Add(presetsTitle);
        var grid = new UniformGrid { Columns = 3 };
        foreach (var p in VideoFlowPresets.All) grid.Children.Add(MakeCard(p));
        _body.Children.Add(grid);
        _estimate = new TextBlock { Style = S("ShellSection"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        AutomationProperties.SetName(_estimate, "Size estimate");
        _body.Children.Add(_estimate);

        Changed(false);
    }

    private TextBlock Label(string text, double left = 0) =>
        new() { Text = text, Style = S("ShellMutedText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, 8, 0) };

    private StackPanel Row(params UIElement[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 6, 0, 0) };
        foreach (var i in items) row.Children.Add(i);
        return row;
    }

    private void SelectSpeed(int pct)
    {
        foreach (ComboBoxItem it in _speedCombo!.Items)
            if (it.Tag is int p && p == pct) { _speedCombo.SelectedItem = it; return; }
        _speedCombo.SelectedIndex = 3;
    }

    private Border MakeCard(FlowPreset p)
    {
        var name = new TextBlock { Text = p.Label, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var detail = new TextBlock { Text = p.Detail, Style = S("ShellCaption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        var size = new TextBlock { Style = S("ShellCaption"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
        size.SetResourceReference(TextBlock.ForegroundProperty, "ShellAccentStrong");
        var stack = new StackPanel();
        stack.Children.Add(name);
        stack.Children.Add(detail);
        stack.Children.Add(size);
        var card = new Border
        {
            Child = stack, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1.5), Cursor = Cursors.Hand, Focusable = true, Background = Br("ShellPaper"),
        };
        AutomationProperties.SetName(card, "Export preset " + p.Label);
        card.MouseLeftButtonUp += (_, _) => ChoosePreset(p.Id);
        card.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) { ChoosePreset(p.Id); e.Handled = true; }
        };
        _cards.Add((p, card, size));
        return card;
    }

    private void ChoosePreset(string id)
    {
        if (_state is null) return;
        _state.PresetId = id;
        Changed(false);
    }

    private void Say(string message)
    {
        if (_hint is null) return;
        _hint.Text = message;
        _hint.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------------------------------------------------------------- state to screen

    private void OnStripEdited(FlowHit hit, bool final)
    {
        if (_state is null) return;
        if (hit != FlowHit.Seek && _playing) StopPlayback();
        // A finished drag of a trim handle shows the frame it now starts or ends on.
        if (final && hit == FlowHit.In) _state.SetPlayhead(_state.In);
        if (final && hit == FlowHit.Out) _state.SetPlayhead(Math.Max(_state.In, _state.Out - 0.1));
        Changed(final);
        if (hit == FlowHit.Seek || final) SeekPlayer(_state.Playhead);
    }

    /// <summary>Pushes the whole state to every control. <paramref name="final"/> is a finished edit (mouse up, committed text).</summary>
    private void Changed(bool final)
    {
        var s = _state;
        if (s is null || _src is null) return;
        _strip?.Redraw();
        _inBox?.Sync(); _outBox?.Sync();
        _muteFrom?.Sync(); _muteTo?.Sync(); _speedFrom?.Sync(); _speedTo?.Sync();
        if (_muteCheck is not null && _muteCheck.IsChecked != s.MuteOn) _muteCheck.IsChecked = s.MuteOn;
        if (_speedCheck is not null && _speedCheck.IsChecked != s.SpeedOn) _speedCheck.IsChecked = s.SpeedOn;
        if (_muteRow is not null) _muteRow.Visibility = s.MuteOn ? Visibility.Visible : Visibility.Collapsed;
        if (_speedRow is not null) _speedRow.Visibility = s.SpeedOn ? Visibility.Visible : Visibility.Collapsed;
        if (_speedCombo is not null) SelectSpeed(s.SpeedPercent);
        if (_selectionLine is not null) _selectionLine.Text = FlowLines.Selection(s);
        if (_timeLine is not null) _timeLine.Text = $"{FlowTime.Format(s.Playhead)} / {FlowTime.Format(s.Duration)}";
        if (_estimate is not null) _estimate.Text = FlowLines.Estimate(s, _src);
        var accent = Br("ShellAccent");
        var line = Br("ShellLine");
        foreach (var (preset, card, size) in _cards)
        {
            var on = preset.Id == s.PresetId;
            card.BorderBrush = on ? accent : line;
            card.Background = on ? (Brush)FindResource("ShellAccentTint") : Br("ShellPaper");
            size.Text = FlowLines.CardSize(preset, s, _src);
            AutomationProperties.SetHelpText(card, on ? "Selected" : "");
        }
        if (final) ScheduleExtras();
    }

    private void ScheduleExtras()
    {
        _posterDebounce.Stop();
        _posterDebounce.Start();
    }

    // ---------------------------------------------------------------- picture and playback

    private async Task ShowPosterAsync(double seconds)
    {
        if (_poster is null) return;
        var id = Interlocked.Increment(ref _posterSeq);
        Interlocked.Increment(ref _pending);
        try
        {
            if (Math.Abs(seconds - _posterAt) < 0.05) return;
            var file = await VideoFilmstrip.PosterAsync(_file, seconds, 640, _cts.Token).ConfigureAwait(false);
            OnUi(() =>
            {
                if (id != Volatile.Read(ref _posterSeq)) return;
                if (LoadImage(file) is { } img)
                {
                    _poster.Source = img;
                    _posterAt = seconds;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[video] poster failed at {seconds:0.0}s: {ex.Message}");
        }
        finally
        {
            Interlocked.Decrement(ref _pending);
        }
    }

    private void AddPlayer(Grid box)
    {
        try
        {
            var m = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.Uniform,
                Volume = 0.8,
                ScrubbingEnabled = true,
            };
            m.MediaOpened += (_, _) =>
            {
                // The poster stays until the player really has a picture (it can take two seconds to open, and on a
                // machine without the codecs it never does).
                _mediaShowing = m.NaturalVideoWidth > 0;
                AppLogger.Info($"[video] flow player opened natural={m.NaturalVideoWidth}x{m.NaturalVideoHeight} hasVideo={m.HasVideo}");
                if (_mediaShowing && _poster is not null && !_playing) { /* keep the poster until the first play */ }
            };
            m.MediaFailed += (_, e) =>
            {
                _mediaShowing = false;
                AppLogger.Warn($"[video] flow player failed: {e.ErrorException?.GetType().Name}: {e.ErrorException?.Message}");
                _timeLine?.SetCurrentValue(TextBlock.TextProperty, "Preview is not available for this file here. Export still works.");
            };
            m.Source = new Uri(_file);
            box.Children.Insert(0, m);
            _media = m;
            m.Play();
            m.Pause();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[video] flow player not created: {ex.Message}");
            _media = null;
        }
    }

    private void SeekPlayer(double t)
    {
        if (_media is null) return;
        try { _media.Position = TimeSpan.FromSeconds(t); }
        catch (Exception) { /* not open yet */ }
    }

    private void TogglePlay()
    {
        if (_state is null) return;
        if (_playing) { StopPlayback(); return; }
        if (_media is null)
        {
            _timeLine?.SetCurrentValue(TextBlock.TextProperty, "Preview is not available here. Export still works.");
            return;
        }
        var s = _state;
        if (s.Playhead < s.In || s.Playhead >= s.Out - 0.05) s.SetPlayhead(s.In);
        SeekPlayer(s.Playhead);
        try
        {
            _media.Play();
            _playing = true;
            if (_poster is not null && _mediaShowing) _poster.Visibility = Visibility.Collapsed;
            if (_playButton is not null) _playButton.Content = "Pause";
            _tick.Start();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[video] play failed: {ex.Message}");
        }
    }

    private void PlayTick()
    {
        if (_media is null || _state is null || !_playing) return;
        double t;
        try { t = _media.Position.TotalSeconds; }
        catch (Exception) { return; }
        // The picture arrived after the first Play(): hide the poster only now.
        if (_mediaShowing && _poster is not null && _poster.Visibility == Visibility.Visible) _poster.Visibility = Visibility.Collapsed;
        if (t >= _state.Out - 0.02)
        {
            StopPlayback();
            _state.SetPlayhead(_state.In);
            SeekPlayer(_state.In);
            Changed(false);
            return;
        }
        _state.SetPlayhead(t);
        _strip?.Redraw();
        if (_timeLine is not null) _timeLine.Text = $"{FlowTime.Format(t)} / {FlowTime.Format(_state.Duration)}";
    }

    public void StopPlayback()
    {
        _tick.Stop();
        if (_playing)
        {
            _playing = false;
            try { _media?.Pause(); } catch (Exception) { /* closing */ }
            if (_poster is not null) _poster.Visibility = Visibility.Visible;
        }
        if (_playButton is not null) _playButton.Content = "Play selection";
    }

    // ---------------------------------------------------------------- snapshots

    /// <summary>Sets the page to one of the CI snapshot states. Returns false for a name that is not one.</summary>
    public bool ApplySnapshotState(string name)
    {
        if (_state is null || _src is null) return false;
        var s = _state;
        var d = s.Duration;
        switch (name)
        {
            case "handles":
                s.SetIn(d * 8 / 120);
                s.SetOut(d * 115 / 120);
                s.SetPlayhead(d * 0.4);
                s.SetMuteOn(true);
                s.SetMuteFrom(d * 0.5);
                s.SetMuteTo(d * 0.62);
                s.Caption = "Launch demo";
                s.PresetId = VideoFlowPresets.Original;
                break;
            case "presets":
                s.SetIn(d * 8 / 120);
                s.SetOut(d * 115 / 120);
                s.SetPlayhead(d * 0.3);
                s.SetSpeedOn(true);
                s.SetSpeedFrom(d * 0.3);
                s.SetSpeedTo(d * 0.45);
                s.SpeedPercent = 200;
                s.Caption = "Launch demo";
                s.PresetId = VideoFlowPresets.WhatsApp;
                break;
            case "gif":
                s.SetIn(d * 62 / 120);
                s.SetOut(d * 68 / 120);
                s.SetPlayhead(d * 65 / 120);
                s.PresetId = VideoFlowPresets.Gif;
                break;
            default:
                return false;
        }
        if (_muteCheck is not null) _muteCheck.IsChecked = s.MuteOn;
        if (_speedCheck is not null) _speedCheck.IsChecked = s.SpeedOn;
        if (_speedCombo is not null) SelectSpeed(s.SpeedPercent);
        if (_captionBox is not null) _captionBox.Text = s.Caption;
        Changed(false);
        _ = ShowPosterAsync(s.Playhead);
        return true;
    }
}

/// <summary>What ToolPageView needs from the quick edit while a tool runs: the tool, the request, the progress line.</summary>
internal sealed class VideoFlowSession
{
    private readonly VideoFlowPanel _panel;
    private readonly Stopwatch _clock = new();

    public VideoFlowSession(VideoFlowPanel panel) { _panel = panel; }

    /// <summary>False once the clip could not be read: the page then runs its own tool and shows its own options.</summary>
    public bool Active => !_panel.Failed;

    public const string Label = "Export";

    public RegisteredTool Tool => VideoToolProvider.FlowTool(_panel.Availability);

    public ToolRequest Request(IReadOnlyList<string> files, string outDir)
    {
        _clock.Restart();
        var options = _panel.State?.ToOptions() ?? new Dictionary<string, string>();
        return new ToolRequest(files, outDir, options);
    }

    public string ProgressLine(ToolProgress p) => FlowLines.Progress(p.Percent, _clock.Elapsed, p.Message);
}

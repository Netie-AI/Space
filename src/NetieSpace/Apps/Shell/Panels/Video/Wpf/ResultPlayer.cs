using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NetieSpace.Apps.Video;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell.Panels.Video;

/// <summary>
/// Under the result card: the exported video, playable in place (W6-4). A still frame is on screen from the first moment;
/// the player replaces it only once it really has a picture, because opening can take a couple of seconds. A GIF shows its
/// first frame and says to press Open (WPF does not animate GIFs).
/// </summary>
internal sealed class ResultPlayer : Border
{
    private readonly string _path;
    private readonly bool _gif;
    private readonly System.Windows.Controls.Image _still = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _note = new();
    private readonly Button _play = new() { Content = "Play result", MinHeight = 30, Padding = new Thickness(14, 4, 14, 4) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly CancellationTokenSource _cts = new();
    private MediaElement? _media;
    private bool _opened;
    private bool _playing;

    public static bool Plays(string path)
    {
        var ext = IoPath.GetExtension(path);
        return ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || VideoFfmpeg.VideoExts.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    public ResultPlayer(string path)
    {
        _path = path;
        _gif = IoPath.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase);
        Margin = new Thickness(0, 10, 0, 0);
        MaxWidth = 440;
        HorizontalAlignment = HorizontalAlignment.Left;
        CornerRadius = new CornerRadius(8);
        ClipToBounds = true;
        Background = Brushes.Black;
        AutomationProperties.SetName(this, "Result player");

        var box = new Grid { Height = 230 };
        box.Children.Add(_still);
        var stack = new StackPanel();
        stack.Children.Add(box);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0), Background = Brushes.White };
        _note.VerticalAlignment = VerticalAlignment.Center;
        _note.Margin = new Thickness(12, 0, 0, 0);
        _note.Foreground = Brushes.DimGray;
        if (!_gif)
        {
            _play.Margin = new Thickness(8);
            _play.Click += (_, _) => Toggle();
            AutomationProperties.SetName(_play, "Play result");
            bar.Children.Add(_play);
        }
        bar.Children.Add(_note);
        stack.Children.Add(bar);
        Child = stack;

        Loaded += (_, _) => Start(box);
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false) Stop();
        };
        _tick.Tick += (_, _) => Tick();
    }

    private async void Start(Grid box)
    {
        if (_media is not null || _cts.IsCancellationRequested) return;
        try
        {
            var poster = await VideoFilmstrip.PosterAsync(_path, 0.3, 480, _cts.Token).ConfigureAwait(false);
            if (!Dispatcher.CheckAccess())
            {
                await Dispatcher.InvokeAsync(() => Finish(box, poster));
                return;
            }
            Finish(box, poster);
            return;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { AppLogger.Warn($"[video] result still failed: {ex.Message}"); }

        // the still failed: the player below is still worth having
        Finish(box, null);
    }

    private void Finish(Grid box, string? poster)
    {
        if (poster is not null && VideoFlowPanel.LoadImage(poster) is { } img) _still.Source = img;
        if (_gif)
        {
            _note.Text = "GIF - press Open above to watch it loop";
            return;
        }
        try
        {
            var m = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = Stretch.Uniform, Volume = 0.8 };
            m.MediaOpened += (_, _) =>
            {
                _opened = m.NaturalVideoWidth > 0;
                AppLogger.Info($"[video] result player opened natural={m.NaturalVideoWidth}x{m.NaturalVideoHeight}");
            };
            m.MediaFailed += (_, e) =>
            {
                _opened = false;
                _note.Text = "Playing needs the Windows media codecs here. Use Open above.";
                AppLogger.Warn($"[video] result player failed: {e.ErrorException?.GetType().Name}: {e.ErrorException?.Message}");
            };
            m.MediaEnded += (_, _) => Stop();
            m.Source = new Uri(_path);
            box.Children.Insert(0, m);
            _media = m;
            m.Play();
            m.Pause();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[video] result player not created: {ex.Message}");
            _note.Text = "Playing is not available here. Use Open above.";
        }
    }

    private void Toggle()
    {
        if (_media is null) return;
        if (_playing) { Stop(); return; }
        try
        {
            _media.Position = TimeSpan.Zero;
            _media.Play();
            _playing = true;
            _play.Content = "Pause";
            _tick.Start();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[video] result play failed: {ex.Message}");
        }
    }

    private void Tick()
    {
        // Hide the still only once the player has a picture of its own.
        if (_playing && _opened && _still.Visibility == Visibility.Visible) _still.Visibility = Visibility.Collapsed;
    }

    public void Stop()
    {
        _tick.Stop();
        if (_playing)
        {
            _playing = false;
            try { _media?.Pause(); } catch (Exception) { /* closing */ }
        }
        _play.Content = "Play result";
        _still.Visibility = Visibility.Visible;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NetieSpace.Helpers;

/// <summary>Spring-ish open/close for Liquid Glass chrome.
/// Prefer <see cref="AnimateOpenFadeOnly"/> when a WebView2 HWND is in the tree —
/// ScaleTransform on ancestors of WebView2 under AllowsTransparency causes compositor faults.</summary>
public static class AnimationHelper
{
    /// <summary>Apple-ish decelerate — the default for anything that opens or grows.</summary>
    public static IEasingFunction Decel => new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Slight overshoot — only for small chrome (chips, pills), never for layout-heavy panels.</summary>
    public static IEasingFunction Spring => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.22 };

    /// <summary>Opacity-only open — safe with WebView2 / PDF / Office HWND hosts.</summary>
    public static void AnimateOpenFadeOnly(UIElement element, int durationMs = 260)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.RenderTransform = null;
        var from = element.Opacity;
        if (from >= 0.99) element.Opacity = 0;
        from = element.Opacity;
        var duration = TimeSpan.FromMilliseconds(AppleMotion.ResolveDuration(Math.Max(80, durationMs)));
        var fade = new DoubleAnimation(from, 1, duration)
        {
            EasingFunction = AppleMotion.UiEase
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    public static Task AnimateCloseFadeOnlyAsync(UIElement element, int durationMs = 160)
    {
        var tcs = new TaskCompletionSource();
        var duration = TimeSpan.FromMilliseconds(AppleMotion.ResolveDuration(Math.Max(60, durationMs)));
        element.BeginAnimation(UIElement.OpacityProperty, null);
        var fade = new DoubleAnimation(element.Opacity, 0, duration)
        {
            EasingFunction = AppleMotion.PrefersReducedMotion
                ? new QuadraticEase { EasingMode = EasingMode.EaseIn }
                : new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) => tcs.TrySetResult();
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        return tcs.Task;
    }

    public static void AnimateOpen(UIElement element, int durationMs = 260)
    {
        // Scale over WebView2 is unsafe — fade-only is the default for reliability.
        AnimateOpenFadeOnly(element, durationMs);
    }

    public static Task AnimateCloseAsync(UIElement element, int durationMs = 200) =>
        AnimateCloseFadeOnlyAsync(element, durationMs);

    public static void SlidePanel(UIElement panel, double fromX, double toX, int durationMs = 260)
    {
        // Interruptible: if a transform is already live, start from its current X
        var transform = panel.RenderTransform as TranslateTransform ?? new TranslateTransform(fromX, 0);
        if (panel.RenderTransform is TranslateTransform live)
            fromX = live.X;
        panel.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.XProperty, null);

        var animation = new DoubleAnimation(fromX, toX,
            TimeSpan.FromMilliseconds(AppleMotion.ResolveDuration(durationMs)))
        {
            EasingFunction = AppleMotion.UiEase
        };

        transform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    /// <summary>Slide + fade together — the panel arrives instead of appearing.</summary>
    public static void SlideFadeIn(UIElement panel, double fromX, int durationMs = 260)
    {
        if (AppleMotion.PrefersReducedMotion)
        {
            AnimateOpenFadeOnly(panel, durationMs);
            return;
        }

        panel.BeginAnimation(UIElement.OpacityProperty, null);
        var fromOpacity = panel.Opacity < 0.05 ? 0 : panel.Opacity;
        panel.Opacity = fromOpacity;
        SlidePanel(panel, fromX, 0, durationMs);
        var fade = new DoubleAnimation(fromOpacity, 1,
            TimeSpan.FromMilliseconds(AppleMotion.ResolveDuration(Math.Max(80, durationMs - 40))))
        {
            EasingFunction = AppleMotion.UiEase
        };
        panel.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Vertical arrival — safe only for chrome that is NOT an ancestor of a WebView2 HWND.</summary>
    public static void SlideDownFadeIn(UIElement panel, double fromY, int durationMs = 260)
    {
        var transform = new TranslateTransform(0, fromY);
        panel.RenderTransform = transform;
        panel.BeginAnimation(UIElement.OpacityProperty, null);
        panel.Opacity = 0;

        var slide = new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(Math.Max(80, durationMs)))
        {
            EasingFunction = Decel
        };
        slide.Completed += (_, _) => panel.RenderTransform = null;
        transform.BeginAnimation(TranslateTransform.YProperty, slide);

        panel.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Math.Max(80, durationMs - 40))) { EasingFunction = Decel });
    }

    /// <summary>Collapse height to zero, then hide. Returns the height it had, so Expand can restore it.</summary>
    public static async Task<double> CollapseHeightAsync(FrameworkElement element, int durationMs = 220)
    {
        var start = element.ActualHeight;
        if (start <= 0 || element.Visibility != Visibility.Visible)
        {
            element.Visibility = Visibility.Collapsed;
            return start;
        }

        var tcs = new TaskCompletionSource();
        var duration = TimeSpan.FromMilliseconds(Math.Max(80, durationMs));

        var shrink = new DoubleAnimation(start, 0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        shrink.Completed += (_, _) => tcs.TrySetResult();

        var fade = new DoubleAnimation(element.Opacity, 0, TimeSpan.FromMilliseconds(Math.Max(60, durationMs - 60)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        element.BeginAnimation(UIElement.OpacityProperty, fade);
        element.BeginAnimation(FrameworkElement.HeightProperty, shrink);
        await tcs.Task;

        // Release the animation so the element measures naturally again on expand.
        element.BeginAnimation(FrameworkElement.HeightProperty, null);
        element.Height = 0;
        element.Visibility = Visibility.Collapsed;
        return start;
    }

    /// <summary>Grow back from zero to <paramref name="targetHeight"/>, then hand height back to layout.</summary>
    public static void ExpandHeight(FrameworkElement element, double targetHeight, int durationMs = 240)
    {
        if (targetHeight <= 1)
            targetHeight = element.MaxHeight is > 0 and < 4000 ? element.MaxHeight : 96;

        element.Visibility = Visibility.Visible;
        element.BeginAnimation(FrameworkElement.HeightProperty, null);
        element.Height = 0;
        element.Opacity = 0;

        var duration = TimeSpan.FromMilliseconds(Math.Max(80, durationMs));
        var grow = new DoubleAnimation(0, targetHeight, duration) { EasingFunction = Decel };
        grow.Completed += (_, _) =>
        {
            element.BeginAnimation(FrameworkElement.HeightProperty, null);
            element.Height = double.NaN; // back to Auto so content can reflow
        };

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Math.Max(80, durationMs - 40)))
        {
            BeginTime = TimeSpan.FromMilliseconds(40),
            EasingFunction = Decel
        };

        element.BeginAnimation(FrameworkElement.HeightProperty, grow);
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Animate a grid column between pixel widths — chat / summary rail.
    /// Always starts from the live width so open/close mid-flight is interruptible.</summary>
    public static void AnimateColumn(ColumnDefinition column, double fromPx, double toPx, int durationMs = 240,
                                     Action? completed = null)
    {
        var live = column.Width.IsAbsolute ? column.Width.Value : fromPx;
        fromPx = live;
        column.BeginAnimation(ColumnDefinition.WidthProperty, null);

        var animation = new GridLengthAnimation
        {
            From = new GridLength(Math.Max(0, fromPx)),
            To = new GridLength(Math.Max(0, toPx)),
            Duration = TimeSpan.FromMilliseconds(AppleMotion.ResolveDuration(Math.Max(80, durationMs))),
            EasingFunction = AppleMotion.UiEase
        };

        animation.Completed += (_, _) =>
        {
            column.BeginAnimation(ColumnDefinition.WidthProperty, null);
            column.Width = new GridLength(Math.Max(0, toPx));
            completed?.Invoke();
        };

        column.BeginAnimation(ColumnDefinition.WidthProperty, animation);
    }

    /// <summary>Pill / chip entrance. Only safe on small chrome that is not an ancestor of a WebView2 HWND.</summary>
    public static void PopIn(FrameworkElement element, int durationMs = 220)
    {
        if (AppleMotion.PrefersReducedMotion)
        {
            element.Visibility = Visibility.Visible;
            AnimateOpenFadeOnly(element, durationMs);
            return;
        }

        element.Visibility = Visibility.Visible;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 0;

        var scale = new ScaleTransform(0.92, 0.92);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;

        var duration = TimeSpan.FromMilliseconds(AppleMotion.ResolveDuration(Math.Max(80, durationMs)));
        // Slight overshoot only for chips (momentum-like arrival) — Amplitude kept low
        var grow = new DoubleAnimation(0.92, 1, duration) { EasingFunction = Spring };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(AppleMotion.ResolveDuration(Math.Max(60, durationMs - 60))))
            { EasingFunction = AppleMotion.UiEase });
    }

    public static async Task PopOutAsync(FrameworkElement element, int durationMs = 160)
    {
        if (element.Visibility != Visibility.Visible)
            return;

        var scale = element.RenderTransform as ScaleTransform;
        if (scale is null)
        {
            scale = new ScaleTransform(1, 1);
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = scale;
        }

        var duration = TimeSpan.FromMilliseconds(Math.Max(60, durationMs));
        var shrink = new DoubleAnimation(1, 0.86, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        await AnimateCloseFadeOnlyAsync(element, durationMs);
        element.Visibility = Visibility.Collapsed;
    }
}

/// <summary>WPF ships no GridLength animation — this interpolates pixel widths for animated columns.</summary>
public sealed class GridLengthAnimation : AnimationTimeline
{
    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(GridLength), typeof(GridLengthAnimation),
            new PropertyMetadata(new GridLength(0)));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(GridLength), typeof(GridLengthAnimation),
            new PropertyMetadata(new GridLength(0)));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(GridLengthAnimation),
            new PropertyMetadata(null));

    public override Type TargetPropertyType => typeof(GridLength);

    protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

    public GridLength From
    {
        get => (GridLength)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public GridLength To
    {
        get => (GridLength)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock clock)
    {
        var progress = clock.CurrentProgress ?? 0d;
        if (EasingFunction is not null)
            progress = EasingFunction.Ease(progress);

        var from = From.Value;
        var to = To.Value;
        return new GridLength(from + ((to - from) * progress), GridUnitType.Pixel);
    }
}

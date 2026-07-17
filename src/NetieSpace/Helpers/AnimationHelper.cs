using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NetieSpace.Helpers;

/// <summary>Spring-ish open/close for Liquid Glass chrome.
/// Prefer <see cref="AnimateOpenFadeOnly"/> when a WebView2 HWND is in the tree —
/// ScaleTransform on ancestors of WebView2 under AllowsTransparency causes compositor faults.</summary>
public static class AnimationHelper
{
    /// <summary>Opacity-only open — safe with WebView2 / PDF / Office HWND hosts.</summary>
    public static void AnimateOpenFadeOnly(UIElement element, int durationMs = 260)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.RenderTransform = null;
        element.Opacity = 0;
        var duration = TimeSpan.FromMilliseconds(Math.Max(80, durationMs));
        var fade = new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    public static Task AnimateCloseFadeOnlyAsync(UIElement element, int durationMs = 160)
    {
        var tcs = new TaskCompletionSource();
        var duration = TimeSpan.FromMilliseconds(Math.Max(60, durationMs));
        var fade = new DoubleAnimation(element.Opacity, 0, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
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
        var transform = new TranslateTransform(fromX, 0);
        panel.RenderTransform = transform;

        var easing = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.18 };
        var animation = new DoubleAnimation(fromX, toX, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = easing
        };

        transform.BeginAnimation(TranslateTransform.XProperty, animation);
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NetieSpace.Helpers;

/// <summary>
/// Apple Design (WWDC fluid interfaces) mapped to WPF:
/// response on press, springs from current value, interruptible retarget,
/// critically damped defaults, reduced-motion / reduced-transparency.
/// </summary>
public static class AppleMotion
{
    /// <summary>Default UI response (~0.35s) — critically damped feel via CubicEase Out.</summary>
    public const int ResponseMs = 340;

    /// <summary>Sheet / drawer response — slightly snappier.</summary>
    public const int SheetResponseMs = 300;

    /// <summary>Press highlight — must feel instant.</summary>
    public const int PressMs = 90;

    /// <summary>Windows "Show animations" via SPI — SystemParameters has no ClientAnimation on WPF.</summary>
    public static bool PrefersReducedMotion
    {
        get
        {
            try
            {
                // SPI_GETCLIENTAREAANIMATION = 0x1042
                var enabled = 1;
                if (SystemParametersInfo(0x1042u, 0u, ref enabled, 0u) && enabled == 0)
                    return true;
                if (!SystemParameters.MenuAnimation && !SystemParameters.ComboBoxAnimation)
                    return true;
            }
            catch { /* ignore */ }
            return false;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

    /// <summary>Heavier frost when user asked for less transparency (approx).</summary>
    public static bool PrefersReducedTransparency
    {
        get
        {
            try
            {
                // No first-class WPF API — approximate via high-contrast / solid preference.
                return SystemParameters.HighContrast;
            }
            catch
            {
                return false;
            }
        }
    }

    public static int ResolveDuration(int preferredMs) =>
        PrefersReducedMotion ? Math.Min(180, Math.Max(80, preferredMs / 2)) : preferredMs;

    public static IEasingFunction UiEase =>
        PrefersReducedMotion
            ? new QuadraticEase { EasingMode = EasingMode.EaseOut }
            : AnimationHelper.Decel;

    /// <summary>
    /// Interruptible retarget: clears prior animations on the property, animates from the
    /// live presentation value (no jump). Safe for gesture-driven chrome.
    /// </summary>
    public static void SpringDouble(
        UIElement element,
        DependencyProperty property,
        double to,
        int responseMs = ResponseMs,
        double? fromOverride = null,
        Action? completed = null)
    {
        element.BeginAnimation(property, null);
        var from = fromOverride ?? (double)element.GetValue(property);
        var duration = TimeSpan.FromMilliseconds(ResolveDuration(responseMs));
        var anim = new DoubleAnimation(from, to, duration) { EasingFunction = UiEase };
        if (completed is not null)
            anim.Completed += (_, _) => completed();
        element.BeginAnimation(property, anim);
    }

    /// <summary>Materialize a glass surface: opacity + slight scale together (not fade-only).</summary>
    public static void Materialize(FrameworkElement element, int responseMs = ResponseMs)
    {
        if (PrefersReducedMotion)
        {
            AnimationHelper.AnimateOpenFadeOnly(element, ResolveDuration(responseMs));
            return;
        }

        element.Visibility = Visibility.Visible;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 0;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = element.RenderTransform as ScaleTransform ?? new ScaleTransform(0.98, 0.98);
        element.RenderTransform = scale;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        var duration = TimeSpan.FromMilliseconds(ResolveDuration(responseMs));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.98, 1, duration) { EasingFunction = UiEase });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.98, 1, duration) { EasingFunction = UiEase });
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ResolveDuration(responseMs - 40)))
            { EasingFunction = UiEase });
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace NetieSpace.Helpers;

/// <summary>
/// Liquid-glass chrome. Acrylic is rectangular at the HWND level — never leave a
/// transparent Margin around rounded content or Windows paints a sharp box behind it.
/// Prefer SizeToContent / Margin=0 shells; optional rounded Win32 region clips the blur.
/// </summary>
public static class GlassHelper
{
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int AccentEnableBlurBehind = 3;
    private const int AcrylicTintAbgr = unchecked((int)0x48101820);

    /// <param name="opacity">Window opacity (1 = solid).</param>
    /// <param name="useAcrylic">When false, XAML transparency only (dialogs stay crisp rounded).</param>
    /// <param name="cornerRadius">If &gt; 0, clips HWND to rounded rect so no square blur halo.</param>
    public static void ApplyGlass(Window window, double opacity = 0.90, bool useAcrylic = true, int cornerRadius = 0)
    {
        window.AllowsTransparency = true;
        window.Background = Brushes.Transparent;
        window.Opacity = Math.Clamp(opacity, 0.55, 1.0);

        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;

            if (useAcrylic)
                TryEnableAcrylic(handle);

            if (cornerRadius > 0)
                TryApplyRoundedRegion(handle, window, cornerRadius);

            window.SizeChanged += (_, _) =>
            {
                if (cornerRadius > 0)
                    TryApplyRoundedRegion(handle, window, cornerRadius);
            };
        };
    }

    /// <summary>Small modals: no acrylic rectangle — XAML rounded glass only.</summary>
    public static void ApplyRoundedDialog(Window window, double opacity = 0.96, int cornerRadius = 18) =>
        ApplyGlass(window, opacity, useAcrylic: false, cornerRadius: cornerRadius);

    public static void TryEnableAcrylic(IntPtr hwnd)
    {
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = AccentEnableAcrylicBlurBehind,
                AccentFlags = 0x20 | 0x40 | 0x80 | 0x100,
                GradientColor = AcrylicTintAbgr
            };

            if (SetAccent(hwnd, accent))
                return;
        }
        catch { /* fall through */ }

        try
        {
            SetAccent(hwnd, new AccentPolicy
            {
                AccentState = AccentEnableBlurBehind,
                GradientColor = AcrylicTintAbgr
            });
        }
        catch { /* XAML only */ }
    }

    public static void TryApplyRoundedRegion(IntPtr hwnd, Window window, int radiusDip)
    {
        try
        {
            var src = PresentationSource.FromVisual(window);
            var transform = src?.CompositionTarget?.TransformToDevice;
            var scaleX = transform?.M11 ?? 1.0;
            var scaleY = transform?.M22 ?? 1.0;
            var w = (int)Math.Max(1, Math.Round(window.ActualWidth * scaleX));
            var h = (int)Math.Max(1, Math.Round(window.ActualHeight * scaleY));
            if (w < 8 || h < 8) return;
            var r = (int)Math.Max(4, Math.Round(radiusDip * ((scaleX + scaleY) * 0.5)));
            var region = CreateRoundRectRgn(0, 0, w + 1, h + 1, r * 2, r * 2);
            if (region != IntPtr.Zero)
                SetWindowRgn(hwnd, region, true);
        }
        catch { /* ignore */ }
    }

    private static bool SetAccent(IntPtr hwnd, AccentPolicy accent)
    {
        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.AccentPolicy,
                SizeOfData = size,
                Data = ptr
            };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    private enum WindowCompositionAttribute { AccentPolicy = 19 }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }
}

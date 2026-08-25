using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using NetieSpace.Helpers;
using NetieSpace.Services;

namespace NetieSpace.Views;

/// <summary>
/// Opaque owned window that hosts IPreviewHandler. PreviewWindow is layered glass;
/// a child HWND inside it does not paint. This overlay sits over ContentHost instead.
/// </summary>
public sealed class PreviewHandlerOverlay : IDisposable
{
    private readonly Window _owner;
    private readonly FrameworkElement _slot;
    private Window? _win;
    private NativeHost? _host;
    private PreviewHandlerService.IPreviewHandler? _handler;
    private object? _raw;
    private bool _hooked;

    public PreviewHandlerOverlay(Window owner, FrameworkElement slot)
    {
        _owner = owner;
        _slot = slot;
    }

    public bool TryShow(string filePath)
    {
        Hide();
        if (PreviewHandlerService.IsDisabled)
            return false;
        if (string.IsNullOrWhiteSpace(filePath) || IoDirectory.Exists(filePath))
            return false;
        var ext = IoPath.GetExtension(filePath);
        if (!PreviewHandlerService.TryFindClsid(ext, out var clsid))
            return false;

        var raw = PreviewHandlerService.CreateHandler(clsid);
        if (raw is null || !PreviewHandlerService.TryInitialize(raw, filePath))
        {
            ReleaseCom(raw);
            return false;
        }

        if (raw is not PreviewHandlerService.IPreviewHandler handler)
        {
            ReleaseCom(raw);
            return false;
        }

        try
        {
            _raw = raw;
            _handler = handler;
            EnsureWindow();
            AttachHooks();
            Reposition();
            _win!.Show();
            _win.UpdateLayout();
            _host!.UpdateLayout();
            var hwnd = _host.Handle;
            if (hwnd == IntPtr.Zero)
            {
                Hide();
                return false;
            }

            var rect = HostRect();
            handler.SetWindow(hwnd, ref rect);
            handler.DoPreview();
            handler.SetRect(ref rect);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"IPreviewHandler DoPreview: {ex.Message}");
            Hide();
            return false;
        }
    }

    public void Hide()
    {
        try { _handler?.Unload(); }
        catch { /* handler already dead */ }
        _handler = null;
        ReleaseCom(_raw);
        _raw = null;
        if (_win is not null)
        {
            try { _win.Hide(); }
            catch { /* ignore */ }
        }
    }

    public void Dispose()
    {
        Hide();
        DetachHooks();
        if (_win is not null)
        {
            try { _win.Close(); }
            catch { /* ignore */ }
            _win = null;
        }
        _host = null;
    }

    private void EnsureWindow()
    {
        if (_win is not null)
            return;
        _host = new NativeHost();
        _win = new Window
        {
            Owner = _owner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false,
            Background = System.Windows.Media.Brushes.Black,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            Topmost = _owner.Topmost,
            Content = _host
        };
        _host.SizeChanged += (_, _) => PushRect();
    }

    private void AttachHooks()
    {
        if (_hooked)
            return;
        _owner.LocationChanged += OwnerMoved;
        _owner.SizeChanged += OwnerSized;
        _owner.StateChanged += OwnerMoved;
        _slot.SizeChanged += OwnerSized;
        _hooked = true;
    }

    private void DetachHooks()
    {
        if (!_hooked)
            return;
        _owner.LocationChanged -= OwnerMoved;
        _owner.SizeChanged -= OwnerSized;
        _owner.StateChanged -= OwnerMoved;
        _slot.SizeChanged -= OwnerSized;
        _hooked = false;
    }

    private void OwnerMoved(object? sender, EventArgs e) => Reposition();

    private void OwnerSized(object sender, SizeChangedEventArgs e) => Reposition();

    private void Reposition()
    {
        if (_win is null || !_slot.IsVisible || _slot.ActualWidth < 8 || _slot.ActualHeight < 8)
            return;
        try
        {
            var origin = _slot.PointToScreen(new Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(_owner);
            var sx = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
            var sy = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;
            _win.Left = origin.X / sx;
            _win.Top = origin.Y / sy;
            _win.Width = _slot.ActualWidth;
            _win.Height = _slot.ActualHeight;
            PushRect();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"IPreviewHandler layout: {ex.Message}");
        }
    }

    private void PushRect()
    {
        if (_handler is null || _host is null)
            return;
        try
        {
            var rect = HostRect();
            _handler.SetRect(ref rect);
        }
        catch { /* ignore */ }
    }

    private PreviewHandlerService.RECT HostRect()
    {
        var w = Math.Max(8, (int)(_host?.ActualWidth ?? _slot.ActualWidth));
        var h = Math.Max(8, (int)(_host?.ActualHeight ?? _slot.ActualHeight));
        return new PreviewHandlerService.RECT { Left = 0, Top = 0, Right = w, Bottom = h };
    }

    private static void ReleaseCom(object? obj)
    {
        if (obj is null)
            return;
        try { Marshal.FinalReleaseComObject(obj); }
        catch { /* not COM */ }
    }

    private sealed class NativeHost : HwndHost
    {
        private IntPtr _hwnd;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            _hwnd = CreateWindowEx(
                0, "Static", "",
                WsChild | WsVisible | WsClipSiblings | WsClipChildren,
                0, 0, 8, 8,
                hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return new HandleRef(this, _hwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            if (hwnd.Handle != IntPtr.Zero)
                DestroyWindow(hwnd.Handle);
            _hwnd = IntPtr.Zero;
        }

        private const int WsChild = 0x40000000;
        private const int WsVisible = 0x10000000;
        private const int WsClipSiblings = 0x04000000;
        private const int WsClipChildren = 0x02000000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);
    }
}

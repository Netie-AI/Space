using System.Runtime.InteropServices;
using System.Text;

namespace NetieSpace.Helpers;

internal static class Win32Api
{
    public const int WhKeyboardLl = 13;
    public const int WmKeydown = 0x0100;
    public const int WmSyskeydown = 0x0104;
    public const int VkSpace = 0x20;
    public const int VkLeft = 0x25;
    public const int VkUp = 0x26;
    public const int VkRight = 0x27;
    public const int VkDown = 0x28;
    public const int VkEscape = 0x1B;
    public const int VkHome = 0x24;
    public const int VkEnd = 0x23;
    public const uint GaRoot = 2;
    public const uint GaParent = 1;

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // Taskbar identity for the Netie app processes (Apps/Shell). HRESULT: 0 = S_OK.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    public const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachConsole(uint dwProcessId);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] out string appId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public static readonly IntPtr HwndTopmost = new(-1);
    public const uint SwpNomove = 0x0002;
    public const uint SwpNosize = 0x0001;
    public const uint SwpNoactivate = 0x0010;
    public const int GwlExstyle = -20;
    public const int WsExTopmost = 0x00000008;
    public const int WmWindowposchanged = 0x0047;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    /// <summary>Keep HWND_TOPMOST without stealing focus (acrylic/WebView2 often drops it).</summary>
    public static void EnsureTopmostNoActivate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        try
        {
            var ex = GetWindowLong(hwnd, GwlExstyle);
            if ((ex & WsExTopmost) != 0)
                return;
            SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
        }
        catch { /* ignore */ }
    }

    /// <summary>True when the foreground window belongs to this process (preview / owned dialogs).</summary>
    public static bool IsOurProcessForeground()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(fg, out var pid);
        if (pid == 0)
            return false;
        return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    public const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo info);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MonitorInfoEx
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuiThreadInfo
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>
    /// Best HWND for Explorer selection: focus control if available, else foreground.
    /// </summary>
    public static IntPtr GetExplorerContextHwnd()
    {
        var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
        if (GetGUIThreadInfo(0, ref info))
        {
            if (info.hwndFocus != IntPtr.Zero)
                return info.hwndFocus;
            if (info.hwndActive != IntPtr.Zero)
                return info.hwndActive;
        }

        return GetForegroundWindow();
    }

    /// <summary>Window under the mouse cursor (root-capable via TryFindExplorerShellWindow).</summary>
    public static IntPtr GetWindowUnderCursor()
    {
        if (!GetCursorPos(out var pt))
            return IntPtr.Zero;
        return WindowFromPoint(pt);
    }

    public static bool IsDesktopClass(string className) =>
        className is "Progman" or "WorkerW";

    /// <summary>
    /// True when Explorer/Desktop F2 rename (or in-place label edit) owns the caret.
    /// Space must type a space, not open/close preview.
    /// </summary>
    public static bool IsExplorerLabelEdit()
    {
        try
        {
            var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
            if (!GetGUIThreadInfo(0, ref info))
                return false;

            foreach (var hwnd in new[] { info.hwndFocus, info.hwndCaret })
            {
                if (hwnd == IntPtr.Zero)
                    continue;
                if (!IsEditClass(GetWindowClass(hwnd)))
                    continue;
                if (HasExplorerListAncestor(hwnd))
                    return true;
            }
        }
        catch { /* fail closed = not editing */ }

        return false;
    }

    private static bool IsEditClass(string cls) =>
        cls is "Edit" or "RichEdit20W" or "RichEdit20A" or "RichEdit50W" ||
        cls.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase);

    private static bool HasExplorerListAncestor(IntPtr hwnd)
    {
        var current = hwnd;
        for (var i = 0; i < 16 && current != IntPtr.Zero; i++)
        {
            var cls = GetWindowClass(current);
            if (cls is "SysListView32" or "DirectUIHWND" or "SHELLDLL_DefView"
                or "Progman" or "WorkerW" or "CabinetWClass" or "ExploreWClass")
                return true;

            var parent = GetParent(current);
            if (parent == IntPtr.Zero || parent == current)
            {
                var root = GetRootWindow(current);
                if (root != IntPtr.Zero && root != current)
                {
                    var rootCls = GetWindowClass(root);
                    if (rootCls is "Progman" or "WorkerW" or "CabinetWClass" or "ExploreWClass")
                        return true;
                }
                break;
            }
            current = parent;
        }
        return false;
    }

    /// <summary>Work-area rect for the monitor that owns hwnd (falls back to primary work area).</summary>
    public static bool TryGetMonitorWorkArea(IntPtr hwnd, out RECT work)
    {
        work = default;
        if (hwnd == IntPtr.Zero)
            return false;

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return false;

        var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
        if (!GetMonitorInfo(monitor, ref info))
            return false;

        work = info.rcWork;
        return true;
    }

    public static string GetWindowClass(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return string.Empty;

        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static IntPtr GetRootWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return IntPtr.Zero;

        var root = GetAncestor(hwnd, GaRoot);
        return root != IntPtr.Zero ? root : hwnd;
    }

    public static bool IsExplorerShellClass(string className) =>
        className is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW"
            or "ShellTabWindowClass"; // Win11 tabbed Explorer host

    /// <summary>Win11 File Explorer / Desktop chrome that lives under explorer.exe.</summary>
    public static bool IsExplorerViewClass(string className) =>
        className is "SysListView32" or "SHELLDLL_DefView" or "DirectUIHWND"
            or "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW"
            or "ShellTabWindowClass"
            or "Microsoft.UI.Content.DesktopChildSiteBridge"
            or "InputSiteWindowClass"
            or "UIXamlHostWindowClass"
            or "XamlExplorerHostIslandWindow";

    public static bool TryFindExplorerShellWindow(IntPtr hwnd, out IntPtr shellHwnd, out string className)
    {
        shellHwnd = IntPtr.Zero;
        className = string.Empty;

        var current = hwnd;
        for (var i = 0; i < 24 && current != IntPtr.Zero; i++)
        {
            var cls = GetWindowClass(current);
            if (IsExplorerShellClass(cls))
            {
                shellHwnd = current;
                className = cls;
                return true;
            }

            // Prefer GA_PARENT over GetParent — Win11 islands often break GetParent chains
            var parent = GetAncestor(current, GaParent);
            if (parent == IntPtr.Zero || parent == current)
            {
                parent = GetParent(current);
                if (parent == IntPtr.Zero || parent == current)
                    break;
            }
            current = parent;
        }

        var root = GetRootWindow(hwnd);
        var rootClass = GetWindowClass(root);
        if (IsExplorerShellClass(rootClass))
        {
            shellHwnd = root;
            className = rootClass;
            return true;
        }

        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>Collect Progman + all WorkerW HWNDs (multi-monitor desktop hosts).</summary>
    public static List<IntPtr> GetDesktopHostWindows()
    {
        var list = new List<IntPtr>();
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
            list.Add(progman);

        EnumWindows((hwnd, _) =>
        {
            if (GetWindowClass(hwnd) == "WorkerW" && IsWindowVisible(hwnd))
                list.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        return list;
    }

    /// <summary>Desktop hosts that actually own icons (have SHELLDLL_DefView).</summary>
    public static List<IntPtr> GetDesktopIconHostWindows()
    {
        var hosts = new List<IntPtr>();
        foreach (var host in GetDesktopHostWindows())
        {
            if (FindShellDefView(host) != IntPtr.Zero)
                hosts.Add(host);
        }
        return hosts;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int StrCmpLogicalW(string psz1, string psz2);

    public const int WmKeyup = 0x0101;
    public const int WmChar = 0x0102;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    /// <summary>Find SHELLDLL_DefView under Explorer/Desktop host (Win10/11).</summary>
    public static IntPtr FindShellDefView(IntPtr shellHwnd)
    {
        if (shellHwnd == IntPtr.Zero)
            return IntPtr.Zero;

        var def = FindWindowEx(shellHwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (def != IntPtr.Zero)
            return def;

        // Win11: nested under ShellTabWindowClass / Microsoft.UI.Content.DesktopChildSiteBridge etc.
        return FindDescendantByClass(shellHwnd, "SHELLDLL_DefView", maxDepth: 10);
    }

    /// <summary>ListView / DirectUI that actually owns arrow navigation.</summary>
    public static IntPtr FindShellViewInputTarget(IntPtr shellHwnd)
    {
        var def = FindShellDefView(shellHwnd);
        if (def == IntPtr.Zero)
            return shellHwnd;

        var list = FindWindowEx(def, IntPtr.Zero, "SysListView32", null);
        if (list != IntPtr.Zero)
            return list;

        var dui = FindWindowEx(def, IntPtr.Zero, "DirectUIHWND", null);
        if (dui != IntPtr.Zero)
            return dui;

        return def;
    }

    public static IntPtr FindDescendantByClass(IntPtr parent, string className, int maxDepth)
    {
        if (parent == IntPtr.Zero || maxDepth < 0)
            return IntPtr.Zero;

        IntPtr child = IntPtr.Zero;
        while (true)
        {
            child = FindWindowEx(parent, child, null, null);
            if (child == IntPtr.Zero)
                break;

            if (string.Equals(GetWindowClass(child), className, StringComparison.OrdinalIgnoreCase))
                return child;

            var nested = FindDescendantByClass(child, className, maxDepth - 1);
            if (nested != IntPtr.Zero)
                return nested;
        }

        return IntPtr.Zero;
    }

    /// <summary>Post arrow key to Explorer/Desktop view so the real highlight moves.</summary>
    public static bool PostArrowToShellView(IntPtr shellHwnd, uint vk)
    {
        if (shellHwnd == IntPtr.Zero)
            return false;

        var target = FindShellViewInputTarget(shellHwnd);
        if (target == IntPtr.Zero)
            target = shellHwnd;

        // Brief focus attach so Win11 DirectUI accepts keys while preview is topmost
        var fg = GetForegroundWindow();
        var targetThread = GetWindowThreadProcessId(target, out _);
        var fgThread = GetWindowThreadProcessId(fg, out _);
        var thisThread = GetCurrentThreadId();
        var attachedFg = false;
        var attachedThis = false;

        try
        {
            if (fgThread != 0 && fgThread != targetThread)
                attachedFg = AttachThreadInput(fgThread, targetThread, true);
            if (thisThread != targetThread)
                attachedThis = AttachThreadInput(thisThread, targetThread, true);

            SetForegroundWindow(shellHwnd);
            SetFocus(target);

            var wp = new IntPtr(unchecked((int)vk));
            // lParam: repeat=1, scan omitted — enough for DefView/ListView
            var lp = new IntPtr(1);
            PostMessage(target, WmKeydown, wp, lp);
            PostMessage(target, WmKeyup, wp, new IntPtr(1 | (1 << 30) | (1 << 31)));
            return true;
        }
        finally
        {
            if (attachedThis)
                AttachThreadInput(thisThread, targetThread, false);
            if (attachedFg)
                AttachThreadInput(fgThread, targetThread, false);
        }
    }

    [DllImport("user32.dll")]
    public static extern uint GetDoubleClickTime();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegisterApplicationRestart(string? pwzCommandLine, int dwFlags);

    public const int RestartNoCrash = 1;
    public const int RestartNoHang = 2;
    public const int RestartNoPatch = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}


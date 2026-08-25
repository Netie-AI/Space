using System.Diagnostics;
using System.Runtime.InteropServices;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

public sealed class GlobalKeyboardHook : IDisposable
{
    private readonly Win32Api.LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _disposed;

    /// <summary>Invoked on Space. Return true to swallow the key.</summary>
    public Func<bool>? SpacePressed { get; set; }

    /// <summary>Invoked on Escape while preview may be open. Return true to swallow.</summary>
    public Func<bool>? EscapePressed { get; set; }

    /// <summary>
    /// Invoked for Up/Down/Left/Right while preview may be open.
    /// Return true to swallow (Mac Quick Look–style sibling travel).
    /// </summary>
    public Func<uint, bool>? ArrowPressed { get; set; }

    /// <summary>Home / End → first / last sibling. Return true to swallow.</summary>
    public Func<uint, bool>? HomeEndPressed { get; set; }

    public GlobalKeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero)
            return;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule
            ?? throw new InvalidOperationException("Unable to resolve main module for keyboard hook.");

        _hookId = Win32Api.SetWindowsHookEx(
            Win32Api.WhKeyboardLl,
            _proc,
            Win32Api.GetModuleHandle(curModule.ModuleName),
            0);

        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException("Failed to register global keyboard hook.");
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            Win32Api.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)Win32Api.WmKeydown || wParam == (IntPtr)Win32Api.WmSyskeydown))
        {
            var data = Marshal.PtrToStructure<Win32Api.KbdLlHookStruct>(lParam);
            var swallow = false;
            try
            {
                if (data.vkCode == Win32Api.VkSpace)
                {
                    swallow = SpacePressed?.Invoke() == true;
                }
                else if (data.vkCode == Win32Api.VkEscape)
                {
                    swallow = EscapePressed?.Invoke() == true;
                }
                else if (data.vkCode is Win32Api.VkUp or Win32Api.VkDown or Win32Api.VkLeft or Win32Api.VkRight)
                {
                    swallow = ArrowPressed?.Invoke(data.vkCode) == true;
                }
                else if (data.vkCode is Win32Api.VkHome or Win32Api.VkEnd)
                {
                    swallow = HomeEndPressed?.Invoke(data.vkCode) == true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Keyboard hook handler failed");
            }

            if (swallow)
                return (IntPtr)1;
        }

        return Win32Api.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public static bool IsExplorerOrDesktopForeground()
    {
        // Prefer cursor: clicking a Desktop icon often leaves focus quirks on Win11
        var candidates = new[]
        {
            Win32Api.GetWindowUnderCursor(),
            Win32Api.GetExplorerContextHwnd(),
            Win32Api.GetForegroundWindow()
        };

        foreach (var hwnd in candidates)
        {
            if (hwnd == IntPtr.Zero)
                continue;

            Win32Api.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
                continue;

            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (!process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            catch
            {
                continue;
            }

            if (Win32Api.TryFindExplorerShellWindow(hwnd, out _, out _))
                return true;

            // Win11 XAML islands / DirectUI: class alone is enough under explorer.exe
            var cls = Win32Api.GetWindowClass(hwnd);
            if (Win32Api.IsExplorerViewClass(cls))
                return true;

            // Root of an explorer window even when leaf class is unfamiliar
            var root = Win32Api.GetRootWindow(hwnd);
            if (root != IntPtr.Zero && Win32Api.IsExplorerViewClass(Win32Api.GetWindowClass(root)))
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _disposed = true;
    }
}

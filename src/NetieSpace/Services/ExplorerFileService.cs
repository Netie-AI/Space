using System.Runtime.InteropServices;
using System.Windows.Automation;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

public sealed class ExplorerFileService
{
    // ShellWindows coclass (NOT IWebBrowserApp)
    private static readonly Guid ShellWindowsClsid = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");

    public string? GetSelectedFilePath() => GetSelectedFilePath(Win32Api.GetForegroundWindow(), IntPtr.Zero);

    public string? GetSelectedFilePath(IntPtr foreground) => GetSelectedFilePath(foreground, IntPtr.Zero);

    public string? GetSelectedFilePath(IntPtr foreground, IntPtr cursorHwnd)
    {
        if (foreground == IntPtr.Zero && cursorHwnd == IntPtr.Zero)
        {
            AppLogger.Warn("No foreground/cursor window for selection lookup");
            return null;
        }

        var preferred = PreferContextHwnd(cursorHwnd, foreground);
        Win32Api.TryFindExplorerShellWindow(preferred, out var shellHwnd, out var className);
        var fgClass = Win32Api.GetWindowClass(preferred);
        AppLogger.Info(
            $"Selection lookup preferred=0x{preferred.ToInt64():X} class={fgClass} " +
            $"shell=0x{shellHwnd.ToInt64():X} shellClass={className} " +
            $"cursor=0x{cursorHwnd.ToInt64():X} fg=0x{foreground.ToInt64():X}");

        var target = shellHwnd != IntPtr.Zero ? shellHwnd : preferred;
        var isDesktop = Win32Api.IsDesktopClass(className) || Win32Api.IsDesktopClass(fgClass);

        // Also treat cursor-over-desktop hosts even if preferred hwnd classification failed
        if (!isDesktop && cursorHwnd != IntPtr.Zero)
        {
            Win32Api.TryFindExplorerShellWindow(cursorHwnd, out _, out var cursorClass);
            if (Win32Api.IsDesktopClass(cursorClass))
                isDesktop = true;
        }

        if (isDesktop)
        {
            var desktopPath = GetDesktopSelectedFilePath(target);
            if (!string.IsNullOrWhiteSpace(desktopPath))
            {
                AppLogger.Info($"Desktop selection: {desktopPath}");
                return desktopPath;
            }

            AppLogger.Warn("Desktop context with no icon selection — refusing Explorer fallthrough");
            return null;
        }

        var explorerPath =
            GetSelectedViaShellApplication(target, allowCrossWindowFallback: false) ??
            GetSelectedViaShellWindowsClsid(target, allowCrossWindowFallback: false) ??
            GetSelectedViaUiAutomation(target);

        if (string.IsNullOrWhiteSpace(explorerPath))
        {
            explorerPath =
                GetSelectedViaShellApplication(target, allowCrossWindowFallback: true) ??
                GetSelectedViaShellWindowsClsid(target, allowCrossWindowFallback: true) ??
                GetSelectedFromTopExplorerWindow();
        }

        if (!string.IsNullOrWhiteSpace(explorerPath))
        {
            AppLogger.Info($"Explorer selection: {explorerPath}");
            return explorerPath;
        }

        AppLogger.Warn("All selection strategies returned empty");
        return null;
    }

    /// <summary>
    /// Mac Quick Look–style next/prev: forward the real arrow into Explorer so the
    /// highlight moves in-view order, then read the new selection. Falls back to
    /// Shell SelectItem + logical name sort.
    /// </summary>
    public string? NavigateAdjacent(string currentPath, int delta, IntPtr preferredHwnd = default, uint vk = 0)
    {
        if (string.IsNullOrWhiteSpace(currentPath) || delta == 0)
            return null;

        if (vk == 0)
            vk = delta > 0 ? (uint)Win32Api.VkDown : (uint)Win32Api.VkUp;

        // Sync path used as fallback after async key attempt fails
        var fromShell = NavigateAdjacentViaShell(currentPath, delta, preferredHwnd);
        if (!string.IsNullOrWhiteSpace(fromShell))
            return fromShell;

        var next = GetAdjacentPath(currentPath, delta);
        if (!string.IsNullOrWhiteSpace(next))
            TrySelectPath(preferredHwnd, next);
        return next;
    }

    /// <summary>Post arrow into Explorer/Desktop; returns shell HWND used (or Zero).</summary>
    public IntPtr TryPostArrowToExplorer(string currentPath, IntPtr preferredHwnd, uint vk)
    {
        var shellHwnd = ResolveShellHwndForPath(currentPath, preferredHwnd);
        if (shellHwnd == IntPtr.Zero)
            return IntPtr.Zero;
        return Win32Api.PostArrowToShellView(shellHwnd, vk) ? shellHwnd : IntPtr.Zero;
    }

    public string? ReadSelectionNear(IntPtr shellHwnd, string? currentPath)
    {
        if (shellHwnd == IntPtr.Zero)
            return null;
        var selected = GetSelectedFilePath(shellHwnd, IntPtr.Zero);
        if (string.IsNullOrWhiteSpace(selected))
            return null;
        if (!string.IsNullOrWhiteSpace(currentPath) &&
            string.Equals(selected, currentPath, StringComparison.OrdinalIgnoreCase))
            return null;
        return selected;
    }

    private IntPtr ResolveShellHwndForPath(string currentPath, IntPtr preferredHwnd)
    {
        if (preferredHwnd != IntPtr.Zero &&
            Win32Api.TryFindExplorerShellWindow(preferredHwnd, out var shell, out _))
            return shell != IntPtr.Zero ? shell : preferredHwnd;

        var dir = IoDirectory.Exists(currentPath)
            ? currentPath.TrimEnd('\\')
            : IoPath.GetDirectoryName(currentPath)?.TrimEnd('\\');
        if (string.IsNullOrWhiteSpace(dir))
            return IntPtr.Zero;

        // Desktop special folder
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            .TrimEnd('\\');
        if (string.Equals(dir, desktop, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var host in Win32Api.GetDesktopHostWindows())
            {
                if (Win32Api.FindShellDefView(host) != IntPtr.Zero)
                    return host;
            }
        }

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
                return IntPtr.Zero;

            dynamic shellApp = Activator.CreateInstance(shellType)!;
            dynamic windows = shellApp.Windows();
            var count = (int)windows.Count;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    string? folderPath = null;
                    try { folderPath = ((string)window.Document.Folder.Self.Path)?.TrimEnd('\\'); }
                    catch { continue; }

                    if (!string.Equals(folderPath, dir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    return ToHwnd(window.HWND);
                }
                catch
                {
                    // skip
                }
            }
        }
        catch
        {
            // ignore
        }

        return IntPtr.Zero;
    }

    private string? NavigateAdjacentViaShell(string currentPath, int delta, IntPtr preferredHwnd)
    {
        try
        {
            var dir = IoDirectory.Exists(currentPath)
                ? currentPath.TrimEnd('\\')
                : IoPath.GetDirectoryName(currentPath)?.TrimEnd('\\');
            if (string.IsNullOrWhiteSpace(dir))
                return null;

            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
                return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic windows = shell.Windows();
            var count = (int)windows.Count;

            dynamic? bestWindow = null;
            var bestScore = int.MinValue;

            for (var i = 0; i < count; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    string? folderPath = null;
                    try { folderPath = ((string)window.Document.Folder.Self.Path)?.TrimEnd('\\'); }
                    catch { continue; }

                    if (!string.Equals(folderPath, dir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var score = 10;
                    var hwnd = ToHwnd(window.HWND);
                    if (preferredHwnd != IntPtr.Zero && HwndMatches(hwnd, preferredHwnd))
                        score += 1000;
                    if (Win32Api.IsWindowVisible(hwnd))
                        score += 50;

                    if (score >= bestScore)
                    {
                        bestScore = score;
                        bestWindow = window;
                    }
                }
                catch
                {
                    // skip
                }
            }

            if (bestWindow is null)
                return null;

            dynamic document = bestWindow.Document;
            dynamic folderItems = document.Folder.Items();
            var itemCount = (int)folderItems.Count;
            if (itemCount < 1)
                return null;

            var paths = new List<string>(itemCount);
            for (var j = 0; j < itemCount; j++)
            {
                try
                {
                    dynamic item = folderItems.Item(j);
                    string path = item.Path;
                    if (PathExists(path))
                        paths.Add(path);
                }
                catch
                {
                    // skip
                }
            }

            if (paths.Count == 0)
                return null;

            var idx = paths.FindIndex(p => string.Equals(p, currentPath, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
                idx = 0;

            var nextIdx = idx + delta;
            if (nextIdx < 0 || nextIdx >= paths.Count)
                return null;

            var nextPath = paths[nextIdx];

            // Re-find item object to SelectItem
            for (var j = 0; j < itemCount; j++)
            {
                try
                {
                    dynamic item = folderItems.Item(j);
                    string path = item.Path;
                    if (!string.Equals(path, nextPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // SVSI_SELECT | DESELECTOTHERS | ENSUREVISIBLE | FOCUSED
                    document.SelectItem(item, 1 | 4 | 8 | 16);
                    AppLogger.Info($"Shell adjacent → {nextPath}");
                    return nextPath;
                }
                catch
                {
                    // skip
                }
            }

            return nextPath;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"NavigateAdjacentViaShell failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Mac Quick Look–style: next/prev sibling in the same folder (Explorer logical name sort).
    /// </summary>
    public string? GetAdjacentPath(string currentPath, int delta)
    {
        if (string.IsNullOrWhiteSpace(currentPath) || delta == 0)
            return null;

        try
        {
            var dir = IoDirectory.Exists(currentPath)
                ? currentPath
                : IoPath.GetDirectoryName(currentPath);
            if (string.IsNullOrWhiteSpace(dir) || !IoDirectory.Exists(dir))
                return null;

            var entries = IoDirectory.EnumerateFileSystemEntries(dir)
                .Where(p =>
                {
                    var name = IoPath.GetFileName(p) ?? "";
                    return name is not ("." or "..") &&
                           !name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(p => IoPath.GetFileName(p), Comparer<string>.Create(Win32Api.StrCmpLogicalW))
                .ToList();

            if (entries.Count == 0)
                return null;

            var idx = entries.FindIndex(p =>
                string.Equals(p, currentPath, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
                idx = 0;

            var next = idx + delta;
            if (next < 0 || next >= entries.Count)
                return null;

            return entries[next];
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"GetAdjacentPath failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>First or last sibling in the same folder (Explorer logical name sort).</summary>
    public string? GetExtremePath(string currentPath, bool toEnd)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
            return null;

        try
        {
            var dir = IoDirectory.Exists(currentPath)
                ? currentPath
                : IoPath.GetDirectoryName(currentPath);
            if (string.IsNullOrWhiteSpace(dir) || !IoDirectory.Exists(dir))
                return null;

            var entries = IoDirectory.EnumerateFileSystemEntries(dir)
                .Where(p =>
                {
                    var name = IoPath.GetFileName(p) ?? "";
                    return name is not ("." or "..") &&
                           !name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(p => IoPath.GetFileName(p), Comparer<string>.Create(Win32Api.StrCmpLogicalW))
                .ToList();

            if (entries.Count == 0)
                return null;

            return toEnd ? entries[^1] : entries[0];
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"GetExtremePath failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Best-effort: update Explorer/Desktop selection to match path (may fail silently).</summary>
    public void TrySelectPath(IntPtr explorerHwnd, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !PathExists(path))
            return;

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
                return;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic windows = shell.Windows();
            var count = (int)windows.Count;
            var dir = IoDirectory.Exists(path) ? path : IoPath.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(dir))
                return;

            for (var i = 0; i < count; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    if (window is null)
                        continue;

                    string? folderPath = null;
                    try { folderPath = (string)window.Document.Folder.Self.Path; }
                    catch { continue; }

                    if (!string.Equals(folderPath, dir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Prefer HWND match when available
                    var windowHwnd = ToHwnd(window.HWND);
                    if (explorerHwnd != IntPtr.Zero && !HwndMatches(windowHwnd, explorerHwnd) &&
                        !Win32Api.IsDesktopClass(Win32Api.GetWindowClass(Win32Api.GetRootWindow(explorerHwnd))))
                    {
                        // still allow if only one matching folder
                    }

                    dynamic items = window.Document.Folder.Items();
                    var itemCount = (int)items.Count;
                    for (var j = 0; j < itemCount; j++)
                    {
                        dynamic item = items.Item(j);
                        string itemPath = item.Path;
                        if (!string.Equals(itemPath, path, StringComparison.OrdinalIgnoreCase))
                            continue;

                        window.Document.SelectItem(item, 1 | 4 | 8 | 16);
                        return;
                    }
                }
                catch
                {
                    // skip window
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"TrySelectPath failed: {ex.Message}");
        }
    }

    public static bool PathExists(string? path) =>
        !string.IsNullOrWhiteSpace(path) && (IoFile.Exists(path) || IoDirectory.Exists(path));

    private static IntPtr PreferContextHwnd(IntPtr cursorHwnd, IntPtr foreground)
    {
        if (cursorHwnd != IntPtr.Zero &&
            Win32Api.TryFindExplorerShellWindow(cursorHwnd, out _, out var cursorClass) &&
            Win32Api.IsExplorerShellClass(cursorClass))
        {
            return cursorHwnd;
        }

        return foreground != IntPtr.Zero ? foreground : cursorHwnd;
    }

    private static string? GetSelectedViaShellApplication(IntPtr targetHwnd, bool allowCrossWindowFallback)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
                return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic windows = shell.Windows();
            return ReadSelectionFromWindowsCollection(windows, targetHwnd, allowCrossWindowFallback);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Shell.Application.Windows failed: {ex.Message}");
            return null;
        }
    }

    private static string? GetSelectedViaShellWindowsClsid(IntPtr targetHwnd, bool allowCrossWindowFallback)
    {
        try
        {
            var type = Type.GetTypeFromCLSID(ShellWindowsClsid);
            if (type is null)
                return null;

            dynamic shellWindows = Activator.CreateInstance(type)!;
            return ReadSelectionFromWindowsCollection(shellWindows, targetHwnd, allowCrossWindowFallback);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"ShellWindows CLSID failed: {ex.Message}");
            return null;
        }
    }

    private static string? ReadSelectionFromWindowsCollection(
        dynamic windows, IntPtr targetHwnd, bool allowCrossWindowFallback)
    {
        var count = (int)windows.Count;
        string? onlySelection = null;
        var selectionCount = 0;

        for (var i = 0; i < count; i++)
        {
            try
            {
                dynamic window = windows.Item(i);
                if (window is null)
                    continue;

                var windowHwnd = ToHwnd(window.HWND);
                var matches = HwndMatches(windowHwnd, targetHwnd);
                var path = FirstSelectedPath(window.Document);
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (matches)
                    return path;

                selectionCount++;
                onlySelection = path;
            }
            catch
            {
                // skip
            }
        }

        if (!allowCrossWindowFallback)
            return null;

        return selectionCount == 1 ? onlySelection : null;
    }

    private static string? GetSelectedFromTopExplorerWindow()
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
                return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic windows = shell.Windows();
            var count = (int)windows.Count;

            string? bestPath = null;
            var bestScore = int.MinValue;

            for (var i = 0; i < count; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    var hwnd = ToHwnd(window.HWND);
                    if (hwnd == IntPtr.Zero)
                        continue;

                    var path = FirstSelectedPath(window.Document);
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    var score = i;
                    if (Win32Api.IsWindowVisible(hwnd))
                        score += 1000;
                    var cls = Win32Api.GetWindowClass(Win32Api.GetRootWindow(hwnd));
                    if (cls is "CabinetWClass" or "ExploreWClass")
                        score += 5000;

                    if (score >= bestScore)
                    {
                        bestScore = score;
                        bestPath = path;
                    }
                }
                catch
                {
                    // skip
                }
            }

            return bestPath;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Top explorer fallback failed: {ex.Message}");
            return null;
        }
    }

    private static bool HwndMatches(IntPtr windowHwnd, IntPtr targetHwnd)
    {
        if (windowHwnd == IntPtr.Zero || targetHwnd == IntPtr.Zero)
            return false;

        if (windowHwnd == targetHwnd)
            return true;

        if (Win32Api.GetRootWindow(targetHwnd) == windowHwnd)
            return true;

        var w = windowHwnd.ToInt64() & 0xFFFFFFFF;
        var t = targetHwnd.ToInt64() & 0xFFFFFFFF;
        return w != 0 && w == t;
    }

    private static string? FirstSelectedPath(dynamic? document)
    {
        if (document is null)
            return null;

        try
        {
            dynamic items = document.SelectedItems();
            var path = FirstExistingPathFromFolderItems(items);
            if (!string.IsNullOrWhiteSpace(path))
                return path;
        }
        catch
        {
            // try FocusedItem
        }

        try
        {
            dynamic focused = document.FocusedItem;
            if (focused is not null)
            {
                string path = focused.Path;
                if (PathExists(path))
                    return path;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string? FirstExistingPathFromFolderItems(dynamic items)
    {
        if (items is null)
            return null;

        var count = (int)items.Count;
        if (count < 1)
            return null;

        for (var i = 0; i < count; i++)
        {
            try
            {
                dynamic item = items.Item(i);
                if (item is null)
                    continue;

                string path = item.Path;
                if (PathExists(path))
                    return path;
            }
            catch
            {
                // skip
            }
        }

        return null;
    }

    private static IEnumerable<string> DesktopDirectories()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        if (!string.IsNullOrWhiteSpace(publicDesktop))
            yield return publicDesktop;
    }

    private static string? GetDesktopSelectedFilePath(IntPtr desktopHwnd)
    {
        // A) SysListView32 cross-process — exact filename even when extensions are hidden
        var fromListView = GetDesktopSelectedViaListView();
        if (!string.IsNullOrWhiteSpace(fromListView))
            return FinalizeDesktopPath(fromListView);

        // B) Shell.Windows desktop-folder scan
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is not null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic windows = shell.Windows();
                var count = (int)windows.Count;
                var desktopDirs = DesktopDirectories()
                    .Where(IoDirectory.Exists)
                    .Select(d => d.TrimEnd('\\'))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic window = windows.Item(i);
                        string? folderPath = null;
                        try { folderPath = ((string)window.Document.Folder.Self.Path)?.TrimEnd('\\'); }
                        catch { continue; }

                        if (folderPath is null || !desktopDirs.Contains(folderPath))
                            continue;

                        var path = FirstSelectedPath(window.Document);
                        if (!string.IsNullOrWhiteSpace(path))
                            return FinalizeDesktopPath(path);
                    }
                    catch
                    {
                        // skip
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Desktop via Shell desktop-folder failed: {ex.Message}");
        }

        // C) HWND-matched shell window (legacy)
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is not null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                var fromWindows = ReadSelectionFromWindowsCollection(shell.Windows(), desktopHwnd, allowCrossWindowFallback: false);
                if (!string.IsNullOrWhiteSpace(fromWindows))
                    return FinalizeDesktopPath(fromWindows);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Desktop via Shell.Windows hwnd match failed: {ex.Message}");
        }

        // D) UIA only on hosts that own desktop icons — DefView / ListView roots first
        foreach (var host in Win32Api.GetDesktopIconHostWindows())
        {
            try
            {
                var names = GetDesktopSelectedItemNames(host);
                var resolved = ResolveNamesUnderDesktop(names);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return FinalizeDesktopPath(resolved);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Desktop UIA host 0x{host.ToInt64():X} failed: {ex.Message}");
            }
        }

        // E) UIA on specific target
        try
        {
            foreach (var root in DesktopUiAutomationRoots(desktopHwnd))
            {
                var names = GetSelectedItemNamesViaUiAutomation(root);
                var resolved = ResolveNamesUnderDesktop(names);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return FinalizeDesktopPath(resolved);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Desktop UIA target failed: {ex.Message}");
        }

        return null;
    }

    private const int LvmGetNextItem = 0x100C;
    private const int LvmGetItemTextW = 0x1073;
    private const int LvniSelected = 0x0002;
    private const int LvniFocused = 0x0001;
    private const int LvifText = 0x0001;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint MemCommit = 0x1000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LvItemW
    {
        public int mask;
        public int iItem;
        public int iSubItem;
        public int state;
        public int stateMask;
        public IntPtr pszText;
        public int cchTextMax;
        public int iImage;
        public IntPtr lParam;
        public int iIndent;
        public int iGroupId;
        public int cColumns;
        public IntPtr puColumns;
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static string? GetDesktopSelectedViaListView()
    {
        foreach (var host in Win32Api.GetDesktopIconHostWindows())
        {
            var list = Win32Api.FindShellViewInputTarget(host);
            if (list == IntPtr.Zero ||
                !string.Equals(Win32Api.GetWindowClass(list), "SysListView32", StringComparison.OrdinalIgnoreCase))
                continue;

            var idx = (int)Win32Api.SendMessage(list, LvmGetNextItem, (IntPtr)(-1), (IntPtr)LvniSelected);
            if (idx < 0)
                idx = (int)Win32Api.SendMessage(list, LvmGetNextItem, (IntPtr)(-1), (IntPtr)LvniFocused);
            if (idx < 0)
                continue;

            var label = GetListViewItemTextCrossProcess(list, idx);
            if (string.IsNullOrWhiteSpace(label))
                continue;

            AppLogger.Info($"Desktop ListView label: {label}");
            var resolved = ResolveNamesUnderDesktop([label]);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }

        return null;
    }

    private static string? GetListViewItemTextCrossProcess(IntPtr listView, int index)
    {
        GetWindowThreadProcessId(listView, out var pid);
        if (pid == 0)
            return null;

        var hProcess = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessVmWrite, false, pid);
        if (hProcess == IntPtr.Zero)
            return null;

        const int chars = 512;
        var textBytes = (uint)(chars * sizeof(char));
        var itemSize = (uint)Marshal.SizeOf<LvItemW>();
        var remoteText = IntPtr.Zero;
        var remoteItem = IntPtr.Zero;
        var localItemPtr = IntPtr.Zero;
        try
        {
            remoteText = VirtualAllocEx(hProcess, IntPtr.Zero, textBytes, MemCommit, PageReadWrite);
            remoteItem = VirtualAllocEx(hProcess, IntPtr.Zero, itemSize, MemCommit, PageReadWrite);
            if (remoteText == IntPtr.Zero || remoteItem == IntPtr.Zero)
                return null;

            var item = new LvItemW
            {
                mask = LvifText,
                iItem = index,
                iSubItem = 0,
                pszText = remoteText,
                cchTextMax = chars
            };
            localItemPtr = Marshal.AllocHGlobal((int)itemSize);
            Marshal.StructureToPtr(item, localItemPtr, false);
            if (!WriteProcessMemory(hProcess, remoteItem, localItemPtr, itemSize, out _))
                return null;

            var len = (int)Win32Api.SendMessage(listView, LvmGetItemTextW, (IntPtr)index, remoteItem);
            if (len <= 0)
                return null;

            var buf = new byte[Math.Min(len + 1, chars) * sizeof(char)];
            if (!ReadProcessMemory(hProcess, remoteText, buf, buf.Length, out _))
                return null;

            return System.Text.Encoding.Unicode.GetString(buf).TrimEnd('\0').Trim();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Desktop ListView read failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (localItemPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(localItemPtr);
            if (remoteText != IntPtr.Zero)
                VirtualFreeEx(hProcess, remoteText, 0, MemRelease);
            if (remoteItem != IntPtr.Zero)
                VirtualFreeEx(hProcess, remoteItem, 0, MemRelease);
            CloseHandle(hProcess);
        }
    }

    private static IEnumerable<IntPtr> DesktopUiAutomationRoots(IntPtr host)
    {
        if (host == IntPtr.Zero)
            yield break;

        var input = Win32Api.FindShellViewInputTarget(host);
        if (input != IntPtr.Zero)
            yield return input;

        var defView = Win32Api.FindShellDefView(host);
        if (defView != IntPtr.Zero)
            yield return defView;

        yield return host;
    }

    private static List<string> GetDesktopSelectedItemNames(IntPtr host)
    {
        foreach (var root in DesktopUiAutomationRoots(host))
        {
            var names = GetSelectedItemNamesViaUiAutomation(root);
            if (names.Count > 0)
                return names;
        }
        return [];
    }

    private static string? FinalizeDesktopPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !PathExists(path))
            return path;

        if (!IoPath.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return path;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return path;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(path);
            string target = shortcut.TargetPath;
            if (!string.IsNullOrWhiteSpace(target) && PathExists(target))
            {
                AppLogger.Info($"Desktop .lnk resolved: {IoPath.GetFileName(path)} → {target}");
                return target;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Desktop .lnk resolve failed: {ex.Message}");
        }

        return path;
    }

    /// <summary>Extensions Windows often hides on desktop icon labels (UIA returns stem only).</summary>
    private static readonly string[] DesktopVideoProbeExtensions =
    [
        ".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".m4v", ".mpeg", ".mpg",
        ".hevc", ".h265", ".h264", ".3gp", ".flv", ".ts", ".m2ts", ".mts", ".ogv", ".vob"
    ];

    private static readonly string[] DesktopPresentationProbeExtensions =
    [
        ".pptx", ".ppt", ".ppsx", ".pps", ".odp"
    ];

    private static readonly string[] DesktopHiddenProbeExtensions =
    [
        ".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".m4v", ".mpeg", ".mpg",
        ".hevc", ".h265", ".mp3", ".wav", ".flac", ".m4a", ".aac",
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tif", ".tiff",
        ".svg", ".ico", ".heic", ".heif",
        ".pdf", ".docx", ".doc", ".odt", ".rtf", ".pptx", ".ppt", ".ppsx", ".pps", ".odp",
        ".xlsx", ".xlsm", ".xls", ".ods",
        ".txt", ".text", ".log", ".csv", ".json", ".xml", ".cs", ".js", ".ts", ".tsx", ".jsx",
        ".py", ".java", ".cpp", ".c", ".h", ".hpp", ".rs", ".go", ".rb", ".php",
        ".yaml", ".yml", ".toml", ".ini", ".cfg", ".sql", ".sh", ".ps1", ".bat",
        ".css", ".scss", ".vue", ".swift", ".kt", ".md", ".markdown", ".html", ".htm",
        ".zip", ".7z", ".rar", ".lnk"
    ];

    private static readonly HashSet<string> DesktopPreferVideoExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".mpeg", ".mpg",
        ".hevc", ".h265", ".h264", ".3gp", ".flv", ".ts", ".m2ts", ".mts", ".ogv", ".vob"
    };

    private static readonly HashSet<string> DesktopPreferPresentationExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pptx", ".ppt", ".ppsx", ".pps", ".odp"
    };

    private static string? ResolveNamesUnderDesktop(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
            return null;

        foreach (var desktopDir in DesktopDirectories())
        {
            if (!IoDirectory.Exists(desktopDir))
                continue;

            foreach (var name in names)
            {
                var clean = SanitizeDesktopUiName(name);
                if (string.IsNullOrWhiteSpace(clean))
                    continue;

                var path = IoPath.Combine(desktopDir, clean);
                if (PathExists(path))
                    return path;

                if (!IoPath.HasExtension(clean))
                {
                    var probed = ProbeDesktopExtensions(desktopDir, clean);
                    if (!string.IsNullOrWhiteSpace(probed))
                        return probed;
                }

                try
                {
                    var matches = IoDirectory.EnumerateFileSystemEntries(desktopDir)
                        .Where(p =>
                        {
                            var fileName = IoPath.GetFileName(p);
                            var stem = IoPath.GetFileNameWithoutExtension(p);
                            return string.Equals(fileName, clean, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(stem, clean, StringComparison.OrdinalIgnoreCase);
                        })
                        .ToList();
                    if (matches.Count == 1)
                        return matches[0];
                    if (matches.Count > 1)
                    {
                        var preferred = PreferDesktopAmbiguous(matches, clean);
                        if (preferred is not null)
                            return preferred;
                    }
                }
                catch
                {
                    // skip
                }
            }
        }

        AppLogger.Warn($"Desktop UIA names unresolved: [{string.Join(", ", names)}]");
        return null;
    }

    private static string? ProbeDesktopExtensions(string desktopDir, string clean)
    {
        var hits = new List<string>();
        foreach (var ext in DesktopVideoProbeExtensions
                     .Concat(DesktopPresentationProbeExtensions)
                     .Concat(DesktopHiddenProbeExtensions))
        {
            var probed = IoPath.Combine(desktopDir, clean + ext);
            if (PathExists(probed) && !hits.Contains(probed, StringComparer.OrdinalIgnoreCase))
                hits.Add(probed);
        }

        return hits.Count switch
        {
            0 => null,
            1 => hits[0],
            _ => PreferDesktopAmbiguous(hits, clean)
        };
    }

    private static string? PreferDesktopAmbiguous(IReadOnlyList<string> matches, string clean)
    {
        var video = matches.FirstOrDefault(p =>
            DesktopPreferVideoExt.Contains(IoPath.GetExtension(p)));
        if (video is not null)
        {
            AppLogger.Info(
                $"Desktop ambiguous stem '{clean}' ({matches.Count}) — preferring video {IoPath.GetFileName(video)}");
            return video;
        }

        var pres = matches.FirstOrDefault(p =>
            DesktopPreferPresentationExt.Contains(IoPath.GetExtension(p)));
        if (pres is not null)
        {
            AppLogger.Info(
                $"Desktop ambiguous stem '{clean}' ({matches.Count}) — preferring presentation {IoPath.GetFileName(pres)}");
            return pres;
        }

        var filesOnly = matches.Where(IoFile.Exists).ToList();
        if (filesOnly.Count == 1)
            return filesOnly[0];

        AppLogger.Warn($"Desktop ambiguous stem '{clean}' matched {matches.Count} items — cannot resolve");
        return null;
    }

    private static string SanitizeDesktopUiName(string name)
    {
        var clean = name.Trim();
        var nl = clean.IndexOfAny(['\r', '\n']);
        if (nl >= 0)
            clean = clean[..nl].TrimEnd();

        const string shortcutSuffix = " - Shortcut";
        if (clean.EndsWith(shortcutSuffix, StringComparison.OrdinalIgnoreCase))
            clean = clean[..^shortcutSuffix.Length].TrimEnd();
        return clean;
    }

    private static string? GetSelectedViaUiAutomation(IntPtr shellHwnd)
    {
        try
        {
            string? folderPath = null;
            try
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType is not null)
                {
                    dynamic shell = Activator.CreateInstance(shellType)!;
                    dynamic windows = shell.Windows();
                    var count = (int)windows.Count;
                    for (var i = 0; i < count; i++)
                    {
                        dynamic window = windows.Item(i);
                        var windowHwnd = ToHwnd(window.HWND);
                        if (!HwndMatches(windowHwnd, shellHwnd))
                            continue;

                        try { folderPath = (string)window.Document.Folder.Self.Path; }
                        catch
                        {
                            try
                            {
                                string url = window.LocationURL;
                                if (url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                                    folderPath = Uri.UnescapeDataString(url["file:///".Length..]).Replace('/', '\\');
                            }
                            catch { /* ignore */ }
                        }

                        break;
                    }
                }
            }
            catch
            {
                // continue
            }

            var names = GetSelectedItemNamesViaUiAutomation(shellHwnd);
            if (names.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(folderPath) && IoDirectory.Exists(folderPath))
            {
                foreach (var name in names)
                {
                    var path = IoPath.Combine(folderPath, name);
                    if (PathExists(path))
                        return path;

                    try
                    {
                        var matches = IoDirectory.EnumerateFileSystemEntries(folderPath)
                            .Where(p =>
                            {
                                var fileName = IoPath.GetFileName(p);
                                var stem = IoPath.GetFileNameWithoutExtension(p);
                                return string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(stem, name, StringComparison.OrdinalIgnoreCase);
                            })
                            .Take(2)
                            .ToList();
                        if (matches.Count == 1)
                            return matches[0];
                    }
                    catch { /* skip */ }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"UIA selection failed: {ex.Message}");
        }

        return null;
    }

    private static List<string> GetSelectedItemNamesViaUiAutomation(IntPtr hwnd)
    {
        var result = new List<string>();
        if (hwnd == IntPtr.Zero)
            return result;

        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root is null)
                return result;

            var condition = new PropertyCondition(SelectionItemPattern.IsSelectedProperty, true);
            var selected = root.FindAll(TreeScope.Descendants, condition);
            if (selected is null || selected.Count == 0)
                return result;

            foreach (AutomationElement el in selected)
            {
                try
                {
                    var name = el.Current.Name?.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        result.Add(name);
                }
                catch
                {
                    // skip
                }
            }
        }
        catch
        {
            // ignore
        }

        return result;
    }

    private static IntPtr ToHwnd(object? value)
    {
        if (value is null)
            return IntPtr.Zero;

        try
        {
            return value switch
            {
                IntPtr ptr => ptr,
                int i => new IntPtr(i),
                long l => new IntPtr(unchecked((int)(l & 0xFFFFFFFF))),
                uint ui => new IntPtr(unchecked((int)ui)),
                _ => new IntPtr(unchecked((int)(Convert.ToInt64(value) & 0xFFFFFFFF)))
            };
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
}

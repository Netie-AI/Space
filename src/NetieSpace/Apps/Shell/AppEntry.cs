using System.Reflection;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// Pre-mutex dispatcher. Runs as the first statement of App.OnStartup so an app process
/// (or the headless tool CLI) never takes the Space single-instance mutex, never installs
/// the keyboard hook and never shows the tray.
/// </summary>
public static class AppEntry
{
    public const int ExitInvalidArgs = 2;
    public const int ExitToolCliUnavailable = 3;
    private const string ToolCliType = "NetieSpace.Apps.Tools.ToolCli";

    /// <summary>
    /// True when the args belong to an app or the tool CLI. On true the Space startup must
    /// not run: either the process is already shutting down, or <see cref="App.AppSession"/>
    /// is set and OnStartup continues on the app path.
    /// </summary>
    public static bool TryHandle(string[] args, App app)
    {
        // Start Menu registration: HKCU / Start Menu only, headless, no mutex and no window.
        var registration = AppRegistration.Parse(args);
        if (registration != RegistrationSwitch.None)
        {
            app.Shutdown(AppShortcuts.Run(registration));
            return true;
        }

        var launch = AppLaunch.Parse(args);
        switch (launch.Kind)
        {
            case AppLaunchKind.Space:
                return false;

            case AppLaunchKind.ToolCli:
                app.Shutdown(RunToolCli(args));
                return true;

            case AppLaunchKind.Invalid:
                Console.Error.WriteLine(launch.Error);
                app.Shutdown(ExitInvalidArgs);
                return true;
        }

        var identity = launch.Identity!;
        var pid = Environment.ProcessId;

        // --snapshot renders headless and exits: no single-instance mutex (a running app must not
        // block it), no AUMID, no window, no dialog.
        var snapshot = SnapshotArgs.Parse(args);
        if (snapshot.Error is not null || snapshot.Requested)
        {
            try { Win32Api.AttachConsole(Win32Api.AttachParentProcess); } catch { /* no parent console */ }
            if (snapshot.Error is not null)
            {
                Console.Error.WriteLine(snapshot.Error);
                app.Shutdown(SnapshotArgs.ExitInvalid);
            }
            else
            {
                app.Shutdown(AppSnapshot.Run(identity, snapshot));
            }
            return true;
        }

        // Must run before any window handle exists or the taskbar ignores it.
        var aumid = ApplyAumid(identity);
        AppLogger.Info($"[app:{identity.Key} pid={pid}] AUMID={aumid}");

        var instance = new AppInstance(identity);
        if (!instance.TryAcquire())
        {
            AppLogger.Info($"[app:{identity.Key} pid={pid}] already running, forwarding {launch.OpenPath ?? "(no file)"}");
            instance.ForwardToRunning(launch.OpenPath);
            instance.Dispose();
            app.Shutdown(0);
            return true;
        }

        app.AppSession = new AppSession(identity, instance, launch.OpenPath, args);
        return true;
    }

    /// <summary>Sets the process AUMID and returns the value the shell reads back.</summary>
    private static string ApplyAumid(AppIdentity identity)
    {
        try
        {
            var hr = Win32Api.SetCurrentProcessExplicitAppUserModelID(identity.Aumid);
            if (hr != 0)
                return $"(set failed 0x{hr:X8})";
            hr = Win32Api.GetCurrentProcessExplicitAppUserModelID(out var readBack);
            return hr == 0 ? readBack : $"(read failed 0x{hr:X8})";
        }
        catch (Exception ex)
        {
            return $"(unavailable: {ex.Message})";
        }
    }

    /// <summary>
    /// Reaches NetieSpace.Apps.Tools.ToolCli.Run(string[]) : int by reflection so this
    /// ticket carries no compile-time edge into the tool registry. Never a silent 0.
    /// </summary>
    private static int RunToolCli(string[] args)
    {
        var run = typeof(AppEntry).Assembly
            .GetType(ToolCliType, throwOnError: false)?
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string[]) });
        if (run is null || run.ReturnType != typeof(int))
        {
            Console.Error.WriteLine("TOOL_CLI_UNAVAILABLE");
            return ExitToolCliUnavailable;
        }

        try
        {
            // A WinExe has no console of its own: borrow the caller's so the output is
            // visible in a terminal. Redirected stdout / stderr already work without it.
            try { Win32Api.AttachConsole(Win32Api.AttachParentProcess); } catch { /* no parent console */ }

            // Off the dispatcher thread: a runner that awaits must never be able to
            // deadlock on the WPF synchronization context while we block for its exit code.
            return Task.Run(() => (int)run.Invoke(null, new object[] { args })!).GetAwaiter().GetResult();
        }
        catch (TargetInvocationException ex)
        {
            var inner = ex.InnerException ?? ex;
            AppLogger.Error(inner, "Tool CLI");
            Console.Error.WriteLine($"TOOL_CLI_FAILED {inner.GetType().Name}: {inner.Message}");
            return 1;
        }
    }
}

/// <summary>What the first instance of an app process needs to open its window.</summary>
public sealed record AppSession(AppIdentity Identity, AppInstance Instance, string? OpenPath, string[] Args);

using System.Threading;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// One process per Netie app. The first launch owns the per-app mutex and listens on the
/// activation event; a later launch writes the file it was asked to open, sets the event
/// and exits. Names are per app and never the Space tray names.
/// </summary>
public sealed class AppInstance : IDisposable
{
    private readonly AppIdentity _identity;
    private Mutex? _mutex;
    private bool _owns;
    private EventWaitHandle? _activate;
    private Thread? _watcher;
    private volatile bool _disposed;

    public AppInstance(AppIdentity identity) => _identity = identity;

    /// <summary>True when this process is the app's first instance.</summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(true, _identity.InstanceMutexName, out var isNew);
        _owns = isNew;
        return isNew;
    }

    /// <summary>Second launch: hand the path (or nothing) to the running instance.</summary>
    public void ForwardToRunning(string? openPath)
    {
        try
        {
            IoDirectory.CreateDirectory(IoPath.GetDirectoryName(_identity.PendingOpenPath)!);
            IoFile.WriteAllText(_identity.PendingOpenPath, openPath ?? "");
            using var ping = EventWaitHandle.OpenExisting(_identity.ActivateEventName);
            ping.Set();
        }
        catch (Exception ex)
        {
            // The first instance may still be starting; it reads the pending file once ready.
            AppLogger.Warn($"[app:{_identity.Key}] forward failed: {ex.Message}");
        }
    }

    /// <summary>
    /// First instance: call back with the pending path (null when none) on every activation.
    /// The callback runs on the watcher thread; the caller marshals to the dispatcher.
    /// </summary>
    public void StartActivationWatcher(Action<string?> onActivate)
    {
        try
        {
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, _identity.ActivateEventName);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[app:{_identity.Key}] activate event: {ex.Message}");
            return;
        }

        _watcher = new Thread(() =>
        {
            while (!_disposed)
            {
                try
                {
                    _activate.WaitOne();
                    if (_disposed) return;
                    onActivate(TakePendingOpen());
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"[app:{_identity.Key}] activate watcher: {ex.Message}");
                }
            }
        })
        {
            IsBackground = true,
            Name = $"Netie.{_identity.ObjectName}.Activate",
        };
        _watcher.Start();
    }

    /// <summary>Reads and clears the pending-open file. Null when it is absent or empty.</summary>
    public string? TakePendingOpen()
    {
        try
        {
            var file = _identity.PendingOpenPath;
            if (!IoFile.Exists(file)) return null;
            var path = IoFile.ReadAllText(file).Trim().Trim('"');
            try { IoFile.Delete(file); } catch { /* ignore */ }
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[app:{_identity.Key}] pending-open read: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _activate?.Set(); } catch { /* wake watcher */ }
        try { _activate?.Dispose(); } catch { /* ignore */ }
        if (_owns)
        {
            try { _mutex?.ReleaseMutex(); } catch { /* ignore */ }
        }
        try { _mutex?.Dispose(); } catch { /* ignore */ }
        _mutex = null;
        _owns = false;
    }
}

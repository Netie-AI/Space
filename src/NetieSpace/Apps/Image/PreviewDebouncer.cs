using System;
using System.Threading;
using System.Threading.Tasks;

namespace NetieSpace.Apps.Image;

/// <summary>
/// Runs the newest piece of work once the options have been still for <see cref="Delay"/>; anything older is cancelled
/// before it starts or as it runs. The wait is injectable so the behaviour is tested without a clock.
/// </summary>
public sealed class PreviewDebouncer : IDisposable
{
    /// <summary>The page promises an updated preview within 300 ms of the last change; this is the share spent waiting.</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(120);

    private readonly Func<TimeSpan, CancellationToken, Task> _wait;
    private readonly object _gate = new();
    private CancellationTokenSource? _current;

    public PreviewDebouncer(TimeSpan? delay = null, Func<TimeSpan, CancellationToken, Task>? wait = null)
    {
        Delay = delay ?? DefaultDelay;
        _wait = wait ?? ((d, ct) => Task.Delay(d, ct));
    }

    public TimeSpan Delay { get; }

    /// <summary>The most recently scheduled run, so a test or a snapshot can await it.</summary>
    public Task Last { get; private set; } = Task.CompletedTask;

    /// <summary>Work that throws is reported here, never swallowed; a cancelled run is not an error.</summary>
    public event Action<Exception>? Failed;

    public Task Schedule(Func<CancellationToken, Task> work)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            _current?.Cancel();
            cts = _current = new CancellationTokenSource();
        }
        return Last = RunAsync(work, cts);
    }

    private async Task RunAsync(Func<CancellationToken, Task> work, CancellationTokenSource cts)
    {
        try
        {
            await _wait(Delay, cts.Token).ConfigureAwait(false);
            cts.Token.ThrowIfCancellationRequested();
            await work(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // a newer change replaced this one
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex);
        }
    }

    public void Cancel()
    {
        lock (_gate) _current?.Cancel();
    }

    public void Dispose()
    {
        Cancel();
    }
}

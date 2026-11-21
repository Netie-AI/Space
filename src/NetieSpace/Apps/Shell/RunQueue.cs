using NetieSpace.Apps.Tools;

namespace NetieSpace.Apps.Shell;

public enum QueueState { Waiting, Running, Done, Failed, Cancelled }

public sealed class QueueItem
{
    public QueueItem(string path) => Path = path;
    public string Path { get; }
    public string Name => IoPath.GetFileName(Path);
    public QueueState State { get; internal set; } = QueueState.Waiting;
    /// <summary>The runner's message when failed; the output path when done.</summary>
    public string? Message { get; internal set; }
}

/// <summary>
/// Per-file state of one run, and the token behind the Cancel button. Pure: the view reads
/// <see cref="Items"/> and calls the Mark methods from the progress and completion callbacks.
/// </summary>
public sealed class RunQueue
{
    /// <summary>
    /// Tools whose runner works on each input file on its own (ImageToolProvider.Batch), so a
    /// run can be split into one request per file. Every other tool may combine its inputs
    /// (merge, organize, one PDF from many pictures), so it is always run as one request.
    /// </summary>
    private static readonly HashSet<string> PerFileTools = new(StringComparer.Ordinal)
    {
        "img-resize", "img-crop", "img-rotate", "img-compress", "img-to-jpg",
        "img-from-jpg", "img-convert-any", "img-watermark", "img-dpi",
    };

    private readonly CancellationTokenSource _cts = new();
    private readonly List<QueueItem> _items;

    public RunQueue(IEnumerable<string> files, bool perFile)
    {
        _items = files.Select(f => new QueueItem(f)).ToList();
        PerFile = perFile;
    }

    public static bool IsPerFile(ToolEntry entry) => PerFileTools.Contains(entry.Id);

    public static RunQueue For(ToolEntry entry, IEnumerable<string> files) => new(files, IsPerFile(entry));

    public bool PerFile { get; }
    public IReadOnlyList<QueueItem> Items => _items;
    public CancellationToken Token => _cts.Token;
    public bool CancelRequested => _cts.IsCancellationRequested;

    /// <summary>Per-file tools show the list while running; a single combined request shows it only for several files.</summary>
    public bool ShowsList => _items.Count > 1;

    public bool Finished => _items.All(i => i.State is QueueState.Done or QueueState.Failed or QueueState.Cancelled);

    public int DoneCount => _items.Count(i => i.State == QueueState.Done);
    public int FailedCount => _items.Count(i => i.State == QueueState.Failed);

    /// <summary>Cancels the token and turns every waiting file into Cancelled. A running file ends through its own result.</summary>
    public void Cancel()
    {
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();
        foreach (var i in _items.Where(i => i.State == QueueState.Waiting))
            i.State = QueueState.Cancelled;
    }

    public void MarkRunning(int index) => Set(index, QueueState.Running, null);

    public void MarkDone(int index, string? output) => Set(index, QueueState.Done, output);

    public void MarkFailed(int index, string message) => Set(index, QueueState.Failed, message);

    /// <summary>Running state for every file that has not started (the one-request run).</summary>
    public void MarkAllRunning()
    {
        for (var i = 0; i < _items.Count; i++)
            if (_items[i].State == QueueState.Waiting)
                _items[i].State = QueueState.Running;
    }

    /// <summary>
    /// Settles every file still running from one combined result: Done on success, otherwise
    /// Failed with the runner's message (Cancelled when the run was cancelled).
    /// </summary>
    public void Settle(ToolResult result)
    {
        foreach (var i in _items.Where(i => i.State is QueueState.Running or QueueState.Waiting))
        {
            if (result.Ok)
            {
                i.State = QueueState.Done;
                i.Message = null;
            }
            else if (CancelRequested)
            {
                i.State = QueueState.Cancelled;
            }
            else
            {
                i.State = QueueState.Failed;
                i.Message = result.Error ?? result.Reason ?? "failed";
            }
        }
    }

    /// <summary>
    /// Drives the run. <paramref name="run"/> executes one request over the files it is given.
    /// Per-file: one call per file in order, a failure does not stop the rest, cancel stops
    /// before the next file. Otherwise one call over all files. Returns every result obtained.
    /// </summary>
    public async Task<IReadOnlyList<ToolResult>> RunAsync(
        Func<IReadOnlyList<string>, CancellationToken, Task<ToolResult>> run, Action? changed = null)
    {
        var results = new List<ToolResult>();
        if (!PerFile)
        {
            MarkAllRunning();
            changed?.Invoke();
            try
            {
                var r = await run(_items.Select(i => i.Path).ToList(), Token);
                results.Add(r);
                Settle(r);
            }
            catch (OperationCanceledException)
            {
                Cancel();
                foreach (var i in _items.Where(i => i.State == QueueState.Running))
                    i.State = QueueState.Cancelled;
            }
            changed?.Invoke();
            return results;
        }

        for (var n = 0; n < _items.Count; n++)
        {
            if (CancelRequested)
            {
                Cancel();
                break;
            }
            MarkRunning(n);
            changed?.Invoke();
            try
            {
                var r = await run(new[] { _items[n].Path }, Token);
                results.Add(r);
                if (r.Ok)
                    MarkDone(n, r.Outputs.Count > 0 ? r.Outputs[0] : null);
                else if (CancelRequested)
                    Set(n, QueueState.Cancelled, null);
                else
                    MarkFailed(n, r.Error ?? r.Reason ?? "failed");
            }
            catch (OperationCanceledException)
            {
                Set(n, QueueState.Cancelled, null);
            }
            changed?.Invoke();
        }
        changed?.Invoke();
        return results;
    }

    /// <summary>
    /// One result for the page after <see cref="RunAsync"/>: Ok only when every file is Done.
    /// A failure carries the runner's message verbatim (with a count when several files ran).
    /// </summary>
    public ToolResult Combine(IReadOnlyList<ToolResult> results, string toolId, string engine)
    {
        var outputs = results.SelectMany(r => r.Outputs).ToList();
        var elapsed = results.Sum(r => r.ElapsedMs);
        if (_items.Count > 0 && _items.All(i => i.State == QueueState.Done))
            return new ToolResult(true, ToolOutcome.Ok, toolId, engine, outputs, null, null, elapsed);

        string error;
        var failed = _items.Where(i => i.State == QueueState.Failed).ToList();
        if (failed.Count == 0)
            error = "Cancelled";
        else if (_items.Count == 1)
            error = failed[0].Message ?? "failed";
        else
            error = $"{failed.Count} of {_items.Count} failed: {failed[0].Name}: {failed[0].Message}";
        return new ToolResult(false, ToolOutcome.Failed, toolId, engine, outputs, error, null, elapsed);
    }

    public static string Label(QueueState s) => s switch
    {
        QueueState.Waiting => "Waiting",
        QueueState.Running => "Running",
        QueueState.Done => "Done",
        QueueState.Failed => "Failed",
        _ => "Cancelled",
    };

    private void Set(int index, QueueState state, string? message)
    {
        if (index < 0 || index >= _items.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _items[index].State = state;
        _items[index].Message = message;
    }
}

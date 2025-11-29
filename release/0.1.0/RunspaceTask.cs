using System.Collections.Concurrent;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;

namespace BackgroundWorker;

/// <summary>
/// Represents an individual background task executed through the runspace pool.
/// </summary>
public sealed class RunspaceTask
{
    private readonly ConcurrentQueue<PSObject> _output = new();
    private readonly ConcurrentQueue<ErrorRecord> _errors = new();
    private readonly ConcurrentQueue<ProgressRecord> _progress = new();
    private readonly object _stateLock = new();

    internal RunspaceTask(ScriptBlock scriptBlock, object[] arguments, TimeSpan? timeout)
    {
        ScriptBlock = scriptBlock ?? throw new ArgumentNullException(nameof(scriptBlock));
        ArgumentList = arguments ?? Array.Empty<object>();
        Timeout = timeout;
        Id = Guid.NewGuid();
        CreatedAt = DateTimeOffset.UtcNow;
        Status = RunspaceTaskStatus.Created;
        Cancellation = new CancellationTokenSource();
    }

    public Guid Id { get; }

    public RunspaceTaskStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public TimeSpan? Duration => StartedAt.HasValue && CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt.Value
        : null;

    public Exception? FailureReason { get; private set; }

    public TimeSpan? Timeout { get; }

    public string ScriptText => ScriptBlock.ToString();

    public ProgressRecord? LastProgress { get; private set; }

    internal ScriptBlock ScriptBlock { get; }

    internal object[] ArgumentList { get; }

    internal Task? Execution { get; private set; }

    internal CancellationTokenSource Cancellation { get; }

    internal void BindExecution(Task execution)
    {
        Execution = execution;
    }

    internal void MarkScheduled()
    {
        lock (_stateLock)
        {
            if (Status == RunspaceTaskStatus.Created)
            {
                Status = RunspaceTaskStatus.Scheduled;
            }
        }
    }

    internal void MarkRunning()
    {
        lock (_stateLock)
        {
            Status = RunspaceTaskStatus.Running;
            StartedAt ??= DateTimeOffset.UtcNow;
        }
    }

    internal void MarkCompleted()
    {
        lock (_stateLock)
        {
            Status = RunspaceTaskStatus.Completed;
            CompletedAt ??= DateTimeOffset.UtcNow;
        }
    }

    internal void MarkFailed(Exception ex)
    {
        lock (_stateLock)
        {
            Status = RunspaceTaskStatus.Failed;
            FailureReason = ex;
            CompletedAt ??= DateTimeOffset.UtcNow;
        }
    }

    internal void MarkCancelled()
    {
        lock (_stateLock)
        {
            Status = RunspaceTaskStatus.Cancelled;
            CompletedAt ??= DateTimeOffset.UtcNow;
        }
    }

    internal void MarkTimedOut(Exception? ex = null)
    {
        lock (_stateLock)
        {
            Status = RunspaceTaskStatus.TimedOut;
            FailureReason ??= ex;
            CompletedAt ??= DateTimeOffset.UtcNow;
        }
    }

    internal void AddOutput(PSObject item)
    {
        _output.Enqueue(item);
    }

    internal void AddError(ErrorRecord record)
    {
        _errors.Enqueue(record);
    }

    internal void AddProgress(ProgressRecord record)
    {
        _progress.Enqueue(record);
        LastProgress = record;
    }

    public IReadOnlyList<PSObject> ReceiveOutput(bool keep)
    {
        return keep ? _output.ToArray() : Drain(_output);
    }

    public IReadOnlyList<ErrorRecord> ReceiveError(bool keep)
    {
        return keep ? _errors.ToArray() : Drain(_errors);
    }

    public IReadOnlyList<ProgressRecord> ReceiveProgress(bool keep)
    {
        return keep ? _progress.ToArray() : Drain(_progress);
    }

    private static List<T> Drain<T>(ConcurrentQueue<T> queue)
    {
        var list = new List<T>();
        while (queue.TryDequeue(out var item))
        {
            list.Add(item);
        }

        return list;
    }
}

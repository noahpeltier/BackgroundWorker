using System.Management.Automation;

namespace BackgroundWorker;

/// <summary>
/// Event payload for task lifecycle and progress notifications.
/// </summary>
public sealed class RunspaceTaskEventArgs : EventArgs
{
    public RunspaceTaskEventArgs(RunspaceTask task, RunspaceTaskEventType eventType, ProgressRecord? progress = null)
    {
        Task = task ?? throw new ArgumentNullException(nameof(task));
        EventType = eventType;
        Progress = progress;
        TimestampUtc = DateTimeOffset.UtcNow;
    }

    public RunspaceTask Task { get; }

    public RunspaceTaskEventType EventType { get; }

    public ProgressRecord? Progress { get; }

    public DateTimeOffset TimestampUtc { get; }
}

namespace BackgroundWorker;

/// <summary>
/// Event kinds emitted for task lifecycle and progress.
/// </summary>
public enum RunspaceTaskEventType
{
    Created,
    Scheduled,
    Started,
    Progress,
    Completed,
    Failed,
    Cancelled,
    TimedOut
}

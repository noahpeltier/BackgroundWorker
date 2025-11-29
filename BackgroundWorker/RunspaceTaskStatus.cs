namespace BackgroundWorker;

/// <summary>
/// Lifecycle states for a runspace-backed background task.
/// </summary>
public enum RunspaceTaskStatus
{
    Created = 0,
    Scheduled,
    Running,
    Completed,
    Failed,
    Cancelled,
    TimedOut
}

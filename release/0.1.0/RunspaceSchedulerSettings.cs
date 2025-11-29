namespace BackgroundWorker;

/// <summary>
/// Configuration snapshot for the runspace scheduler.
/// </summary>
public sealed class RunspaceSchedulerSettings
{
    public int MinRunspaces { get; init; }

    public int MaxRunspaces { get; init; }

    public TimeSpan Retention { get; init; }
}

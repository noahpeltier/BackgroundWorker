using System.Management.Automation;

namespace BackgroundWorker;

public sealed class RunspacePoolInfo
{
    public string Name { get; init; } = string.Empty;
    public int MinRunspaces { get; init; }
    public int MaxRunspaces { get; init; }
    public TimeSpan Retention { get; init; }
    public string[] Modules { get; init; } = Array.Empty<string>();
    public ScriptBlock? InitScript { get; init; }
    public int TaskCount { get; init; }
    public int RunningTasks { get; init; }
}

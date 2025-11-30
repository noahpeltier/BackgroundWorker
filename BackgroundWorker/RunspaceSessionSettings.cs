using System.Management.Automation;

namespace BackgroundWorker;

/// <summary>
/// Snapshot of session defaults applied to each runspace task.
/// </summary>
public sealed class RunspaceSessionSettings
{
    public IReadOnlyList<string> Modules { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, object> Variables { get; init; } =
        new Dictionary<string, object>();

    public ScriptBlock? InitScript { get; init; }
}

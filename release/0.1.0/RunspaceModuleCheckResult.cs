namespace BackgroundWorker;

/// <summary>
/// Result of probing module availability for the runspace pool.
/// </summary>
public sealed class RunspaceModuleCheckResult
{
    public string Name { get; init; } = string.Empty;

    public bool Available { get; init; }

    public string? ModuleBase { get; init; }

    public string? Message { get; init; }
}

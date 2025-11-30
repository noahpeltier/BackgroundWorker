using System;
using System.IO;
using System.Management.Automation;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsLifecycle.Start, "RunspaceTask")]
[OutputType(typeof(RunspaceTask))]
public sealed class StartRunspaceTaskCommand : PSCmdlet
{
    private const string ScriptBlockParameterSet = "ScriptBlock";
    private const string ScriptFileParameterSet = "ScriptFile";

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ScriptBlockParameterSet)]
    public ScriptBlock ScriptBlock { get; set; } = default!;

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ScriptFileParameterSet)]
    [ValidateNotNullOrEmpty]
    public string Script { get; set; } = default!;

    [Parameter(Position = 1, ParameterSetName = ScriptBlockParameterSet)]
    [Parameter(Position = 1, ParameterSetName = ScriptFileParameterSet)]
    public object[]? ArgumentList { get; set; }

    [Parameter(ParameterSetName = ScriptBlockParameterSet)]
    [Parameter(ParameterSetName = ScriptFileParameterSet)]
    public string? Name { get; set; }

    [Parameter(ParameterSetName = ScriptBlockParameterSet)]
    [Parameter(ParameterSetName = ScriptFileParameterSet)]
    [ValidateRange(1, int.MaxValue)]
    public int? TimeoutSeconds { get; set; }

    protected override void ProcessRecord()
    {
        var blockToRun = ParameterSetName == ScriptFileParameterSet
            ? LoadScriptBlockFromFile()
            : ScriptBlock;

        TimeSpan? timeout = TimeoutSeconds.HasValue
            ? TimeSpan.FromSeconds(TimeoutSeconds.Value)
            : null;
        var task = RunspaceTaskManager.Instance.StartTask(blockToRun, ArgumentList, timeout, Name);
        WriteObject(task);
    }

    private ScriptBlock LoadScriptBlockFromFile()
    {
        var resolved = GetResolvedProviderPathFromPSPath(Script, out var provider);
        if (!string.Equals(provider.Name, "FileSystem", StringComparison.OrdinalIgnoreCase))
        {
            throw new PSArgumentException("Script must be a filesystem path.", nameof(Script));
        }

        if (resolved.Count != 1)
        {
            throw new PSArgumentException("Script path must resolve to a single file.", nameof(Script));
        }

        var path = resolved[0];
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Script file not found: {path}", path);
        }

        var content = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new PSArgumentException("Script file is empty.", nameof(Script));
        }

        return ScriptBlock.Create(content);
    }
}

using System.Management.Automation;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsLifecycle.Start, "RunspaceTask")]
[OutputType(typeof(RunspaceTask))]
public sealed class StartRunspaceTaskCommand : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0)]
    public ScriptBlock ScriptBlock { get; set; } = default!;

    [Parameter(Position = 1)]
    public object[]? ArgumentList { get; set; }

    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int? TimeoutSeconds { get; set; }

    protected override void ProcessRecord()
    {
        TimeSpan? timeout = TimeoutSeconds.HasValue
            ? TimeSpan.FromSeconds(TimeoutSeconds.Value)
            : null;
        var task = RunspaceTaskManager.Instance.StartTask(ScriptBlock, ArgumentList, timeout);
        WriteObject(task);
    }
}

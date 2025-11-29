using System.Management.Automation;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsCommon.Get, "RunspaceTask")]
[OutputType(typeof(RunspaceTask))]
public sealed class GetRunspaceTaskCommand : PSCmdlet
{
    [Parameter(ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    public Guid[]? Id { get; set; }

    protected override void ProcessRecord()
    {
        var ids = Id is { Length: > 0 } ? Id : null;
        var tasks = RunspaceTaskManager.Instance.GetTasks(ids);
        foreach (var task in tasks)
        {
            WriteObject(task);
        }
    }
}

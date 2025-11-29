using System.Management.Automation;
using BackgroundWorker;
using System.Linq;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsCommon.Remove, "RunspaceTask", SupportsShouldProcess = true)]
public sealed class RemoveRunspaceTaskCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public RunspaceTask[] Task { get; set; } = Array.Empty<RunspaceTask>();

    [Parameter]
    public SwitchParameter PassThru { get; set; }

    protected override void ProcessRecord()
    {
        if (Task.Length == 0)
        {
            return;
        }

        var toRemove = Task.Where(t => t is not null).ToArray();
        foreach (var t in toRemove)
        {
            if (!ShouldProcess(t.Id.ToString(), "Remove task"))
            {
                return;
            }
        }

        var removed = RunspaceTaskManager.Instance.RemoveTasks(toRemove);
        if (PassThru)
        {
            foreach (var task in removed)
            {
                WriteObject(task);
            }
        }
    }
}

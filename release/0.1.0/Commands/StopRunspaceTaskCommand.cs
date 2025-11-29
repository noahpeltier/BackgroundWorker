using System.Management.Automation;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsLifecycle.Stop, "RunspaceTask", SupportsShouldProcess = true)]
public sealed class StopRunspaceTaskCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public RunspaceTask Task { get; set; } = default!;

    [Parameter]
    public SwitchParameter PassThru { get; set; }

    protected override void ProcessRecord()
    {
        if (!ShouldProcess(Task.Id.ToString(), "Stop runspace task"))
        {
            return;
        }

        var stopped = RunspaceTaskManager.Instance.StopTask(Task);
        if (!stopped)
        {
            WriteVerbose($"Task {Task.Id} is already completed.");
        }

        if (PassThru)
        {
            WriteObject(Task);
        }
    }
}

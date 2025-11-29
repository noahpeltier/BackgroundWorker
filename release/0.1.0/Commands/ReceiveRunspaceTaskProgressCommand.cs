using System.Management.Automation;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsCommunications.Receive, "RunspaceTaskProgress")]
public sealed class ReceiveRunspaceTaskProgressCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public RunspaceTask Task { get; set; } = default!;

    [Parameter]
    public SwitchParameter Keep { get; set; }

    protected override void ProcessRecord()
    {
        var items = Task.ReceiveProgress(Keep);
        foreach (var item in items)
        {
            WriteObject(item);
        }
    }
}

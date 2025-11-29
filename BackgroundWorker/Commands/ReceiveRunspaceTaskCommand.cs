using System.Management.Automation;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsCommunications.Receive, "RunspaceTask")]
public sealed class ReceiveRunspaceTaskCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public RunspaceTask Task { get; set; } = default!;

    [Parameter]
    public SwitchParameter Keep { get; set; }

    [Parameter]
    public SwitchParameter IncludeError { get; set; }

    protected override void ProcessRecord()
    {
        var output = Task.ReceiveOutput(Keep);
        foreach (var item in output)
        {
            WriteObject(item);
        }

        if (IncludeError)
        {
            var errors = Task.ReceiveError(Keep);
            foreach (var error in errors)
            {
                WriteError(error);
            }
        }
    }
}

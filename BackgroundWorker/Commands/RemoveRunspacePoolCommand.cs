using System.Management.Automation;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsCommon.Remove, "RunspacePool", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
public sealed class RemoveRunspacePoolCommand : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    [ArgumentCompleter(typeof(PoolNameCompleter))]
    public string Name { get; set; } = string.Empty;

    [Parameter]
    public SwitchParameter Force { get; set; }

    protected override void ProcessRecord()
    {
        if (ShouldProcess(Name, "Remove runspace pool"))
        {
            RunspaceTaskManager.Instance.RemovePool(Name, Force);
        }
    }
}

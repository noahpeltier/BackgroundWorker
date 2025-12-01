using System.Collections;
using System.Management.Automation;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsCommon.Get, "RunspaceSessionState")]
[OutputType(typeof(RunspaceSessionSettings))]
public sealed class GetRunspaceSessionStateCommand : PSCmdlet
{
    [Parameter]
    [ArgumentCompleter(typeof(PoolNameCompleter))]
    public string? Pool { get; set; }

    protected override void ProcessRecord()
    {
        var settings = RunspaceTaskManager.Instance.GetSessionSettings(Pool ?? "default");
        WriteObject(settings);
    }
}

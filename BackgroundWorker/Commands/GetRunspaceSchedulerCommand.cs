using System.Management.Automation;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsCommon.Get, "RunspaceScheduler")]
[OutputType(typeof(RunspaceSchedulerSettings))]
public sealed class GetRunspaceSchedulerCommand : PSCmdlet
{
    [Parameter]
    [ArgumentCompleter(typeof(PoolNameCompleter))]
    public string? Pool { get; set; }

    protected override void ProcessRecord()
    {
        var settings = RunspaceTaskManager.Instance.GetSettings(Pool ?? "default");
        WriteObject(settings);
    }
}

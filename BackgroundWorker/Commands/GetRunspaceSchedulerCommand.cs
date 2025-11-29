using System.Management.Automation;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsCommon.Get, "RunspaceScheduler")]
[OutputType(typeof(RunspaceSchedulerSettings))]
public sealed class GetRunspaceSchedulerCommand : PSCmdlet
{
    protected override void ProcessRecord()
    {
        var settings = RunspaceTaskManager.Instance.GetSettings();
        WriteObject(settings);
    }
}

using System.Management.Automation;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsCommon.Set, "RunspaceScheduler")]
[OutputType(typeof(RunspaceSchedulerSettings))]
public sealed class SetRunspaceSchedulerCommand : PSCmdlet
{
    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int? MinRunspaces { get; set; }

    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int? MaxRunspaces { get; set; }

    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int? RetentionMinutes { get; set; }

    [Parameter]
    [ArgumentCompleter(typeof(PoolNameCompleter))]
    public string? Pool { get; set; }

    protected override void ProcessRecord()
    {
        TimeSpan? retention = null;
        if (RetentionMinutes.HasValue)
        {
            retention = TimeSpan.FromMinutes(RetentionMinutes.Value);
        }

        var settings = RunspaceTaskManager.Instance.Configure(Pool ?? "default", MinRunspaces, MaxRunspaces, retention);
        WriteObject(settings);
    }
}

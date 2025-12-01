using System.Management.Automation;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsCommon.Get, "RunspacePool")]
[OutputType(typeof(RunspacePoolInfo))]
public sealed class GetRunspacePoolCommand : PSCmdlet
{
    [Parameter(Position = 0)]
    [ArgumentCompleter(typeof(PoolNameCompleter))]
    public string? Name { get; set; }

    protected override void ProcessRecord()
    {
        var pools = RunspaceTaskManager.Instance.GetPools(Name);
        foreach (var pool in pools)
        {
            WriteObject(pool);
        }
    }
}

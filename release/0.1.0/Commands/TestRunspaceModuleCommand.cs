using System.Management.Automation;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsDiagnostic.Test, "RunspaceModule")]
[OutputType(typeof(RunspaceModuleCheckResult))]
public sealed class TestRunspaceModuleCommand : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    public string[] Module { get; set; } = Array.Empty<string>();

    protected override void ProcessRecord()
    {
        foreach (var module in Module)
        {
            if (string.IsNullOrWhiteSpace(module))
            {
                continue;
            }

            var result = RunspaceModuleProbe.Check(module.Trim());
            WriteObject(result);
        }
    }
}

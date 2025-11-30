using System.Collections;
using System.Management.Automation;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsCommon.Set, "RunspaceSessionState")]
[OutputType(typeof(RunspaceSessionSettings))]
public sealed class SetRunspaceSessionStateCommand : PSCmdlet
{
    [Parameter]
    public string[]? Module { get; set; }

    [Parameter]
    public Hashtable? Variable { get; set; }

    [Parameter]
    public ScriptBlock? InitScript { get; set; }

    protected override void ProcessRecord()
    {
        IDictionary<string, object>? variables = null;
        if (Variable is not null)
        {
            variables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in Variable)
            {
                if (entry.Key is null)
                {
                    continue;
                }

                variables[entry.Key.ToString() ?? string.Empty] = entry.Value!;
            }
        }

        var settings = RunspaceTaskManager.Instance.ConfigureSession(Module, variables, InitScript);
        WriteObject(settings);
    }
}

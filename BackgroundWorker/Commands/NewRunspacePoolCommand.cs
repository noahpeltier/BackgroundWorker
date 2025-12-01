using System.Collections;
using System.Management.Automation;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsCommon.New, "RunspacePool")]
[OutputType(typeof(RunspacePoolInfo))]
public sealed class NewRunspacePoolCommand : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

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
                variables[entry.Key.ToString() ?? string.Empty] = entry.Value!;
            }
        }

        TimeSpan? retention = null;
        if (RetentionMinutes.HasValue)
        {
            retention = TimeSpan.FromMinutes(RetentionMinutes.Value);
        }

        var info = RunspaceTaskManager.Instance.CreatePool(Name, MinRunspaces, MaxRunspaces, retention, Module, variables, InitScript);
        WriteObject(info);
    }
}

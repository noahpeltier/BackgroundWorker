using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace BackgroundWorker.Commands;

public sealed class PoolNameCompleter : IArgumentCompleter
{
    public IEnumerable<CompletionResult> CompleteArgument(string commandName, string parameterName, string wordToComplete, CommandAst commandAst, IDictionary fakeBoundParameters)
    {
        var prefix = wordToComplete ?? string.Empty;
        var pools = RunspaceTaskManager.Instance.GetPools()
            .Select(p => p.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n);

        return pools.Select(n => new CompletionResult(n, n, CompletionResultType.ParameterValue, n));
    }
}

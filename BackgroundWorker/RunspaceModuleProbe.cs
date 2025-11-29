using System.Management.Automation;

namespace BackgroundWorker;

internal static class RunspaceModuleProbe
{
    public static RunspaceModuleCheckResult Check(string moduleName)
    {
        using var ps = PowerShell.Create();
        ps.AddCommand("Get-Module")
            .AddParameter("ListAvailable", true)
            .AddParameter("Name", moduleName);

        var results = ps.Invoke<PSModuleInfo>();

        if (ps.HadErrors && ps.Streams.Error.Count > 0)
        {
            var error = ps.Streams.Error[0];
            return new RunspaceModuleCheckResult
            {
                Name = moduleName,
                Available = false,
                Message = error.ToString()
            };
        }

        if (results.Count > 0)
        {
            var module = results[0];
            return new RunspaceModuleCheckResult
            {
                Name = moduleName,
                Available = true,
                ModuleBase = module.ModuleBase,
                Message = $"Found {module.Name}"
            };
        }

        var path = Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty;
        return new RunspaceModuleCheckResult
        {
            Name = moduleName,
            Available = false,
            Message = $"Not found in PSModulePath: {path}"
        };
    }
}

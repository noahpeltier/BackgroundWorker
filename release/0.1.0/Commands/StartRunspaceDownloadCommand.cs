using System.Management.Automation;
using System.Runtime.InteropServices;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsLifecycle.Start, "RunspaceDownload")]
[OutputType(typeof(RunspaceTask))]
public sealed class StartRunspaceDownloadCommand : PSCmdlet
{
    private static readonly ScriptBlock DownloadScript = ScriptBlock.Create(@"
param(
    $Url,
    $Destination,
    [bool]$UseBits,
    [int]$DownloadTimeoutSec
)

if ($UseBits -and -not $IsWindows) {
    throw ""BITS is only available on Windows.""
}

if ($UseBits) {
    $job = Start-BitsTransfer -Source $Url -Destination $Destination -Asynchronous -ErrorAction Stop
    try {
        while ($true) {
            $job = Get-BitsTransfer -JobId $job.JobId -ErrorAction Stop
            switch ($job.JobState) {
                'Transferred' { Complete-BitsTransfer -BitsJob $job; break }
                'Cancelled'   { throw 'BITS job cancelled' }
                'Error'       { throw ($job | Format-List -Force | Out-String) }
                default {
                    if ($job.BytesTotal -gt 0) {
                        $pct = [int](($job.BytesTransferred * 100) / $job.BytesTotal)
                        Write-Progress -Activity 'Downloading' -Status ""$pct%"" -PercentComplete $pct
                    }
                    else {
                        Write-Progress -Activity 'Downloading' -Status ""Bytes $($job.BytesTransferred)"" -PercentComplete 0
                    }
                    Start-Sleep -Seconds 2
                }
            }
        }
        ""Saved to $Destination""
    }
    finally {
        if ($job -and $job.JobState -notin 'Transferred','Cancelled') {
            Remove-BitsTransfer -BitsJob $job -ErrorAction SilentlyContinue
        }
    }
}
else {
    $invokeParams = @{
        Uri = $Url
        OutFile = $Destination
        ErrorAction = 'Stop'
    }
    if ($DownloadTimeoutSec -gt 0) {
        $invokeParams.TimeoutSec = $DownloadTimeoutSec
    }
    Invoke-WebRequest @invokeParams
    ""Saved to $Destination""
}
");

    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Url { get; set; } = string.Empty;

    [Parameter(Position = 1)]
    public string? Destination { get; set; }

    [Parameter]
    public SwitchParameter UseBits { get; set; }

    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int? TimeoutSeconds { get; set; }

    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int? DownloadTimeoutSeconds { get; set; }

    protected override void ProcessRecord()
    {
        if (UseBits && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PSNotSupportedException("BITS downloads are only supported on Windows.");
        }

        var destinationPath = ResolveDestination();
        TimeSpan? timeout = TimeoutSeconds.HasValue
            ? TimeSpan.FromSeconds(TimeoutSeconds.Value)
            : null;

        var args = new object[]
        {
            Url,
            destinationPath,
            UseBits.IsPresent,
            DownloadTimeoutSeconds.GetValueOrDefault()
        };

        var task = RunspaceTaskManager.Instance.StartTask(DownloadScript, args, timeout);
        WriteObject(task);
    }

    private string ResolveDestination()
    {
        if (!string.IsNullOrWhiteSpace(Destination))
        {
            var fullPath = GetUnresolvedProviderPathFromPSPath(Destination);
            EnsureDirectory(fullPath);
            return fullPath;
        }

        var fileName = GetFileNameFromUrl() ?? "download.bin";
        var basePath = SessionState.Path.CurrentFileSystemLocation.Path;
        var combined = Path.GetFullPath(Path.Combine(basePath, fileName));
        EnsureDirectory(combined);
        return combined;
    }

    private string? GetFileNameFromUrl()
    {
        try
        {
            var uri = new Uri(Url, UriKind.Absolute);
            var name = Path.GetFileName(uri.LocalPath);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}

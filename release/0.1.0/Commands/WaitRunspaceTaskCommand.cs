using System.Management.Automation;
using BackgroundWorker;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsLifecycle.Wait, "RunspaceTask")]
public sealed class WaitRunspaceTaskCommand : PSCmdlet
{
    private readonly CancellationTokenSource _stopToken = new();

    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public RunspaceTask Task { get; set; } = default!;

    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int? TimeoutSeconds { get; set; }

    [Parameter]
    public SwitchParameter PassThru { get; set; }

    protected override void StopProcessing()
    {
        _stopToken.Cancel();
    }

    protected override void ProcessRecord()
    {
        TimeSpan? timeout = TimeoutSeconds.HasValue
            ? TimeSpan.FromSeconds(TimeoutSeconds.Value)
            : null;

        var completed = RunspaceTaskManager.Instance.WaitAsync(Task, timeout, _stopToken.Token)
            .GetAwaiter()
            .GetResult();

        if (!completed)
        {
            var timeoutEx = new TimeoutException($"Timed out waiting for task {Task.Id}.");
            var errorRecord = new ErrorRecord(timeoutEx, "RunspaceTaskWaitTimeout", ErrorCategory.OperationTimeout, Task);
            WriteError(errorRecord);
            return;
        }

        if (PassThru)
        {
            WriteObject(Task);
        }
    }
}

using System.Management.Automation;
using Spectre.Console;

namespace BackgroundWorker.Commands;

[Cmdlet(VerbsCommon.Show, "RunspaceTasks")]
public sealed class ShowRunspaceTasksCommand : PSCmdlet
{
    private CancellationTokenSource _cts = new();
    private static readonly string[] SpinnerFrames = new[]
    {
        "⠋","⠙","⠹","⠸","⠼","⠴","⠦","⠧","⠇","⠏"
    };

    [Parameter]
    [ValidateRange(100, 60000)]
    public int RefreshMilliseconds { get; set; } = 500;

    [Parameter]
    public SwitchParameter ExitWhenIdle { get; set; }

    [Parameter]
    public SwitchParameter IncludeProgress { get; set; }

    protected override void StopProcessing()
    {
        _cts.Cancel();
    }

    protected override void ProcessRecord()
    {
        var refresh = TimeSpan.FromMilliseconds(RefreshMilliseconds);
        var manager = RunspaceTaskManager.Instance;

        var spinIndex = 0;
        var initial = BuildTable(manager.GetTasks(), IncludeProgress.IsPresent, SpinnerFrames[spinIndex % SpinnerFrames.Length]);
        AnsiConsole.Live(initial)
            .Start(ctx =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    var tasks = manager.GetTasks();
                    spinIndex = (spinIndex + 1) % SpinnerFrames.Length;
                    var table = BuildTable(tasks, IncludeProgress.IsPresent, SpinnerFrames[spinIndex]);
                    ctx.UpdateTarget(table);
                    ctx.Refresh();

                    if (ExitWhenIdle.IsPresent && !tasks.Any(t =>
                            t.Status is RunspaceTaskStatus.Created or RunspaceTaskStatus.Scheduled or RunspaceTaskStatus.Running))
                    {
                        break;
                    }

                    try
                    {
                        Task.Delay(refresh, _cts.Token).Wait();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            });
    }

    private static Table BuildTable(IEnumerable<RunspaceTask> tasks, bool includeProgress, string spinnerFrame)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand()
            .AddColumn(new TableColumn("Id"))
            .AddColumn(new TableColumn("Name"))
            .AddColumn(new TableColumn("Status"))
            .AddColumn(new TableColumn("Created"))
            .AddColumn(new TableColumn("Started"))
            .AddColumn(new TableColumn("Completed"))
            .AddColumn(new TableColumn("Duration"))
            .AddColumn(new TableColumn("Timeout"))
            .AddColumn(new TableColumn("Progress"));

        var any = false;
        foreach (var task in tasks)
        {
            any = true;
            table.AddRow(
                new Markup(Markup.Escape(task.Id.ToString())),
                new Markup(Markup.Escape(task.Name ?? string.Empty)),
                StatusMarkup(task.Status, spinnerFrame),
                new Markup(Markup.Escape(FormatTime(task.CreatedAt))),
                new Markup(Markup.Escape(FormatTime(task.StartedAt))),
                new Markup(Markup.Escape(FormatTime(task.CompletedAt))),
                new Markup(Markup.Escape(FormatDuration(task.Duration))),
                new Markup(Markup.Escape(FormatDuration(task.Timeout))),
                new Markup(Markup.Escape(includeProgress ? FormatProgress(task.LastProgress) : string.Empty))
            );
        }

        if (!any)
        {
            table.AddRow(new Markup("[grey]No tasks[/]"), new Markup(""), new Markup(""), new Markup(""), new Markup(""), new Markup(""), new Markup(""), new Markup(""));
        }

        return table;
    }

    private static string FormatTime(DateTimeOffset? time)
    {
        if (!time.HasValue)
        {
            return string.Empty;
        }

        return time.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        if (!duration.HasValue)
        {
            return string.Empty;
        }

        return duration.Value.ToString(@"hh\:mm\:ss");
    }

    private static string FormatProgress(ProgressRecord? progress)
    {
        if (progress is null)
        {
            return string.Empty;
        }

        if (progress.PercentComplete >= 0)
        {
            return $"{progress.PercentComplete}% {progress.StatusDescription}";
        }

        return progress.StatusDescription ?? string.Empty;
    }

    private static Markup StatusMarkup(RunspaceTaskStatus status, string frame)
    {
        var text = status.ToString();
        var markup = status switch
        {
            RunspaceTaskStatus.Completed => $"[green]{text}[/]",
            RunspaceTaskStatus.Running => $"[cyan]{text}[/]",
            RunspaceTaskStatus.Scheduled => $"[blue]{text}[/]",
            RunspaceTaskStatus.Created => $"[grey]{text}[/]",
            RunspaceTaskStatus.Failed => $"[red]{text}[/]",
            RunspaceTaskStatus.Cancelled => $"[yellow]{text}[/]",
            RunspaceTaskStatus.TimedOut => $"[yellow]{text}[/]",
            _ => text
        };

        if (status is RunspaceTaskStatus.Running or RunspaceTaskStatus.Scheduled or RunspaceTaskStatus.Created)
        {
            var spin = $"[cyan]{frame}[/]";
            return new Markup($"{spin} {markup}");
        }

        return new Markup(markup);
    }
}

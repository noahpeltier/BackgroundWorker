using System.Management.Automation;
using Spectre.Console;
using Spectre.Console.Rendering;

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

    [Parameter]
    [ArgumentCompleter(typeof(PoolNameCompleter))]
    public string? Pool { get; set; }

    protected override void StopProcessing()
    {
        _cts.Cancel();
    }

    protected override void ProcessRecord()
    {
        var refresh = TimeSpan.FromMilliseconds(RefreshMilliseconds);
        var manager = RunspaceTaskManager.Instance;

        var spinIndex = 0;
        var initial = BuildTable(manager.GetTasks(Pool), IncludeProgress.IsPresent, SpinnerFrames[spinIndex % SpinnerFrames.Length]);
        AnsiConsole.Live(initial)
            .Start(ctx =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    var tasks = manager.GetTasks(Pool);
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
            .AddColumn(new TableColumn("Pool"))
            .AddColumn(new TableColumn("Name"))
            .AddColumn(new TableColumn("Status"))
            .AddColumn(new TableColumn("Started"))
            .AddColumn(new TableColumn("Duration"))
            .AddColumn(new TableColumn("Progress").LeftAligned().NoWrap().Padding(new Padding(0, 0, 0, 0)));

        var any = false;
        foreach (var task in tasks)
        {
            any = true;
            table.AddRow(
                new Markup(Markup.Escape(task.PoolName)),
                new Markup(Markup.Escape(task.Name ?? string.Empty)),
                StatusMarkup(task.Status, spinnerFrame),
                new Markup(Markup.Escape(FormatTime(task.StartedAt))),
                new Markup(Markup.Escape(FormatDuration(task.Duration))),
                includeProgress ? BuildProgressRenderable(task.LastProgress) : Text.Empty
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

    private static IRenderable BuildProgressRenderable(ProgressRecord? progress)
    {
        if (progress is null)
        {
            return Text.Empty;
        }

        if (progress.PercentComplete >= 0)
        {
            var pct = Math.Clamp(progress.PercentComplete, 0, 100);
            var blocks = 20;
            var filled = (int)Math.Round((pct / 100.0) * blocks);
            var bar = new string('⣿', filled) + new string('⣀', blocks - filled);
            return new Markup($"[cyan]{bar}[/] {pct,3}%");
        }

        return new Markup(Markup.Escape(progress.StatusDescription ?? string.Empty));
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

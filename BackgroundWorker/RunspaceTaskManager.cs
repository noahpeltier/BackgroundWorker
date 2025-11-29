using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BackgroundWorker;

/// <summary>
/// Central scheduler that manages the runspace pool and task lifecycle.
/// </summary>
public sealed class RunspaceTaskManager : IDisposable
{
    private RunspacePool _pool;
    private readonly ConcurrentDictionary<Guid, RunspaceTask> _tasks = new();
    private readonly SemaphoreSlim _throttle;
    private readonly Timer _cleanupTimer;
    private readonly object _configLock = new();
    private int _minRunspaces;
    private int _maxRunspaces;
    private TimeSpan _retention;
    private List<string> _importModules = new();
    private Dictionary<string, object> _sessionVariables = new();
    private bool _disposed;

    public event EventHandler<RunspaceTaskEventArgs>? TaskEvent;

    private RunspaceTaskManager(int minRunspaces, int maxRunspaces)
    {
        var initialState = InitialSessionState.CreateDefault2();
        initialState.ImportPSModule(new[] { "Microsoft.PowerShell.Management", "Microsoft.PowerShell.Utility" });
        _pool = RunspaceFactory.CreateRunspacePool(initialState);
        _pool.SetMinRunspaces(minRunspaces);
        _pool.SetMaxRunspaces(maxRunspaces);
        _pool.ApartmentState = ApartmentState.MTA;
        _pool.Open();
        _minRunspaces = minRunspaces;
        _maxRunspaces = maxRunspaces;
        _retention = TimeSpan.FromMinutes(30);
        _throttle = new SemaphoreSlim(maxRunspaces, maxRunspaces);
        _cleanupTimer = new Timer(_ => PruneExpired(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public static RunspaceTaskManager Instance { get; } =
        new(minRunspaces: 1, maxRunspaces: Math.Max(2, Environment.ProcessorCount));

    public IEnumerable<RunspaceTask> GetTasks(IEnumerable<Guid>? ids = null)
    {
        ThrowIfDisposed();

        if (ids is null)
        {
            return _tasks.Values.OrderBy(t => t.CreatedAt).ToArray();
        }

        var set = new HashSet<Guid>(ids);
        return _tasks.Values.Where(t => set.Contains(t.Id)).OrderBy(t => t.CreatedAt).ToArray();
    }

    public RunspaceTask? GetTask(Guid id)
    {
        ThrowIfDisposed();
        _tasks.TryGetValue(id, out var task);
        return task;
    }

    public IReadOnlyCollection<RunspaceTask> RemoveTasks(IEnumerable<RunspaceTask> tasks)
    {
        ThrowIfDisposed();

        var removed = new List<RunspaceTask>();
        foreach (var task in tasks)
        {
            if (task is null)
            {
                continue;
            }

            if (!_tasks.TryGetValue(task.Id, out var tracked))
            {
                continue;
            }

            if (tracked.Status is RunspaceTaskStatus.Created or RunspaceTaskStatus.Scheduled or RunspaceTaskStatus.Running)
            {
                throw new InvalidOperationException($"Task {tracked.Id} is still running; stop it before removing.");
            }

            _tasks.TryRemove(tracked.Id, out _);
            removed.Add(tracked);
        }

        return removed;
    }

    public RunspaceSchedulerSettings GetSettings()
    {
        ThrowIfDisposed();
        lock (_configLock)
        {
            return new RunspaceSchedulerSettings
            {
                MinRunspaces = _minRunspaces,
                MaxRunspaces = _maxRunspaces,
                Retention = _retention
            };
        }
    }

    public RunspaceSchedulerSettings Configure(int? minRunspaces, int? maxRunspaces, TimeSpan? retention)
    {
        ThrowIfDisposed();

        lock (_configLock)
        {
            var desiredMin = minRunspaces ?? _minRunspaces;
            var desiredMax = maxRunspaces ?? _maxRunspaces;

            if (desiredMin < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(minRunspaces), "Minimum runspaces must be at least 1.");
            }

            if (desiredMax < desiredMin)
            {
                throw new ArgumentException("Max runspaces must be greater than or equal to min runspaces.");
            }

        if (desiredMax != _maxRunspaces)
        {
            AdjustThrottle(desiredMax);
            _pool.SetMaxRunspaces(desiredMax);
            _maxRunspaces = desiredMax;
        }

        if (desiredMin != _minRunspaces)
        {
            _pool.SetMinRunspaces(desiredMin);
            _minRunspaces = desiredMin;
        }

        if (retention.HasValue)
        {
            _retention = retention.Value;
            }

            return new RunspaceSchedulerSettings
            {
                MinRunspaces = _minRunspaces,
                MaxRunspaces = _maxRunspaces,
                Retention = _retention
            };
        }
    }

    public RunspaceSessionSettings GetSessionSettings()
    {
        ThrowIfDisposed();
        lock (_configLock)
        {
            return new RunspaceSessionSettings
            {
                Modules = _importModules.ToArray(),
                Variables = new Dictionary<string, object>(_sessionVariables)
            };
        }
    }

    public RunspaceSessionSettings ConfigureSession(IEnumerable<string>? modules, IDictionary<string, object>? variables)
    {
        ThrowIfDisposed();

        lock (_configLock)
        {
            var nextModules = modules is not null
                ? modules
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Select(m => m.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>(_importModules);

            var nextVariables = variables is not null
                ? new Dictionary<string, object>(variables, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object>(_sessionVariables, StringComparer.OrdinalIgnoreCase);

            ValidateModules(nextModules);
            RebuildRunspacePoolIfIdle(nextModules, nextVariables);

            _importModules = nextModules;
            _sessionVariables = nextVariables;

            return GetSessionSettings();
        }
    }

    public RunspaceTask StartTask(ScriptBlock scriptBlock, object[]? arguments, TimeSpan? timeout)
    {
        ThrowIfDisposed();

        var task = new RunspaceTask(scriptBlock, arguments ?? Array.Empty<object>(), timeout);
        if (!_tasks.TryAdd(task.Id, task))
        {
            throw new InvalidOperationException("Failed to track new task.");
        }

        var execution = ExecuteAsync(task);
        task.BindExecution(execution);
        PublishEvent(task, RunspaceTaskEventType.Created);
        return task;
    }

    public bool StopTask(RunspaceTask task)
    {
        ThrowIfDisposed();

        if (task.Status is RunspaceTaskStatus.Completed or RunspaceTaskStatus.Failed or RunspaceTaskStatus.Cancelled or RunspaceTaskStatus.TimedOut)
        {
            return false;
        }

        task.Cancellation.Cancel();
        return true;
    }

    public async Task<bool> WaitAsync(RunspaceTask task, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (task.Execution is null)
        {
            return true;
        }

        try
        {
            if (timeout.HasValue)
            {
                var finished = await Task.WhenAny(task.Execution, Task.Delay(timeout.Value, cancellationToken))
                    .ConfigureAwait(false);
                if (finished != task.Execution)
                {
                    return false;
                }

                await task.Execution.ConfigureAwait(false);
                return true;
            }

            await task.Execution.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private Task ExecuteAsync(RunspaceTask task)
    {
        task.MarkScheduled();

        var sessionSnapshot = GetSessionSettings();

        return Task.Run(async () =>
        {
            await _throttle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (task.Cancellation.IsCancellationRequested)
                {
                    task.MarkCancelled();
                    PublishEvent(task, RunspaceTaskEventType.Cancelled);
                    return;
                }

                task.MarkRunning();
                PublishEvent(task, RunspaceTaskEventType.Started);
                using var ps = PowerShell.Create();
                ps.RunspacePool = _pool;

                var output = new PSDataCollection<PSObject>();
                output.DataAdded += (_, args) =>
                {
                    var item = output[args.Index];
                    task.AddOutput(item);
                };

                ps.Streams.Progress.DataAdded += (_, args) =>
                {
                    var record = ps.Streams.Progress[args.Index];
                    task.AddProgress(record);
                    PublishEvent(task, RunspaceTaskEventType.Progress, record);
                };

                ps.Streams.Error.DataAdded += (_, args) =>
                {
                    var record = ps.Streams.Error[args.Index];
                    task.AddError(record);
                };

                using var linkedCts = CreateLinkedToken(task);
                using var registration = linkedCts.Token.Register(() =>
                {
                    try
                    {
                        ps.Stop();
                    }
                    catch
                    {
                        // swallow Stop errors
                    }
                });

                IAsyncResult asyncResult;
                try
                {
                    ps.AddScript(task.ScriptBlock.ToString(), useLocalScope: true);
                    if (task.ArgumentList.Length > 0)
                    {
                        foreach (var arg in task.ArgumentList)
                        {
                            ps.AddArgument(arg);
                        }
                    }

                    asyncResult = ps.BeginInvoke<PSObject, PSObject>(input: null, output);
                }
                catch (Exception ex)
                {
                    task.MarkFailed(ex);
                    return;
                }

                var invokeTask = Task.Factory.FromAsync(asyncResult, ps.EndInvoke);

                try
                {
                    await invokeTask.ConfigureAwait(false);

                    if (task.Cancellation.IsCancellationRequested)
                    {
                        task.MarkCancelled();
                        PublishEvent(task, RunspaceTaskEventType.Cancelled);
                    }
                    else if (task.Timeout.HasValue && linkedCts.IsCancellationRequested)
                    {
                        task.MarkTimedOut();
                        PublishEvent(task, RunspaceTaskEventType.TimedOut);
                    }
                    else
                    {
                        task.MarkCompleted();
                        PublishEvent(task, RunspaceTaskEventType.Completed);
                    }
                }
                catch (Exception ex)
                {
                    if (task.Timeout.HasValue && linkedCts.IsCancellationRequested && !task.Cancellation.IsCancellationRequested)
                    {
                        task.MarkTimedOut(ex);
                        PublishEvent(task, RunspaceTaskEventType.TimedOut);
                    }
                    else if (task.Cancellation.IsCancellationRequested)
                    {
                        task.MarkCancelled();
                        PublishEvent(task, RunspaceTaskEventType.Cancelled);
                    }
                    else
                    {
                        task.MarkFailed(ex);
                        PublishEvent(task, RunspaceTaskEventType.Failed);
                    }
                }
            }
            finally
            {
                _throttle.Release();
            }
        });
    }

    private static CancellationTokenSource CreateLinkedToken(RunspaceTask task)
    {
        if (task.Timeout.HasValue)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(task.Cancellation.Token);
            cts.CancelAfter(task.Timeout.Value);
            return cts;
        }

        return CancellationTokenSource.CreateLinkedTokenSource(task.Cancellation.Token);
    }

    private void AdjustThrottle(int desiredMax)
    {
        var currentMax = _maxRunspaces;
        var delta = desiredMax - currentMax;
        if (delta == 0)
        {
            return;
        }

        if (delta > 0)
        {
            _throttle.Release(delta);
        }
        else
        {
            for (var i = 0; i < Math.Abs(delta); i++)
            {
                _throttle.Wait();
            }
        }
    }

    private static void ValidateModules(IReadOnlyCollection<string> modules)
    {
        if (modules.Count == 0)
        {
            return;
        }

        var missing = new List<string>();
        foreach (var name in modules)
        {
            var probe = RunspaceModuleProbe.Check(name);
            if (!probe.Available)
            {
                missing.Add($"{name} ({probe.Message})");
            }
        }

        if (missing.Count == 0)
        {
            return;
        }

        var psModulePath = Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty;
        var details = string.Join("; ", missing);
        throw new InvalidOperationException(
            $"Failed to import modules. Missing: {details}. Ensure they are installed and visible in PSModulePath: {psModulePath}");
    }

    private void RebuildRunspacePoolIfIdle(IReadOnlyList<string> modules, IReadOnlyDictionary<string, object> variables)
    {
        var active = _tasks.Values.Any(t =>
            t.Status is RunspaceTaskStatus.Created or RunspaceTaskStatus.Scheduled or RunspaceTaskStatus.Running);
        if (active)
        {
            throw new InvalidOperationException("Cannot change session state while tasks are active. Wait for tasks to finish or stop them, then retry.");
        }

        var initialState = InitialSessionState.CreateDefault2();
        if (modules.Count > 0)
        {
            initialState.ImportPSModule(modules.ToArray());
        }

        if (variables.Count > 0)
        {
            foreach (var kvp in variables)
            {
                initialState.Variables.Add(new SessionStateVariableEntry(kvp.Key, kvp.Value, description: null, options: ScopedItemOptions.AllScope));
            }
        }

        var newPool = RunspaceFactory.CreateRunspacePool(initialState);
        newPool.SetMinRunspaces(_minRunspaces);
        newPool.SetMaxRunspaces(_maxRunspaces);
        newPool.ApartmentState = ApartmentState.MTA;
        newPool.Open();

        var oldPool = _pool;
        _pool = newPool;
        oldPool.Dispose();
    }

    private void PruneExpired()
    {
        if (_disposed)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _tasks)
        {
            var task = kvp.Value;
            if (task.CompletedAt.HasValue && now - task.CompletedAt.Value >= _retention)
            {
                _tasks.TryRemove(kvp.Key, out _);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RunspaceTaskManager));
        }
    }

    private void PublishEvent(RunspaceTask task, RunspaceTaskEventType eventType, ProgressRecord? progress = null)
    {
        var handler = TaskEvent;
        if (handler is null)
        {
            return;
        }

        var args = new RunspaceTaskEventArgs(task, eventType, progress);
        // fire-and-forget to avoid blocking task execution
        _ = Task.Run(() =>
        {
            try
            {
                handler.Invoke(this, args);
            }
            catch
            {
                // swallow listener failures
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cleanupTimer.Dispose();
        foreach (var kvp in _tasks)
        {
            kvp.Value.Cancellation.Cancel();
        }

        _pool.Dispose();
        _throttle.Dispose();
    }
}

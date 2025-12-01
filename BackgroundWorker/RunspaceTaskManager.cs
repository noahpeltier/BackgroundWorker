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
    private const string DefaultPoolName = "default";

    private sealed class PoolContext : IDisposable
    {
        public string Name { get; init; } = string.Empty;
        public RunspacePool Pool { get; set; } = default!;
        public SemaphoreSlim Throttle { get; set; } = default!;
        public ConcurrentDictionary<Guid, RunspaceTask> Tasks { get; } = new();
        public int MinRunspaces { get; set; }
        public int MaxRunspaces { get; set; }
        public TimeSpan Retention { get; set; }
        public List<string> ImportModules { get; set; } = new();
        public Dictionary<string, object> SessionVariables { get; set; } = new();
        public ScriptBlock? InitScript { get; set; }

        public void Dispose()
        {
            Pool.Dispose();
            Throttle.Dispose();
        }
    }

    private readonly ConcurrentDictionary<string, PoolContext> _pools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _cleanupTimer;
    private readonly object _configLock = new();
    private bool _disposed;

    public event EventHandler<RunspaceTaskEventArgs>? TaskEvent;

    private RunspaceTaskManager(int minRunspaces, int maxRunspaces)
    {
        _cleanupTimer = new Timer(_ => PruneExpired(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        CreateOrGetPool(DefaultPoolName, minRunspaces, maxRunspaces);
    }

    public static RunspaceTaskManager Instance { get; } =
        new(minRunspaces: 1, maxRunspaces: Math.Max(2, Environment.ProcessorCount));

    public IEnumerable<RunspaceTask> GetTasks(string? poolName = null, IEnumerable<Guid>? ids = null)
    {
        ThrowIfDisposed();

        IEnumerable<RunspaceTask> source = poolName is null
            ? _pools.Values.SelectMany(p => p.Tasks.Values)
            : GetPool(poolName).Tasks.Values;

        if (ids is null)
        {
            return source.OrderBy(t => t.CreatedAt).ToArray();
        }

        var set = new HashSet<Guid>(ids);
        return source.Where(t => set.Contains(t.Id)).OrderBy(t => t.CreatedAt).ToArray();
    }

    public RunspaceTask? GetTask(Guid id)
    {
        ThrowIfDisposed();
        foreach (var pool in _pools.Values)
        {
            if (pool.Tasks.TryGetValue(id, out var task))
            {
                return task;
            }
        }

        return null;
    }

    public IEnumerable<RunspacePoolInfo> GetPools(string? poolName = null)
    {
        ThrowIfDisposed();
        var pools = poolName is null ? _pools.Values : new[] { GetPool(poolName) };
        return pools.Select(p => new RunspacePoolInfo
        {
            Name = p.Name,
            MinRunspaces = p.MinRunspaces,
            MaxRunspaces = p.MaxRunspaces,
            Retention = p.Retention,
            Modules = p.ImportModules.ToArray(),
            InitScript = p.InitScript,
            TaskCount = p.Tasks.Count,
            RunningTasks = p.Tasks.Values.Count(t => t.Status is RunspaceTaskStatus.Running or RunspaceTaskStatus.Scheduled or RunspaceTaskStatus.Created)
        }).ToArray();
    }

    public RunspacePoolInfo CreatePool(string poolName, int? minRunspaces, int? maxRunspaces, TimeSpan? retention, IEnumerable<string>? modules, IDictionary<string, object>? variables, ScriptBlock? initScript)
    {
        ThrowIfDisposed();

        var moduleList = modules?.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (moduleList is not null)
        {
            ValidateModules(moduleList);
        }

        var ctx = CreateOrGetPool(poolName, minRunspaces, maxRunspaces, retention, moduleList, variables, initScript);

        // If pool already existed and settings supplied, apply updates
        if (minRunspaces.HasValue || maxRunspaces.HasValue || retention.HasValue)
        {
            Configure(poolName, minRunspaces, maxRunspaces, retention);
        }

        if (modules is not null || variables is not null || initScript is not null)
        {
            ConfigureSession(poolName, modules, variables, initScript);
        }

        return GetPools(poolName).Single();
    }

    public void RemovePool(string poolName, bool force = false)
    {
        ThrowIfDisposed();
        if (string.Equals(poolName, DefaultPoolName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The default pool cannot be removed.");
        }

        if (!_pools.TryGetValue(poolName, out var ctx))
        {
            return;
        }

        var running = ctx.Tasks.Values.Any(t => t.Status is RunspaceTaskStatus.Created or RunspaceTaskStatus.Scheduled or RunspaceTaskStatus.Running);
        if (running && !force)
        {
            throw new InvalidOperationException($"Pool '{poolName}' has running tasks. Stop them or use -Force.");
        }

        if (force)
        {
            foreach (var task in ctx.Tasks.Values)
            {
                task.Cancellation.Cancel();
            }
        }

        ctx.Dispose();
        _pools.TryRemove(poolName, out _);
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

            var pool = GetPool(task.PoolName);

            if (!pool.Tasks.TryGetValue(task.Id, out var tracked))
            {
                continue;
            }

            if (tracked.Status is RunspaceTaskStatus.Created or RunspaceTaskStatus.Scheduled or RunspaceTaskStatus.Running)
            {
                throw new InvalidOperationException($"Task {tracked.Id} is still running; stop it before removing.");
            }

            pool.Tasks.TryRemove(tracked.Id, out _);
            removed.Add(tracked);
        }

        return removed;
    }

    public RunspaceSchedulerSettings GetSettings(string? poolName = null)
    {
        ThrowIfDisposed();
        var target = GetPool(poolName ?? DefaultPoolName);
        lock (_configLock)
        {
            return new RunspaceSchedulerSettings
            {
                MinRunspaces = target.MinRunspaces,
                MaxRunspaces = target.MaxRunspaces,
                Retention = target.Retention
            };
        }
    }

    public RunspaceSchedulerSettings Configure(string poolName, int? minRunspaces, int? maxRunspaces, TimeSpan? retention)
    {
        ThrowIfDisposed();
        var target = GetPool(poolName);

        lock (_configLock)
        {
            var desiredMin = minRunspaces ?? target.MinRunspaces;
            var desiredMax = maxRunspaces ?? target.MaxRunspaces;

            if (desiredMin < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(minRunspaces), "Minimum runspaces must be at least 1.");
            }

            if (desiredMax < desiredMin)
            {
                throw new ArgumentException("Max runspaces must be greater than or equal to min runspaces.");
            }

            if (desiredMax != target.MaxRunspaces)
            {
                AdjustThrottle(target, desiredMax);
                target.Pool.SetMaxRunspaces(desiredMax);
                target.MaxRunspaces = desiredMax;
            }

            if (desiredMin != target.MinRunspaces)
            {
                target.Pool.SetMinRunspaces(desiredMin);
                target.MinRunspaces = desiredMin;
            }

            if (retention.HasValue)
            {
                target.Retention = retention.Value;
            }

            return new RunspaceSchedulerSettings
            {
                MinRunspaces = target.MinRunspaces,
                MaxRunspaces = target.MaxRunspaces,
                Retention = target.Retention
            };
        }
    }

    public RunspaceSessionSettings GetSessionSettings(string? poolName = null)
    {
        ThrowIfDisposed();
        var target = GetPool(poolName ?? DefaultPoolName);
        lock (_configLock)
        {
            return new RunspaceSessionSettings
            {
                Modules = target.ImportModules.ToArray(),
                Variables = new Dictionary<string, object>(target.SessionVariables),
                InitScript = target.InitScript
            };
        }
    }

    public RunspaceSessionSettings ConfigureSession(string poolName, IEnumerable<string>? modules, IDictionary<string, object>? variables, ScriptBlock? initScript)
    {
        ThrowIfDisposed();
        var target = GetPool(poolName);

        lock (_configLock)
        {
            var nextModules = modules is not null
                ? modules
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Select(m => m.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>(target.ImportModules);

            var nextVariables = variables is not null
                ? new Dictionary<string, object>(variables, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object>(target.SessionVariables, StringComparer.OrdinalIgnoreCase);

            ValidateModules(nextModules);
            RebuildRunspacePoolIfIdle(target, nextModules, nextVariables);

            target.ImportModules = nextModules;
            target.SessionVariables = nextVariables;
            target.InitScript = initScript;

            return GetSessionSettings(poolName);
        }
    }

    public RunspaceTask StartTask(ScriptBlock scriptBlock, object[]? arguments, TimeSpan? timeout, string? name = null, string? poolName = null)
    {
        ThrowIfDisposed();

        var targetPool = poolName ?? DefaultPoolName;
        var ctx = CreateOrGetPool(targetPool);

        var task = new RunspaceTask(scriptBlock, arguments ?? Array.Empty<object>(), timeout);
        if (!string.IsNullOrWhiteSpace(name))
        {
            task.Name = name.Trim();
        }
        task.PoolName = targetPool;

        if (!ctx.Tasks.TryAdd(task.Id, task))
        {
            throw new InvalidOperationException("Failed to track new task.");
        }

        var execution = ExecuteAsync(ctx, task);
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

    private Task ExecuteAsync(PoolContext ctx, RunspaceTask task)
    {
        task.MarkScheduled();

        var sessionSnapshot = GetSessionSettings(task.PoolName);

        return Task.Run(async () =>
        {
            await ctx.Throttle.WaitAsync().ConfigureAwait(false);
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
                ps.RunspacePool = ctx.Pool;

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
                    var scriptText = task.ScriptBlock.ToString();
                    if (sessionSnapshot.InitScript is not null)
                    {
                        var initText = sessionSnapshot.InitScript.ToString();
                        scriptText = $@"
$state = $ExecutionContext.SessionState
if (-not (Get-Variable -Name 'BWInitRan' -Scope Global -ErrorAction SilentlyContinue)) {{
    Set-Variable -Name 'BWInitRan' -Scope Global -Value $true -Force
    & ([scriptblock]::Create(@'
{initText}
'@))
}}
{scriptText}";
                    }

                    ps.AddScript(scriptText, useLocalScope: true);
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
                ctx.Throttle.Release();
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

    private void AdjustThrottle(PoolContext ctx, int desiredMax)
    {
        var currentMax = ctx.MaxRunspaces;
        var delta = desiredMax - currentMax;
        if (delta == 0)
        {
            return;
        }

        if (delta > 0)
        {
            ctx.Throttle.Release(delta);
        }
        else
        {
            for (var i = 0; i < Math.Abs(delta); i++)
            {
                ctx.Throttle.Wait();
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

    private void RebuildRunspacePoolIfIdle(PoolContext ctx, IReadOnlyList<string> modules, IReadOnlyDictionary<string, object> variables)
    {
        var active = ctx.Tasks.Values.Any(t =>
            t.Status is RunspaceTaskStatus.Created or RunspaceTaskStatus.Scheduled or RunspaceTaskStatus.Running);
        if (active)
        {
            throw new InvalidOperationException("Cannot change session state while tasks are active. Wait for tasks to finish or stop them, then retry.");
        }

        var initialState = InitialSessionState.CreateDefault2();
        var baseModules = new[] { "Microsoft.PowerShell.Management", "Microsoft.PowerShell.Utility" };
        var toImport = baseModules.Concat(modules).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        initialState.ImportPSModule(toImport);

        if (variables.Count > 0)
        {
            foreach (var kvp in variables)
            {
                initialState.Variables.Add(new SessionStateVariableEntry(kvp.Key, kvp.Value, description: null, options: ScopedItemOptions.AllScope));
            }
        }

        var newPool = RunspaceFactory.CreateRunspacePool(initialState);
        newPool.SetMinRunspaces(ctx.MinRunspaces);
        newPool.SetMaxRunspaces(ctx.MaxRunspaces);
        newPool.ApartmentState = ApartmentState.MTA;
        newPool.Open();

        var oldPool = ctx.Pool;
        ctx.Pool = newPool;
        oldPool.Dispose();
    }

    private void PruneExpired()
    {
        if (_disposed)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var pool in _pools.Values)
        {
            foreach (var kvp in pool.Tasks)
            {
                var task = kvp.Value;
                if (task.CompletedAt.HasValue && now - task.CompletedAt.Value >= pool.Retention)
                {
                    pool.Tasks.TryRemove(kvp.Key, out _);
                }
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

    private PoolContext GetPool(string poolName)
    {
        if (!_pools.TryGetValue(poolName, out var ctx))
        {
            throw new InvalidOperationException($"Runspace pool '{poolName}' does not exist. Create it first with New-RunspacePool or use the default.");
        }

        return ctx;
    }

    private PoolContext CreateOrGetPool(string poolName, int? minRunspaces = null, int? maxRunspaces = null, TimeSpan? retention = null,
        IEnumerable<string>? modules = null, IDictionary<string, object>? variables = null, ScriptBlock? initScript = null)
    {
        return _pools.GetOrAdd(poolName, name =>
        {
            var min = minRunspaces ?? 1;
            var max = maxRunspaces ?? Math.Max(2, Environment.ProcessorCount);
            var retentionValue = retention ?? TimeSpan.FromMinutes(30);

            var initialState = InitialSessionState.CreateDefault2();
            var baseModules = new[] { "Microsoft.PowerShell.Management", "Microsoft.PowerShell.Utility" };
            var toImport = baseModules;
            if (modules is not null)
            {
                toImport = baseModules.Concat(modules).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            initialState.ImportPSModule(toImport);

            if (variables is not null)
            {
                foreach (var kvp in variables)
                {
                    initialState.Variables.Add(new SessionStateVariableEntry(kvp.Key, kvp.Value, description: null, options: ScopedItemOptions.AllScope));
                }
            }

            if (initScript is not null)
            {
                initialState.StartupScripts.Add(initScript.ToString());
            }

            var pool = RunspaceFactory.CreateRunspacePool(initialState);
            pool.SetMinRunspaces(min);
            pool.SetMaxRunspaces(max);
            pool.ApartmentState = ApartmentState.MTA;
            pool.Open();

            var throttle = new SemaphoreSlim(max, max);

            return new PoolContext
            {
                Name = poolName,
                Pool = pool,
                Throttle = throttle,
                MinRunspaces = min,
                MaxRunspaces = max,
                Retention = retentionValue,
                ImportModules = toImport.ToList(),
                SessionVariables = variables is null ? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, object>(variables, StringComparer.OrdinalIgnoreCase),
                InitScript = initScript
            };
        });
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
        foreach (var pool in _pools.Values)
        {
            foreach (var task in pool.Tasks.Values)
            {
                task.Cancellation.Cancel();
            }

            pool.Dispose();
        }
    }
}

# BackgroundWorker Telemetry/Storage Plugin Design (Draft)

Purpose: allow external providers (SQL, MongoDB, Webhook, etc.) to receive task lifecycle, progress, and stream data from the runspace scheduler without coupling core execution to any specific datastore.

## Goals
- Pluggable sinks: multiple simultaneous providers; easy to add new targets without touching the scheduler.
- Non-intrusive: task execution never blocks on slow sinks; failures in sinks must not break tasks.
- Rich telemetry: lifecycle transitions, output/error, progress, timing, and metadata.
- Durable history: optional persistence for UI/backends to query independently of live PowerShell.
- Minimal coupling: sinks operate on DTOs, not PowerShell engine objects.

## Event Surface
- Lifecycle events: `TaskCreated`, `TaskScheduled`, `TaskStarted`, `TaskProgress`, `TaskCompleted`, `TaskFailed`, `TaskCancelled`, `TaskTimedOut`.
- Stream events: `TaskOutput`, `TaskError`, `TaskWarning` (optional later), `TaskVerbose` (optional later), `TaskProgress` (progress records).
- Payload DTO fields (draft):
  - `TaskId` (Guid)
  - `Status` (enum)
  - `CreatedUtc`, `StartedUtc`, `CompletedUtc` (nullable)
  - `Timeout` (seconds)
  - `LastProgressPercent`, `LastProgressMessage` (for snapshots)
  - `ScriptId/Hash` (string), `ScriptText` (optional/configurable), `Arguments` (serialized JSON)
  - `FailureType`, `FailureMessage`, `InvocationInfo` (optional)
  - Streams: `Seq` (monotonic per task), `StreamType` (Out, Err, Progress, etc.), `Payload` (string/JSON), `CreatedUtc`

## Sink Contract
- Interface (concept): `IRunspaceTaskSink`
  - Async hooks: `OnTaskStartedAsync`, `OnTaskProgressAsync`, `OnTaskCompletedAsync`, `OnTaskFailedAsync`, `OnTaskOutputAsync`, `OnTaskErrorAsync`, etc.
  - Accept DTOs only; no PowerShell engine types.
  - Should be resilient: internal buffering/batching, retry/backoff, idempotent writes.
- Registration:
  - `Register-RunspaceTaskSink -Instance <object>` (or `-TypeName/-AssemblyPath` to load) and `Unregister-RunspaceTaskSink`.
  - Allow multiple sinks; fan-out events to each, isolating failures (log and continue).
  - Optional configuration object/hashtable passed at registration.

## Suggested Provider Behaviors
- SQL sink:
  - Tables: `Tasks(Id, Status, CreatedUtc, StartedUtc, CompletedUtc, TimeoutSec, LastProgressPct, LastProgressMsg, ScriptHash, ArgJson, FailureType, FailureMessage)`.
  - Streams: `TaskStreams(Id, TaskId, Seq, StreamType, Payload, CreatedUtc)`, unique index on `(TaskId, Seq)`.
  - Optional `Scripts` table (`Id/Hash`, `Text`) to de-duplicate script text.
  - Batching inserts; parameterized queries; JSON payload limits and truncation.
- Mongo sink:
  - Collections mirroring SQL tables; capped payload size; indexed by `TaskId` and `CreatedUtc`.
- Webhook sink:
  - Post JSON envelopes; retry with backoff; drop/park after N failures; sign payloads (HMAC).

## Scheduler Changes Needed
- Introduce DTOs for events; map from internal `RunspaceTask` and streams.
- Add event dispatcher with bounded queues per sink to avoid blocking task threads.
- Add registration cmdlets or API to attach/detach sinks.
- Configurable retention vs. persistence: allow disabling in-memory prune when relying on external storage.
- Optional: expose subscription in-process events (PowerShell events) for UI consumers.

## Config & Security
- Sinks own their configuration (connection strings, endpoints). Do not store secrets in task DTOs.
- Provide opt-in for including script text/args; default to hashes/IDs to avoid leaking sensitive data.
- Enforce payload size limits; truncate or reject oversize entries.
- Treat all output/args as untrusted when storing/serving to UI.

## Performance & Resilience
- Bounded queues per sink; drop or park on persistent failure with logging.
- Coalesce progress events (e.g., emit every N ms or on percent change) to reduce churn.
- Idempotent writes using task/seq keys.
- Ensure sink failures are surfaced via logs/events but do not fail task execution.

## Testing Requirements
- Unit tests for event mapping, sink fan-out, and backpressure behavior.
- Integration tests for sample sinks (e.g., SQL) using local containers or in-memory providers.
- Load tests to ensure progress-heavy tasks do not overwhelm sinks.

## Open Questions
- Should registration be per-runspace-pool instance or global singleton? (Current scheduler is singleton.)
- How to version event payloads for forward compatibility?
- Do we need a built-in file sink for offline debugging?
- Should we support resuming tasks from persisted state (requires queue/dispatcher semantics)?

## Rollout Plan
1) Define DTOs and `IRunspaceTaskSink` interface; add registration/fan-out in scheduler (no sinks yet).
2) Add sample sink (e.g., no-op logger) and tests.
3) Implement SQL sink in separate package/module with schema and docs.
4) Optional: webhook sink and UI sample that subscribes and updates via dispatcher.
5) Document configuration and retention interplay when external persistence is enabled.

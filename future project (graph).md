# BackgroundWorker Graph Helper (Future Project)

Goal: provide a thin helper module that layers on top of BackgroundWorker to call Microsoft Graph with per-task tokens, avoiding the Microsoft.Graph PowerShell module’s global MSAL cache and enabling safer multi-tenant concurrency.

## Objectives
- Per-task authentication: acquire Graph tokens inside each task with tenant-specific credentials (client credentials, device code, interactive) and keep caches in-memory only.
- No shared module state: do not load Microsoft.Graph.* modules; use raw REST (Invoke-RestMethod/Invoke-WebRequest) with explicit Authorization headers.
- First-class task helpers: expose Start-GraphTask wrappers that run via BackgroundWorker, emitting progress/events and returning structured results.
- Optional token refresh: for long-running tasks, refresh tokens inside the task without persisting tokens to disk.
- Extensible inputs: make it easy to declare request templates (method/URI/body/scopes) and batches; allow custom headers.
- Safety & isolation: never write token cache to disk; keep per-tenant secrets and token state scoped to the task scriptblock.

## Proposed PowerShell Surface
- `Get-GraphAccessToken`: returns an access token string or headers; parameters for tenant/client credentials, device code, interactive. Option to disable persistent MSAL cache by default.
- `Start-GraphTask`: wraps `Start-RunspaceTask`; parameters: `-TenantId/-ClientId/-ClientSecret|-Certificate|-DeviceCode`, `-Request` (URI/method/body/headers/scopes), `-OnProgress` scriptblock, `-Timeout`. Internally obtains token and issues REST call(s) with progress updates.
- `Start-GraphBatchTask`: accepts a set of request descriptors, executes sequentially or with limited parallelism inside the task, aggregates results/errors.
- `New-GraphRequest`: helper to build request objects (URI/method/body/headers/scopes) for reuse.
- Formatting: table views for common outputs; leverage BackgroundWorker progress/receive cmdlets.

## Implementation Notes
- Build as a pure PowerShell module that depends on BackgroundWorker (no binary unless needed). Keep MSAL usage minimal (Microsoft.Identity.Client or REST token endpoint).
- For token acquisition: prefer in-memory MSAL (disable persistent cache), or call the OAuth2 token endpoint with client credentials; device/interactive only when necessary.
- Handle throttling/retries: add simple backoff on 429/503; surface retry count in progress.
- Respect BackgroundWorker’s session state rules: do not mutate global pool; perform all auth inside the task scriptblock.
- Examples to ship:
  - List users in a tenant via client credentials.
  - Long-running export (paged GET) with progress and token refresh.
  - Multi-tenant scenario: start two tasks with different TenantIds and observe isolation.

## Testing
- Pester tests mocking token acquisition and REST calls.
- Verify no disk writes for cache; ensure parallel tasks with different tenants do not share tokens.
- Validate progress and error propagation through Receive-RunspaceTask and Receive-RunspaceTaskProgress.

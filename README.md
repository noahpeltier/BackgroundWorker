# BackgroundWorker (Runspace Task Scheduler)

Run background PowerShell work in a shared runspace pool with progress, cancellation, and throttling.

## Quick start
- Build (Release): `DOTNET_CLI_HOME=/tmp dotnet build BackgroundWorker/BackgroundWorker.csproj -c Release`
- Import: `Import-Module ./BackgroundWorker/BackgroundWorker.psd1`
- Run: `$t = Start-RunspaceTask { 1..3 | ForEach-Object { Start-Sleep 1; $_ } }`
- Run from file: `$t = Start-RunspaceTask -Script ./mytask.ps1 -ArgumentList 'hello'`
- Observe: `Get-RunspaceTask` and `Receive-RunspaceTask -Task $t`
- Cancel/timeout: `Stop-RunspaceTask -Task $t` or `Start-RunspaceTask -TimeoutSeconds 60 { ... }`
- Name tasks: `Start-RunspaceTask -Name 'Download ISO' { ... }` and see names in tables/live view
- Live view: `Show-RunspaceTasks -RefreshMilliseconds 500` (Spectre.Console-powered live table; Ctrl+C to exit)

## Session state (modules/variables)
- Configure defaults for all runspace tasks: `Set-RunspaceSessionState -Module 'MyModule' -Variable @{ PSModulePath = "$env:PSModulePath;/extra/path" }`
- The pool must be idle to change session state; if tasks are running you’ll get a friendly error—wait or stop them first.
- Validate modules before setting them: `Test-RunspaceModule -Module 'MyModule','OtherModule'`
- If a module isn’t found, you’ll get a clear error that includes `PSModulePath`; add the correct path or install the module.

## Progress and downloads
- Progress records are buffered: `Receive-RunspaceTaskProgress -Task $t`
- Helper for file transfers: `Start-RunspaceDownload -Uri 'https://example/file' -Destination '/tmp/file.iso' -UseBits`

## Testing
- Pester suite: `pwsh -NoLogo -NoProfile -Command "Invoke-Pester -Script ./tests/BackgroundWorker.Tests.ps1 -Output Detailed"`

## Notes
- Task state is in-memory only; retention defaults to 30 minutes after completion.
- Throttling defaults to `maxrunspaces = max(2, CPU count)`; adjust with `Set-RunspaceScheduler`.

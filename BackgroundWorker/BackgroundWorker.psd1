@{
    RootModule           = 'out/Release/net8.0/BackgroundWorker.dll'
    ModuleVersion        = '0.1.0'
    GUID                 = 'd03a60a3-419f-4b5c-9dfa-2e61f652e8a8'
    Author               = 'Runspace Scheduler'
    CompanyName          = 'Community'
    Copyright            = '(c) Runspace Scheduler. All rights reserved.'
    Description          = 'Runspace pool-based background task scheduler for long-running PowerShell workloads.'
    PowerShellVersion    = '7.0'
    CompatiblePSEditions = @('Core')
    FunctionsToExport    = @()
    CmdletsToExport      = @(
        'Start-RunspaceTask',
        'Get-RunspaceTask',
        'Receive-RunspaceTask',
        'Receive-RunspaceTaskProgress',
        'Stop-RunspaceTask',
        'Wait-RunspaceTask',
        'Get-RunspaceScheduler',
        'Set-RunspaceScheduler',
        'Start-RunspaceDownload',
        'Get-RunspaceSessionState',
        'Set-RunspaceSessionState',
        'Test-RunspaceModule'
    )
    AliasesToExport      = @()
    FormatsToProcess     = @('BackgroundWorker.format.ps1xml')
    PrivateData          = @{
        PSData = @{
            Tags = @('Runspace', 'Scheduler', 'ThreadJob', 'Task')
        }
    }
}

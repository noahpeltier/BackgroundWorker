#requires -Version 7

$ErrorActionPreference = 'Stop'

Describe 'BackgroundWorker module' {
    BeforeAll {
        $script:here = $PSScriptRoot
        $script:repoRoot = Resolve-Path (Join-Path $here '..')
        $script:modulePath = Join-Path $repoRoot 'BackgroundWorker/BackgroundWorker.psd1'
        $script:moduleName = 'BackgroundWorker'

        Push-Location $repoRoot
        try {
            $env:DOTNET_CLI_HOME = '/tmp'
            dotnet build BackgroundWorker/BackgroundWorker.csproj -c Release | Out-Null
        }
        finally {
            Pop-Location
        }

        Import-Module $script:modulePath -Force
    }

    AfterAll {
        Remove-Module $script:moduleName -ErrorAction SilentlyContinue
    }

    It 'completes a simple task and returns output' {
        $task = Start-RunspaceTask -Name 'Simple' { param($ms) Start-Sleep -Milliseconds $ms; "done-$ms" } -ArgumentList 50
        Wait-RunspaceTask -Task $task -TimeoutSeconds 5 | Out-Null

        $task.Status | Should -Be ([BackgroundWorker.RunspaceTaskStatus]::Completed)
        $task.Name | Should -Be 'Simple'
        (Receive-RunspaceTask -Task $task) | Should -Contain 'done-50'
    }

    It 'can cancel a running task' {
        $task = Start-RunspaceTask { Start-Sleep -Seconds 10; 'ignored' }
        Stop-RunspaceTask -Task $task | Out-Null
        Wait-RunspaceTask -Task $task -TimeoutSeconds 5 | Out-Null

        $task.Status | Should -Be ([BackgroundWorker.RunspaceTaskStatus]::Cancelled)
    }

    It 'times out a task when internal timeout elapses' {
        $task = Start-RunspaceTask -TimeoutSeconds 1 { Start-Sleep -Seconds 5; 'late' }
        Wait-RunspaceTask -Task $task -TimeoutSeconds 5 | Out-Null

        $task.Status | Should -Be ([BackgroundWorker.RunspaceTaskStatus]::TimedOut)
    }

    It 'updates scheduler configuration' {
        $configured = Set-RunspaceScheduler -MinRunspaces 1 -MaxRunspaces 2 -RetentionMinutes 5
        $configured.MinRunspaces | Should -Be 1
        $configured.MaxRunspaces | Should -Be 2
        [int]$configured.Retention.TotalMinutes | Should -Be 5

        $fetched = Get-RunspaceScheduler
        $fetched.MaxRunspaces | Should -Be 2
    }

    It 'captures progress records' {
        $task = Start-RunspaceTask {
            for ($i = 0; $i -le 100; $i += 50) {
                Write-Progress -Activity 'Work' -Status "Step $i" -PercentComplete $i
                Start-Sleep -Milliseconds 10
            }
        }

        Wait-RunspaceTask -Task $task -TimeoutSeconds 5 | Out-Null
        $progress = Receive-RunspaceTaskProgress -Task $task
        $progress.Count | Should -BeGreaterThan 0
        ($task.LastProgress.PercentComplete) | Should -Be 100
    }

    It 'runs a script file with arguments' {
        $scriptPath = Join-Path ([System.IO.Path]::GetTempPath()) "bw-test-$([guid]::NewGuid()).ps1"
        try {
            Set-Content -Path $scriptPath -Value 'param($name) "hello $name"'
            $task = Start-RunspaceTask -Script $scriptPath -ArgumentList 'world'
            Wait-RunspaceTask -Task $task -TimeoutSeconds 5 | Out-Null
            (Receive-RunspaceTask -Task $task) | Should -Contain 'hello world'
        }
        finally {
            Remove-Item $scriptPath -ErrorAction SilentlyContinue
        }
    }

    It 'throws when script file is missing' {
        $missing = Join-Path ([System.IO.Path]::GetTempPath()) "bw-missing-$([guid]::NewGuid()).ps1"
        { Start-RunspaceTask -Script $missing } | Should -Throw
    }

    It 'applies configured session variables' {
        $state = Set-RunspaceSessionState -Variable @{ Answer = 41 }
        $state.Variables['Answer'] | Should -Be 41
    }

    It 'runs init script once per runspace' {
        $origSched = Get-RunspaceScheduler
        $origSession = Get-RunspaceSessionState
        try {
            Set-RunspaceScheduler -MinRunspaces 1 -MaxRunspaces 1 | Out-Null
            Set-RunspaceSessionState -InitScript {
                if (-not (Get-Variable -Name Count -Scope Global -ErrorAction SilentlyContinue)) {
                    $Global:Count = 0
                }
                $Global:Count++
            } | Out-Null

            $t1 = Start-RunspaceTask { $Global:Count }
            Wait-RunspaceTask -Task $t1 -TimeoutSeconds 5 | Out-Null
            $t2 = Start-RunspaceTask { $Global:Count }
            Wait-RunspaceTask -Task $t2 -TimeoutSeconds 5 | Out-Null

            (Receive-RunspaceTask -Task $t1) | Should -Contain 1
            (Receive-RunspaceTask -Task $t2) | Should -Contain 1
        }
        finally {
            Set-RunspaceSessionState -Module $origSession.Modules -Variable ([hashtable]$origSession.Variables) -InitScript $origSession.InitScript | Out-Null
            Set-RunspaceScheduler -MinRunspaces $origSched.MinRunspaces -MaxRunspaces $origSched.MaxRunspaces -RetentionMinutes ([int]$origSched.Retention.TotalMinutes) | Out-Null
        }
    }

    It 'isolates session state across pools' {
        # Pool A with variable 1
        New-RunspacePool -Name PoolA -Variable @{ Marker = 'A' } | Out-Null
        # Pool B with variable 2
        New-RunspacePool -Name PoolB -Variable @{ Marker = 'B' } | Out-Null

        $tA = Start-RunspaceTask -Pool PoolA { $Marker }
        $tB = Start-RunspaceTask -Pool PoolB { $Marker }
        Wait-RunspaceTask -Task $tA -TimeoutSeconds 5 | Out-Null
        Wait-RunspaceTask -Task $tB -TimeoutSeconds 5 | Out-Null

        (Receive-RunspaceTask -Task $tA) | Should -Contain 'A'
        (Receive-RunspaceTask -Task $tB) | Should -Contain 'B'
    }

    It 'filters tasks by pool' {
        New-RunspacePool -Name PoolX | Out-Null
        $tX = Start-RunspaceTask -Pool PoolX { 'x' }
        $tD = Start-RunspaceTask { 'd' }
        Wait-RunspaceTask -Task $tX -TimeoutSeconds 5 | Out-Null
        Wait-RunspaceTask -Task $tD -TimeoutSeconds 5 | Out-Null

        (Get-RunspaceTask -Pool PoolX).Id | Should -Contain $tX.Id
        (Get-RunspaceTask -Pool PoolX).Id | Should -Not -Contain $tD.Id
    }

    It 'reports module availability' {
        $results = Test-RunspaceModule -Module 'Microsoft.PowerShell.Management', 'DefinitelyMissingModule.ForTest'
        ($results | Where-Object Name -eq 'Microsoft.PowerShell.Management').Available | Should -BeTrue
        ($results | Where-Object Name -eq 'DefinitelyMissingModule.ForTest').Available | Should -BeFalse
    }

    It 'throws a friendly error when a module is missing' {
        $caught = $null
        try {
            Set-RunspaceSessionState -Module 'DefinitelyMissingModule.ForTest'
        }
        catch {
            $caught = $_
        }

        $caught | Should -Not -BeNullOrEmpty
        $caught.Exception.Message | Should -Match 'Failed to import modules'
    }

    It 'removes completed tasks' {
        $task = Start-RunspaceTask { 'done' }
        Wait-RunspaceTask -Task $task -TimeoutSeconds 5 | Out-Null

        Remove-RunspaceTask -Task $task | Out-Null
        (Get-RunspaceTask | Where-Object Id -eq $task.Id).Count | Should -Be 0
    }

    It 'refuses to remove running tasks' {
        $task = Start-RunspaceTask { Start-Sleep -Seconds 2 }
        { Remove-RunspaceTask -Task $task } | Should -Throw
        Stop-RunspaceTask -Task $task | Out-Null
        Wait-RunspaceTask -Task $task -TimeoutSeconds 5 | Out-Null
        Remove-RunspaceTask -Task $task | Out-Null
    }
}

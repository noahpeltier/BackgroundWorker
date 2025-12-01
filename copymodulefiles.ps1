$ErrorActionPreference = 'Stop'

$moduleRoot = Join-Path $PSScriptRoot 'BackgroundWorker'
$binPath    = Join-Path $moduleRoot 'bin/Release/net8.0'

$paths = @(
    (Join-Path $moduleRoot 'BackgroundWorker.psd1'),
    (Join-Path $moduleRoot 'BackgroundWorker.format.ps1xml'),
    (Join-Path $binPath 'BackgroundWorker.dll'),
    (Join-Path $binPath 'Spectre.Console.dll')
)

$destination = Join-Path $PSScriptRoot 'module'
New-Item -ItemType Directory -Path $destination -Force | Out-Null

foreach ($path in $paths) {
    if (-not (Test-Path $path)) {
        throw "Missing build output: $path. Run a Release build first."
    }
    Copy-Item -Path $path -Destination $destination -Force
}

Write-Host "Copied module artifacts to $destination"

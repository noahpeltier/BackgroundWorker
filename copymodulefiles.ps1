$ErrorActionPreference = 'Stop'

$moduleRoot = Join-Path $PSScriptRoot 'BackgroundWorker'
$binPath    = Join-Path $moduleRoot 'bin/Release/net8.0'

$paths = @(
    (Join-Path $moduleRoot 'BackgroundWorker.format.ps1xml'),
    (Join-Path $binPath 'BackgroundWorker.dll'),
    (Join-Path $binPath 'Spectre.Console.dll')
)
$version = (Import-PowerShellDataFile -path (Join-Path $moduleRoot 'BackgroundWorker.psd1')).ModuleVersion.tostring()
$psd1Content = Get-Content -raw -path (Join-Path $moduleRoot 'BackgroundWorker.psd1')
$psd1Content = $psd1Content -replace 'bin/Release/net8.0/'

$destination = Join-Path $PSScriptRoot $version
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$psd1Content | out-file (Join-Path $destination 'BackgroundWorker.psd1') -Encoding utf8

foreach ($path in $paths) {
    if (-not (Test-Path $path)) {
        throw "Missing build output: $path. Run a Release build first."
    }
    Copy-Item -Path $path -Destination $destination -Force
}

Write-Host "Copied module artifacts to $destination"

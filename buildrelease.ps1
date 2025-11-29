#requires -Version 7

param(
    [string]$Configuration = 'Release',
    [string]$Version = '0.1.0',
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

$env:DOTNET_CLI_HOME = '/tmp'

Write-Host "Building BackgroundWorker ($Configuration)..." -ForegroundColor Cyan
dotnet build "$repoRoot/BackgroundWorker/BackgroundWorker.csproj" -c $Configuration | Out-Null

$moduleSrc = Join-Path $repoRoot 'BackgroundWorker'
$moduleName = Split-Path $moduleSrc -Leaf
$targetRoot = if ($OutputRoot) { $OutputRoot } else { Join-Path $repoRoot 'release' }
$targetPath = Join-Path $targetRoot $Version

if (Test-Path $targetPath) {
    Remove-Item -Recurse -Force $targetPath
}

Write-Host "Staging module to $targetPath" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $targetPath | Out-Null

Copy-Item -Recurse -Force "$moduleSrc/*" $targetPath

# Optionally flatten DLL to module root instead of out/Release path
# (uncomment if you change RootModule in the PSD1 to 'BackgroundWorker.dll')
# $dll = Join-Path $moduleSrc "out/$Configuration/net8.0/BackgroundWorker.dll"
# Copy-Item -Force $dll (Join-Path $targetPath 'BackgroundWorker.dll')

Write-Host "Done. Copy $targetPath to a PSModulePath location (e.g. $HOME/Documents/PowerShell/Modules/BackgroundWorker/$Version)" -ForegroundColor Green

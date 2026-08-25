# Build the ContentUnlocker mod without rebuilding font atlases.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
New-Item -ItemType Directory -Force -Path `
    (Join-Path $root 'build\mods\ContentUnlocker') | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe"
if (-not (Test-Path "$root\build\PunchLoader.dll")) {
    Write-Error "build\PunchLoader.dll not found. Run scripts\build_all.ps1 first."
    exit 1
}

& $csc "@mods\ContentUnlocker\build.rsp"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Built ContentUnlocker.dll"

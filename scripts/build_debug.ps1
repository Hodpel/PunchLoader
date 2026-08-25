# Build the developer-only localization QA mod.
# This script is intentionally separate from build_all.ps1.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
New-Item -ItemType Directory -Force -Path (Join-Path $root 'build\mods\LocalizationDebug') | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe"
if (-not (Test-Path "$root\build\PunchLoader.dll")) {
    Write-Error "build\PunchLoader.dll not found. Run scripts\build_all.ps1 first."
    exit 1
}

& $csc "@mods\LocalizationDebug\build.rsp"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Built LocalizationDebug.dll"

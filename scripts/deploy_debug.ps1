# Explicitly build and deploy the developer-only localization QA mod.
# The normal deploy.ps1 never installs this mod.

param(
    [string]$GameDir = "F:\Codex\MBP_PROJ\ModdedGame"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
& "$PSScriptRoot\build_debug.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$source = "$root\mods\LocalizationDebug"
$build = "$root\build\mods\LocalizationDebug\LocalizationDebug.dll"
$target = "$GameDir\Mods\LocalizationDebug"

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item $build "$target\LocalizationDebug.dll" -Force -ErrorAction Stop
Copy-Item "$source\plugin.json" "$target\plugin.json" -Force -ErrorAction Stop
Copy-Item "$source\untranslated-allowlist.txt" "$target\untranslated-allowlist.txt" -Force -ErrorAction Stop
Write-Host "Deployed LocalizationDebug mod to $target"

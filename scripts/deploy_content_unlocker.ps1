param(
    [string]$GameDir = "F:\Codex\MBP_PROJ\ModdedGame"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
& "$PSScriptRoot\build_content_unlocker.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$source = "$root\mods\ContentUnlocker"
$build = "$root\build\mods\ContentUnlocker\ContentUnlocker.dll"
$target = "$GameDir\Mods\ContentUnlocker"

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -LiteralPath $build -Destination `
    (Join-Path $target 'ContentUnlocker.dll') -Force
Copy-Item -LiteralPath "$source\plugin.json" -Destination `
    (Join-Path $target 'plugin.json') -Force
Write-Host "Deployed ContentUnlocker mod to $target"

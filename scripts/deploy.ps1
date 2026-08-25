param(
    [string]$GameDir = 'F:\Codex\MBP_PROJ\ModdedGame'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$managed = Join-Path $GameDir 'MegabytePunch_Data\Managed'
if (-not (Test-Path -LiteralPath $managed -PathType Container)) {
    throw "Game Managed directory not found: $managed"
}

foreach ($file in @(
    'build\PunchLoader.dll',
    'build\Injector.exe',
    'deps\Cecil\Mono.Cecil.dll',
    'deps\Cecil\Mono.Cecil.Pdb.dll',
    'deps\Cecil\Mono.Cecil.Mdb.dll'
)) {
    $source = Join-Path $root $file
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Build or dependency file not found: $source"
    }
    Copy-Item -LiteralPath $source -Destination $managed -Force
}

Write-Host "Deployed PunchLoader runtime files to: $managed"
Write-Host 'External mods are built and deployed from their own repositories.'


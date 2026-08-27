param(
    [string]$Csc = 'C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $root 'build\tests\setup'
$resolvedBuild = [IO.Path]::GetFullPath((Join-Path $root 'build')).TrimEnd('\') + '\'
$resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
if (-not $resolvedFixture.StartsWith($resolvedBuild, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Setup fixture escaped build directory: $resolvedFixture"
}

& (Join-Path $PSScriptRoot 'build_setup.ps1') -Csc $Csc
if ($LASTEXITCODE -ne 0) { throw "Setup build failed: $LASTEXITCODE" }

if (Test-Path -LiteralPath $fixtureRoot) {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null

function Invoke-Setup([string]$game, [string]$action, [int]$expected) {
    $process = Start-Process -FilePath (Join-Path $game 'PunchLoader.Setup.exe') `
        -ArgumentList $action -Wait -PassThru -WindowStyle Hidden
    $actual = $process.ExitCode
    if ($actual -ne $expected) {
        throw "$action returned $actual, expected $expected ($game)"
    }
}

function Assert-File([string]$path, [bool]$expected) {
    if ((Test-Path -LiteralPath $path) -ne $expected) {
        throw "Unexpected file state: $path expected=$expected"
    }
}

function Test-Channel([string]$name, [string]$source) {
    $game = Join-Path $fixtureRoot $name
    $managed = Join-Path $game 'MegabytePunch_Data\Managed'
    $backup = Join-Path $managed 'PunchLoader.Backup'
    New-Item -ItemType Directory -Force -Path $managed | Out-Null
    New-Item -ItemType File -Force -Path (Join-Path $game 'MegabytePunch.exe') | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'build\PunchLoader.Setup.exe') -Destination $game
    Copy-Item -LiteralPath $source -Destination (Join-Path $managed 'Assembly-CSharp.dll')
    Copy-Item -LiteralPath (Join-Path $root 'deps\Unity\UnityEngine.dll') -Destination $managed
    $originalHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash

    Invoke-Setup $game 'install' 0
    Assert-File (Join-Path $managed 'PunchLoader.dll') $true
    Assert-File (Join-Path $backup 'Assembly-CSharp.original.dll') $true
    Assert-File (Join-Path $backup 'Assembly-CSharp.before-install.dll') $false
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $backup 'Assembly-CSharp.original.dll')).Hash -ne $originalHash) {
        throw "$name original backup hash mismatch"
    }
    Invoke-Setup $game 'status' 0

    Invoke-Setup $game 'uninstall' 0
    Assert-File (Join-Path $managed 'PunchLoader.dll') $false
    Assert-File (Join-Path $backup 'Assembly-CSharp.before-uninstall.dll') $false
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll')).Hash -ne $originalHash) {
        throw "$name uninstall hash mismatch"
    }

    # A fully injected game may update the loader without a backup, but cannot uninstall.
    Invoke-Setup $game 'install' 0
    Remove-Item -LiteralPath (Join-Path $backup 'Assembly-CSharp.original.dll') -Force
    Invoke-Setup $game 'install' 0
    Invoke-Setup $game 'uninstall' 1
    Write-Host "PASS setup fixture: $name"
}

Test-Channel 'Retail' (Join-Path $root 'deps\Game\Retail\Assembly-CSharp.dll')
Test-Channel 'Steam' (Join-Path $root 'deps\Game\Steam\Assembly-CSharp.dll')

Write-Host 'All setup fixtures passed.'

param(
    [string]$Version = '1.0.0',
    [string]$Csc = 'C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$expectedOriginal = 'EF53EB7B6438422EA0C40C96C7CE698E03C164CC2B1E2A21F601DAF3DEDB8492'
$original = Join-Path $root 'deps\Game\Assembly-CSharp.dll'

& (Join-Path $PSScriptRoot 'build_setup.ps1') -Csc $Csc
if ($LASTEXITCODE -ne 0) { throw "Setup build failed: $LASTEXITCODE" }

& $Csc '@mods\ChineseLocalization\build.rsp'
if ($LASTEXITCODE -ne 0) { throw "ChineseLocalization build failed: $LASTEXITCODE" }

$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $original).Hash
if ($actual -ne $expectedOriginal) {
    throw "Original Assembly-CSharp.dll hash mismatch: $actual"
}

$dist = Join-Path $root 'dist'
$unpacked = Join-Path $dist 'unpacked'
$folder = Join-Path $unpacked ("PunchLoader-v" + $Version)
$zip = Join-Path $dist ("PunchLoader-v" + $Version + '.zip')
$legacyFolder = Join-Path $dist ("PunchLoader-v" + $Version)

$resolvedDist = [IO.Path]::GetFullPath($dist).TrimEnd('\') + '\'
foreach ($candidate in @($folder, $legacyFolder)) {
    $resolvedCandidate = [IO.Path]::GetFullPath($candidate)
    if (-not $resolvedCandidate.StartsWith($resolvedDist,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release path escaped dist: $resolvedCandidate"
    }
    if (Test-Path -LiteralPath $resolvedCandidate) {
        Remove-Item -LiteralPath $resolvedCandidate -Recurse -Force
    }
}

New-Item -ItemType Directory -Force -Path $folder | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'build\PunchLoader.Setup.exe') -Destination $folder -Force
Copy-Item -LiteralPath $original -Destination (Join-Path $folder 'Assembly-CSharp.dll') -Force

$modSource = Join-Path $root 'mods\ChineseLocalization'
$modTarget = Join-Path $folder 'Mods\ChineseLocalization'
New-Item -ItemType Directory -Force -Path $modTarget | Out-Null
Copy-Item -LiteralPath (Join-Path $modSource 'ChineseLocalization.dll') -Destination $modTarget -Force
Copy-Item -LiteralPath (Join-Path $modSource 'plugin.json') -Destination $modTarget -Force
foreach ($directory in @('fonts', 'data', 'licenses')) {
    Copy-Item -LiteralPath (Join-Path $modSource $directory) `
        -Destination (Join-Path $modTarget $directory) -Recurse -Force
}

if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $zip

Write-Host "Package: $zip"
Write-Host "Unpacked: $folder"
Write-Host "Contents: PunchLoader.Setup.exe, Assembly-CSharp.dll, Mods\ChineseLocalization"

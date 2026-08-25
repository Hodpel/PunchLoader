param(
    [string]$Version = '1.0.0',
    [string]$Csc = 'C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$expectedOriginal = 'EF53EB7B6438422EA0C40C96C7CE698E03C164CC2B1E2A21F601DAF3DEDB8492'
$original = Join-Path $root 'deps\Game\Retail\Assembly-CSharp.dll'

function Ensure-Directory([string]$Path) {
    if ([IO.Directory]::Exists($Path)) { return }
    New-Item -Path $Path -ItemType Directory -Force | Out-Null
    if (-not [IO.Directory]::Exists($Path)) {
        throw "Could not create release directory: $Path"
    }
}

function Wait-ReleasePathRemoved([string]$Path) {
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if (-not [IO.Directory]::Exists($Path) -and -not [IO.File]::Exists($Path)) {
            return
        }
        Start-Sleep -Milliseconds 50
    }
    throw "Release path was not removed in time: $Path"
}

& (Join-Path $PSScriptRoot 'build_setup.ps1') -Csc $Csc
if ($LASTEXITCODE -ne 0) { throw "Setup build failed: $LASTEXITCODE" }

Ensure-Directory (Join-Path $root 'build\mods\ChineseLocalization')
& $Csc '@mods\ChineseLocalization\build.rsp'
if ($LASTEXITCODE -ne 0) { throw "ChineseLocalization build failed: $LASTEXITCODE" }

$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $original).Hash
if ($actual -ne $expectedOriginal) {
    throw "Original Assembly-CSharp.dll hash mismatch: $actual"
}

$dist = Join-Path $root 'dist'
$unpacked = Join-Path $dist 'unpacked'
$loaderName = "PunchLoader-v" + $Version
$localizationName = "ChineseLocalization-v" + $Version
$combinedName = $loaderName + '-with-ChineseLocalization'
$loaderFolder = Join-Path $unpacked $loaderName
$localizationFolder = Join-Path $unpacked $localizationName
$combinedFolder = Join-Path $unpacked $combinedName
$loaderZip = Join-Path $dist ($loaderName + '.zip')
$localizationZip = Join-Path $dist ($localizationName + '.zip')
$combinedZip = Join-Path $dist ($combinedName + '.zip')
$legacyFolder = Join-Path $dist $loaderName

Ensure-Directory $dist
Ensure-Directory $unpacked

$resolvedDist = [IO.Path]::GetFullPath($dist).TrimEnd('\') + '\'
foreach ($candidate in @($loaderFolder, $localizationFolder, $combinedFolder, $legacyFolder)) {
    $resolvedCandidate = [IO.Path]::GetFullPath($candidate)
    if (-not $resolvedCandidate.StartsWith($resolvedDist,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release path escaped dist: $resolvedCandidate"
    }
    if (Test-Path -LiteralPath $resolvedCandidate) {
        Remove-Item -LiteralPath $resolvedCandidate -Recurse -Force
        Wait-ReleasePathRemoved $resolvedCandidate
    }
}

foreach ($zip in @($loaderZip, $localizationZip, $combinedZip)) {
    if (Test-Path -LiteralPath $zip) {
        Remove-Item -LiteralPath $zip -Force
        Wait-ReleasePathRemoved $zip
    }
}

# PunchLoader 的标准包必须保持纯净，只提供 Loader 安装所需文件。
Ensure-Directory $loaderFolder
Copy-Item -LiteralPath (Join-Path $root 'build\PunchLoader.Setup.exe') `
    -Destination (Join-Path $loaderFolder 'PunchLoader.Setup.exe') -Force
Copy-Item -LiteralPath $original -Destination (Join-Path $loaderFolder 'Assembly-CSharp.dll') -Force

$modSource = Join-Path $root 'mods\ChineseLocalization'
$modBinary = Join-Path $root 'build\mods\ChineseLocalization\ChineseLocalization.dll'
function Copy-ChineseLocalization([string]$Target) {
    Ensure-Directory $Target
    Copy-Item -LiteralPath $modBinary `
        -Destination (Join-Path $Target 'ChineseLocalization.dll') -Force
    Copy-Item -LiteralPath (Join-Path $modSource 'plugin.json') `
        -Destination (Join-Path $Target 'plugin.json') -Force
    foreach ($directory in @('fonts', 'data', 'licenses')) {
        Copy-Item -LiteralPath (Join-Path $modSource $directory) `
            -Destination (Join-Path $Target $directory) -Recurse -Force
    }
}

# 简体中文模组独立包：解压到游戏 Mods 目录即可。
$standaloneMod = Join-Path $localizationFolder 'ChineseLocalization'
Copy-ChineseLocalization $standaloneMod

# 可选整合包：同时包含干净 Loader 安装文件和 Mods/ChineseLocalization。
Ensure-Directory $combinedFolder
Copy-Item -LiteralPath (Join-Path $loaderFolder 'PunchLoader.Setup.exe') -Destination $combinedFolder -Force
Copy-Item -LiteralPath (Join-Path $loaderFolder 'Assembly-CSharp.dll') -Destination $combinedFolder -Force
Copy-ChineseLocalization (Join-Path $combinedFolder 'Mods\ChineseLocalization')

Compress-Archive -Path (Join-Path $loaderFolder '*') -DestinationPath $loaderZip
Compress-Archive -Path (Join-Path $localizationFolder '*') -DestinationPath $localizationZip
Compress-Archive -Path (Join-Path $combinedFolder '*') -DestinationPath $combinedZip

Write-Host "PunchLoader: $loaderZip"
Write-Host "ChineseLocalization: $localizationZip"
Write-Host "Combined: $combinedZip"
Write-Host "Unpacked: $unpacked"

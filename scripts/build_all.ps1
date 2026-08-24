# 一键编译 PunchLoader
# 编译 Injector.exe、PunchLoader.dll 与随仓库维护的 ChineseLocalization 模组
# 用法: .\build_all.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
New-Item -ItemType Directory -Force -Path (Join-Path $root 'build') | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe"
$python = Join-Path $env:LOCALAPPDATA "Programs\Python\Python311\python.exe"
if (-not (Test-Path $python)) {
    $python = "C:\Python311\python.exe"
}
if (-not (Test-Path $python)) {
    Write-Host "Python 3.11 not found. Set up Python 3.11 under LocalAppData or C:\Python311."
    exit 1
}

Write-Host "=== Building menu and part font atlases ==="
$exportedAssets = Get-ChildItem (Split-Path -Parent $root) -Directory |
    ForEach-Object { Join-Path $_.FullName 'ExportedProject\Assets' } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
if (-not $exportedAssets) {
    Write-Host "ExportedProject Assets directory not found beside the repository parent."
    exit 1
}
& $python tools\build_font_atlas.py `
  --ack-font (Join-Path $exportedAssets 'Font\ACKNOWTT_white.asset') `
  --ack-texture (Join-Path $exportedAssets 'Texture2D\Font Texture_0.texture2D') `
  --ack-small-font (Join-Path $exportedAssets 'Font\ACKNOWTT_white_small.asset') `
  --ack-small-texture (Join-Path $exportedAssets 'Texture2D\Font Texture_2.texture2D')
if ($LASTEXITCODE -ne 0) { Write-Host "FAILED"; exit $LASTEXITCODE }

Write-Host "=== Building dialogue font atlas ==="
& $csc /nologo /target:exe /out:build\DialogueFontAtlasBuilder.exe /r:System.Drawing.dll tools\build_dialogue_font_atlas.cs
if ($LASTEXITCODE -ne 0) { Write-Host "FAILED"; exit $LASTEXITCODE }
& .\build\DialogueFontAtlasBuilder.exe $root
if ($LASTEXITCODE -ne 0) { Write-Host "FAILED"; exit $LASTEXITCODE }

Write-Host "=== Compiling Injector.exe ==="
& $csc "@src\injector\build_injector.rsp"
if ($LASTEXITCODE -ne 0) { Write-Host "FAILED"; exit $LASTEXITCODE }

Write-Host "=== Compiling PunchLoader.dll ==="
& $csc "@src\modloader\build_loader.rsp"
if ($LASTEXITCODE -ne 0) { Write-Host "FAILED"; exit $LASTEXITCODE }

Write-Host "=== Compiling ChineseLocalization.dll ==="
& $csc "@mods\ChineseLocalization\build.rsp"
if ($LASTEXITCODE -ne 0) { Write-Host "FAILED"; exit $LASTEXITCODE }

Write-Host "=== Done ==="


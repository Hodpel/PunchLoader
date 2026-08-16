# 一键编译 PunchLoader
# 编译 Injector.exe、PunchLoader.dll 与随仓库维护的 ChineseLocalization 模组
# 用法: .\build_all.ps1

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
New-Item -ItemType Directory -Force -Path (Join-Path F:\Codex\MBP_PROJ\PunchLoader 'build') | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe"

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


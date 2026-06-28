# 一键编译 PunchLoader
# 编译 Injector.exe + MBPModLoader.dll
# 用法: .\build_all.ps1

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$csc = "C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe"

Write-Host "=== Compiling Injector.exe ==="
& $csc "@src\injector\build_injector.rsp"
if ($LASTEXITCODE -ne 0) { Write-Host "FAILED"; exit $LASTEXITCODE }

Write-Host "=== Compiling MBPModLoader.dll ==="
& $csc "@src\modloader\build_loader.rsp"
if ($LASTEXITCODE -ne 0) { Write-Host "FAILED"; exit $LASTEXITCODE }

Write-Host "=== Done ==="

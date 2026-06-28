# 一键编译 PunchLoader
# 编译 Injector.exe + PunchModLoader.dll
# 用法: .\build_all.ps1

$root = Split-Path -Parent $PSScriptRoot

Write-Host "=== Compiling Injector.exe ==="
csc @"$root\src\injector\build_injector.rsp"

Write-Host "=== Compiling PunchModLoader.dll ==="
csc @"$root\src\modloader\build_loader.rsp"

Write-Host "=== Done ==="

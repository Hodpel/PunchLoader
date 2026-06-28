# 运行注入器
# 用法: .\inject.ps1
# 前提: 已编译 Injector.exe

$root = Split-Path -Parent $PSScriptRoot

$injector = "$root\src\injector\Injector.exe"
if (-not (Test-Path $injector)) {
    Write-Host "Injector.exe not found, run build_all.ps1 first"
    exit 1
}

Write-Host "=== Running Injector ==="
& $injector
Write-Host "=== Done ==="

# 运行注入器
# 用法: .\inject.ps1

$root = Split-Path -Parent $PSScriptRoot

$injector = "$root\build\Injector.exe"
if (-not (Test-Path $injector)) {
    Write-Host "Injector.exe not found, run build_all.ps1 first"
    exit 1
}

Write-Host "=== Running Injector ==="
Push-Location "$root\build"
try {
    & $injector
} finally {
    Pop-Location
}
Write-Host "=== Done ==="

# 部署到测试游戏目录
# 用法: .\deploy.ps1 -GameDir "F:\Codex\MBP_PROJ\Game"
# 复制 PunchLoader.dll + Injector.exe 到游戏 Managed 目录

param(
    [string]$GameDir = "F:\Codex\MBP_PROJ\ModdedGame"
)

$root = Split-Path -Parent $PSScriptRoot

$managed = "$GameDir\MegabytePunch_Data\Managed"
if (-not (Test-Path $managed)) {
    Write-Host "Game directory not found: $managed"
    exit 1
}

Copy-Item "$root\build\PunchLoader.dll" "$managed\"
Write-Host "Deployed PunchLoader.dll"

Copy-Item "$root\build\Injector.exe" "$managed\"
Copy-Item "$root\deps\Cecil\Mono.Cecil.dll" "$managed\"
Copy-Item "$root\deps\Cecil\Mono.Cecil.Pdb.dll" "$managed\"
Copy-Item "$root\deps\Cecil\Mono.Cecil.Mdb.dll" "$managed\"
Write-Host "Deployed Injector.exe + Cecil"

Write-Host "=== Done ==="

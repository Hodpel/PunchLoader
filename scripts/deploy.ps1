# 部署到测试游戏目录
# 用法: .\deploy.ps1 -GameDir "F:\Codex\MBP_PROJ\Game"
# 复制 PunchLoader.dll + Injector.exe 到游戏 Managed 目录，
# 并部署随仓库维护的 ChineseLocalization 模组。

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

$modSource = "$root\mods\ChineseLocalization"
$modTarget = "$GameDir\MegabytePunch_Data\Mods\ChineseLocalization"
if (-not (Test-Path "$modSource\ChineseLocalization.dll")) {
    Write-Host "ChineseLocalization.dll not found, run build_all.ps1 first"
    exit 1
}
New-Item -ItemType Directory -Force -Path $modTarget | Out-Null
Copy-Item "$modSource\ChineseLocalization.dll" "$modTarget\ChineseLocalization.dll" -Force
Copy-Item "$modSource\plugin.json" "$modTarget\plugin.json" -Force
Copy-Item "$modSource\translations.tsv" "$modTarget\translations.tsv" -Force
Copy-Item "$modSource\font_atlas.png" "$modTarget\font_atlas.png" -Force
Copy-Item "$modSource\glyphs.tsv" "$modTarget\glyphs.tsv" -Force
Copy-Item "$modSource\font_atlas_small.png" "$modTarget\font_atlas_small.png" -Force
Copy-Item "$modSource\glyphs_small.tsv" "$modTarget\glyphs_small.tsv" -Force
Copy-Item "$modSource\dialogue_font_atlas.png" "$modTarget\dialogue_font_atlas.png" -Force
Copy-Item "$modSource\dialogue_glyphs.tsv" "$modTarget\dialogue_glyphs.tsv" -Force
Copy-Item "$modSource\dialogue_translations.tsv" "$modTarget\dialogue_translations.tsv" -Force

Copy-Item "$modSource\FONT_LICENSE.txt" "$modTarget\FONT_LICENSE.txt" -Force
Copy-Item "$modSource\FONT_NOTICE.md" "$modTarget\FONT_NOTICE.md" -Force
Write-Host "Deployed ChineseLocalization mod"

Write-Host "=== Done ==="


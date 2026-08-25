# 部署到测试游戏目录
# 用法: .\deploy.ps1 -GameDir "F:\Codex\MBP_PROJ\Game"
# 复制 PunchLoader.dll + Injector.exe 到游戏 Managed 目录，
# 并部署随仓库维护的 ChineseLocalization 与 ContentUnlocker 模组。

param(
    [string]$GameDir = "F:\Codex\MBP_PROJ\ModdedGame"
)

$ErrorActionPreference = "Stop"
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
$modBuild = "$root\build\mods\ChineseLocalization\ChineseLocalization.dll"
$modTarget = "$GameDir\Mods\ChineseLocalization"
if (-not (Test-Path $modBuild)) {
    Write-Host "ChineseLocalization.dll not found, run build_all.ps1 first"
    exit 1
}
New-Item -ItemType Directory -Force -Path $modTarget | Out-Null
Copy-Item $modBuild "$modTarget\ChineseLocalization.dll" -Force
Copy-Item "$modSource\plugin.json" "$modTarget\plugin.json" -Force

# Replace only the three managed resource directories.  Resolve and verify each
# destination before recursive removal so deployment cannot escape this mod.
$resolvedModTarget = [IO.Path]::GetFullPath($modTarget).TrimEnd('\') + '\'
foreach ($directory in @('fonts', 'data', 'licenses')) {
    $targetDirectory = [IO.Path]::GetFullPath((Join-Path $modTarget $directory))
    if (-not $targetDirectory.StartsWith($resolvedModTarget,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Resource target escaped mod directory: $targetDirectory"
    }
    if (Test-Path -LiteralPath $targetDirectory) {
        Remove-Item -LiteralPath $targetDirectory -Recurse -Force
    }
    Copy-Item -LiteralPath (Join-Path $modSource $directory) `
        -Destination $targetDirectory -Recurse -Force
}

# `assets/fonts` was used briefly before the package was flattened.  Remove
# that exact obsolete tree after validating it remains under this mod.
$legacyAssets = [IO.Path]::GetFullPath((Join-Path $modTarget 'assets'))
if (-not $legacyAssets.StartsWith($resolvedModTarget,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Legacy assets target escaped mod directory: $legacyAssets"
}
if (Test-Path -LiteralPath $legacyAssets) {
    Remove-Item -LiteralPath $legacyAssets -Recurse -Force
}

# Remove files deployed by pre-consolidation versions.  These exact names are
# no longer read by the mod; leaving them behind makes package inspection
# misleading and can mask an incomplete deployment.
$legacyFiles = @(
    'translations.tsv', 'dialogue_translations.tsv', 'part_translations.tsv',
    'ability_translations.tsv', 'dialogue_wrap_blocks.txt',
    'dialogue_wrap_overrides.tsv', 'font_atlas.png', 'glyphs.tsv',
    'font_atlas_small.png', 'glyphs_small.tsv', 'part_font_atlas.png',
    'part_glyphs.tsv', 'dialogue_font_atlas.png', 'dialogue_glyphs.tsv',
    'part_description_font_atlas.png', 'part_description_glyphs.tsv',
    'FONT_LICENSE.txt', 'FONT_NOTICE.md', 'ZLABS_LICENSE.txt'
)
foreach ($file in $legacyFiles) {
    $legacyPath = Join-Path $modTarget $file
    if (Test-Path -LiteralPath $legacyPath) {
        Remove-Item -LiteralPath $legacyPath -Force
    }
}
Write-Host "Deployed ChineseLocalization mod"

$unlockerSource = "$root\mods\ContentUnlocker"
$unlockerBuild = "$root\build\mods\ContentUnlocker\ContentUnlocker.dll"
$unlockerTarget = "$GameDir\Mods\ContentUnlocker"
if (-not (Test-Path $unlockerBuild)) {
    Write-Host "ContentUnlocker.dll not found, run build_all.ps1 first"
    exit 1
}
New-Item -ItemType Directory -Force -Path $unlockerTarget | Out-Null
Copy-Item -LiteralPath $unlockerBuild -Destination `
    (Join-Path $unlockerTarget 'ContentUnlocker.dll') -Force
Copy-Item -LiteralPath (Join-Path $unlockerSource 'plugin.json') -Destination `
    (Join-Path $unlockerTarget 'plugin.json') -Force
Write-Host "Deployed ContentUnlocker mod"

Write-Host "=== Done ==="


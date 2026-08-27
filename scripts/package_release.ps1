param(
    [string]$Version = '1.1.0',
    [string]$Csc = 'C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot 'build_setup.ps1') -Csc $Csc
if ($LASTEXITCODE -ne 0) {
    throw "Setup build failed: $LASTEXITCODE"
}
$name = 'PunchLoader-v' + $Version
$dist = Join-Path $root 'dist'
$stage = Join-Path (Join-Path $dist 'unpacked') $name
$zip = Join-Path $dist ($name + '.zip')
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$resolvedDist = [IO.Path]::GetFullPath($dist).TrimEnd('\') + '\'
foreach ($candidate in @($stage, $zip)) {
    $resolved = [IO.Path]::GetFullPath($candidate)
    if (-not $resolved.StartsWith($resolvedDist,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release path escaped dist: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'build\PunchLoader.Setup.exe') `
    -Destination (Join-Path $stage 'PunchLoader.Setup.exe') -Force
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName -replace '\\', '/' })
    if ($entries.Count -ne 1 -or
        $entries -notcontains 'PunchLoader.Setup.exe') {
        throw ('Unexpected PunchLoader package contents: ' + ($entries -join ', '))
    }
}
finally {
    $archive.Dispose()
}
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash
Write-Host "Package: $zip"
Write-Host "SHA256: $hash"

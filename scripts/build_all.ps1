param(
    [string]$Csc = 'C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot 'build_setup.ps1') -Csc $Csc
if ($LASTEXITCODE -ne 0) {
    throw "PunchLoader build failed: $LASTEXITCODE"
}

New-Item -ItemType Directory -Force -Path (Join-Path $root 'build\ExampleMod') | Out-Null
Push-Location $root
try {
    & $Csc '@ExampleMod\build.rsp'
    if ($LASTEXITCODE -ne 0) {
        throw "ExampleMod build failed: $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

Write-Host 'Built PunchLoader and ExampleMod.'

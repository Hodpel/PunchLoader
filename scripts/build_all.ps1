param(
    [string]$Csc = 'C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot 'build_setup.ps1') -Csc $Csc
if ($LASTEXITCODE -ne 0) {
    throw "PunchLoader build failed: $LASTEXITCODE"
}

foreach ($example in @('ExampleMod', 'ExampleMod2')) {
    $outputDirectory = Join-Path $root ('build\examples\' + $example)
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    Push-Location $root
    try {
        & $Csc ("@examples\" + $example + "\build.rsp")
        if ($LASTEXITCODE -ne 0) {
            throw "$example build failed: $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}

Write-Host 'Built PunchLoader and example mods.'


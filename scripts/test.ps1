param(
    [string]$Csc = 'C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'build\tests'

& (Join-Path $PSScriptRoot 'build_all.ps1') -Csc $Csc
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }

New-Item -ItemType Directory -Force -Path $output | Out-Null
Push-Location $root
try {
    & $Csc '@tests\dependency_resolver.rsp'
    if ($LASTEXITCODE -ne 0) { throw "Test build failed: $LASTEXITCODE" }

    Copy-Item -Force 'build\PunchLoader.dll' $output
    Copy-Item -Force 'deps\Unity\UnityEngine.dll' $output
    Copy-Item -Force 'deps\Game\Retail\Assembly-CSharp.dll' $output
    & (Join-Path $output 'DependencyResolverTests.exe')
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: $LASTEXITCODE" }
}
finally {
    Pop-Location
}

& (Join-Path $PSScriptRoot 'test_setup.ps1') -Csc $Csc
if ($LASTEXITCODE -ne 0) { throw "Setup tests failed: $LASTEXITCODE" }

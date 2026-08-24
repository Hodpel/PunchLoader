param(
    [string]$Csc = 'C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    & $Csc '@src\injector\build_injector.rsp'
    if ($LASTEXITCODE -ne 0) { throw "Injector build failed: $LASTEXITCODE" }

    & $Csc '@src\modloader\build_loader.rsp'
    if ($LASTEXITCODE -ne 0) { throw "PunchLoader build failed: $LASTEXITCODE" }

    & $Csc '@src\setup\build_setup.rsp'
    if ($LASTEXITCODE -ne 0) { throw "Setup build failed: $LASTEXITCODE" }

    Write-Host "Built: $root\build\PunchLoader.Setup.exe"
}
finally {
    Pop-Location
}

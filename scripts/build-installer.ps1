[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'publish-gateway.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "Gateway publication failed before installer creation."
}

$compilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) {
    throw "Inno Setup 6 compiler was not found. Install JRSoftware.InnoSetup with winget."
}

$definition = Join-Path $projectRoot 'installer\RoamADB-Gateway.iss'
& $compiler $definition
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installer = Join-Path $projectRoot 'artifacts\installer\RoamADB-Gateway-Setup-0.1.1-spike.exe'
$releaseOutput = Join-Path $projectRoot 'artifacts\release'
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Installer output was not created."
}
Copy-Item -LiteralPath $installer -Destination (Join-Path $releaseOutput (Split-Path -Leaf $installer)) -Force
Get-FileHash -LiteralPath $installer -Algorithm SHA256

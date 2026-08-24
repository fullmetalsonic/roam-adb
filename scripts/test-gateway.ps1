[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$testProject = Join-Path $projectRoot 'tests\gateway\RoamADB.Gateway.Tests\RoamADB.Gateway.Tests.csproj'
$cliProject = Join-Path $projectRoot 'src\gateway\RoamADB.Gateway.Cli\RoamADB.Gateway.Cli.csproj'

& $dotnet build $testProject -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Gateway build failed with exit code $LASTEXITCODE."
}

& $dotnet run --project $testProject -c Release --no-build
if ($LASTEXITCODE -ne 0) {
    throw "Gateway tests failed with exit code $LASTEXITCODE."
}

& $dotnet build $cliProject -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Gateway CLI build failed with exit code $LASTEXITCODE."
}

& $dotnet run --project $cliProject -c Release --no-build -- doctor
if ($LASTEXITCODE -ne 0) {
    throw "Gateway doctor failed with exit code $LASTEXITCODE."
}

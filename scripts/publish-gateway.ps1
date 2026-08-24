[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$project = Join-Path $projectRoot 'src\gateway\RoamADB.Gateway.Cli\RoamADB.Gateway.Cli.csproj'
$output = Join-Path $projectRoot 'artifacts\gateway\win-x64'

& $dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output
if ($LASTEXITCODE -ne 0) {
    throw "Gateway publish failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $output 'RoamADBGateway.exe'
& $executable doctor
if ($LASTEXITCODE -ne 0) {
    throw "Published Gateway doctor failed with exit code $LASTEXITCODE."
}

$item = Get-Item -LiteralPath $executable
$hash = Get-FileHash -LiteralPath $executable -Algorithm SHA256
Write-Output "Gateway: $($item.FullName)"
Write-Output "Size: $($item.Length) bytes"
Write-Output "SHA-256: $($hash.Hash)"

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$project = Join-Path $projectRoot 'src\gateway\RoamADB.Gateway.Desktop\RoamADB.Gateway.Desktop.csproj'
$output = Join-Path $projectRoot 'artifacts\gateway\win-x64'
$releaseOutput = Join-Path $projectRoot 'artifacts\release'

foreach ($target in @($output, $releaseOutput)) {
    $resolvedTarget = [System.IO.Path]::GetFullPath($target)
    $resolvedRoot = [System.IO.Path]::GetFullPath($projectRoot) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedTarget.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a publish path outside the project: $resolvedTarget"
    }
    if (Test-Path -LiteralPath $resolvedTarget) {
        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolvedTarget -Force | Out-Null
}

& $dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output
if ($LASTEXITCODE -ne 0) {
    throw "Gateway publish failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $output 'RoamADBGateway.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Published Gateway executable was not created."
}

$portable = Join-Path $releaseOutput 'RoamADB-Gateway-Portable-0.1.1-spike.exe'
Copy-Item -LiteralPath $executable -Destination $portable -Force
$item = Get-Item -LiteralPath $portable
$hash = Get-FileHash -LiteralPath $portable -Algorithm SHA256
Write-Output "Gateway: $($item.FullName)"
Write-Output "Size: $($item.Length) bytes"
Write-Output "SHA-256: $($hash.Hash)"

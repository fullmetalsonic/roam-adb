[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$releaseOutput = Join-Path $projectRoot 'artifacts\release'
$assets = @(
    'RoamADB-Gateway-Setup-0.1.1-spike.exe',
    'RoamADB-Gateway-Portable-0.1.1-spike.exe',
    'RoamADB-0.1.1-spike-debug.apk'
)

$lines = foreach ($name in $assets) {
    $path = Join-Path $releaseOutput $name
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Release asset is missing: $path"
    }
    $hash = Get-FileHash -LiteralPath $path -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $name
}

$checksumPath = Join-Path $releaseOutput 'SHA256SUMS.txt'
[System.IO.File]::WriteAllLines($checksumPath, $lines, [System.Text.UTF8Encoding]::new($false))
Get-ChildItem -LiteralPath $releaseOutput | Sort-Object Name | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    [PSCustomObject]@{
        Name = $_.Name
        Size = $_.Length
        SHA256 = $hash.Hash
    }
}

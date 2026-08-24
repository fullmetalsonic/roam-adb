[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$androidRoot = Join-Path $projectRoot 'android'
$configuredJavaHome = $env:JAVA_HOME
$configuredReleaseFile = if ([string]::IsNullOrWhiteSpace($configuredJavaHome)) {
    $null
}
else {
    Join-Path $configuredJavaHome 'release'
}
$configuredJavaIsSupported = $null -ne $configuredReleaseFile -and
    (Test-Path -LiteralPath $configuredReleaseFile) -and
    (Select-String -LiteralPath $configuredReleaseFile -Pattern '^JAVA_VERSION="(?:17|21)(?:\.|\")' -Quiet)

if (-not $configuredJavaIsSupported) {
    $jdkCandidates = Get-ChildItem 'C:\Program Files\Microsoft' -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -Like 'jdk-21*' -or $_.Name -Like 'jdk-17*' } |
        Sort-Object @{ Expression = { if ($_.Name -Like 'jdk-21*') { 0 } else { 1 } } }, Name -Descending
    $jdk = $jdkCandidates | Select-Object -First 1

    if ($null -eq $jdk) {
        throw 'JDK 17 or 21 was not found. Set JAVA_HOME or install Microsoft.OpenJDK.17 with winget.'
    }

    $env:JAVA_HOME = $jdk.FullName
}
Push-Location $androidRoot
try {
    & .\gradlew.bat test assembleDebug lintDebug
    if ($LASTEXITCODE -ne 0) {
        throw "Android verification failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$apk = Join-Path $androidRoot 'app\build\outputs\apk\debug\app-debug.apk'
$releaseOutput = Join-Path $projectRoot 'artifacts\release'
New-Item -ItemType Directory -Path $releaseOutput -Force | Out-Null
$releaseApk = Join-Path $releaseOutput 'RoamADB-0.1.1-spike-debug.apk'
Copy-Item -LiteralPath $apk -Destination $releaseApk -Force
$hash = Get-FileHash -LiteralPath $apk -Algorithm SHA256
Write-Output "APK: $apk"
Write-Output "Release APK: $releaseApk"
Write-Output "SHA-256: $($hash.Hash)"

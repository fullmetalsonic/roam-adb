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
$configuredJavaIs21 = $null -ne $configuredReleaseFile -and
    (Test-Path -LiteralPath $configuredReleaseFile) -and
    (Select-String -LiteralPath $configuredReleaseFile -Pattern '^JAVA_VERSION="21(?:\.|\")' -Quiet)

if (-not $configuredJavaIs21) {
    $jdkCandidates = Get-ChildItem 'C:\Program Files\Microsoft' -Directory -ErrorAction SilentlyContinue |
        Where-Object Name -Like 'jdk-21*' |
        Sort-Object Name -Descending
    $jdk = $jdkCandidates | Select-Object -First 1

    if ($null -eq $jdk) {
        throw 'JDK 21 was not found. Set JAVA_HOME or install Microsoft.OpenJDK.21 with winget.'
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
$hash = Get-FileHash -LiteralPath $apk -Algorithm SHA256
Write-Output "APK: $apk"
Write-Output "SHA-256: $($hash.Hash)"

param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z]:\\?$')]
    [string]$BootDrive,
    [string]$OpenSslPath = "C:\Program Files\Git\usr\bin\openssl.exe"
)

$ErrorActionPreference = "Stop"

function ConvertFrom-SecureValue {
    param([Security.SecureString]$Value)
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

$first = $null
$second = $null
$plainFirst = $null
$plainSecond = $null
$hash = $null
$exitCode = 0

try {
    $driveLetter = $BootDrive.Substring(0, 1).ToUpperInvariant()
    $BootDrive = "${driveLetter}:\"
    $volume = Get-Volume -DriveLetter $driveLetter -ErrorAction Stop

    if ($volume.FileSystem -ne "FAT32" -or $volume.FileSystemLabel -ne "bootfs") {
        throw "선택한 드라이브가 Raspberry Pi bootfs가 아닙니다: $BootDrive"
    }
    if (-not (Test-Path -LiteralPath $OpenSslPath)) {
        throw "OpenSSL을 찾지 못했습니다: $OpenSslPath"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $BootDrive "cmdline.txt")) -or
        -not (Test-Path -LiteralPath (Join-Path $BootDrive "config.txt"))) {
        throw "Raspberry Pi bootfs를 찾지 못했습니다: $BootDrive"
    }

    $target = Join-Path $BootDrive "roamadb-password.hash"
    if (Test-Path -LiteralPath $target) {
        throw "기존 복구 해시를 덮어쓰지 않습니다: $target"
    }

    while ($true) {
        $first = Read-Host "RoamADB Pi의 새 비밀번호" -AsSecureString
        $second = Read-Host "같은 비밀번호를 한 번 더 입력" -AsSecureString
        $plainFirst = ConvertFrom-SecureValue $first
        $plainSecond = ConvertFrom-SecureValue $second

        if ($plainFirst.Length -lt 10) {
            Write-Host "비밀번호는 10자 이상으로 입력해 주세요." -ForegroundColor Yellow
        }
        elseif ($plainFirst -cne $plainSecond) {
            Write-Host "두 비밀번호가 다릅니다. 다시 입력해 주세요." -ForegroundColor Yellow
        }
        else {
            break
        }

        $first.Dispose()
        $second.Dispose()
        $first = $null
        $second = $null
        $plainFirst = $null
        $plainSecond = $null
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $OpenSslPath
    # Fixed arguments keep this compatible with Windows PowerShell 5.1, whose
    # .NET ProcessStartInfo does not expose ArgumentList.
    $startInfo.Arguments = "passwd -6 -stdin"
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $process.StandardInput.WriteLine($plainFirst)
    $process.StandardInput.Close()
    $hash = $process.StandardOutput.ReadToEnd().Trim()
    $errorText = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    if ($process.ExitCode -ne 0 -or $hash -notmatch '^\$6\$[^$]+\$[./0-9A-Za-z]+$') {
        throw "비밀번호 해시 생성에 실패했습니다. $errorText"
    }

    [IO.File]::WriteAllText($target, $hash + "`n", [Text.UTF8Encoding]::new($false))
    Write-Host "`n완료: 평문 비밀번호는 저장하지 않았고 복구용 해시만 bootfs에 기록했습니다." -ForegroundColor Green
}
catch {
    $exitCode = 1
    Write-Host "`n실패: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    if ($first) { $first.Dispose() }
    if ($second) { $second.Dispose() }
    $plainFirst = $null
    $plainSecond = $null
    $hash = $null
    [GC]::Collect()
}

[void](Read-Host "창을 닫으려면 Enter")
exit $exitCode

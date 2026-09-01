param(
    [string]$Machine = "roamadb-bridge",
    [string]$User = "roamadb",
    [string]$IdentityFile = "$env:USERPROFILE\.ssh\roamadb_bridge_ed25519"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command tailscale -ErrorAction SilentlyContinue)) {
    throw "Tailscale CLI를 찾지 못했습니다."
}
if (-not (Get-Command ssh -ErrorAction SilentlyContinue)) {
    throw "OpenSSH 클라이언트를 찾지 못했습니다."
}
if (-not (Test-Path -LiteralPath $IdentityFile)) {
    throw "SSH 개인키를 찾지 못했습니다: $IdentityFile"
}

$status = tailscale status --json | ConvertFrom-Json
$peer = @($status.Peer.PSObject.Properties.Value) |
    Where-Object { $_.HostName -eq $Machine } |
    Select-Object -First 1

if (-not $peer -or -not $peer.TailscaleIPs) {
    throw "Tailscale에서 '$Machine'을 찾지 못했습니다."
}

$address = $peer.TailscaleIPs[0]
Write-Host "'$Machine'에 안전 종료를 요청합니다..."
& ssh -i $IdentityFile -o BatchMode=yes -o ConnectTimeout=10 "$User@$address" "sudo /usr/local/sbin/roamadb-poweroff"
if ($LASTEXITCODE -ne 0) {
    throw "안전 종료 요청이 실패했습니다."
}

Write-Host "종료 요청을 보냈습니다. 초록 LED가 완전히 꺼진 뒤 전원을 분리하세요."

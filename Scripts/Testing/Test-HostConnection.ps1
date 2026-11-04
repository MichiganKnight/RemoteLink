param(
    [string]$Address
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "=== RemoteLink Host Connection Test ===" -ForegroundColor Cyan

if (-not $Address) {
    $Address = Read-Host "Remote LAN IP Address"
}

Write-Host ""
Write-Host "Testing TCP 5019..." -ForegroundColor Yellow

$result = Test-NetConnection `
    -ComputerName $Address `
    -Port 5019 `
    -WarningAction SilentlyContinue

Write-Host "TCP Connected: $($result.TcpTestSucceeded)"

if (-not $result.TcpTestSucceeded) {
    Write-Host "Tcp connection failed."
}
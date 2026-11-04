$ErrorActionPreference = "Stop"

$url = "http://127.0.0.1:5017/api/local/pairing/incoming"

Write-Host ""
Write-Host "=== Incoming Pairing Requests ===" -ForegroundColor Cyan

$requests = @(
    Invoke-RestMethod -Uri $url -TimeoutSec 5
)

if ($requests.Count -eq 0) {
    Write-Host "No incoming pairing requests." -ForegroundColor Yellow
    return
}

$requests |
    Select-Object `
        requestId,
        deviceName,
        deviceId,
        status,
        createdUtc,
        expiresUtc |
    Format-List

Write-Host "Total requests: $($requests.Count)" -ForegroundColor Green
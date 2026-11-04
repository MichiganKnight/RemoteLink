param(
    [string]$DeviceId,
    [string]$Address,
    [string]$Thumbprint
)

$ErrorActionPreference = "Stop"

$localHostUrl = "http://127.0.0.1:5017"

Write-Host ""
Write-Host "=== Send RemoteLink Pairing Proposal ===" -ForegroundColor Cyan

if (-not $DeviceId) {
    $DeviceId = Read-Host "Remote Device ID"
}

if (-not $Address) {
    $Address = Read-Host "Remote LAN IP Address"
}

if (-not $Thumbprint) {
    $Thumbprint = Read-Host "Remote TLS Certificate Thumbprint"
}

$body = @{
    deviceId = $DeviceId
    address = $Address
    expectedTlsThumbprint = $Thumbprint
} | ConvertTo-Json

Write-Host ""
Write-Host "Sending pairing proposal to $Address..." -ForegroundColor Yellow

$response = Invoke-RestMethod `
    -Uri "$localHostUrl/api/local/pairing/start" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body `
    -TimeoutSec 15

Write-Host ""
Write-Host "Response:" -ForegroundColor Green

$response | Format-List

if ($response.acceptedForReview) {
    Write-Host "Proposal accepted for review by remote Host." -ForegroundColor Green
    Write-Host "Request ID: $($response.requestId)"
}
param(
    [string]$RequestId,
    [string]$DeviceId,
    [string]$Address,
    [string]$Thumbprint
)

$ErrorActionPreference = "Stop"

$baseUrl = "http://127.0.0.1:5017"

Write-Host ""
Write-Host "=== Verify RemoteLink Pairing Handshake ===" -ForegroundColor Cyan

if (-not $RequestId) {
    $RequestId = Read-Host "Pairing Request ID"
}

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
    requestId = $RequestId
    deviceId = $DeviceId
    address = $Address
    expectedTlsThumbprint = $Thumbprint
} | ConvertTo-Json

Write-Host ""
Write-Host "Verifying signed handshake..." -ForegroundColor Yellow

$result = Invoke-RestMethod `
    -Uri "$baseUrl/api/local/pairing/verify" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body `
    -TimeoutSec 15

Write-Host ""
Write-Host "=== Handshake Verification Result ===" -ForegroundColor Green

Write-Host "Request ID: $($result.requestId)"
Write-Host "Remote Device: $($result.remoteDeviceName)"
Write-Host "Remote Device ID: $($result.remoteDeviceId)"
Write-Host "Signature Verified: $($result.signatureVerified)"

Write-Host ""
Write-Host "Remote Identity Fingerprint:"
Write-Host $result.remoteIdentityFingerprint -ForegroundColor Cyan

Write-Host ""
Write-Host "Verification Code:"
Write-Host $result.verificationCode -ForegroundColor Yellow

Write-Host ""
Write-Host "This code does not mean the device is trusted."
Write-Host "Both users must compare codes before pairing can be approved."
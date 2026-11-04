$ErrorActionPreference = "Stop"

$baseUrl = "http://127.0.0.1:5017"

Write-Host ""
Write-Host "=== RemoteLink Local Device ===" -ForegroundColor Cyan

$device = Invoke-RestMethod `
    -Uri "$baseUrl/api/devices/local"

$identity = Invoke-RestMethod `
    -Uri "$baseUrl/api/identity/public"

$tls = Invoke-RestMethod `
    -Uri "$baseUrl/api/local/security/tls-thumbprint"

Write-Host ""
Write-Host "Device Name: $($device.deviceName)"
Write-Host "Device ID:   $($identity.deviceId)"
Write-Host ""
Write-Host "ECDSA Identity Fingerprint:"
Write-Host $identity.fingerprint -ForegroundColor Green
Write-Host ""
Write-Host "TLS Certificate Thumbprint:"
Write-Host $tls.thumbprint -ForegroundColor Green

Write-Host ""
Write-Host "=== Discovered Devices ===" -ForegroundColor Cyan

$devices = @(
    Invoke-RestMethod -Uri "$baseUrl/api/devices"
)

if ($devices.Count -eq 0) {
    Write-Host "No discovered devices."
}
else {
    $devices | Format-Table -AutoSize
}
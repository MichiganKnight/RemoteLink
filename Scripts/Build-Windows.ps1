$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

$hostProject = Join-Path $root "RemoteLink.Host/RemoteLink.Host.csproj"
$hostOutput = Join-Path $root "Artifacts/Host"
$desktopPath = Join-Path $root "RemoteLink.Desktop"

Write-Host "=================================="
Write-Host " RemoteLink Windows Build"
Write-Host "=================================="

Write-Host ""
Write-Host "[1/4] Publishing .NET Host..."

dotnet publish $hostProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=false -o $hostOutput

if ($LASTEXITCODE -ne 0) {
    throw "Host publish failed."
}

Write-Host ""
Write-Host "[2/4] Installing locked npm dependencies..."

Push-Location $desktopPath

try {
    npm ci

    if ($LASTEXITCODE -ne 0) {
        throw "npm ci failed."
    }

    Write-Host ""
    Write-Host "[3/4] Building React frontend..."

    npm run build

    if ($LASTEXITCODE -ne 0) {
        throw "Frontend build failed."
    }

    Write-Host ""
    Write-Host "[4/4] Packaging Windows installer..."

    npx electron-builder --win nsis

    if ($LASTEXITCODE -ne 0) {
        throw "Installer packaging failed."
    }
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "=================================="
Write-Host " RemoteLink Build Complete"
Write-Host "=================================="

Write-Host "Installer directory:"
Write-Host (Join-Path $desktopPath "release")
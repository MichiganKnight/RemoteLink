$ErrorActionPreference = "Stop"

$testingDirectory = Join-Path $PSScriptRoot "Testing"

function Invoke-TestingScript {
    param([Parameter(Mandatory)] [string] $ScriptName)

    $scriptPath = Join-Path $testingDirectory $ScriptName

    if (-not (Test-Path $scriptPath)) {
        Write-Host "Script not found: $scriptPath" -ForegroundColor Red

        return
    }

    & $scriptPath
}

while ($true) {
    Clear-Host

    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "         RemoteLink Developer Tools"
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  1. Send Pairing Proposal"
    Write-Host "  2. View Incoming Pairing Requests"
    Write-Host "  3. View Local Device Information"
    Write-Host "  4. Test Remote Host Connection"
    Write-Host ""
    Write-Host "  5. Build Windows Installer"
    Write-Host ""
    Write-Host "  0. Exit"
    Write-Host ""

    $choice = Read-Host "Select an option"

    try {
        switch ($choice) {
            "1" { Invoke-TestingScript "Send-PairingProposal.ps1" }
            "2" { Invoke-TestingScript "Get-IncomingPairing.ps1" }
            "3" { Invoke-TestingScript "Get-DeviceInfo.ps1" }
            "4" { Invoke-TestingScript "Test-HostConnection.ps1"}

            "5" {
                $buildScript = Join-Path $PSScriptRoot "Build-Windows.ps1"

                if (-not (Test-Path $buildScript)) {
                    throw "Build script not found: $buildScript"
                }

                & $buildScript

                if (-not $?) {
                    throw "Windows build script failed."
                }
            }

            "0" { return }

            default {
                Write-Host "Invalid option." -ForegroundColor Yellow
            }
        }
    } catch {
        Write-Host ""
        Write-Host "Operation failed:" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
    }

    Write-Host ""
    Read-Host "Press Enter to return to the menu" | Out-Null
}
# Signs files with the code signing certificate found by Signing.psm1. Exit code 0 = all signed.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\signing\Sign-File.ps1 -Path <file> [-Path <file> ...] [-Thumbprint <hex>]
# Inno Setup calls it as its sign tool (see build.ps1) to sign the setup exe and the uninstaller.

param(
    [Parameter(Mandatory = $true)][string[]]$Path,
    [string]$Thumbprint
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
Import-Module (Join-Path $PSScriptRoot 'Signing.psm1') -Force -DisableNameChecking

try {
    $readiness = Get-SigningReadiness -Thumbprint $Thumbprint
    if ($readiness.Problems.Count -gt 0) { throw ($readiness.Problems -join ' ') }
    foreach ($file in $Path) {
        $signature = Invoke-CodeSigning -Path $file -Certificate $readiness.Certificate -SignTool $readiness.SignTool
        Write-Host "Signed: $file ($($signature.Status))"
    }
    exit 0
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

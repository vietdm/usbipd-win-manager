# One-time setup on the build machine (run as administrator):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\signing\New-CodeSigningCert.ps1 [-New] [-PfxPath <file.pfx>]
# Creates the self-signed code signing certificate in Cert:\CurrentUser\My (reuses a valid one unless -New),
# trusts it on this machine (LocalMachine\Root) and exports the public part to dist\certificate\ for other machines.
# -PfxPath also writes a password-protected backup including the private key. Never commit or share the .pfx.
#Requires -RunAsAdministrator

param(
    [switch]$New,
    [string]$PfxPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
Import-Module (Join-Path $PSScriptRoot 'Signing.psm1') -Force -DisableNameChecking

$defaults = Get-CodeSigningDefaults
$certificate = $null
if (-not $New) { $certificate = Find-CodeSigningCertificate }

if ($null -ne $certificate) {
    Write-Host "Reusing the existing certificate $($certificate.Thumbprint) (valid until $($certificate.NotAfter.ToString('yyyy-MM-dd')))." -ForegroundColor Cyan
} else {
    # End-entity certificate (Basic Constraints ca=0) limited to code signing, so trusting it as a root cannot be
    # used to issue other certificates.
    $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject $defaults.Subject -FriendlyName $defaults.FriendlyName `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyUsage DigitalSignature -KeyExportPolicy ExportableEncrypted `
        -NotAfter (Get-Date).AddYears(10) -CertStoreLocation Cert:\CurrentUser\My -TextExtension @('2.5.29.19={critical}{text}ca=0')
    Write-Host "Created certificate $($certificate.Thumbprint) (valid until $($certificate.NotAfter.ToString('yyyy-MM-dd')))." -ForegroundColor Green
}

Add-TrustedRootCertificate -Certificate $certificate
if (-not (Test-CertificateTrusted $certificate)) { throw 'The certificate was added to LocalMachine\Root but is still not trusted.' }
Write-Host 'Trusted on this machine (Local Computer > Trusted Root Certification Authorities).' -ForegroundColor Green

$exportDir = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'dist\certificate'
$export = Export-CodeSigningCertificate -Certificate $certificate -Directory $exportDir
Write-Host "Public certificate for other machines: $($export.Certificate)"
Write-Host "Installer for other machines         : $($export.Installer)"

if (-not [string]::IsNullOrWhiteSpace($PfxPath)) {
    $password = Read-Host -Prompt 'Password for the .pfx backup' -AsSecureString
    $confirm = Read-Host -Prompt 'Repeat the password' -AsSecureString
    $plain1 = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($password))
    $plain2 = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($confirm))
    if ($plain1 -cne $plain2 -or $plain1.Length -eq 0) { throw 'The passwords are empty or do not match; no .pfx was written.' }
    Export-PfxCertificate -Cert $certificate -FilePath $PfxPath -Password $password | Out-Null
    Write-Host "Private key backup (keep it secret): $PfxPath" -ForegroundColor Yellow
}

Write-Host ''
Write-Host 'Done. build.ps1 now signs every build with this certificate.' -ForegroundColor Green

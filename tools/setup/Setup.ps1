# Prepares this machine and builds USBIPD Manager: installs missing build tools with winget (asks first; Enter = yes),
# creates and trusts the code signing certificate, then runs build.ps1 for the portable exe and the installer.
# Started by setup.bat, which requests administrator rights. Usage: setup.bat [--yes] [build.ps1 options]
#Requires -RunAsAdministrator

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $Root 'tools\build\BuildTools.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $Root 'tools\signing\Signing.psm1') -Force -DisableNameChecking

$UserArgs = @($args)
$AssumeYes = @($UserArgs | Where-Object { $_ -in @('-y', '--yes') }).Count -gt 0
$script:StepNumber = 0

if ($UserArgs -contains '-h' -or $UserArgs -contains '--help') {
    Write-Host @'
USBIPD Manager setup

Usage: setup.bat [--yes] [build options]

Checks .NET SDK 10, Inno Setup 6, the Windows SDK signtool and the code signing certificate.
Anything missing is installed with winget after a confirmation (Enter = yes). Then it builds
the portable exe and the installer with build.ps1.

  -y, --yes       Answer yes to every confirmation.
  build options   Passed to build.ps1 (see build.bat --help), e.g. setup.bat --no-bump
'@
    exit 0
}

function Write-Step([string]$Title) {
    $script:StepNumber++
    Write-Host ''
    Write-Host ("==> [{0}] {1}" -f $script:StepNumber, $Title) -ForegroundColor Cyan
}

function Write-Ok([string]$Text) { Write-Host "  [OK]   $Text" -ForegroundColor Green }

function Write-Warn([string]$Text) { Write-Host "  [WARN] $Text" -ForegroundColor Yellow }

function Write-Note([string]$Text) { Write-Host "         $Text" -ForegroundColor Gray }

function Stop-Setup([string]$Message) {
    Write-Host ''
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}

function Confirm-Action([string]$Question) {
    if ($AssumeYes) {
        Write-Host "  $Question [Y/n] y (--yes)"
        return $true
    }
    while ($true) {
        $answer = ConvertFrom-YesNoAnswer (Read-Host "  $Question [Y/n]")
        if ($null -ne $answer) { return $answer }
        Write-Host '  Please answer y or n (Enter = yes).' -ForegroundColor Yellow
    }
}

# winget installs update the registry PATH, not this process; reload it so the next checks see the new tools.
function Update-SessionPath {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = (@($machine, $user) | Where-Object { $_ }) -join ';'
}

# The caller re-detects the tool afterwards; winget's exit code alone is not reliable (e.g. "already installed").
function Install-WingetPackage([string]$Id, [string]$Name) {
    $winget = Get-Command winget.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $winget) {
        Stop-Setup (("winget is not available, so {0} cannot be installed automatically. Install 'App Installer' from the Microsoft Store " +
            "(https://apps.microsoft.com/detail/9NBLGGH4NNS1) or install {0} by hand, then run setup.bat again.") -f $Name)
    }
    $arguments = @('install', '--id', $Id, '-e', '--silent', '--accept-package-agreements', '--accept-source-agreements')
    Write-Host ("  > winget {0}" -f ($arguments -join ' ')) -ForegroundColor DarkGray
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $winget.Source @arguments | Out-Host
    } finally {
        $ErrorActionPreference = $previous
    }
    Update-SessionPath
}

Write-Host 'USBIPD Manager setup' -ForegroundColor Cyan
Write-Host 'Checks the build tools, installs what is missing (asks first; Enter = yes), prepares code signing and builds.'

# --- .NET SDK 10 (required) ----------------------------------------------------------------------

Write-Step '.NET SDK 10 (required)'
$dotnet = Find-DotNet
if ($null -ne $dotnet -and $dotnet.Sdk) {
    Write-Ok (".NET SDK {0} ({1})" -f $dotnet.Sdk, $dotnet.Path)
} else {
    Write-Warn '.NET SDK 10 is not installed.'
    if (-not (Confirm-Action 'Install .NET SDK 10 with winget (Microsoft.DotNet.SDK.10)?')) { Stop-Setup '.NET SDK 10 is required to build. Nothing was built.' }
    Install-WingetPackage -Id 'Microsoft.DotNet.SDK.10' -Name '.NET SDK 10'
    $dotnet = Find-DotNet
    if ($null -eq $dotnet -or -not $dotnet.Sdk) { Stop-Setup '.NET SDK 10 was still not found after the installation; see the winget output above.' }
    Write-Ok (".NET SDK {0} installed" -f $dotnet.Sdk)
}

# --- Inno Setup 6 (installer only) ---------------------------------------------------------------

Write-Step 'Inno Setup 6 (for the installer)'
$installer = $true
$iscc = Find-Iscc
if ($null -ne $iscc) {
    Write-Ok $iscc
} else {
    Write-Warn 'Inno Setup 6 is not installed. Without it only the portable exe is built.'
    if (Confirm-Action 'Install Inno Setup 6 with winget (JRSoftware.InnoSetup)?') {
        Install-WingetPackage -Id 'JRSoftware.InnoSetup' -Name 'Inno Setup 6'
        $iscc = Find-Iscc
        if ($null -ne $iscc) {
            Write-Ok "Inno Setup installed: $iscc"
        } else {
            Write-Warn 'Inno Setup was still not found after the installation; building the portable exe only.'
            $installer = $false
        }
    } else {
        Write-Note 'Skipped: only the portable exe is built.'
        $installer = $false
    }
}

# --- Code signing -----------------------------------------------------------------------------------

$sign = -not ($UserArgs -contains '--no-sign')
if (-not $sign) {
    Write-Step 'Code signing'
    Write-Note 'Skipped (--no-sign).'
} else {
    Write-Step 'Windows SDK signtool (optional)'
    $signTool = Find-SignTool
    if ($null -ne $signTool) {
        Write-Ok $signTool
    } else {
        Write-Warn 'signtool.exe was not found. Signing still works without it (Set-AuthenticodeSignature).'
        if (Confirm-Action 'Install the Windows SDK with winget (Microsoft.WindowsSDK.10.0.26100, a large download)?') {
            Install-WingetPackage -Id 'Microsoft.WindowsSDK.10.0.26100' -Name 'the Windows SDK'
            $signTool = Find-SignTool
            if ($null -ne $signTool) { Write-Ok "signtool installed: $signTool" } else { Write-Warn 'signtool was still not found; signing uses Set-AuthenticodeSignature.' }
        } else {
            Write-Note 'Skipped: signing uses Set-AuthenticodeSignature.'
        }
    }

    Write-Step 'Code signing certificate'
    $certificate = Find-CodeSigningCertificate
    if ($null -eq $certificate) {
        Write-Warn 'No valid code signing certificate was found in Cert:\CurrentUser\My.'
        $subject = (Get-CodeSigningDefaults).Subject
        if (Confirm-Action "Create a self-signed code signing certificate ($subject) and trust it on this machine?") {
            try {
                & (Join-Path $Root 'tools\signing\New-CodeSigningCert.ps1')
            } catch {
                Stop-Setup "Creating the certificate failed: $($_.Exception.Message)"
            }
            $certificate = Find-CodeSigningCertificate
            if ($null -eq $certificate) { Stop-Setup 'The certificate was still not found after creating it.' }
        } else {
            Write-Note 'Skipped: the build is not signed (--no-sign).'
            $sign = $false
        }
    } else {
        Write-Ok ("{0} ({1}, valid until {2})" -f $certificate.Subject, $certificate.Thumbprint, $certificate.NotAfter.ToString('yyyy-MM-dd'))
        if (Test-CertificateTrusted $certificate) {
            Write-Ok 'Trusted on this machine.'
        } else {
            Write-Warn 'The certificate is not trusted on this machine, so Windows shows "Unknown publisher".'
            if (Confirm-Action 'Trust it on this machine (Local Computer > Trusted Root Certification Authorities)?') {
                Add-TrustedRootCertificate -Certificate $certificate
                if (Test-CertificateTrusted $certificate) { Write-Ok 'Trusted on this machine.' } else { Write-Warn 'Added to Trusted Root, but it is still not trusted.' }
            } else {
                Write-Note 'Skipped: builds are signed but show "Unknown publisher" on this machine.'
            }
        }
    }
}

# --- Close the running app ---------------------------------------------------------------------------
# A running portable exe in dist\ is locked, so the build could not remove it as an older build.
# --exit asks it to return the devices to Windows and quit (the saved mode is restored on its next start).

Write-Step 'Close a running USBIPD Manager'
$running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'UsbipdManager*' })
if ($UserArgs -contains '--dry-run') {
    Write-Note 'Skipped (--dry-run).'
} elseif ($running.Count -eq 0) {
    Write-Ok 'Not running.'
} else {
    foreach ($process in $running) { Write-Note ("Running: {0} (PID {1})" -f $process.Path, $process.Id) }
    $graceful = $false
    $exe = @($running | Where-Object { $_.Path } | Select-Object -ExpandProperty Path -First 1)
    if ($exe.Count -gt 0) {
        Write-Host ("  > {0} --exit" -f $exe[0]) -ForegroundColor DarkGray
        $exit = Start-Process -FilePath $exe[0] -ArgumentList '--exit' -WindowStyle Hidden -Wait -PassThru
        $graceful = $exit.ExitCode -eq 0
        if (-not $graceful) { Write-Warn "--exit returned $($exit.ExitCode)." }
    }
    foreach ($process in $running) {
        if (-not $process.WaitForExit(5000)) {
            Write-Warn ("PID {0} did not exit; stopping it. Devices may still be attached to WSL2." -f $process.Id)
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            $graceful = $false
        }
    }
    Write-Ok $(if ($graceful) { 'Closed; devices were returned to Windows.' } else { 'Closed.' })
}

# --- Build -----------------------------------------------------------------------------------------

Write-Step $(if ($installer) { 'Build the portable exe and the installer' } else { 'Build the portable exe' })
$buildArgs = Get-SetupBuildArguments -Arguments $UserArgs -Installer $installer -Sign $sign
Write-Host ("  > build.ps1 {0}" -f ($buildArgs -join ' ')) -ForegroundColor DarkGray
$global:LASTEXITCODE = 0
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root 'build.ps1') @buildArgs
if ($LASTEXITCODE -ne 0) { Stop-Setup "The build failed (exit code $LASTEXITCODE)." }

Write-Host ''
Write-Host 'Setup finished.' -ForegroundColor Green
exit 0

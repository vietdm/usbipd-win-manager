# Code signing for build.ps1, Sign-File.ps1 and New-CodeSigningCert.ps1.
# The certificate is a self-signed code signing certificate in Cert:\CurrentUser\My, trusted by importing its
# public part into LocalMachine\Root. Keep this file ASCII (Windows PowerShell 5.1 reads BOM-less files as ANSI).

Set-StrictMode -Version 2.0

$script:CertificateSubject = 'CN=Minh Viet'
$script:CertificateFriendlyName = 'USBIPD Manager Code Signing'
$script:CertificateFileName = 'USBIPD-Manager-CodeSigning.cer'
$script:CodeSigningEku = '1.3.6.1.5.5.7.3.3'
$script:TimestampUrl = 'http://timestamp.digicert.com'
$script:ThumbprintVariable = 'USBIPD_SIGN_THUMBPRINT'
$script:SignatureDescription = 'USBIPD Manager'

function Get-CodeSigningDefaults {
    return [pscustomobject]@{
        Subject            = $script:CertificateSubject
        FriendlyName       = $script:CertificateFriendlyName
        CertificateFile    = $script:CertificateFileName
        TimestampUrl       = $script:TimestampUrl
        ThumbprintVariable = $script:ThumbprintVariable
    }
}

function Test-CodeSigningCertificate {
    param([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

    if ($null -eq $Certificate -or -not $Certificate.HasPrivateKey) { return $false }
    $now = Get-Date
    if ($Certificate.NotBefore -gt $now -or $Certificate.NotAfter -le $now) { return $false }
    foreach ($extension in $Certificate.Extensions) {
        if ($extension -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]) {
            foreach ($usage in $extension.EnhancedKeyUsages) {
                if ($usage.Value -eq $script:CodeSigningEku) { return $true }
            }
        }
    }
    return $false
}

function Find-CodeSigningCertificate {
    # $env:USBIPD_SIGN_THUMBPRINT selects another certificate (e.g. a purchased one) from Cert:\CurrentUser\My.
    param([string]$Thumbprint)

    if ([string]::IsNullOrWhiteSpace($Thumbprint)) { $Thumbprint = [Environment]::GetEnvironmentVariable($script:ThumbprintVariable) }
    $all = @(Get-ChildItem -Path Cert:\CurrentUser\My)
    if (-not [string]::IsNullOrWhiteSpace($Thumbprint)) {
        $wanted = ($Thumbprint -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
        $candidates = @($all | Where-Object { $_.Thumbprint -eq $wanted })
    } else {
        $candidates = @($all | Where-Object { $_.FriendlyName -eq $script:CertificateFriendlyName })
    }
    $valid = @($candidates | Where-Object { Test-CodeSigningCertificate $_ } | Sort-Object -Property NotAfter -Descending)
    if ($valid.Count -gt 0) { return $valid[0] }
    return $null
}

function Test-CertificateTrusted {
    # True when the chain ends in a trusted root, i.e. UAC and Explorer show the publisher as verified.
    param([Parameter(Mandatory = $true)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

    $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
    try {
        $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
        [void]$chain.ChainPolicy.ApplicationPolicy.Add((New-Object System.Security.Cryptography.Oid -ArgumentList $script:CodeSigningEku))
        return $chain.Build($Certificate)
    } finally {
        $chain.Reset()
    }
}

function Find-SignTool {
    # signtool.exe from the newest installed Windows SDK; $null when no SDK is installed.
    $command = Get-Command signtool.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) { return $command.Source }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path -LiteralPath $kits -PathType Container)) { return $null }
    $versions = @(Get-ChildItem -LiteralPath $kits -Directory | Where-Object { $_.Name -match '^\d+(\.\d+){3}$' } |
        Sort-Object -Property { [version]$_.Name } -Descending)
    foreach ($version in $versions) {
        $candidate = Join-Path $version.FullName 'x64\signtool.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    return $null
}

function Get-SignToolArguments {
    param(
        [Parameter(Mandatory = $true)][string]$Thumbprint,
        [Parameter(Mandatory = $true)][string]$Path,
        [switch]$NoTimestamp
    )

    $arguments = @('sign', '/fd', 'SHA256', '/sha1', $Thumbprint, '/s', 'My', '/d', $script:SignatureDescription)
    if (-not $NoTimestamp) { $arguments += @('/tr', $script:TimestampUrl, '/td', 'SHA256') }
    return $arguments + @($Path)
}

function Get-SigningReadiness {
    # Everything build.ps1 checks before it starts building. Problems stop the build, warnings do not.
    param([string]$Thumbprint)

    $problems = @()
    $warnings = @()
    $certificate = Find-CodeSigningCertificate -Thumbprint $Thumbprint
    if ($null -eq $certificate) {
        $problems += ("No valid code signing certificate with a private key was found in Cert:\CurrentUser\My. " +
            "Create one once with (as administrator): powershell -ExecutionPolicy Bypass -File tools\signing\New-CodeSigningCert.ps1. " +
            "Or build without signing: --no-sign")
    } else {
        if (-not (Test-CertificateTrusted $certificate)) {
            $warnings += ("The certificate $($certificate.Thumbprint) is not trusted on this machine, so Windows shows the app as " +
                "'Unknown publisher'. Fix (as administrator): powershell -ExecutionPolicy Bypass -File tools\signing\New-CodeSigningCert.ps1")
        }
        if ($certificate.NotAfter -lt (Get-Date).AddDays(30)) {
            $warnings += "The certificate expires on $($certificate.NotAfter.ToString('yyyy-MM-dd')). Create a new one with New-CodeSigningCert.ps1 -New."
        }
    }
    $signTool = Find-SignTool
    if ($null -eq $signTool) {
        $warnings += 'signtool.exe (Windows SDK) was not found; signing falls back to Set-AuthenticodeSignature. Install it with: winget install Microsoft.WindowsSDK.10.0.26100'
    }
    return [pscustomobject]@{ Certificate = $certificate; SignTool = $signTool; Problems = $problems; Warnings = $warnings }
}

function Invoke-SignTool([string]$SignTool, [string[]]$Arguments) {
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $global:LASTEXITCODE = 0
        & $SignTool @Arguments | Out-Host
        return $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Invoke-CodeSigning {
    # Signs one file with a timestamp; when the timestamp server is unreachable it signs without one (with a warning),
    # which stays valid until the certificate expires. Throws when the file ends up without our signature.
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        [AllowNull()][string]$SignTool
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "File to sign not found: $Path" }
    $full = (Resolve-Path -LiteralPath $Path).ProviderPath

    if (-not [string]::IsNullOrEmpty($SignTool)) {
        $code = Invoke-SignTool $SignTool (Get-SignToolArguments -Thumbprint $Certificate.Thumbprint -Path $full)
        if ($code -ne 0) {
            Write-Warning "signtool failed with exit code $code (timestamp server $($script:TimestampUrl) unreachable?). Signing without a timestamp."
            $code = Invoke-SignTool $SignTool (Get-SignToolArguments -Thumbprint $Certificate.Thumbprint -Path $full -NoTimestamp)
            if ($code -ne 0) { throw "signtool failed with exit code $code for $full" }
        }
    } else {
        # Set-AuthenticodeSignature still signs (without a timestamp) when the timestamp server fails; checked below.
        Set-AuthenticodeSignature -LiteralPath $full -Certificate $Certificate -HashAlgorithm SHA256 -TimestampServer $script:TimestampUrl | Out-Null
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $full
    if ($null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $Certificate.Thumbprint) {
        throw "$full is not signed with certificate $($Certificate.Thumbprint) (status: $($signature.Status), $($signature.StatusMessage))"
    }
    if ($signature.Status -ne 'Valid' -and (Test-CertificateTrusted $Certificate)) {
        throw "The signature of $full is not valid: $($signature.Status), $($signature.StatusMessage)"
    }
    if ($null -eq $signature.TimeStamperCertificate) { Write-Warning "$full was signed without a timestamp." }
    return $signature
}

function Export-CodeSigningCertificate {
    # Writes the public certificate (no private key) and install-certificate.bat for trusting it on other machines.
    param(
        [Parameter(Mandatory = $true)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        [Parameter(Mandatory = $true)][string]$Directory
    )

    New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    $cerPath = Join-Path $Directory $script:CertificateFileName
    [System.IO.File]::WriteAllBytes($cerPath, $Certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    $batPath = Join-Path $Directory 'install-certificate.bat'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install-certificate.bat') -Destination $batPath -Force
    return [pscustomobject]@{ Certificate = $cerPath; Installer = $batPath }
}

function Add-TrustedRootCertificate {
    # Requires administrator rights. Adds only the public part to LocalMachine\Root.
    param([Parameter(Mandatory = $true)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

    $public = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList (, $Certificate.RawData)
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store -ArgumentList 'Root', 'LocalMachine'
    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $store.Add($public)
    } finally {
        $store.Close()
    }
}

Export-ModuleMember -Function Get-CodeSigningDefaults, Test-CodeSigningCertificate, Find-CodeSigningCertificate,
    Test-CertificateTrusted, Find-SignTool, Get-SignToolArguments, Get-SigningReadiness, Invoke-CodeSigning,
    Export-CodeSigningCertificate, Add-TrustedRootCertificate

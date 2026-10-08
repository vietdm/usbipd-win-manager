# Self-contained tests for BuildTools.psm1 (no Pester). Exit code 0 = all passed.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/build/BuildTools.Tests.ps1

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
Import-Module (Join-Path $PSScriptRoot 'BuildTools.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '..\signing\Signing.psm1') -Force -DisableNameChecking

$script:Passed = 0
$script:Failed = 0

function Test-Case([string]$Name, [scriptblock]$Body) {
    try {
        & $Body
        $script:Passed++
    } catch {
        $script:Failed++
        Write-Host "FAIL  $Name" -ForegroundColor Red
        Write-Host "      $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Assert-Equal($Expected, $Actual, [string]$What = 'value') {
    if ($Expected -is [bool] -or $Actual -is [bool]) {
        if ([bool]$Expected -ne [bool]$Actual -or ($Expected -is [bool]) -ne ($Actual -is [bool])) {
            throw "${What}: expected <$Expected>, got <$Actual>"
        }
        return
    }
    if ($null -eq $Expected) {
        if ($null -ne $Actual) { throw "${What}: expected <null>, got <$Actual>" }
        return
    }
    if ($null -eq $Actual -or -not ($Expected -ceq $Actual)) { throw "${What}: expected <$Expected>, got <$Actual>" }
}

function Assert-Throws([scriptblock]$Body, [string]$MessageLike = '*', [type]$ExceptionType = [System.Exception]) {
    $caught = $null
    try { & $Body } catch { $caught = $_.Exception }
    if ($null -eq $caught) { throw "expected an exception matching '$MessageLike', nothing was thrown" }
    if (-not $ExceptionType.IsInstanceOfType($caught)) { throw "expected $($ExceptionType.Name), got $($caught.GetType().Name): $($caught.Message)" }
    if ($caught.Message -notlike $MessageLike) { throw "expected message like '$MessageLike', got '$($caught.Message)'" }
}

function Parse { ConvertFrom-BuildArguments -Arguments $args }

# Calls a helper with literal tokens so PowerShell passes 1.10 as a number, exactly like `.\build.ps1 --version 1.10`.
function Invoke-WithRawArgs { ConvertFrom-BuildArguments -Arguments $args }

# --- argument parsing --------------------------------------------------------------------------

Test-Case 'parse: no arguments gives defaults' {
    $o = Parse
    Assert-Equal $null $o.Version 'Version'
    foreach ($p in 'NoBump', 'Install', 'Release', 'NoSign', 'SkipTests', 'DryRun', 'Help') { Assert-Equal $false $o.$p $p }
}

Test-Case 'parse: long flags' {
    $o = Parse '--version' '2.2' '--install' '--release' '--skip-tests' '--dry-run'
    Assert-Equal '2.2' $o.Version 'Version'
    Assert-Equal $true $o.Install 'Install'
    Assert-Equal $true $o.Release 'Release'
    Assert-Equal $true $o.SkipTests 'SkipTests'
    Assert-Equal $true $o.DryRun 'DryRun'
}

Test-Case 'parse: short flags' {
    $o = Parse '-v' '3' '-i' '-r'
    Assert-Equal '3' $o.Version 'Version'
    Assert-Equal $true $o.Install 'Install'
    Assert-Equal $true $o.Release 'Release'
}

Test-Case 'parse: help' {
    Assert-Equal $true (Parse '--help').Help 'Help'
    Assert-Equal $true (Parse '-h').Help 'Help'
}

Test-Case 'parse: --no-bump' {
    Assert-Equal $true (Parse '--no-bump').NoBump 'NoBump'
}

Test-Case 'parse: --no-sign' {
    $o = Parse '--no-sign' '-i'
    Assert-Equal $true $o.NoSign 'NoSign'
    Assert-Equal $true $o.Install 'Install'
}

Test-Case 'parse: --version=<v> and -v:<v>' {
    Assert-Equal '2.2.5' (Parse '--version=2.2.5').Version 'Version'
    Assert-Equal '4' (Parse '-v:4').Version 'Version'
}

Test-Case 'parse: flags are case-insensitive' {
    $o = Parse '--INSTALL' '-R'
    Assert-Equal $true $o.Install 'Install'
    Assert-Equal $true $o.Release 'Release'
}

Test-Case 'parse: bare -- is ignored' {
    Assert-Equal $true (Parse '--' '-i').Install 'Install'
}

Test-Case 'parse: numeric tokens keep their literal text (1.10 stays 1.10)' {
    $o = Invoke-WithRawArgs --version 1.10 -i
    Assert-Equal '1.10' $o.Version 'Version'
    Assert-Equal $true $o.Install 'Install'
    Assert-Equal '2' (Invoke-WithRawArgs -v 2).Version 'Version'
    Assert-Equal '2.20' (Invoke-WithRawArgs -v 2.20).Version 'Version'
}

Test-Case 'parse: numeric tokens are culture-independent' {
    $old = [System.Threading.Thread]::CurrentThread.CurrentCulture
    try {
        [System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('de-DE')
        Assert-Equal '2.2' (Invoke-WithRawArgs --version 2.2).Version 'Version'
    } finally {
        [System.Threading.Thread]::CurrentThread.CurrentCulture = $old
    }
}

Test-Case 'parse: unknown argument' {
    Assert-Throws { Parse '--frobnicate' } 'Unknown argument: --frobnicate' ([System.ArgumentException])
    Assert-Throws { Parse '2.2' } 'Unknown argument: 2.2' ([System.ArgumentException])
    Assert-Throws { Parse '-x' } 'Unknown argument: -x' ([System.ArgumentException])
}

Test-Case 'parse: --version needs a value' {
    Assert-Throws { Parse '--version' } 'Missing value for --version.' ([System.ArgumentException])
    Assert-Throws { Parse '-v' '-i' } 'Missing value for -v.' ([System.ArgumentException])
    Assert-Throws { Parse '--version=' } 'Missing value for --version.' ([System.ArgumentException])
}

Test-Case 'parse: --version twice' {
    Assert-Throws { Parse '--version' '2' '-v' '3' } '*more than once*' ([System.ArgumentException])
}

Test-Case 'parse: --no-bump with --version is an error' {
    Assert-Throws { Parse '--no-bump' '--version' '2' } '--no-bump cannot be combined with --version.' ([System.ArgumentException])
}

# --- signing -----------------------------------------------------------------------------------

function New-TestCertificate([string[]]$Eku = @('1.3.6.1.5.5.7.3.3'), [switch]$Expired) {
    # In-memory certificate; nothing is written to a certificate store.
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $request = New-Object System.Security.Cryptography.X509Certificates.CertificateRequest -ArgumentList 'CN=Signing Test', $rsa,
        ([System.Security.Cryptography.HashAlgorithmName]::SHA256), ([System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    if ($Eku.Count -gt 0) {
        $oids = New-Object System.Security.Cryptography.OidCollection
        foreach ($value in $Eku) { [void]$oids.Add((New-Object System.Security.Cryptography.Oid -ArgumentList $value)) }
        $request.CertificateExtensions.Add((New-Object System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension -ArgumentList $oids, $false))
    }
    $now = [DateTimeOffset]::Now
    if ($Expired) { return $request.CreateSelfSigned($now.AddDays(-30), $now.AddDays(-1)) }
    return $request.CreateSelfSigned($now.AddDays(-1), $now.AddDays(365))
}

Test-Case 'signing: signtool arguments with and without timestamp' {
    $with = Get-SignToolArguments -Thumbprint 'ABC' -Path 'D:\my app\a.exe'
    Assert-Equal 'sign /fd SHA256 /sha1 ABC /s My /d USBIPD Manager /tr http://timestamp.digicert.com /td SHA256 D:\my app\a.exe' ($with -join ' ')
    Assert-Equal 'D:\my app\a.exe' $with[-1] 'path is one argument'
    $without = Get-SignToolArguments -Thumbprint 'ABC' -Path 'a.exe' -NoTimestamp
    Assert-Equal 'sign /fd SHA256 /sha1 ABC /s My /d USBIPD Manager a.exe' ($without -join ' ')
}

Test-Case 'signing: certificate validity rules' {
    Assert-Equal $true (Test-CodeSigningCertificate (New-TestCertificate)) 'code signing cert with key'
    Assert-Equal $false (Test-CodeSigningCertificate (New-TestCertificate -Eku @('1.3.6.1.5.5.7.3.1'))) 'server auth only'
    Assert-Equal $false (Test-CodeSigningCertificate (New-TestCertificate -Eku @())) 'no EKU'
    Assert-Equal $false (Test-CodeSigningCertificate (New-TestCertificate -Expired)) 'expired'
    $publicOnly = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList (, (New-TestCertificate).RawData)
    Assert-Equal $false (Test-CodeSigningCertificate $publicOnly) 'no private key'
    Assert-Equal $false (Test-CodeSigningCertificate $null) 'null'
}

Test-Case 'signing: an unknown self-signed certificate is not trusted' {
    Assert-Equal $false (Test-CertificateTrusted (New-TestCertificate)) 'trusted'
}

Test-Case 'signing: export writes the public certificate and the installer batch file' {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('SigningTests-' + [guid]::NewGuid().ToString('N'))
    try {
        $cert = New-TestCertificate
        $result = Export-CodeSigningCertificate -Certificate $cert -Directory $dir
        $exported = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $result.Certificate
        Assert-Equal $cert.Thumbprint $exported.Thumbprint 'thumbprint'
        Assert-Equal $false $exported.HasPrivateKey 'HasPrivateKey'
        Assert-Equal 'USBIPD-Manager-CodeSigning.cer' ([System.IO.Path]::GetFileName($result.Certificate)) 'file name'
        if (-not (Test-Path -LiteralPath $result.Installer -PathType Leaf)) { throw 'install-certificate.bat was not copied' }
        if ([System.IO.File]::ReadAllText($result.Installer) -notmatch 'USBIPD-Manager-CodeSigning\.cer') { throw 'batch file does not reference the certificate file' }
    } finally {
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    }
}

# --- version normalization and comparison -------------------------------------------------------

Test-Case 'normalize: pads to MAJOR.MINOR.PATCH' {
    Assert-Equal '2.0.0' (ConvertTo-BuildVersion '2')
    Assert-Equal '2.2.0' (ConvertTo-BuildVersion '2.2')
    Assert-Equal '2.2.5' (ConvertTo-BuildVersion '2.2.5')
    Assert-Equal '1.10.0' (ConvertTo-BuildVersion '1.10')
    Assert-Equal '2.3.0' (ConvertTo-BuildVersion '02.03')
}

Test-Case 'normalize: rejects invalid input' {
    foreach ($bad in @('', 'a', '1.', '.1', '1.2.3.4', 'v1.2', '1.-2', '1 .2', '1.2.3-beta', '99999999999')) {
        Assert-Throws { ConvertTo-BuildVersion $bad } '*Invalid version*' ([System.ArgumentException])
    }
}

Test-Case 'compare: numeric per segment' {
    Assert-Equal 1 (Compare-BuildVersion '1.0.10' '1.0.9')
    Assert-Equal -1 (Compare-BuildVersion '1.0.9' '1.0.10')
    Assert-Equal 0 (Compare-BuildVersion '2' '2.0.0')
    Assert-Equal 1 (Compare-BuildVersion '1.10.0' '1.9.99')
    Assert-Equal 1 (Compare-BuildVersion '10.0.0' '9.9.9')
    Assert-Equal -1 (Compare-BuildVersion '1.2.3' '1.3')
}

Test-Case 'next patch' {
    Assert-Equal '1.0.4' (Get-NextPatchVersion '1.0.3')
    Assert-Equal '1.0.10' (Get-NextPatchVersion '1.0.9')
}

# --- version resolution ------------------------------------------------------------------------

Test-Case 'resolve: default bumps PATCH' {
    Assert-Equal '1.0.4' (Resolve-BuildVersion -CurrentVersion '1.0.3')
    Assert-Equal '1.0.1' (Resolve-BuildVersion -CurrentVersion '1.0.0' -RequestedVersion '')
}

Test-Case 'resolve: --version forms' {
    Assert-Equal '2.0.0' (Resolve-BuildVersion -CurrentVersion '1.0.3' -RequestedVersion '2')
    Assert-Equal '2.2.0' (Resolve-BuildVersion -CurrentVersion '1.0.3' -RequestedVersion '2.2')
    Assert-Equal '2.2.5' (Resolve-BuildVersion -CurrentVersion '1.0.3' -RequestedVersion '2.2.5')
    Assert-Equal '1.0.10' (Resolve-BuildVersion -CurrentVersion '1.0.9' -RequestedVersion '1.0.10')
}

Test-Case 'resolve: --version must be greater' {
    Assert-Throws { Resolve-BuildVersion -CurrentVersion '1.0.3' -RequestedVersion '1.0.3' } 'Version 1.0.3 must be greater than the current version 1.0.3.' ([System.ArgumentException])
    Assert-Throws { Resolve-BuildVersion -CurrentVersion '2.2.0' -RequestedVersion '2.2' } 'Version 2.2.0 must be greater than the current version 2.2.0.' ([System.ArgumentException])
    Assert-Throws { Resolve-BuildVersion -CurrentVersion '1.0.10' -RequestedVersion '1.0.9' } 'Version 1.0.9 must be greater than the current version 1.0.10.' ([System.ArgumentException])
    Assert-Throws { Resolve-BuildVersion -CurrentVersion '1.0.3' -RequestedVersion '1' } '*must be greater*' ([System.ArgumentException])
}

Test-Case 'resolve: invalid --version' {
    Assert-Throws { Resolve-BuildVersion -CurrentVersion '1.0.3' -RequestedVersion '2.x' } '*Invalid version*' ([System.ArgumentException])
}

Test-Case 'resolve: --no-bump keeps the current version' {
    Assert-Equal '1.0.3' (Resolve-BuildVersion -CurrentVersion '1.0.3' -NoBump)
    Assert-Throws { Resolve-BuildVersion -CurrentVersion '1.0.3' -RequestedVersion '2' -NoBump } '*cannot be combined*' ([System.ArgumentException])
}

Test-Case 'resolve: invalid current version' {
    Assert-Throws { Resolve-BuildVersion -CurrentVersion '1.0' } '*invalid version*'
    Assert-Throws { Resolve-BuildVersion -CurrentVersion '' } '*invalid version*'
}

# --- dates -------------------------------------------------------------------------------------

Test-Case 'dates: non-release keeps both' {
    $d = Resolve-BuildDates -CreatedDate $null -UpdatedDate $null -Today '2026-10-08'
    Assert-Equal $null $d.CreatedDate 'CreatedDate'
    Assert-Equal $null $d.UpdatedDate 'UpdatedDate'
    $d = Resolve-BuildDates -CreatedDate '2026-01-01' -UpdatedDate '2026-02-02' -Today '2026-10-08'
    Assert-Equal '2026-01-01' $d.CreatedDate 'CreatedDate'
    Assert-Equal '2026-02-02' $d.UpdatedDate 'UpdatedDate'
}

Test-Case 'dates: first release sets createdDate' {
    $d = Resolve-BuildDates -CreatedDate $null -UpdatedDate $null -Release -Today '2026-10-08'
    Assert-Equal '2026-10-08' $d.CreatedDate 'CreatedDate'
    Assert-Equal $null $d.UpdatedDate 'UpdatedDate'
}

Test-Case 'dates: later release sets updatedDate' {
    $d = Resolve-BuildDates -CreatedDate '2026-01-01' -UpdatedDate $null -Release -Today '2026-10-08'
    Assert-Equal '2026-01-01' $d.CreatedDate 'CreatedDate'
    Assert-Equal '2026-10-08' $d.UpdatedDate 'UpdatedDate'
    $d = Resolve-BuildDates -CreatedDate '2026-01-01' -UpdatedDate '2026-05-05' -Release -Today '2026-10-08'
    Assert-Equal '2026-10-08' $d.UpdatedDate 'UpdatedDate'
}

Test-Case 'dates: default today is yyyy-MM-dd' {
    $d = Resolve-BuildDates -CreatedDate $null -UpdatedDate $null -Release
    if ($d.CreatedDate -notmatch '^\d{4}-\d{2}-\d{2}$') { throw "unexpected date '$($d.CreatedDate)'" }
    Assert-Equal (Get-Date -Format 'yyyy-MM-dd') $d.CreatedDate 'CreatedDate'
}

# --- version.json ------------------------------------------------------------------------------

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ('BuildToolsTests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempDir | Out-Null
$utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false

try {
    $repoVersionFile = Join-Path $PSScriptRoot '..\..\version.json'

    Test-Case 'json: format matches the repository layout' {
        $expected = "{`n  `"version`": `"1.0.0`",`n  `"createdDate`": null,`n  `"updatedDate`": null`n}`n"
        Assert-Equal $expected (Format-VersionJson -Version '1.0.0' -CreatedDate $null -UpdatedDate $null)
        $expected = "{`r`n  `"version`": `"1.2.3`",`r`n  `"createdDate`": `"2026-01-01`",`r`n  `"updatedDate`": null`r`n}`r`n"
        Assert-Equal $expected (Format-VersionJson -Version '1.2.3' -CreatedDate '2026-01-01' -UpdatedDate '' -NewLine "`r`n")
    }

    Test-Case 'json: rejects invalid values' {
        Assert-Throws { Format-VersionJson -Version '1.2' -CreatedDate $null -UpdatedDate $null } '*Invalid version*'
        Assert-Throws { Format-VersionJson -Version '1.2.3' -CreatedDate '08/10/2026' -UpdatedDate $null } '*Invalid date*'
    }

    Test-Case 'json: repository version.json round-trips byte for byte' {
        $copy = Join-Path $tempDir 'repo-version.json'
        Copy-Item -LiteralPath $repoVersionFile -Destination $copy
        $before = [System.IO.File]::ReadAllBytes($copy)
        $v = Read-VersionFile -Path $copy
        Write-VersionFile -Path $copy -Version $v.Version -CreatedDate $v.CreatedDate -UpdatedDate $v.UpdatedDate -NewLine $v.NewLine
        $after = [System.IO.File]::ReadAllBytes($copy)
        Assert-Equal ([Convert]::ToBase64String($before)) ([Convert]::ToBase64String($after)) 'bytes'
    }

    Test-Case 'json: write then read with dates, UTF-8 without BOM, trailing newline' {
        $file = Join-Path $tempDir 'dated.json'
        Write-VersionFile -Path $file -Version '2.2.0' -CreatedDate '2026-01-01' -UpdatedDate '2026-10-08'
        $bytes = [System.IO.File]::ReadAllBytes($file)
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { throw 'file has a UTF-8 BOM' }
        Assert-Equal 10 ([int]$bytes[$bytes.Length - 1]) 'last byte'
        $v = Read-VersionFile -Path $file
        Assert-Equal '2.2.0' $v.Version 'Version'
        Assert-Equal '2026-01-01' $v.CreatedDate 'CreatedDate'
        Assert-Equal '2026-10-08' $v.UpdatedDate 'UpdatedDate'
        Assert-Equal "`n" $v.NewLine 'NewLine'
        if (Test-Path -LiteralPath ($file + '.tmp')) { throw 'temporary file was left behind' }
    }

    Test-Case 'json: overwriting an existing file keeps no temp file' {
        $file = Join-Path $tempDir 'dated.json'
        Write-VersionFile -Path $file -Version '2.2.1' -CreatedDate '2026-01-01' -UpdatedDate $null
        Assert-Equal '2.2.1' (Read-VersionFile -Path $file).Version 'Version'
        Assert-Equal $null (Read-VersionFile -Path $file).UpdatedDate 'UpdatedDate'
        if (Test-Path -LiteralPath ($file + '.tmp')) { throw 'temporary file was left behind' }
    }

    Test-Case 'json: CRLF files keep CRLF' {
        $file = Join-Path $tempDir 'crlf.json'
        [System.IO.File]::WriteAllText($file, "{`r`n  `"version`": `"1.0.0`",`r`n  `"createdDate`": null,`r`n  `"updatedDate`": null`r`n}`r`n", $utf8NoBom)
        $v = Read-VersionFile -Path $file
        Assert-Equal "`r`n" $v.NewLine 'NewLine'
        Assert-Equal $null $v.CreatedDate 'CreatedDate'
    }

    Test-Case 'json: compact one-line file and missing date properties' {
        $file = Join-Path $tempDir 'compact.json'
        [System.IO.File]::WriteAllText($file, '{ "version": "1.0.3" }', $utf8NoBom)
        $v = Read-VersionFile -Path $file
        Assert-Equal '1.0.3' $v.Version 'Version'
        Assert-Equal $null $v.CreatedDate 'CreatedDate'
        Assert-Equal $null $v.UpdatedDate 'UpdatedDate'
    }

    Test-Case 'json: read errors' {
        $file = Join-Path $tempDir 'broken.json'
        [System.IO.File]::WriteAllText($file, '{ "version": ', $utf8NoBom)
        Assert-Throws { Read-VersionFile -Path $file } '*not valid JSON*'
        [System.IO.File]::WriteAllText($file, '{ "name": "x" }', $utf8NoBom)
        Assert-Throws { Read-VersionFile -Path $file } "*no 'version' property*"
        [System.IO.File]::WriteAllText($file, '{ "version": "1.0.0", "createdDate": "yesterday" }', $utf8NoBom)
        Assert-Throws { Read-VersionFile -Path $file } '*createdDate*'
        Assert-Throws { Read-VersionFile -Path (Join-Path $tempDir 'missing.json') } '*not found*'
    }

    Test-Case 'end to end: first release of a bumped version' {
        $file = Join-Path $tempDir 'e2e.json'
        Copy-Item -LiteralPath $repoVersionFile -Destination $file -Force
        [System.IO.File]::WriteAllText($file, (Format-VersionJson -Version '1.0.9' -CreatedDate $null -UpdatedDate $null), $utf8NoBom)
        $o = Invoke-WithRawArgs --version 1.0.10 --release -i
        $cur = Read-VersionFile -Path $file
        $ver = Resolve-BuildVersion -CurrentVersion $cur.Version -RequestedVersion $o.Version -NoBump:$o.NoBump
        $dates = Resolve-BuildDates -CreatedDate $cur.CreatedDate -UpdatedDate $cur.UpdatedDate -Release:$o.Release -Today '2026-10-08'
        Write-VersionFile -Path $file -Version $ver -CreatedDate $dates.CreatedDate -UpdatedDate $dates.UpdatedDate -NewLine $cur.NewLine
        $expected = "{`n  `"version`": `"1.0.10`",`n  `"createdDate`": `"2026-10-08`",`n  `"updatedDate`": null`n}`n"
        Assert-Equal $expected ([System.IO.File]::ReadAllText($file)) 'file content'
    }

    Test-Case 'size formatting' {
        Assert-Equal '512 B' (Format-FileSize 512)
        if ((Format-FileSize (70 * 1MB)) -notlike '70*0 MB') { throw "unexpected '$(Format-FileSize (70 * 1MB))'" }
    }
} finally {
    Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host ("PowerShell {0}: {1} passed, {2} failed" -f $PSVersionTable.PSVersion, $script:Passed, $script:Failed) -ForegroundColor $(if ($script:Failed) { 'Red' } else { 'Green' })
if ($script:Failed -gt 0) { exit 1 }
exit 0

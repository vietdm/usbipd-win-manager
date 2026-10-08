# Builds USBIPD Manager: tests, self-contained single-file publish, portable exe and optional installer.
# Usage: .\build.ps1 --help
# There is deliberately no param() block: GNU-style flags (--version 2.2, -i) are parsed from $args.

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$Root = $PSScriptRoot
Import-Module (Join-Path $Root 'tools\build\BuildTools.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $Root 'tools\signing\Signing.psm1') -Force -DisableNameChecking

function Write-Info([string]$Text, [string]$Color = 'Gray') { Write-Host $Text -ForegroundColor $Color }

function Stop-Build([string]$Message, [switch]$ShowUsage) {
    Write-Host ''
    Write-Host "ERROR: $Message" -ForegroundColor Red
    if ($ShowUsage) {
        Write-Host ''
        Write-Host (Get-BuildUsage)
    }
    exit 1
}

function Format-CommandLine([string]$FilePath, [string[]]$Arguments) {
    $parts = @($FilePath) + $Arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    return ($parts -join ' ')
}

function Get-RelativePath([string]$Path) {
    if ($Path.StartsWith($Root + '\', [System.StringComparison]::OrdinalIgnoreCase)) { return $Path.Substring($Root.Length + 1) }
    return $Path
}

function Find-DotNet {
    $path = $null
    $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) {
        $path = $command.Source
    } elseif ($env:ProgramFiles -and (Test-Path -LiteralPath (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe') -PathType Leaf)) {
        # Freshly installed SDKs are not on PATH until a new terminal is opened.
        $path = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    }
    if ($null -eq $path) { return $null }
    $sdks = @()
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $sdks = @(& $path --list-sdks)
    } catch {
        $sdks = @()
    } finally {
        $ErrorActionPreference = $previous
    }
    $sdk10 = @($sdks | Where-Object { "$_" -match '^10\.' } | ForEach-Object { ("$_" -split ' ')[0] })
    return [pscustomobject]@{ Path = $path; Sdk = $(if ($sdk10.Count -gt 0) { $sdk10[-1] } else { $null }) }
}

function Find-Iscc {
    $candidates = @()
    if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
    if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe' }
    if ($env:LOCALAPPDATA) { $candidates += Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    $command = Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) { return $command.Source }
    return $null
}

# --- parse and validate --------------------------------------------------------------------------

try {
    $Options = ConvertFrom-BuildArguments -Arguments $args
} catch [System.ArgumentException] {
    Stop-Build $_.Exception.Message -ShowUsage
}

if ($Options.Help) {
    Write-Host (Get-BuildUsage)
    exit 0
}

$VersionFile = Join-Path $Root 'version.json'
try {
    $Current = Read-VersionFile -Path $VersionFile
    $Version = Resolve-BuildVersion -CurrentVersion $Current.Version -RequestedVersion $Options.Version -NoBump:$Options.NoBump
    $Dates = Resolve-BuildDates -CreatedDate $Current.CreatedDate -UpdatedDate $Current.UpdatedDate -Release:$Options.Release
} catch {
    Stop-Build $_.Exception.Message
}

$Solution = Join-Path $Root 'UsbipdManager.slnx'
$Project = Join-Path $Root 'src\UsbipdManager\UsbipdManager.csproj'
$PublishDir = Join-Path $Root ".artifacts\publish\$Version"
$PublishedExe = Join-Path $PublishDir 'UsbipdManager.exe'
$DistDir = Join-Path $Root 'dist'
$PortableExe = Join-Path $DistDir "UsbipdManager-$Version-portable.exe"
$SetupExe = Join-Path $DistDir "UsbipdManager-Setup-$Version.exe"
$IssFile = Join-Path $Root 'installer\UsbipdManager.iss'
$AppIcon = Join-Path $Root 'src\UsbipdManager\Assets\app.ico'
$CertificateDir = Join-Path $DistDir 'certificate'
$SignScript = Join-Path $Root 'tools\signing\Sign-File.ps1'
$Sign = -not $Options.NoSign

# --- toolchain -----------------------------------------------------------------------------------

$toolProblems = @()
$DotNet = Find-DotNet
if ($null -eq $DotNet) {
    $toolProblems += 'The .NET SDK was not found (dotnet is not on PATH). Install it with: winget install Microsoft.DotNet.SDK.10'
} elseif ($null -eq $DotNet.Sdk) {
    $toolProblems += ".NET SDK 10 is not installed ($($DotNet.Path) has no 10.x SDK). Install it with: winget install Microsoft.DotNet.SDK.10"
}
$Iscc = $null
if ($Options.Install) {
    $Iscc = Find-Iscc
    if ($null -eq $Iscc) {
        $toolProblems += 'Inno Setup 6 (ISCC.exe) was not found; it is needed for --install. Install it with: winget install JRSoftware.InnoSetup'
    }
    if (-not (Test-Path -LiteralPath $IssFile -PathType Leaf)) { $toolProblems += "Installer script not found: $IssFile" }
}
$Signing = $null
if ($Sign) {
    $Signing = Get-SigningReadiness
    $toolProblems += $Signing.Problems
}
if (-not $Options.DryRun -and $toolProblems.Count -gt 0) { Stop-Build ($toolProblems -join [Environment]::NewLine + '       ') }

$DotNetExe = $(if ($null -ne $DotNet) { $DotNet.Path } else { 'dotnet' })
$IsccExe = $(if ($null -ne $Iscc) { $Iscc } else { 'ISCC.exe' })

# --- steps ---------------------------------------------------------------------------------------

$publishArgs = @(
    'publish', $Project,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=none',
    "-p:Version=$Version"
)
# Empty dates are left out; Directory.Build.props then falls back to version.json (which has the same null value).
if ($Dates.CreatedDate) { $publishArgs += "-p:AppCreatedDate=$($Dates.CreatedDate)" }
if ($Dates.UpdatedDate) { $publishArgs += "-p:AppUpdatedDate=$($Dates.UpdatedDate)" }
$publishArgs += @('-o', $PublishDir)

$isccArgs = @("/DAppVersion=$Version", "/DSourceExe=$PublishedExe")
if (Test-Path -LiteralPath $AppIcon -PathType Leaf) { $isccArgs += "/DAppIcon=$AppIcon" }
if ($Sign) {
    # Inno Setup signs the setup exe and the uninstaller through this named sign tool; it replaces $q with a quote
    # and $f with the quoted file name, so the string must stay single-quoted here.
    $thumbprint = $(if ($null -ne $Signing.Certificate) { $Signing.Certificate.Thumbprint } else { '<thumbprint>' })
    $isccArgs += '/DSignToolName=usbipdsign'
    $isccArgs += ('/Susbipdsign=powershell.exe -NoProfile -ExecutionPolicy Bypass -File $q' + $SignScript + '$q -Thumbprint ' + $thumbprint + ' -Path $f')
}
$isccArgs += @('/Q', "/O$DistDir", $IssFile)

$Steps = @()
if (-not $Options.SkipTests) {
    $Steps += [pscustomobject]@{ Title = 'Run unit tests'; File = $DotNetExe; Args = @('test', $Solution, '-c', 'Release'); Action = $null; Show = $null }
}
$Steps += [pscustomobject]@{
    Title  = 'Clean publish folder'
    File   = $null; Args = $null
    Show   = "remove $(Get-RelativePath $PublishDir)"
    Action = { if (Test-Path -LiteralPath $PublishDir) { Remove-Item -LiteralPath $PublishDir -Recurse -Force } }
}
$Steps += [pscustomobject]@{ Title = 'Publish (self-contained, single file, win-x64)'; File = $DotNetExe; Args = $publishArgs; Action = $null; Show = $null }
if ($Sign) {
    # Signed before it is copied and packaged, so the portable exe and the installed exe carry the signature.
    $Steps += [pscustomobject]@{
        Title  = 'Sign exe'
        File   = $null; Args = $null
        Show   = "sign $(Get-RelativePath $PublishedExe) with $(if ($Signing.SignTool) { 'signtool' } else { 'Set-AuthenticodeSignature' })"
        Action = { Invoke-CodeSigning -Path $PublishedExe -Certificate $Signing.Certificate -SignTool $Signing.SignTool | Out-Null }
    }
}
$Steps += [pscustomobject]@{
    Title  = 'Copy portable exe'
    File   = $null; Args = $null
    Show   = "copy $(Get-RelativePath $PublishedExe) -> $(Get-RelativePath $PortableExe)"
    Action = {
        if (-not (Test-Path -LiteralPath $PublishedExe -PathType Leaf)) { throw "Published exe not found: $PublishedExe" }
        New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
        Copy-Item -LiteralPath $PublishedExe -Destination $PortableExe -Force
    }
}
if ($Options.Install) {
    $Steps += [pscustomobject]@{ Title = $(if ($Sign) { 'Build and sign installer (Inno Setup)' } else { 'Build installer (Inno Setup)' }); File = $IsccExe; Args = $isccArgs; Action = $null; Show = $null }
}
if ($Sign) {
    $Steps += [pscustomobject]@{
        Title  = 'Export public certificate for other machines'
        File   = $null; Args = $null
        Show   = "write $(Get-RelativePath $CertificateDir)\$((Get-CodeSigningDefaults).CertificateFile) and install-certificate.bat"
        Action = { Export-CodeSigningCertificate -Certificate $Signing.Certificate -Directory $CertificateDir | Out-Null }
    }
}

# Older builds of the kinds this build produces; removed only after the whole build succeeded.
$staleKinds = @('Portable')
if ($Options.Install) { $staleKinds += 'Setup' }
function Get-StaleArtifacts { foreach ($kind in $staleKinds) { Get-StaleBuildArtifacts -Directory $DistDir -Kind $kind -KeepVersion $Version } }

$versionText = Format-VersionJson -Version $Version -CreatedDate $Dates.CreatedDate -UpdatedDate $Dates.UpdatedDate -NewLine $Current.NewLine
$currentText = [System.IO.File]::ReadAllText($VersionFile, [System.Text.Encoding]::UTF8)
$VersionFileChanged = ($versionText -cne $currentText)

function Format-Date($Value) { if ($Value) { return $Value } return 'null' }

Write-Info ''
Write-Info 'USBIPD Manager build' 'Cyan'
Write-Info ("  Version      : {0} -> {1}" -f $Current.Version, $Version)
Write-Info ("  Created date : {0} -> {1}" -f (Format-Date $Current.CreatedDate), (Format-Date $Dates.CreatedDate))
Write-Info ("  Updated date : {0} -> {1}" -f (Format-Date $Current.UpdatedDate), (Format-Date $Dates.UpdatedDate))
Write-Info ("  Mode         : {0}{1}{2}{3}" -f $(if ($Options.Release) { 'release' } else { 'development' }), $(if ($Options.Install) { ', installer' } else { '' }), $(if ($Sign) { ', signed' } else { ', unsigned' }), $(if ($Options.SkipTests) { ', tests skipped' } else { '' }))
if ($Sign -and $null -ne $Signing.Certificate) {
    Write-Info ("  Certificate  : {0} ({1}, valid until {2})" -f $Signing.Certificate.Subject, $Signing.Certificate.Thumbprint, $Signing.Certificate.NotAfter.ToString('yyyy-MM-dd'))
}
if ($Sign) { foreach ($warning in $Signing.Warnings) { Write-Info "  WARNING: $warning" 'Yellow' } }

# --- dry run -------------------------------------------------------------------------------------

if ($Options.DryRun) {
    Write-Info ''
    Write-Info 'Dry run: nothing is executed and no file is written.' 'Yellow'
    Write-Info ''
    Write-Info 'Toolchain:'
    if ($null -ne $DotNet) { Write-Info ("  dotnet : {0} (SDK {1})" -f $DotNet.Path, (Format-Date $DotNet.Sdk)) } else { Write-Info '  dotnet : not found' }
    if ($Options.Install) { Write-Info ("  ISCC   : {0}" -f $(if ($Iscc) { $Iscc } else { 'not found' })) }
    if ($Sign) { Write-Info ("  signer : {0}" -f $(if ($Signing.SignTool) { $Signing.SignTool } else { 'Set-AuthenticodeSignature (signtool not found)' })) }
    foreach ($problem in $toolProblems) { Write-Info "  WARNING: $problem" 'Yellow' }
    Write-Info ''
    Write-Info 'Steps:'
    $n = 0
    foreach ($step in $Steps) {
        $n++
        Write-Info ("  {0}. {1}" -f $n, $step.Title)
        if ($null -ne $step.File) { Write-Info ("     {0}" -f (Format-CommandLine $step.File $step.Args)) 'DarkGray' }
        else { Write-Info ("     {0}" -f $step.Show) 'DarkGray' }
    }
    $n++
    if ($VersionFileChanged) {
        Write-Info ("  {0}. Write version.json" -f $n)
        foreach ($line in ($versionText.TrimEnd() -split "`r?`n")) { Write-Info "     $line" 'DarkGray' }
    } else {
        Write-Info ("  {0}. version.json stays unchanged" -f $n)
    }
    $n++
    $stale = @(Get-StaleArtifacts)
    if ($stale.Count -gt 0) {
        Write-Info ("  {0}. Remove older builds from {1}\" -f $n, (Get-RelativePath $DistDir))
        foreach ($file in $stale) { Write-Info "     $($file.Name)" 'DarkGray' }
    } else {
        Write-Info ("  {0}. No older builds to remove" -f $n)
    }
    Write-Info ''
    Write-Info 'Outputs:'
    Write-Info "  $(Get-RelativePath $PortableExe)"
    if ($Options.Install) { Write-Info "  $(Get-RelativePath $SetupExe)" }
    if ($Sign) { Write-Info "  $(Get-RelativePath $CertificateDir)\" }
    exit 0
}

# --- run -----------------------------------------------------------------------------------------

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
Push-Location $Root
try {
    $n = 0
    foreach ($step in $Steps) {
        $n++
        Write-Host ''
        Write-Host ("==> [{0}/{1}] {2}" -f $n, $Steps.Count, $step.Title) -ForegroundColor Cyan
        if ($null -ne $step.File) {
            Write-Host ("> {0}" -f (Format-CommandLine $step.File $step.Args)) -ForegroundColor DarkGray
            $global:LASTEXITCODE = 0
            & $step.File @($step.Args)
            if ($LASTEXITCODE -ne 0) { Stop-Build ("Step '{0}' failed with exit code {1}. version.json was not changed." -f $step.Title, $LASTEXITCODE) }
        } else {
            try {
                & $step.Action
            } catch {
                Stop-Build ("Step '{0}' failed: {1} version.json was not changed." -f $step.Title, $_.Exception.Message)
            }
        }
    }

    if ($Options.Install -and -not (Test-Path -LiteralPath $SetupExe -PathType Leaf)) {
        Stop-Build "The installer was not produced: $SetupExe. version.json was not changed."
    }

    if ($VersionFileChanged) {
        try {
            Write-VersionFile -Path $VersionFile -Version $Version -CreatedDate $Dates.CreatedDate -UpdatedDate $Dates.UpdatedDate -NewLine $Current.NewLine
        } catch {
            Stop-Build "The build succeeded but version.json could not be written: $($_.Exception.Message)"
        }
    }

    # A file still in use (e.g. a running portable exe) stays; that must not fail a successful build.
    $removed = @()
    $kept = @()
    foreach ($file in @(Get-StaleArtifacts)) {
        try {
            Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop
            $removed += $file.Name
        } catch {
            $kept += $file.Name
        }
    }
} finally {
    Pop-Location
}
$stopwatch.Stop()

Write-Host ''
Write-Host ('Build succeeded in {0:mm\:ss}.' -f $stopwatch.Elapsed) -ForegroundColor Green
Write-Info ("  Version      : {0}" -f $Version)
Write-Info ("  Created date : {0}" -f $(if ($Dates.CreatedDate) { $Dates.CreatedDate } else { 'none (development build)' }))
Write-Info ("  Updated date : {0}" -f (Format-Date $Dates.UpdatedDate))
Write-Info ("  version.json : {0}" -f $(if ($VersionFileChanged) { 'updated' } else { 'unchanged' }))
Write-Info ("  Signed       : {0}" -f $(if ($Sign) { "yes, $($Signing.Certificate.Subject) ($($Signing.Certificate.Thumbprint))" } else { 'no (--no-sign)' }))
Write-Info '  Artifacts    :'
$artifacts = @($PortableExe)
if ($Options.Install) { $artifacts += $SetupExe }
foreach ($artifact in $artifacts) {
    $signatureText = ''
    if ($Sign) { $signatureText = ', signature: ' + (Get-AuthenticodeSignature -LiteralPath $artifact).Status }
    Write-Info ("    {0}  ({1}{2})" -f (Get-RelativePath $artifact), (Format-FileSize (Get-Item -LiteralPath $artifact).Length), $signatureText)
}
if ($Sign) { Write-Info ("    {0}\  (public certificate + install-certificate.bat for other machines)" -f (Get-RelativePath $CertificateDir)) }
if ($removed.Count -gt 0) { Write-Info ("  Removed      : {0}" -f ($removed -join ', ')) }
foreach ($name in $kept) { Write-Info "  WARNING: could not remove the older build $name (in use?); delete it by hand." 'Yellow' }
exit 0

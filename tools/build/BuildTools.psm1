# Pure build logic for build.ps1: argument parsing, version rules, release dates and version.json I/O.
# Must run on Windows PowerShell 5.1 and PowerShell 7. Keep this file ASCII (5.1 reads BOM-less files as ANSI).

Set-StrictMode -Version 2.0

$script:InputVersionPattern = '^\d+(\.\d+){0,2}$'
$script:FullVersionPattern = '^\d+\.\d+\.\d+$'
$script:DatePattern = '^\d{4}-\d{2}-\d{2}$'
$script:Invariant = [System.Globalization.CultureInfo]::InvariantCulture

function Get-BuildUsage {
    return @'
USBIPD Manager build script

Usage: .\build.ps1 [options]
       build.bat [options]

Options:
  -v, --version <v>   Build version <v>: MAJOR, MAJOR.MINOR or MAJOR.MINOR.PATCH
                      (2 -> 2.0.0, 2.2 -> 2.2.0). Must be greater than the current version.
                      Default: bump the PATCH number of the current version.
      --no-bump       Rebuild the current version (cannot be combined with --version).
  -r, --release       Release build: sets createdDate on the first release, updatedDate afterwards.
  -i, --install       Also build the installer (requires Inno Setup 6).
      --no-sign       Do not sign. By default the exe, the installer and its uninstaller are signed
                      (see tools\signing\New-CodeSigningCert.ps1) and the build stops early if it cannot sign.
      --skip-tests    Do not run the unit tests.
      --dry-run       Show the resolved version, dates, steps and outputs without running anything.
  -h, --help          Show this help.

After a successful build, older builds of the same kind are removed from dist\
(other UsbipdManager-<v>-portable.exe; with -i also other UsbipdManager-Setup-<v>.exe).

Examples:
  .\build.ps1                          1.0.3 -> 1.0.4, portable exe
  .\build.ps1 --version 2.2            -> 2.2.0
  .\build.ps1 --no-bump -i             rebuild the current version, also build the installer
  .\build.ps1 --version 1.1 --release -i
'@
}

function New-ArgumentError([string]$Message) {
    return New-Object System.ArgumentException -ArgumentList $Message
}

function ConvertFrom-BuildArguments {
    # Takes the raw $args of build.ps1. Numeric tokens such as 1.10 arrive as numbers when the script is
    # invoked directly from PowerShell; a [string] cast returns the token exactly as typed (1.10, not 1.1).
    param([object[]]$Arguments)

    $options = [ordered]@{
        Version   = $null
        NoBump    = $false
        Install   = $false
        Release   = $false
        NoSign    = $false
        SkipTests = $false
        DryRun    = $false
        Help      = $false
    }

    $tokens = New-Object System.Collections.Generic.List[string]
    if ($null -ne $Arguments) {
        for ($k = 0; $k -lt $Arguments.Count; $k++) {
            if ($null -ne $Arguments[$k]) { $tokens.Add([string]$Arguments[$k]) }
        }
    }

    $i = 0
    while ($i -lt $tokens.Count) {
        $token = $tokens[$i]
        $name = $token
        $inlineValue = $null
        if ($token -match '^(--version|-v)[=:](.*)$') {
            $name = $Matches[1]
            $inlineValue = $Matches[2]
        }

        switch ($name) {
            { $_ -eq '--version' -or $_ -eq '-v' } {
                if ($null -ne $options.Version) { throw (New-ArgumentError "$name was given more than once.") }
                if ($null -ne $inlineValue) {
                    $value = $inlineValue
                } else {
                    if ($i + 1 -ge $tokens.Count -or $tokens[$i + 1].StartsWith('-')) {
                        throw (New-ArgumentError "Missing value for $name.")
                    }
                    $i++
                    $value = $tokens[$i]
                }
                if ([string]::IsNullOrWhiteSpace($value)) { throw (New-ArgumentError "Missing value for $name.") }
                $options.Version = $value.Trim()
                break
            }
            '--no-bump'    { $options.NoBump = $true; break }
            '--install'    { $options.Install = $true; break }
            '-i'           { $options.Install = $true; break }
            '--release'    { $options.Release = $true; break }
            '-r'           { $options.Release = $true; break }
            '--no-sign'    { $options.NoSign = $true; break }
            '--skip-tests' { $options.SkipTests = $true; break }
            '--dry-run'    { $options.DryRun = $true; break }
            '--help'       { $options.Help = $true; break }
            '-h'           { $options.Help = $true; break }
            # Direct PowerShell invocation swallows a bare "--"; ignore it under -File too so both behave alike.
            '--'           { break }
            default        { throw (New-ArgumentError "Unknown argument: $token") }
        }
        $i++
    }

    if ($options.NoBump -and $null -ne $options.Version) {
        throw (New-ArgumentError '--no-bump cannot be combined with --version.')
    }

    return [pscustomobject]$options
}

function ConvertTo-BuildVersion {
    # Normalizes a user-supplied version to MAJOR.MINOR.PATCH: 2 -> 2.0.0, 2.2 -> 2.2.0, 02.3 -> 2.3.0.
    param([string]$Version)

    if ($null -eq $Version -or $Version -notmatch $script:InputVersionPattern) {
        throw (New-ArgumentError "Invalid version '$Version'. Use MAJOR, MAJOR.MINOR or MAJOR.MINOR.PATCH (digits only), e.g. 2, 2.2 or 2.2.5.")
    }
    $parts = New-Object System.Collections.Generic.List[int]
    foreach ($segment in $Version.Split('.')) {
        $number = 0
        if (-not [int]::TryParse($segment, [System.Globalization.NumberStyles]::None, $script:Invariant, [ref]$number)) {
            throw (New-ArgumentError "Invalid version '$Version': '$segment' is too large.")
        }
        $parts.Add($number)
    }
    while ($parts.Count -lt 3) { $parts.Add(0) }
    return ($parts -join '.')
}

function Compare-BuildVersion {
    # Numeric comparison per segment (1.0.10 > 1.0.9). Returns -1, 0 or 1.
    param([string]$Left, [string]$Right)

    $a = (ConvertTo-BuildVersion $Left).Split('.')
    $b = (ConvertTo-BuildVersion $Right).Split('.')
    for ($i = 0; $i -lt 3; $i++) {
        $x = [int]$a[$i]
        $y = [int]$b[$i]
        if ($x -lt $y) { return -1 }
        if ($x -gt $y) { return 1 }
    }
    return 0
}

function Get-NextPatchVersion {
    param([string]$Version)

    $parts = (ConvertTo-BuildVersion $Version).Split('.')
    return '{0}.{1}.{2}' -f $parts[0], $parts[1], ([int]$parts[2] + 1)
}

function Resolve-BuildVersion {
    param(
        [string]$CurrentVersion,
        [string]$RequestedVersion,
        [switch]$NoBump
    )

    if ($null -eq $CurrentVersion -or $CurrentVersion -notmatch $script:FullVersionPattern) {
        throw "version.json contains an invalid version '$CurrentVersion' (expected MAJOR.MINOR.PATCH)."
    }
    $current = ConvertTo-BuildVersion $CurrentVersion

    $hasRequested = -not [string]::IsNullOrEmpty($RequestedVersion)
    if ($NoBump -and $hasRequested) {
        throw (New-ArgumentError '--no-bump cannot be combined with --version.')
    }
    if ($NoBump) { return $current }
    if ($hasRequested) {
        $requested = ConvertTo-BuildVersion $RequestedVersion
        if ((Compare-BuildVersion $requested $current) -le 0) {
            throw (New-ArgumentError "Version $requested must be greater than the current version $current.")
        }
        return $requested
    }
    return Get-NextPatchVersion $current
}

function Get-BuildToday {
    return (Get-Date).ToString('yyyy-MM-dd', $script:Invariant)
}

function Resolve-BuildDates {
    # Release builds: the first release sets createdDate, later releases set updatedDate. Other builds keep both.
    param(
        [AllowNull()][string]$CreatedDate,
        [AllowNull()][string]$UpdatedDate,
        [switch]$Release,
        [string]$Today = (Get-BuildToday)
    )

    # [string] parameters turn $null into '', so work on untyped copies.
    $created = $null
    $updated = $null
    if (-not [string]::IsNullOrEmpty($CreatedDate)) { $created = $CreatedDate }
    if (-not [string]::IsNullOrEmpty($UpdatedDate)) { $updated = $UpdatedDate }

    if ($Release) {
        if ($Today -notmatch $script:DatePattern) { throw "Invalid date '$Today' (expected yyyy-MM-dd)." }
        if ($null -eq $created) { $created = $Today } else { $updated = $Today }
    }
    return [pscustomobject]@{ CreatedDate = $created; UpdatedDate = $updated }
}

function ConvertTo-VersionFileDate([object]$Value, [string]$Name) {
    if ($null -eq $Value) { return $null }
    # PowerShell 7's JSON reader may turn date strings into DateTime values.
    if ($Value -is [datetime]) { return $Value.ToString('yyyy-MM-dd', $script:Invariant) }
    $text = [string]$Value
    if ($text -eq '') { return $null }
    if ($text -notmatch $script:DatePattern) { throw "version.json: '$Name' must be null or a yyyy-MM-dd date, found '$text'." }
    return $text
}

function Read-VersionFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not [System.IO.File]::Exists($Path)) { throw "version.json not found: $Path" }
    $raw = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    try {
        $json = $raw | ConvertFrom-Json
    } catch {
        throw "version.json is not valid JSON: $($_.Exception.Message)"
    }
    if ($null -eq $json -or $null -eq $json.PSObject.Properties['version']) {
        throw "version.json has no 'version' property: $Path"
    }

    $created = $null
    $updated = $null
    if ($null -ne $json.PSObject.Properties['createdDate']) { $created = ConvertTo-VersionFileDate $json.createdDate 'createdDate' }
    if ($null -ne $json.PSObject.Properties['updatedDate']) { $updated = ConvertTo-VersionFileDate $json.updatedDate 'updatedDate' }

    $newLine = "`n"
    if ($raw.Contains("`r`n")) { $newLine = "`r`n" }

    return [pscustomobject]@{
        Version     = [string]$json.version
        CreatedDate = $created
        UpdatedDate = $updated
        NewLine     = $newLine
    }
}

function Format-VersionJson {
    # Hand-written instead of ConvertTo-Json: 5.1 uses 4-space indentation and escapes characters differently.
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [AllowNull()][string]$CreatedDate,
        [AllowNull()][string]$UpdatedDate,
        [string]$NewLine = "`n"
    )

    if ($Version -notmatch $script:FullVersionPattern) { throw "Invalid version '$Version' (expected MAJOR.MINOR.PATCH)." }
    $values = @()
    foreach ($date in @($CreatedDate, $UpdatedDate)) {
        if ([string]::IsNullOrEmpty($date)) {
            $values += 'null'
        } elseif ($date -match $script:DatePattern) {
            $values += '"' + $date + '"'
        } else {
            throw "Invalid date '$date' (expected yyyy-MM-dd)."
        }
    }

    $lines = @(
        '{',
        ('  "version": "' + $Version + '",'),
        ('  "createdDate": ' + $values[0] + ','),
        ('  "updatedDate": ' + $values[1]),
        '}'
    )
    return ($lines -join $NewLine) + $NewLine
}

function Write-VersionFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Version,
        [AllowNull()][string]$CreatedDate,
        [AllowNull()][string]$UpdatedDate,
        [string]$NewLine = "`n"
    )

    $text = Format-VersionJson -Version $Version -CreatedDate $CreatedDate -UpdatedDate $UpdatedDate -NewLine $NewLine
    $utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
    $temp = $Path + '.tmp'
    [System.IO.File]::WriteAllText($temp, $text, $utf8NoBom)
    if ([System.IO.File]::Exists($Path)) {
        [System.IO.File]::Replace($temp, $Path, [NullString]::Value)
    } else {
        [System.IO.File]::Move($temp, $Path)
    }
}

function Format-FileSize {
    param([long]$Bytes)

    if ($Bytes -ge 1MB) { return ('{0:N1} MB' -f ($Bytes / 1MB)) }
    if ($Bytes -ge 1KB) { return ('{0:N1} KB' -f ($Bytes / 1KB)) }
    return "$Bytes B"
}

# Older builds of one kind in dist/ that a successful build replaces. Only exact build names match
# (UsbipdManager-<x.y.z>-portable.exe / UsbipdManager-Setup-<x.y.z>.exe), so other files are never touched.
function Get-StaleBuildArtifacts {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][ValidateSet('Portable', 'Setup')][string]$Kind,
        [Parameter(Mandatory)][string]$KeepVersion
    )

    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { return @() }
    $pattern = if ($Kind -eq 'Portable') { '^UsbipdManager-(\d+\.\d+\.\d+)-portable\.exe$' } else { '^UsbipdManager-Setup-(\d+\.\d+\.\d+)\.exe$' }
    $stale = Get-ChildItem -LiteralPath $Directory -File |
        Where-Object { $_.Name -match $pattern -and $Matches[1] -ne $KeepVersion } |
        Sort-Object Name
    return @($stale)
}

Export-ModuleMember -Function Get-BuildUsage, ConvertFrom-BuildArguments, ConvertTo-BuildVersion, Compare-BuildVersion,
    Get-NextPatchVersion, Resolve-BuildVersion, Get-BuildToday, Resolve-BuildDates, Read-VersionFile,
    Format-VersionJson, Write-VersionFile, Format-FileSize, Get-StaleBuildArtifacts

[CmdletBinding()]
param(
    [Parameter(Position = 0)][AllowNull()][string]$Spec,
    [Alias('sv')][ValidatePattern('^\d+\.\d+\.\d+$')][string]$SetVersion,
    [switch]$Major,
    [switch]$Minor,
    [switch]$Patch
)

. "$PSScriptRoot\scriptHelper.ps1"

$in = (($Spec ?? '').Trim())
if ($in -match '^-(\+{1,3}|\d+\.\d+\.\d+)$') { $in = $Matches[1] }

$swN = [int]$Major.IsPresent + [int]$Minor.IsPresent + [int]$Patch.IsPresent
if ($swN -gt 1) { throw "Use only one of -Major, -Minor, -Patch." }

$hasSw = $swN -eq 1
$hasSpec = $in.Length -gt 0
$hasSet = -not [string]::IsNullOrEmpty($SetVersion)
if (@(($hasSet, $hasSw, $hasSpec) | Where-Object { $_ }).Count -gt 1) {
    throw "Pick one: -SetVersion, -Major/-Minor/-Patch, or + / ++ / +++ / a.b.c."
}
if ($hasSpec -and $in -notmatch '^(\+{1,3}|\d+\.\d+\.\d+)$') {
    throw "Invalid spec. Use +, ++, +++, a.b.c, or -SetVersion."
}

if ($hasSet -or $hasSw -or $hasSpec) {
    $raw = [System.IO.File]::ReadAllText($version).Trim()
    $p = @($raw -split '\.')
    if ($p.Count -ne 3) { throw "Version must be major.minor.patch (got '$raw')." }
    foreach ($x in $p) {
        if ($x -notmatch '^\d+$') { throw "Invalid version: $raw" }
    }
    [int]$ma = $p[0]; [int]$mi = $p[1]; [int]$pa = $p[2]

    if ($hasSet) {
        $v = $SetVersion -split '\.'
        $ma = [int]$v[0]; $mi = [int]$v[1]; $pa = [int]$v[2]
    }
    elseif ($hasSw) {
        if ($Major) { $ma++; $mi = 0; $pa = 0 }
        elseif ($Minor) { $mi++; $pa = 0 }
        else { $pa++ }
    }
    else {
        switch -Regex ($in) {
            '^\+$' { $ma++; $mi = 0; $pa = 0; break }
            '^\+\+$' { $mi++; $pa = 0; break }
            '^\+\+\+$' { $pa++; break }
            default {
                $v = $in -split '\.'
                $ma = [int]$v[0]; $mi = [int]$v[1]; $pa = [int]$v[2]
            }
        }
    }
    Write-RepoUtf8NoBomFile -LiteralPath $version -Content "$ma.$mi.$pa"
}

Write-Host (& "$root\.resources\exe\yapCli.exe" | Tee-Object -FilePath $versionTag)

$versionContents = [System.IO.File]::ReadAllText($version).Trim()
$versionTagContents = [System.IO.File]::ReadAllText($versionTag).Trim()
$firstLine = "v$versionContents release ($versionTagContents)"

$existingTail = @()
if (Test-Path -LiteralPath $buildNotes) {
    $prev = [System.IO.File]::ReadAllLines($buildNotes)
    if ($prev.Length -gt 1) { $existingTail = $prev[1..($prev.Length - 1)] }
}
$out = ((@($firstLine) + @($existingTail)) -join [Environment]::NewLine) + [Environment]::NewLine
Write-RepoUtf8NoBomFile -LiteralPath $buildNotes -Content $out

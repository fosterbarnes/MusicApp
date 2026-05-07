$root = (Resolve-Path "$PSScriptRoot\..\..\..").Path
. "$root\.scripts\scriptHelper.ps1"

$hugoToml = "$webMainPage\hugo.toml"
if (-not (Test-Path -LiteralPath $hugoToml)) {
    throw "hugo.toml not found: $hugoToml"
}

$tag = "v$($versionContents.Trim())"
$seg = Get-ReleaseTagSegment -Tag $versionTagContents
$base = "https://github.com/fosterbarnes/musicApp/releases/download/$tag/musicApp-$tag-$seg"
$urls = @{
    x64      = "$base-x64-installer.exe"
    x86      = "$base-x86-installer.exe"
    arm64    = "$base-arm64-installer.exe"
    portable = "$base-portable.zip"
}

$rx = '^\s*(x64|x86|arm64|portable)\s*=\s*''[^'']*''\s*$'
$lines = Get-Content -LiteralPath $hugoToml -Encoding UTF8
$out = foreach ($line in $lines) {
    if ($line -match $rx) { "$($Matches[1]) = '$($urls[$Matches[1]])'" }
    else { $line }
}
Write-Host "`nSyncing version with website..." -ForegroundColor Yellow
Write-RepoUtf8NoBomFile -LiteralPath $hugoToml -Content (($out -join "`n") + "`n")


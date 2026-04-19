$Host.UI.RawUI.WindowTitle = "Draft musicApp Release"
. "$PSScriptRoot\scriptHelper.ps1"; Set-Location $root
$portableRelease = "$appRoot\bin\portable\Release\net8.0-windows"
$portableZip = Join-Path $env:TEMP "musicApp_${versionContents}"
if (Test-Path $portableZip) { Remove-Item $portableZip -Recurse -Force }
Copy-Item -Path $portableRelease -Destination $portableZip -Recurse
Write-Host "Portable Release output (copied to temp for zip): $portableZip"
Write-Host "Version: $versionContents"
Write-Host "`nEnter release notes:" -ForegroundColor Yellow
Write-Host "Tabs will be converted to spaces for GitHub formatting." -ForegroundColor Cyan
$releaseNotesLines = @()
$consecutiveEmptyLines = 0
$hasReleaseNotes = $false

while ($true) {
    $line = Read-Host ">"
    if ($line -eq "") {
        $consecutiveEmptyLines++
        if ($consecutiveEmptyLines -ge 2) { break }
        $releaseNotesLines += ""
    } else {
        $line = $line -replace "`t", "    "
        $releaseNotesLines += $line
        $consecutiveEmptyLines = 0
        $hasReleaseNotes = $true
    }
}

if (-not $hasReleaseNotes) {
    Write-Host "Error: No release notes entered." -ForegroundColor Red
    exit 1
}

$releaseNotes = $releaseNotesLines -join "`n"
$releaseTagSegment = Get-ReleaseTagSegment -Tag $versionTagContents
Write-Host "VersionTag raw: '$versionTagContents' => segment: '$releaseTagSegment'" -ForegroundColor Cyan

$v = $versionContents
$finalPortable = Join-Path $env:TEMP "musicApp-v${v}-${releaseTagSegment}-portable.zip"
$finalX64 = Join-Path $env:TEMP "musicApp-v${v}-${releaseTagSegment}-x64-installer.exe"
$finalX86 = Join-Path $env:TEMP "musicApp-v${v}-${releaseTagSegment}-x86-installer.exe"
$finalArm = Join-Path $env:TEMP "musicApp-v${v}-${releaseTagSegment}-arm64-installer.exe"
$tagName = "v$v"
$releaseName = "musicApp v$v $versionTagContents"

foreach ($p in @($finalPortable, $finalX64, $finalX86, $finalArm)) {
    if (Test-Path $p) { Remove-Item $p -Force -ErrorAction SilentlyContinue }
}

& 7z a -tzip -mx=5 "$finalPortable" "$portableZip\*"
Copy-Item -Path "$root\.installer\Output\musicApp-x64-installer.exe" -Destination $finalX64 -Force
Copy-Item -Path "$root\.installer\Output\musicApp-x86-installer.exe" -Destination $finalX86 -Force
Copy-Item -Path "$root\.installer\Output\musicApp-arm64-installer.exe" -Destination $finalArm -Force

if (git tag -l $tagName) {
    Write-Host "Local tag $tagName exists. Deleting..."
    git tag -d $tagName
}

$remoteTags = git ls-remote --tags origin | ForEach-Object { ($_ -split "`t")[1] }
if ($remoteTags -contains "refs/tags/$tagName") {
    Write-Host "Remote tag $tagName exists. Deleting..."
    git push origin --delete $tagName
}

git tag $tagName && git push origin $tagName
& gh release create $tagName "$finalPortable" "$finalX64" "$finalX86" "$finalArm" --title "$releaseName" --notes "$releaseNotes" --prerelease

Remove-Item -Path $finalPortable, $finalX64, $finalX86, $finalArm, $portableZip -Recurse -Force -ErrorAction SilentlyContinue

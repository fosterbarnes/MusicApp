$Host.UI.RawUI.WindowTitle = "Draft musicApp Release"
. $PROFILE; Center-PSWindow

$basePath = $PSScriptRoot
$sourceBuild = Join-Path $basePath "bin\Release\net8.0-windows"

if (-not (Test-Path $sourceBuild)) {
    Write-Host "Error: Build folder not found: $sourceBuild" -ForegroundColor Red
    Write-Host "Build the project in Release configuration first." -ForegroundColor Red
    exit 1
}

# Read version from Version file (first line)
$versionFile = Join-Path $basePath "Version"
if (-not (Test-Path $versionFile)) {
    Write-Host "Error: Version file not found: $versionFile" -ForegroundColor Red
    exit 1
}
$version = (Get-Content $versionFile -First 1).Trim()
if (-not ($version -match "^[0-9]+\.[0-9]+\.[0-9]+$")) {
    Write-Host "Error: Invalid version in Version file: $version" -ForegroundColor Red
    exit 1
}

# Read version tag from VersionTag file (first line)
$versionTagFile = Join-Path $basePath "VersionTag"
if (-not (Test-Path $versionTagFile)) {
    Write-Host "Error: VersionTag file not found: $versionTagFile" -ForegroundColor Red
    exit 1
}
$versionTag = (Get-Content $versionTagFile -First 1).Trim()

# Copy build to temp and use that for release (so we can rename and zip without touching the live build)
$buildFolder = Join-Path $env:TEMP "MusicApp_${version}"
if (Test-Path $buildFolder) { Remove-Item $buildFolder -Recurse -Force }
Copy-Item -Path $sourceBuild -Destination $buildFolder -Recurse
Write-Host "Using build folder (copied to temp): $buildFolder"
Write-Host "Version: $version"

# Prompt for release notes (paste bullet list, then press Enter twice to finish)
Write-Host "`nEnter release notes (paste your bullet list, then press Enter twice to finish):" -ForegroundColor Yellow
Write-Host "Tabs will be converted to spaces for GitHub formatting." -ForegroundColor Cyan

$releaseNotesLines = @()
$consecutiveEmptyLines = 0
$hasEnteredContent = $false

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
        $hasEnteredContent = $true
    }
}

if (-not $hasEnteredContent) {
    Write-Host "Error: No release notes entered." -ForegroundColor Red
    exit 1
}
$releaseNotes = $releaseNotesLines -join "`n"

# Construct paths and release details
$zipPath = "$env:TEMP\musicApp_v${version}_${versionTag}.zip"
$tagName = "v$version"   # git tag = "v" + Version file (e.g. v0.0.14)
$releaseName = "musicApp v$version $versionTag"

# Compress the build folder using 7-Zip (moderate compression)
& 7z a -tzip -mx=5 "$zipPath" "$buildFolder\*"

# Change to repo root (parent of script directory)
$repoRoot = (Get-Item $basePath).Parent.FullName
Set-Location $repoRoot

# Check and delete existing tags if they exist (local and remote)
if (git tag -l $tagName) {
    Write-Host "Local tag $tagName exists. Deleting..."
    git tag -d $tagName
}

$remoteTags = git ls-remote --tags origin | ForEach-Object { ($_ -split "`t")[1] }
if ($remoteTags -contains "refs/tags/$tagName") {
    Write-Host "Remote tag $tagName exists. Deleting..."
    git push origin --delete $tagName
}

# Create and push the new tag
git tag $tagName
git push origin $tagName

# Create the GitHub release and upload the zip
& gh release create $tagName "$zipPath" --title "$releaseName" --notes "$releaseNotes" --prerelease

# Clean up temporary zip and copied build folder
Remove-Item -Path $zipPath, $buildFolder -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Temporary files cleaned up."

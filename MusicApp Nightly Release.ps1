$Host.UI.RawUI.WindowTitle = "MusicApp Nightly Release"
. $PROFILE; Center-PSWindow

# Use script directory as base path
$basePath = if ($PSScriptRoot) { $PSScriptRoot } else { Get-Location }
Write-Host "Base path: $basePath" -ForegroundColor Cyan

# Find all version folders
$buildFolders = Get-ChildItem -Path $basePath -Directory | Where-Object { $_.Name -match "^\d+\.\d+\.\d+$" }
if (-not $buildFolders) {
    Write-Host "Error: No build folders found in $basePath" -ForegroundColor Red
    exit 1
}

# Get newest version folder
$newestFolder = $buildFolders | ForEach-Object {
    $parts = $_.Name -split "\."
    [PSCustomObject]@{
        Path = $_.FullName
        Name = $_.Name
        Major = [int]$parts[0]
        Minor = [int]$parts[1]
        Patch = [int]$parts[2]
    }
} | Sort-Object Major, Minor, Patch -Descending | Select-Object -First 1

# Prompt for build selection
Write-Host "Newest build: $($newestFolder.Name)"
$userInput = Read-Host "Press Enter to use newest, or enter version/path"

# Determine build folder
$buildFolder = if ([string]::IsNullOrWhiteSpace($userInput)) {
    $newestFolder.Path
} elseif ($userInput -match "^\d+\.\d+\.\d+$") {
    "$basePath\$userInput"
} elseif (Test-Path $userInput) {
    $userInput
} else {
    "$basePath\$userInput"
}

if (-not (Test-Path $buildFolder)) {
    Write-Host "Error: Build folder not found: $buildFolder" -ForegroundColor Red
    exit 1
}

# Extract and validate version
$version = Split-Path $buildFolder -Leaf
if ($version -notmatch "^\d+\.\d+\.\d+$") {
    Write-Host "Error: Invalid version format: $version" -ForegroundColor Red
    exit 1
}

Write-Host "Using build: $version" -ForegroundColor Green

# Handle release notes
$releaseNotesPath = "$buildFolder\releaseNotes.txt"
$releaseNotes = $null

if (Test-Path $releaseNotesPath) {
    Write-Host "Loading release notes from file..." -ForegroundColor Green
    $releaseNotes = (Get-Content -Path $releaseNotesPath -Raw -Encoding UTF8) -replace "`t", "    "
}

if (-not $releaseNotes) {
    Write-Host "`nEnter release notes (press Enter twice to finish):" -ForegroundColor Yellow
    $lines = @()
    $emptyCount = 0
    
    while ($true) {
        $line = Read-Host ">"
        if ($line -eq "") {
            if (++$emptyCount -ge 2) { break }
            $lines += ""
        } else {
            $lines += $line -replace "`t", "    "
            $emptyCount = 0
        }
    }
    
    $releaseNotes = if ($lines.Count -eq 0) {
        "Nightly build release for version $version."
    } else {
        $lines -join "`n"
    }
}

# Create release packages
$tagName = "v$version"
$zipPath = "$env:TEMP\MusicApp_${version}_Build.zip"
$binZipPath = "$env:TEMP\MusicApp_${version}_Release.zip"

Write-Host "`nCreating release packages..." -ForegroundColor Cyan
& 7z a -tzip -mx=5 "$zipPath" "$buildFolder\*" | Out-Null
& 7z a -tzip -mx=5 "$binZipPath" "$buildFolder\bin\Debug\net8.0-windows\*" | Out-Null

# Manage Git tags
Set-Location $basePath

if (git tag -l $tagName) {
    Write-Host "Deleting existing local tag: $tagName" -ForegroundColor Yellow
    git tag -d $tagName | Out-Null
}

$remoteTags = git ls-remote --tags origin | ForEach-Object { ($_ -split "`t")[1] }
if ($remoteTags -contains "refs/tags/$tagName") {
    Write-Host "Deleting existing remote tag: $tagName" -ForegroundColor Yellow
    git push origin --delete $tagName | Out-Null
}

Write-Host "Creating and pushing tag: $tagName" -ForegroundColor Cyan
git tag $tagName
git push origin $tagName

# Create GitHub release
Write-Host "Creating GitHub release..." -ForegroundColor Cyan
& gh release create $tagName "$zipPath" "$binZipPath" --title "$version Nightly Release" --notes "$releaseNotes"

# Cleanup
Remove-Item -Path $zipPath, $binZipPath -ErrorAction SilentlyContinue
Write-Host "`nRelease complete!" -ForegroundColor Green
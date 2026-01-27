param(
    [Parameter(Mandatory=$false, Position=0)]
    [string]$Date
)

$Host.UI.RawUI.WindowTitle = "MusicApp Nightly Release"

if ($Date) {
    if ($Date -match '^\d+\.\d+\.\d+$') {
        $currentDate = $Date
        Write-Host "Using custom date: $currentDate" -ForegroundColor Cyan
    } else {
        Write-Host "Error: Invalid date format. Please use format M.d.yy (e.g., '1.27.26')." -ForegroundColor Red
        exit 1
    }
} else {
    $currentDate = Get-Date -Format "M.d.yy"
}

$repoRoot = $PSScriptRoot
$buildPath = "$repoRoot\MusicApp\bin\Release\net8.0-windows"
$zipFileName = "MusicApp_$currentDate.zip"
$zipPath = "$env:TEMP\$zipFileName"
$releaseName = "$currentDate Nightly Release"
$repoUrl = "https://github.com/fosterbarnes/MusicApp"

if (-not (Test-Path $buildPath)) {
    Write-Host "Error: Build directory not found: $buildPath" -ForegroundColor Red
    Write-Host "Please build the project first." -ForegroundColor Red
    exit 1
}

Set-Location $repoRoot

Write-Host "`nCreating zip file: $zipFileName" -ForegroundColor Cyan
Write-Host "Source: $buildPath" -ForegroundColor Gray

try {
    & 7z a -tzip -mx=5 "$zipPath" "$buildPath\*" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "7zip exited with code $LASTEXITCODE"
    }
    Write-Host "Zip file created successfully: $zipPath" -ForegroundColor Green
} catch {
    Write-Host "Error creating zip file: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "`nGetting most recent commit message..." -ForegroundColor Cyan
$commitMessageFull = git log -1 --pretty=%B
$commitBody = $commitMessageFull | Select-Object -Skip 1 # Skip the first line and use only the extended description
$releaseDescription = $commitBody -join "`n"
Write-Host "Commit message retrieved successfully." -ForegroundColor Green

Write-Host "`n" -NoNewline
Write-Host "Release Preview:" -ForegroundColor Cyan
Write-Host "`n$releaseName" -ForegroundColor Yellow
Write-Host "$releaseDescription" -ForegroundColor White
Write-Host "`nPress any key to create release..." -ForegroundColor Green
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

Write-Host "`nCreating release on GitHub..." -ForegroundColor Cyan

try {
    & gh release create "v$currentDate" "$zipPath" `
        --repo "fosterbarnes/MusicApp" `
        --title "$releaseName" `
        --notes "$releaseDescription" `
        --prerelease
    
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI exited with code $LASTEXITCODE"
    }
    
    Write-Host "`nRelease created successfully as pre-release!" -ForegroundColor Green
    Write-Host "Release URL: $repoUrl/releases/tag/v$currentDate" -ForegroundColor Cyan
    
} catch {
    Write-Host "Error creating GitHub release: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Cleaning up zip file..." -ForegroundColor Yellow
    Remove-Item -Path $zipPath -ErrorAction SilentlyContinue
    exit 1
}

Write-Host "`nCleaning up temporary zip file..." -ForegroundColor Gray
Remove-Item -Path $zipPath -ErrorAction SilentlyContinue

Write-Host "`nDone! Release created successfully." -ForegroundColor Green
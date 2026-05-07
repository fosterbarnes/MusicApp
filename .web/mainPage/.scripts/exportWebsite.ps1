. (Resolve-Path "$PSScriptRoot\..\..\..\.scripts\scriptHelper.ps1"); $root = (Resolve-Path "$PSScriptRoot\..\..\..")
Set-Location $webMainPage

Write-Host "`nExporting website to repo root..." -ForegroundColor Yellow
hugo --cleanDestinationDir --gc; Write-Host "Cleaned and built new site."
Remove-Item "$root\musicApp.info" -Recurse -Force; Write-Host "Cleared existing site from repo root."
Copy-Item "$webMainPage\public" "$root\musicApp.info" -Recurse; Write-Host "Exported site to $root\musicApp.info"
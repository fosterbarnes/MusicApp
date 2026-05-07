. "$PSScriptRoot\scriptHelper.ps1"; Write-Host "Running pre-push tasks..." -ForegroundColor Yellow
& $scripts\build.ps1
& $scripts\taskCounter.ps1
& $scripts\writeFeatures.ps1
& $scripts\updateReleaseLink.ps1
& $scripts\countCode.ps1
& $webScripts\versionSync.ps1
& $webScripts\exportWebsite.ps1
Write-Host "`nPre-push tasks completed." -ForegroundColor Green
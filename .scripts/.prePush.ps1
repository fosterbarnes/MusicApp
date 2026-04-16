Write-Host "Running pre-push tasks..." -ForegroundColor Yellow
& $PSScriptRoot\build.ps1
& $PSScriptRoot\taskCounter.ps1
& $PSScriptRoot\writeFeatures.ps1
& $PSScriptRoot\updateReleaseLink.ps1
& $PSScriptRoot\countCode.ps1
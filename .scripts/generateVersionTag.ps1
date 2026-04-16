. "$PSScriptRoot\scriptHelper.ps1"
Write-Host (& "$root\.resources\exe\yapCli.exe" | Tee-Object -FilePath $versionTag)
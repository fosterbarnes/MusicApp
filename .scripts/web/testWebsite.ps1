. (Join-Path (Split-Path -Parent $PSScriptRoot) 'scriptHelper.ps1')
Set-Location $webMainPage
hugo serve -O
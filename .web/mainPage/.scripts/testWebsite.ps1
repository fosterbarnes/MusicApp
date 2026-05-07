. (Resolve-Path "$PSScriptRoot\..\..\..\.scripts\scriptHelper.ps1"); $root = (Resolve-Path "$PSScriptRoot\..\..\..")
cd "$webMainPage"; hugo serve -O
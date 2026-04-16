. "$PSScriptRoot\scriptHelper.ps1"; Set-Location $appRoot
foreach ($platform in 'x64', 'x86', 'arm64', 'portable') {
    Write-Host "Cleaning & building $platform app..." -ForegroundColor Yellow
    Set-VersionBuildPlatform $platform
    dotnet clean $musicAppCsproj -c Release -p:Platform="$platform" --tl:off
    dotnet build $musicAppCsproj -c Release -p:Platform="$platform" --tl:off
    Write-Host ""
}

& "$PSScriptRoot\buildUpdater.ps1"
& "$PSScriptRoot\buildInstaller.ps1"
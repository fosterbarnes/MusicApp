. "$PSScriptRoot\scriptHelper.ps1"; Set-Location $appRoot
Write-Host "Cleaning old installers..." -ForegroundColor Yellow
Remove-Item -Path "$appRoot\.installer\Output\*" -Recurse -Force
$DMyAppVersion = "/DMyAppVersion=$versionContents"
$DMyAppVersionTag = "/DMyAppVersionTag=$versionTagContents"

foreach ($platform in 'x64', 'x86', 'arm64', 'portable') {
    Write-Host "Building $platform installer..." -ForegroundColor Yellow
    Set-VersionBuildPlatform $platform
    Write-Host "Wrote VersionBuild -> $platform ($versionBuild)" -ForegroundColor DarkGray
    & ISCC.exe $DMyAppVersion $DMyAppVersionTag "$appRoot\.installer\musicApp.$platform.installer.iss"
    Write-Host ""
}

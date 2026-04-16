. "$PSScriptRoot\scriptHelper.ps1"; Set-Location $root
$tagName = "v$versionContents"
$zipFileName = "musicApp-v${versionContents}-${versionTagContents}-portable.zip"
$releaseUrl = "https://github.com/fosterbarnes/musicApp/releases/download/$tagName/$zipFileName"
$oldLinkRegex = '\[download the latest release\]\(https://github\.com/' + [regex]::Escape('fosterbarnes/musicApp') + '/releases/download/v[^)]+\)'

$text = Get-Content -LiteralPath $readme -Raw -Encoding UTF8
$updated = [regex]::Replace($text, $oldLinkRegex, "[download the latest release]($releaseUrl)").TrimEnd()
Write-Host "`nUpdating release link in README..." -ForegroundColor Yellow; Write-Host $releaseUrl
Write-RepoUtf8NoBomFile -LiteralPath $readme -Content $updated
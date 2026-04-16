# Regenerates Features.md from Tasks.md and syncs README General Usage section.
. "$PSScriptRoot\scriptHelper.ps1"; Set-Location $root

$newLine = [Environment]::NewLine
$taskLines = Trim-TrailingBlankLines (Normalize-MarkdownLineArray -Lines $tasksRaw)
$featureLines = Trim-TrailingBlankLines (Normalize-MarkdownLineArray -Lines $featuresRaw)
$renderedLines = Trim-TrailingBlankLines (Merge-FeaturesFromTasks -TaskLines $taskLines -FeatureLines $featureLines)

if (-not ($featureLines -match '^\s*##\s+Settings Menu\s*$')) {
    $settingsLines = @(Get-MarkdownTopLevelSectionLines -Lines $taskLines -SectionTitle 'Settings Menu')
    if ($settingsLines.Count -gt 0) {
        $renderedLines = Trim-TrailingBlankLines @($renderedLines; ''; $settingsLines)
    }
}

$newFeatureText = (($renderedLines -join $newLine) -replace '~~', '')

Write-Host "`nUpdating Features.md..." -ForegroundColor Yellow
Write-RepoUtf8NoBomFile -LiteralPath $features -Content ($newFeatureText + $newLine)

$featuresUpdatedLines = @(Normalize-MarkdownLineArray -Lines $newFeatureText)
$readmeLines = @(Normalize-MarkdownLineArray -Lines $readmeRaw)
$newReadmeLines = Trim-TrailingBlankLines (Sync-ReadmeGeneralUsageFromFeatures -ReadmeLines $readmeLines -FeaturesUpdatedLines $featuresUpdatedLines)

Write-Host "`nUpdating README.md..." -ForegroundColor Yellow
$readmeBody = if ($newReadmeLines.Count -eq 0) { '' } else { ($newReadmeLines -join $newLine) + $newLine }
Write-RepoUtf8NoBomFile -LiteralPath $readme -Content $readmeBody

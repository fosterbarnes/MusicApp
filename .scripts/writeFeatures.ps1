# Regenerates Features.md from Tasks.md (README is not modified).
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

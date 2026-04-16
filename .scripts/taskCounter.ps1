. "$PSScriptRoot\scriptHelper.ps1"; Set-Location $root
Write-Host "`nCounting tasks..." -ForegroundColor Yellow
$lines = @(Get-Content -LiteralPath "$root\.md\Tasks.md" -Encoding UTF8)
$todoOut = [System.Collections.ArrayList]::new()
[void]$todoOut.Add('# ToDo')
[void]$todoOut.Add('')
$total = $completed = $notDone = 0
foreach ($line in $lines) {
    $ts = $line.TrimStart()
    if ($ts.StartsWith('#')) { [void]$todoOut.Add($line); continue }
    if ($line.Trim().Length -eq 0) {
        if ($todoOut.Count -ge 2) {
            $l = [string]$todoOut[$todoOut.Count - 1]
            if ($l -ne '' -and ($l.TrimStart().StartsWith('#') -or $l -match '\S')) { [void]$todoOut.Add('') }
        }
        continue
    }
    if (-not ($ts -match '\S')) { continue }
    $total++
    if ($line.Contains('~')) { $completed++; continue }
    [void]$todoOut.Add($line)
    $notDone++
}

$pct = if ($total -eq 0) { 0 } else { [math]::Round(100 * $completed / $total, 1) }
Write-Host "$completed/$total tasks completed ($pct%)"
Write-Host "`nUpdating ToDo.md..." -ForegroundColor Yellow

if ($notDone -eq 0) { $todoOut = @('# ToDo', '', '_No undone tasks._') }

$nl = [Environment]::NewLine
Write-RepoUtf8NoBomFile -LiteralPath "$root\.md\ToDo.md" -Content ((@($todoOut) -join $nl) + $nl)
Write-Host "`nUpdating README.md..." -ForegroundColor Yellow

$newReadme = @(Get-Content -LiteralPath $readme -Encoding UTF8)
$pctInt = [int][math]::Round($pct)
for ($i = 0; $i -lt $newReadme.Count; $i++) {
    if ($newReadme[$i] -match 'progress-bars\.entcheneric\.com.*progress=\d+') { $newReadme[$i] = $newReadme[$i] -replace 'progress=\d+', "progress=$pctInt" }
    if ($newReadme[$i] -match '^\*\*\d+ / \d+ tasks complete \(\d+.*%\)\*\*') { $newReadme[$i] = "**$completed / $total tasks complete ($pct%)**" }
}
while ($newReadme.Count -gt 0 -and $newReadme[$newReadme.Count - 1] -match '^\s*$') {
    if ($newReadme.Count -eq 1) { $newReadme = @(); break }
    $newReadme = $newReadme[0..($newReadme.Count - 2)]
}
Write-RepoUtf8NoBomFile -LiteralPath $readme -Content $(if ($newReadme.Count -eq 0) { '' } else { ($newReadme -join $nl) + $nl })
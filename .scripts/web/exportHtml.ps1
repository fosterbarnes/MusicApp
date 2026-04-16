. (Join-Path (Split-Path -Parent $PSScriptRoot) 'scriptHelper.ps1')
$ErrorActionPreference = 'Stop'

function Resolve-UrlDataUri {
    param([string]$FilePath)
    $bytes = [System.IO.File]::ReadAllBytes($FilePath)
    $b64 = [Convert]::ToBase64String($bytes)
    $ext = [System.IO.Path]::GetExtension($FilePath).ToLowerInvariant()
    $mime = switch ($ext) {
        '.woff2' { 'font/woff2' }
        '.woff' { 'font/woff' }
        '.ttf' { 'font/ttf' }
        '.svg' { 'image/svg+xml' }
        '.png' { 'image/png' }
        '.gif' { 'image/gif' }
        '.webp' { 'image/webp' }
        '.jpg' { 'image/jpeg' }
        '.jpeg' { 'image/jpeg' }
        default { 'application/octet-stream' }
    }
    "url(data:$mime;base64,$b64)"
}

function Expand-CssUrlsToDataUri {
    param([string]$CssText, [string]$CssFileDirectory, [string]$PublicDir)
    $sep = [IO.Path]::DirectorySeparatorChar
    $rxMatches = [regex]::Matches($CssText, 'url\(([^)]+)\)')
    $ops = @(foreach ($m in $rxMatches) {
        $inner = $m.Groups[1].Value.Trim().Trim('"').Trim("'")
        $q = $inner.IndexOf([char]'?')
        $pathPart = if ($q -ge 0) { $inner.Substring(0, $q) } else { $inner }
        if ($pathPart -match '^(https?:)?//' -or $pathPart.StartsWith('data:')) { continue }
        if (-not ($pathPart.StartsWith('.') -or $pathPart.StartsWith('/'))) { continue }
        $rel = $pathPart.TrimStart('/')
        $candidate = if ($pathPart.StartsWith('/')) {
            [System.IO.Path]::GetFullPath((Join-Path $PublicDir ($rel -replace '/', $sep)))
        }
        else {
            [System.IO.Path]::GetFullPath((Join-Path $CssFileDirectory ($pathPart -replace '/', $sep)))
        }
        if (-not (Test-Path -LiteralPath $candidate)) {
            Write-Warning "CSS url missing file: $candidate"
            continue
        }
        [pscustomobject]@{
            Index  = $m.Index
            Length = $m.Length
            NewVal = (Resolve-UrlDataUri -FilePath $candidate)
        }
    })
    foreach ($r in ($ops | Sort-Object -Property Index -Descending)) {
        $CssText = $CssText.Remove($r.Index, $r.Length).Insert($r.Index, $r.NewVal)
    }
    $CssText
}

function Inline-Stylesheets {
    param([string]$Html, [string]$PublicDir)
    $sep = [IO.Path]::DirectorySeparatorChar
    $patterns = @(
        '(?is)<link\s[^>]*href="(/[^"]+)"[^>]*rel="stylesheet"[^>]*>'
        '(?is)<link\s[^>]*rel="stylesheet"[^>]*href="(/[^"]+)"[^>]*>'
    )
    while ($true) {
        $m = $null
        foreach ($p in $patterns) {
            $m = [regex]::Match($Html, $p)
            if ($m.Success) { break }
        }
        if (-not $m.Success) { break }

        $href = $m.Groups[1].Value
        $relFs = $href.TrimStart('/') -replace '/', $sep
        $cssPath = Join-Path $PublicDir $relFs
        if (-not (Test-Path -LiteralPath $cssPath)) {
            throw "Stylesheet not found: $cssPath"
        }
        $cssDir = Split-Path -Parent $cssPath
        $css = [System.IO.File]::ReadAllText($cssPath)
        $css = Expand-CssUrlsToDataUri -CssText $css -CssFileDirectory $cssDir -PublicDir $PublicDir
        $block = "<style>`r`n$css`r`n</style>"
        $Html = $Html.Remove($m.Index, $m.Length).Insert($m.Index, $block)
    }
    $Html
}

function Inline-Scripts {
    param([string]$Html, [string]$PublicDir)
    $sep = [IO.Path]::DirectorySeparatorChar
    while ($true) {
        $m = [regex]::Match($Html, '(?is)<script[^>]*?src="(/[^"]+)"[^>]*>\s*</script>')
        if (-not $m.Success) { break }
        $relFs = $m.Groups[1].Value.TrimStart('/') -replace '/', $sep
        $jsPath = Join-Path $PublicDir $relFs
        if (-not (Test-Path -LiteralPath $jsPath)) {
            throw "Script not found: $jsPath"
        }
        $js = [System.IO.File]::ReadAllText($jsPath)
        $block = "<script>`r`n$js`r`n</script>"
        $Html = $Html.Remove($m.Index, $m.Length).Insert($m.Index, $block)
    }
    $Html
}

function Compress-ExportedHtml([string]$Text) {
    $t = [regex]::Replace($Text, '(?m)^[ \t]+\r?\n', '')
    $t = [regex]::Replace($t, '(?:\r?\n){2,}', "`n")
    $t.Trim()
}

$publicDir = Join-Path $webMainPage 'public'
$indexPath = Join-Path $publicDir 'index.html'
$distDir = Join-Path $webMainPage 'dist'
$outputPath = Join-Path $distDir 'musicApp.html'

Push-Location $webMainPage
try {
    & hugo --gc
    if ($LASTEXITCODE -ne 0) { throw "hugo exited with code $LASTEXITCODE" }
}
finally { Pop-Location }

if (-not (Test-Path -LiteralPath $indexPath)) {
    throw "Missing $indexPath after hugo."
}

$null = New-Item -ItemType Directory -Force -Path $distDir
$html = [System.IO.File]::ReadAllText($indexPath)
$html = [regex]::Replace($html, '(?is)<script[^>]*livereload\.js[^>]*></script>\s*', '')
$html = [regex]::Replace($html, '(?m)^\s*<link[^>]+rel="(?:alternate|manifest|(?:apple-touch-icon|shortcut icon|icon))"[^>]*>\s*\r?\n', '')
$html = [regex]::Replace($html, '(?is)<script type="application/ld\+json">.*?</script>\s*', '')
$html = Inline-Stylesheets -Html $html -PublicDir $publicDir
$html = Inline-Scripts -Html $html -PublicDir $publicDir
$html = Compress-ExportedHtml -Text $html

Write-RepoUtf8NoBomFile -LiteralPath $outputPath -Content $html
Write-Host "Wrote: $outputPath"
Copy-Item -Path $outputPath -Destination $root -Force
Write-Host "Copied to: $root\musicApp.html"
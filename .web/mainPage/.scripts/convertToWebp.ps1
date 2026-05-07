$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$src = Join-Path $repoRoot "musicApp\Resources\scr"
$dst = Join-Path $repoRoot "musicApp\Resources\web"
Set-Location $src
New-Item -ItemType Directory -Path $dst -Force | Out-Null
Get-ChildItem -Recurse -Filter *.png | ForEach-Object {
    cwebp -q 80 $_.FullName -o (Join-Path $_.DirectoryName "$($_.BaseName).webp")
}
Get-ChildItem -Filter *.webp | Move-Item -Destination $dst -Force

. (Join-Path (Split-Path -Parent $PSScriptRoot) 'scriptHelper.ps1')
$src = "$root\.resources\scr"
$dst = "$root\.resources\web"
Set-Location $src
New-Item -ItemType Directory -Path $dst -Force | Out-Null
Get-ChildItem -Recurse -Filter *.png | ForEach-Object {
    cwebp -q 80 $_.FullName -o (Join-Path $_.DirectoryName "$($_.BaseName).webp")
}
Get-ChildItem -Filter *.webp | Move-Item -Destination $dst -Force
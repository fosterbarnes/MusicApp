Write-Host "---Building musicApp for x86...---`n" -ForegroundColor Yellow
dotnet build musicApp.csproj -c Release -p:Platform=x86
Write-Host "`n---Building musicApp for x64...---`n" -ForegroundColor Yellow
dotnet build musicApp.csproj -c Release -p:Platform=x64
Write-Host "`n---Building musicApp for AnyCPU...---`n" -ForegroundColor Yellow
dotnet build musicApp.csproj -c Release -p:Platform=AnyCPU
Write-Host "`n---Building installer...---`n" -ForegroundColor Yellow
& (Join-Path $PSScriptRoot ".buildSetup.ps1")
Write-Host "`n---Done.---" -ForegroundColor Green
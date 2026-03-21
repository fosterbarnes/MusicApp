Set-Location (Split-Path -Parent $PSScriptRoot)
& ISCC.exe ".installer\musicApp.x64.installer.iss"
& ISCC.exe ".installer\musicApp.x86.installer.iss"
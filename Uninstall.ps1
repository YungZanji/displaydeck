$ErrorActionPreference = 'SilentlyContinue'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\DisplayDeck'
$DataDir = Join-Path $env:LOCALAPPDATA 'DisplayDeck'
Get-Process -Name 'DisplayDeck' | Stop-Process -Force
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'DisplayDeck'
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayDeck' -Recurse -Force
Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) 'DisplayDeck.lnk') -Force
Remove-Item (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\DisplayDeck') -Recurse -Force
Start-Sleep -Milliseconds 300
Remove-Item $InstallDir -Recurse -Force
Write-Host ''
Write-Host 'DisplayDeck was uninstalled.' -ForegroundColor Green
Write-Host "Your display profiles and settings were preserved at: $DataDir" -ForegroundColor DarkGray

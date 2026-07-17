#Requires -Version 5.1
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'NetieSpace' -ErrorAction SilentlyContinue
Write-Host "[OK] Removed Netie Space from Windows startup." -ForegroundColor Green

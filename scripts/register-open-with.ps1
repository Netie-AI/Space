#Requires -Version 5.1
<#
.SYNOPSIS
  Additive Explorer "Open with Netie Space" (HKCU). Does not steal Word/Excel/PDF defaults.
#>
param(
    [string]$ExePath = ""
)

$root = Split-Path $PSScriptRoot -Parent
if (-not $ExePath) {
    $ExePath = Join-Path $root 'dist\NetieSpace\NetieSpace.exe'
}

if (-not (Test-Path $ExePath)) {
    Write-Host "Exe not found: $ExePath" -ForegroundColor Red
    Write-Host "Run scripts\publish.ps1 first." -ForegroundColor Yellow
    exit 1
}

$ExePath = (Resolve-Path $ExePath).Path
$command = "`"$ExePath`" --preview `"%1`""
$icon = "`"$ExePath`",0"

# App Paths so Start / Run can find it
$appPaths = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\NetieSpace.exe'
New-Item -Path $appPaths -Force | Out-Null
Set-ItemProperty -Path $appPaths -Name '(default)' -Value $ExePath
Set-ItemProperty -Path $appPaths -Name 'Path' -Value (Split-Path $ExePath -Parent)

# Open-with list (does not change UserChoice defaults)
$appKey = 'HKCU:\Software\Classes\Applications\NetieSpace.exe'
New-Item -Path $appKey -Force | Out-Null
Set-ItemProperty -Path $appKey -Name 'FriendlyAppName' -Value 'Netie Space'
$openCmd = Join-Path $appKey 'shell\open\command'
New-Item -Path $openCmd -Force | Out-Null
Set-ItemProperty -Path $openCmd -Name '(default)' -Value $command

$supported = Join-Path $appKey 'SupportedTypes'
New-Item -Path $supported -Force | Out-Null
$exts = @(
    '.pdf', '.txt', '.text', '.log', '.json', '.xml', '.csv', '.md',
    '.docx', '.doc', '.odt', '.rtf', '.xlsx', '.xlsm', '.xls', '.ods',
    '.pptx', '.ppsx', '.ppt', '.pps', '.odp',
    '.png', '.jpg', '.jpeg', '.gif', '.bmp', '.webp', '.tif', '.tiff', '.ico', '.svg', '.heic', '.heif',
    '.mp4', '.webm', '.mkv', '.avi', '.mov', '.wmv', '.m4v',
    '.html', '.htm', '.css', '.js', '.ts', '.cs', '.py', '.go', '.rs', '.java',
    '.exe', '.dll', '.msi', '.zip', '.7z'
)
foreach ($ext in $exts) {
    New-ItemProperty -Path $supported -Name $ext -Value '' -PropertyType String -Force | Out-Null
}

# Right-click on any file (additive). Not the default handler.
$allFiles = 'HKCU:\Software\Classes\*\shell\NetieSpace'
New-Item -Path $allFiles -Force | Out-Null
Set-ItemProperty -Path $allFiles -Name '(default)' -Value 'Open with Netie Space'
Set-ItemProperty -Path $allFiles -Name 'Icon' -Value $icon
Set-ItemProperty -Path $allFiles -Name 'Position' -Value 'Top'
$allCmd = Join-Path $allFiles 'command'
New-Item -Path $allCmd -Force | Out-Null
Set-ItemProperty -Path $allCmd -Name '(default)' -Value $command

# Folders too (Space already previews a folder digest)
$dirKey = 'HKCU:\Software\Classes\Directory\shell\NetieSpace'
New-Item -Path $dirKey -Force | Out-Null
Set-ItemProperty -Path $dirKey -Name '(default)' -Value 'Open with Netie Space'
Set-ItemProperty -Path $dirKey -Name 'Icon' -Value $icon
$dirCmd = Join-Path $dirKey 'command'
New-Item -Path $dirCmd -Force | Out-Null
Set-ItemProperty -Path $dirCmd -Name '(default)' -Value $command

# Start Menu shortcut
$startDir = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs'
New-Item -ItemType Directory -Path $startDir -Force | Out-Null
$lnkPath = Join-Path $startDir 'Netie Space.lnk'
$wsh = New-Object -ComObject WScript.Shell
$lnk = $wsh.CreateShortcut($lnkPath)
$lnk.TargetPath = $ExePath
$lnk.WorkingDirectory = Split-Path $ExePath -Parent
$lnk.IconLocation = $icon
$lnk.Description = 'Netie Space - preview and edit files'
$lnk.Save()

# Netie PDF / Image / Video Start Menu shortcuts (AppUserModel.ID + pin relaunch). The exe does the
# COM work because WScript.Shell cannot write the AUMID. HKCU / Start Menu only, never UserChoice.
# A WinExe does not block the call operator, so wait on a started process to get its exit code.
$reg = Start-Process -FilePath $ExePath -ArgumentList '--register-apps' -WindowStyle Hidden -Wait -PassThru
if ($reg.ExitCode -ne 0) {
    throw "NetieSpace.exe --register-apps failed with exit code $($reg.ExitCode)"
}

Write-Host "[OK] Open with Netie Space (additive, HKCU). Defaults for Word/Excel/PDF were not changed." -ForegroundColor Green
Write-Host "     $ExePath" -ForegroundColor Gray
Write-Host "     Start Menu: $lnkPath" -ForegroundColor Gray
Write-Host "`nTo remove:" -ForegroundColor Gray
Write-Host "  Remove-Item HKCU:\Software\Classes\*\shell\NetieSpace -Recurse -Force" -ForegroundColor Gray
Write-Host "  Remove-Item HKCU:\Software\Classes\Directory\shell\NetieSpace -Recurse -Force" -ForegroundColor Gray
Write-Host "  Remove-Item HKCU:\Software\Classes\Applications\NetieSpace.exe -Recurse -Force" -ForegroundColor Gray

#Requires -Version 5.1
<#
.SYNOPSIS
  Proves the built setup exe: silent install, installed exe works, shortcuts correct,
  no UserChoice change, silent uninstall leaves nothing.
.PARAMETER Setup
  Path to NetieSpace-Setup.exe. Default dist\installer\NetieSpace-Setup.exe.
.PARAMETER OutDir
  Folder for logs, JSON and the snapshot PNG (uploaded as an artifact by CI).
.NOTES
  Prints OK / FAIL lines; exit 1 on any failure. Windows PowerShell 5.1 safe. ASCII only.
#>
param(
    [string]$Setup,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Setup) { $Setup = Join-Path $root 'dist\installer\NetieSpace-Setup.exe' }
if (-not $OutDir) { $OutDir = Join-Path $root 'dist\installer-check' }

$script:failed = 0
function Ok([string]$m)   { Write-Host "OK   $m" }
function Fail([string]$m) { Write-Host "FAIL $m"; $script:failed++ }
function Check([bool]$cond, [string]$m) { if ($cond) { Ok $m } else { Fail $m } }

Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
using System.Text;
public static class NetieLongPath {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetLongPathName(string s, StringBuilder b, uint n);
    public static string Get(string p) {
        var b = new StringBuilder(1024);
        uint r = GetLongPathName(p, b, 1024);
        return (r == 0 || r > 1024) ? p : b.ToString();
    }
}
'@

if (-not (Test-Path -LiteralPath $Setup)) { Write-Host "FAIL setup exe missing: $Setup"; exit 1 }
$Setup = (Resolve-Path -LiteralPath $Setup).Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

$tmpBase = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$installDir = [NetieLongPath]::Get((Join-Path $tmpBase ('netie-install-' + [guid]::NewGuid().ToString('N'))))
$exe = Join-Path $installDir 'NetieSpace.exe'
$programs = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs'
$lnkNames = @('Netie PDF.lnk', 'Netie Image.lnk', 'Netie Video.lnk')

function Get-UserChoice {
    $k = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\UserChoice'
    if (-not (Test-Path -LiteralPath $k)) { return 'absent' }
    $p = Get-ItemProperty -LiteralPath $k
    return ('ProgId={0};Hash={1}' -f $p.ProgId, $p.Hash)
}

function Run-Exe([string]$file, [string[]]$argList, [int]$timeoutSec = 180) {
    $p = Start-Process -FilePath $file -ArgumentList $argList -PassThru -WindowStyle Hidden
    $null = $p.Handle
    if (-not $p.WaitForExit($timeoutSec * 1000)) {
        try { $p.Kill() } catch { }
        return -999
    }
    return $p.ExitCode
}

$ucBefore = Get-UserChoice
Write-Host "UserChoice .pdf before: $ucBefore"

# 1. silent install
$installLog = Join-Path $OutDir 'install.log'
$code = Run-Exe $Setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$installDir`"", "/LOG=`"$installLog`"")
Check ($code -eq 0) "silent install exit code $code (want 0)"
Check (Test-Path -LiteralPath $exe) "installed exe exists: $exe"
Check (Test-Path -LiteralPath (Join-Path $installDir 'THIRD-PARTY-NOTICES.txt')) 'THIRD-PARTY-NOTICES.txt installed'
Check (Test-Path -LiteralPath (Join-Path $installDir 'Assets\apps\netie-pdf.ico')) 'app icons installed'

if (Test-Path -LiteralPath $exe) {
    # 2. --list-tools
    $listJson = Join-Path $OutDir 'list-tools.json'
    $code = Run-Exe $exe @('--list-tools', '--json', "`"$listJson`"")
    Check ($code -eq 0) "--list-tools exit $code"
    if (Test-Path -LiteralPath $listJson) {
        $doc = Get-Content -LiteralPath $listJson -Raw | ConvertFrom-Json
        $total = @($doc.tools).Count
        $avail = @($doc.tools | Where-Object { $_.available }).Count
        Check ($total -eq 91) "tool rows $total (want 91)"
        Check ($avail -ge 68) "tools available $avail (want >= 68)"
    } else { Fail 'list-tools.json not written' }

    # 3. one real tool
    $a = Join-Path $root 'tests\fixtures\preview\Q3_Report.pdf'
    $b = Join-Path $root 'tests\fixtures\preview\Contract_Lease.pdf'
    $toolOut = Join-Path $OutDir 'merge-out'
    New-Item -ItemType Directory -Force -Path $toolOut | Out-Null
    $toolJson = Join-Path $OutDir 'pdf-merge.json'
    $code = Run-Exe $exe @('--tool', 'pdf-merge', '--in', "`"$a`"", '--in', "`"$b`"", '--out', "`"$toolOut`"", '--json', "`"$toolJson`"")
    Check ($code -eq 0) "pdf-merge exit $code"
    if (Test-Path -LiteralPath $toolJson) {
        $r = Get-Content -LiteralPath $toolJson -Raw | ConvertFrom-Json
        Check ([bool]$r.ok) "pdf-merge ok=$($r.ok) error=$($r.error)"
        $outs = @($r.outputs)
        $good = $false
        if ($outs.Count -gt 0 -and (Test-Path -LiteralPath $outs[0])) { $good = ((Get-Item -LiteralPath $outs[0]).Length -gt 0) }
        Check $good 'pdf-merge output exists and is non-empty'
    } else { Fail 'pdf-merge.json not written' }

    # 4. snapshot of the installed exe
    $png = Join-Path $OutDir 'netie-pdf-snapshot.png'
    $code = Run-Exe $exe @('--app', 'pdf', '--snapshot', "`"$png`"") 120
    Check ($code -eq 0) "--app pdf --snapshot exit $code"
    $size = 0
    if (Test-Path -LiteralPath $png) { $size = (Get-Item -LiteralPath $png).Length }
    Check ($size -gt 10240) "snapshot PNG $size bytes (want > 10240)"

    # 5. shortcuts, read back by the independent reader
    $ErrorActionPreference = 'Continue'
    $sc = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'check-app-shortcuts.ps1') -ExePath $exe 2>&1
    $scCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $sc | ForEach-Object { Write-Host "     $_" }
    Check ($scCode -eq 0) "check-app-shortcuts exit $scCode"
}

# 6. uninstall
$unins = Get-ChildItem -LiteralPath $installDir -Filter 'unins*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($unins) {
    $code = Run-Exe $unins.FullName @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$(Join-Path $OutDir 'uninstall.log')`"")
    Check ($code -eq 0) "silent uninstall exit code $code (want 0)"
    # The uninstaller copies itself to temp and deletes the folder after it exits.
    for ($i = 0; $i -lt 30 -and (Test-Path -LiteralPath $installDir); $i++) { Start-Sleep -Seconds 1 }
} else { Fail 'uninstaller (unins*.exe) not found in install folder' }

foreach ($n in $lnkNames) {
    Check (-not (Test-Path -LiteralPath (Join-Path $programs $n))) "shortcut removed: $n"
}
Check (-not (Test-Path -LiteralPath (Join-Path $programs 'Netie Space.lnk'))) 'shortcut removed: Netie Space.lnk'
Check (-not (Test-Path -LiteralPath $installDir)) "install folder removed: $installDir"

$ucAfter = Get-UserChoice
Write-Host "UserChoice .pdf after:  $ucAfter"
Check ($ucBefore -ceq $ucAfter) 'UserChoice for .pdf identical before install and after uninstall'

if ($script:failed -gt 0) { Write-Host "$($script:failed) check(s) FAILED"; exit 1 }
Write-Host 'ALL CHECKS OK'
exit 0

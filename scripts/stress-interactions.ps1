#Requires -Version 5.1
<#
.SYNOPSIS
  Interaction stress: rapid --preview spam, sequential travel through fixtures, process survival.
#>
param(
    [int]$Rounds = 3,
    [int]$HoldSeconds = 2
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'dist\NetieSpace\NetieSpace.exe'
$fx = Join-Path $root 'tests\fixtures\preview'
$log = Join-Path $env:LOCALAPPDATA 'NetieSpace\logs\app.log'

Write-Host '=== Netie Space interaction stress ===' -ForegroundColor Cyan
if (-not (Test-Path $exe)) { Write-Host 'Missing dist exe'; exit 1 }

$files = @(Get-ChildItem $fx -File | Sort-Object Name)
if ($files.Count -lt 2) { Write-Host 'Need >=2 fixtures'; exit 1 }

$fail = 0

function Test-Alive([datetime]$mark) {
    $alive = [bool](Get-Process NetieSpace -ErrorAction SilentlyContinue)
    $errors = @()
    if (Test-Path $log) {
        $errors = @(Get-Content $log | Where-Object {
            ($_ -match 'ERROR|UI unhandled|Preview failed') -and ($_ -match '^\d{4}-\d{2}-\d{2}')
        } | Where-Object {
            try {
                $ts = [datetime]::ParseExact($_.Substring(0, 19), 'yyyy-MM-dd HH:mm:ss', $null)
                $ts -ge $mark.AddSeconds(-1)
            } catch { $false }
        })
    }
    return @{ Alive = $alive; Errors = $errors }
}

# 1) Open/close spam via process kill (Space toggle can't be injected easily)
Write-Host "`n[1] Preview open/close spam ($Rounds rounds)" -ForegroundColor Yellow
for ($r = 1; $r -le $Rounds; $r++) {
    $f = $files[($r - 1) % $files.Count]
    Get-Process NetieSpace -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 300
    $mark = Get-Date
    $p = Start-Process -FilePath $exe -ArgumentList @('--preview', $f.FullName) -PassThru
    Start-Sleep -Seconds $HoldSeconds
    $r1 = Test-Alive $mark
    if (-not $r1.Alive -or $r1.Errors.Count -gt 0) {
        Write-Host "[FAIL] round $r $($f.Name)" -ForegroundColor Red
        $fail++
    } else {
        Write-Host "[OK] open $($f.Name)" -ForegroundColor Green
    }
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 250
}

# 2) Sequential reload (OpenPreviewAsync reload path) — same process, many --preview not possible;
#    instead chain Load via killing less: open once then send new paths by restarting with next file quickly
Write-Host "`n[2] Rapid file switch (process-per-file travel sim)" -ForegroundColor Yellow
Get-Process NetieSpace -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
foreach ($f in $files) {
    $mark = Get-Date
    Get-Process NetieSpace -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 200
    Start-Process -FilePath $exe -ArgumentList @('--preview', $f.FullName) | Out-Null
    Start-Sleep -Milliseconds 900
    $r2 = Test-Alive $mark
    if (-not $r2.Alive) {
        Write-Host "[FAIL] died on $($f.Name)" -ForegroundColor Red
        $fail++
    } elseif ($r2.Errors.Count -gt 0) {
        Write-Host "[FAIL] errors on $($f.Name)" -ForegroundColor Red
        $r2.Errors | Select-Object -Last 3 | ForEach-Object { Write-Host $_ }
        $fail++
    } else {
        Write-Host "[OK] $($f.Name)" -ForegroundColor Green
    }
}

# 3) Same-file double open race
Write-Host "`n[3] Double-launch race" -ForegroundColor Yellow
Get-Process NetieSpace -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
$mark = Get-Date
$f0 = $files[0].FullName
Start-Process -FilePath $exe -ArgumentList @('--preview', $f0) | Out-Null
Start-Sleep -Milliseconds 200
Start-Process -FilePath $exe -ArgumentList @('--preview', $f0) | Out-Null
Start-Sleep -Seconds 3
$procs = @(Get-Process NetieSpace -ErrorAction SilentlyContinue)
# Single-instance mutex should leave 1
if ($procs.Count -gt 2) {
    Write-Host "[WARN] $($procs.Count) NetieSpace processes (expected 1 via mutex)" -ForegroundColor Yellow
}
$r3 = Test-Alive $mark
if (-not $r3.Alive) {
    Write-Host '[FAIL] died on double launch' -ForegroundColor Red
    $fail++
} else {
    Write-Host "[OK] process count=$($procs.Count)" -ForegroundColor Green
}

Get-Process NetieSpace -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host ''
if ($fail -gt 0) {
    Write-Host "Interaction stress FAILED ($fail)" -ForegroundColor Red
    exit 2
}
Write-Host 'Interaction stress PASS' -ForegroundColor Green
exit 0

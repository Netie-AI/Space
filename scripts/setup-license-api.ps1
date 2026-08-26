#Requires -Version 5.1
<#
.SYNOPSIS
  Scaffold / deploy Netie Space license API on Cloudflare (Workers + D1 + Resend secrets).
#>
param(
    [switch]$Deploy,
    [switch]$LocalOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$api = Join-Path $root 'license-api'
Set-Location $api

if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    Write-Host "npm not found. Install Node.js 20+ first: https://nodejs.org" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== Netie license-api setup ===" -ForegroundColor Cyan
npm install --legacy-peer-deps

$wrangler = Join-Path $api 'node_modules\.bin\wrangler.cmd'
if (-not (Test-Path $wrangler)) { $wrangler = 'npx'; $wranglerArgs = @('wrangler') } else { $wranglerArgs = @() }

function Invoke-Wrangler {
    param([string[]]$Args)
    if (Test-Path (Join-Path $api 'node_modules\.bin\wrangler.cmd')) {
        & (Join-Path $api 'node_modules\.bin\wrangler.cmd') @Args
    } else {
        & npx wrangler @Args
    }
    if ($LASTEXITCODE -ne 0) { throw "wrangler $($Args -join ' ') failed ($LASTEXITCODE)" }
}

# Create fails (exit 1) when netie-licenses already exists. Do not throw.
function Get-WranglerText {
    param([string[]]$Args)
    if (Test-Path (Join-Path $api 'node_modules\.bin\wrangler.cmd')) {
        & (Join-Path $api 'node_modules\.bin\wrangler.cmd') @Args 2>&1 | Out-String
    } else {
        & npx wrangler @Args 2>&1 | Out-String
    }
}

function Find-NetieLicensesId {
    $raw = Get-WranglerText @('d1', 'list', '--json')
    $start = $raw.IndexOf('[')
    $end = $raw.LastIndexOf(']')
    if ($start -lt 0 -or $end -le $start) { return $null }
    $json = $raw.Substring($start, $end - $start + 1)
    try {
        foreach ($row in @($json | ConvertFrom-Json)) {
            if ($row.name -ne 'netie-licenses') { continue }
            if ($row.uuid) { return [string]$row.uuid }
            if ($row.database_id) { return [string]$row.database_id }
        }
    } catch {
        return $null
    }
    return $null
}

Write-Host "`nChecking Cloudflare auth (wrangler whoami)..." -ForegroundColor Gray
try {
    Invoke-Wrangler @('whoami')
} catch {
    Write-Host "Not logged in. Opening browser login..." -ForegroundColor Yellow
    Invoke-Wrangler @('login')
}

# Create D1 if database_id still placeholder. Reuse the existing
# netie-licenses id -- d1 create fails if the name is already taken.
$toml = Get-Content (Join-Path $api 'wrangler.toml') -Raw
if ($toml -match 'REPLACE_AFTER_D1_CREATE') {
    Write-Host "Resolving D1 database netie-licenses..." -ForegroundColor Cyan
    $id = Find-NetieLicensesId
    if (-not $id) {
        $createOut = Get-WranglerText @('d1', 'create', 'netie-licenses')
        Write-Host $createOut
        if ($createOut -match 'database_id\s*=\s*"([^"]+)"') {
            $id = $Matches[1]
        }
        if (-not $id) { $id = Find-NetieLicensesId }
    } else {
        Write-Host "[OK] Reusing existing netie-licenses ($id)" -ForegroundColor Green
    }
    if ($id) {
        $toml = $toml -replace 'REPLACE_AFTER_D1_CREATE', $id
        # Windows PowerShell 5.1 Set-Content -Encoding UTF8 writes a BOM.
        # Wrangler/TOML then rejects the first key. Write UTF-8 with no BOM.
        $utf8 = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText((Join-Path $api 'wrangler.toml'), $toml, $utf8)
        Write-Host "[OK] Wrote database_id to wrangler.toml" -ForegroundColor Green
    } else {
        Write-Host "Paste database_id into license-api\wrangler.toml manually, then re-run." -ForegroundColor Yellow
        exit 1
    }
}

Write-Host "Applying schema (local)..." -ForegroundColor Cyan
Invoke-Wrangler @('d1', 'execute', 'netie-licenses', '--local', '--file=./schema.sql')

if ($LocalOnly) {
    Write-Host "`nStarting wrangler dev (local)..." -ForegroundColor Green
    Invoke-Wrangler @('dev')
    exit 0
}

if ($Deploy) {
    Write-Host "Applying schema (remote)..." -ForegroundColor Cyan
    Invoke-Wrangler @('d1', 'execute', 'netie-licenses', '--remote', '--file=./schema.sql')

    Write-Host "`nSet secrets (press Enter to skip / keep existing):" -ForegroundColor Cyan
    $secrets = @(
        @{ Name = 'RESEND_API_KEY'; Prompt = 'Resend API key (re_...)' },
        @{ Name = 'OWNER_NOTIFY_EMAIL'; Prompt = 'Owner notify email (you)' },
        @{ Name = 'RESEND_FROM_EMAIL'; Prompt = 'From email (verified in Resend, e.g. Netie <onboarding@resend.dev>)' },
        @{ Name = 'JWT_SECRET'; Prompt = 'JWT secret (long random string)' },
        @{ Name = 'DEVICE_PEPPER'; Prompt = 'Device pepper (match appsettings License.DevicePepper)' },
        @{ Name = 'STRIPE_SECRET_KEY'; Prompt = 'Stripe restricted key (rk_test_... or sk_test_...). Enter skips' },
        @{ Name = 'STRIPE_WEBHOOK_SECRET'; Prompt = 'Stripe webhook secret (whsec_...). Enter skips' }
    )
    foreach ($s in $secrets) {
        $val = Read-Host $s.Prompt
        if ([string]::IsNullOrWhiteSpace($val)) { continue }
        $val | Invoke-Wrangler @('secret', 'put', $s.Name)
    }

    Write-Host "Deploying Worker..." -ForegroundColor Cyan
    Invoke-Wrangler @('deploy')
    Write-Host "`nDone. Set appsettings.json License.ApiBaseUrl to your workers.dev URL and SkipGate=false." -ForegroundColor Green
} else {
    Write-Host "`nSetup ready. Next:" -ForegroundColor Green
    Write-Host "  Local API:  .\scripts\setup-license-api.ps1 -LocalOnly"
    Write-Host "  Deploy:     .\scripts\setup-license-api.ps1 -Deploy"
}

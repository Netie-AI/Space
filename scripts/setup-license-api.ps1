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

Write-Host "`nChecking Cloudflare auth (wrangler whoami)..." -ForegroundColor Gray
try {
    Invoke-Wrangler @('whoami')
} catch {
    Write-Host "Not logged in. Opening browser login..." -ForegroundColor Yellow
    Invoke-Wrangler @('login')
}

# Create D1 if database_id still placeholder
$toml = Get-Content (Join-Path $api 'wrangler.toml') -Raw
if ($toml -match 'REPLACE_AFTER_D1_CREATE') {
    Write-Host "Creating D1 database netie-licenses..." -ForegroundColor Cyan
    $createOut = & (Join-Path $api 'node_modules\.bin\wrangler.cmd') d1 create netie-licenses 2>&1 | Out-String
    Write-Host $createOut
    if ($createOut -match 'database_id\s*=\s*"([^"]+)"') {
        $id = $Matches[1]
        $toml = $toml -replace 'REPLACE_AFTER_D1_CREATE', $id
        Set-Content (Join-Path $api 'wrangler.toml') $toml -Encoding UTF8
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
        @{ Name = 'DEVICE_PEPPER'; Prompt = 'Device pepper (match appsettings License.DevicePepper)' }
    )
    foreach ($s in $secrets) {
        $val = Read-Host $s.Prompt
        if ([string]::IsNullOrWhiteSpace($val)) { continue }
        $val | & (Join-Path $api 'node_modules\.bin\wrangler.cmd') secret put $s.Name
    }

    Write-Host "Deploying Worker..." -ForegroundColor Cyan
    Invoke-Wrangler @('deploy')
    Write-Host "`nDone. Set appsettings.json License.ApiBaseUrl to your workers.dev URL and SkipGate=false." -ForegroundColor Green
} else {
    Write-Host "`nSetup ready. Next:" -ForegroundColor Green
    Write-Host "  Local API:  .\scripts\setup-license-api.ps1 -LocalOnly"
    Write-Host "  Deploy:     .\scripts\setup-license-api.ps1 -Deploy"
}

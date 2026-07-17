# Netie Space ↔ AirGPT plugin register
# Copies connector manifest next to AirGPT if found; otherwise stages under dist\airgpt-plugin.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$manifest = Join-Path $root "airgpt-plugin\manifest.json"
$stage = Join-Path $root "dist\airgpt-plugin"
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item $manifest (Join-Path $stage "manifest.json") -Force
Copy-Item (Join-Path $root "airgpt-plugin\README.html") (Join-Path $stage "README.html") -Force

$airCandidates = @(
  "D:\AirGPT\connectors",
  "D:\AirGPT\plugins",
  "$env:USERPROFILE\AirGPT\connectors",
  "$env:LOCALAPPDATA\AirGPT\connectors"
)

$registered = $false
foreach ($dir in $airCandidates) {
  if (Test-Path $dir) {
    $dest = Join-Path $dir "netie-space"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item $manifest (Join-Path $dest "manifest.json") -Force
    Write-Host "Registered AirGPT connector → $dest"
    $registered = $true
    break
  }
}

if (-not $registered) {
  Write-Host "AirGPT connectors folder not found. Staged plugin at $stage"
  Write-Host "Copy dist\airgpt-plugin into your AirGPT connectors directory when ready."
}

# Build Rust netie-embed and copy into tools\netie-embed for publish.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$crate = Join-Path $root "native\netie-embed"
$outDir = Join-Path $root "tools\netie-embed"

if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
  Write-Host "cargo not found - skipping Rust embed build (C# ManagedSparseEmbed fallback is used)."
  exit 0
}

Push-Location $crate
try {
  cargo build --release
} finally {
  Pop-Location
}

$exe = Join-Path $crate "target\release\netie-embed.exe"
if (-not (Test-Path $exe)) {
  Write-Host "Rust build produced no exe"
  exit 1
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Copy-Item $exe (Join-Path $outDir "netie-embed.exe") -Force
Write-Host "Installed netie-embed.exe into tools\netie-embed"

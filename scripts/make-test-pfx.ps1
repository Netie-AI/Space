# Self-signed PFX for PDF More... -> Cert sign (PFX)...
# Adobe Reader will show untrusted, not unsigned. Password: test
param([string]$Out = ".\netie-test.pfx")
$ErrorActionPreference = "Stop"
if (-not (Get-Command openssl -ErrorAction SilentlyContinue)) {
    throw "openssl not on PATH. Git for Windows includes it, or use scripts/make-test-pfx.sh in WSL."
}
$tmp = Join-Path $env:TEMP ("netie-pfx-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
try {
    & openssl req -x509 -newkey rsa:2048 -nodes `
      -keyout (Join-Path $tmp "key.pem") -out (Join-Path $tmp "cert.pem") -days 365 `
      -subj "/CN=Netie Space Test"
    if ($LASTEXITCODE -ne 0) { throw "openssl req failed" }
    & openssl pkcs12 -export -out $Out `
      -inkey (Join-Path $tmp "key.pem") -in (Join-Path $tmp "cert.pem") `
      -passout pass:test
    if ($LASTEXITCODE -ne 0) { throw "openssl pkcs12 failed" }
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
Write-Host "pfx: $Out"
Write-Host "password: test"
Write-Host "next: docs/HUMAN_PARKED.md (Cert sign)"

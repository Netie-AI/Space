# Self-signed PFX for PDF More... -> Cert sign (PFX)...
# Adobe Reader will show untrusted, not unsigned. Password: test
# Prefers Windows New-SelfSignedCertificate (no openssl). openssl is fallback.
param([string]$Out = ".\netie-test.pfx")
$ErrorActionPreference = "Stop"
$Out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
$dir = Split-Path $Out -Parent
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$pwd = ConvertTo-SecureString -String "test" -Force -AsPlainText

function Export-NetiePfxFromStore {
    if (-not (Get-Command New-SelfSignedCertificate -ErrorAction SilentlyContinue)) {
        return $false
    }
    if (-not (Get-Command Export-PfxCertificate -ErrorAction SilentlyContinue)) {
        return $false
    }
    $cert = $null
    try {
        $cert = New-SelfSignedCertificate `
            -Subject "CN=Netie Space Test" `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -HashAlgorithm SHA256 `
            -KeyExportPolicy Exportable `
            -KeyUsage DigitalSignature `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -NotAfter (Get-Date).AddYears(2)
        Export-PfxCertificate -Cert $cert -FilePath $Out -Password $pwd | Out-Null
        return (Test-Path -LiteralPath $Out)
    } catch {
        Write-Host "[WARN] Windows PKI pfx: $($_.Exception.Message)" -ForegroundColor Yellow
        return $false
    } finally {
        if ($cert) {
            $storePath = "Cert:\CurrentUser\My\$($cert.Thumbprint)"
            if (Test-Path $storePath) { Remove-Item $storePath -Force -ErrorAction SilentlyContinue }
        }
    }
}

function Export-NetiePfxFromOpenSsl {
    if (-not (Get-Command openssl -ErrorAction SilentlyContinue)) {
        return $false
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
    } finally {
        Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
    }
    return (Test-Path -LiteralPath $Out)
}

if (-not (Export-NetiePfxFromStore)) {
    if (-not (Export-NetiePfxFromOpenSsl)) {
        throw "Could not write $Out. Need Windows PKI cmdlets or openssl on PATH."
    }
}
Write-Host "pfx: $Out"
Write-Host "password: test"
Write-Host "next: docs/HUMAN_PARKED.md (Cert sign)"

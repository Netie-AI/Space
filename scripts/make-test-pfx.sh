#!/usr/bin/env bash
# Self-signed PFX for PDF More... -> Cert sign (PFX)...
# Adobe Reader will show untrusted, not unsigned. Password: test
set -euo pipefail
out="${1:-./netie-test.pfx}"
command -v openssl >/dev/null
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
openssl req -x509 -newkey rsa:2048 -nodes \
  -keyout "$tmp/key.pem" -out "$tmp/cert.pem" -days 365 \
  -subj "/CN=Netie Space Test"
openssl pkcs12 -export -out "$out" \
  -inkey "$tmp/key.pem" -in "$tmp/cert.pem" \
  -passout pass:test
echo "pfx: $out"
echo "password: test"
echo "next: docs/HUMAN_PARKED.md (Cert sign)"

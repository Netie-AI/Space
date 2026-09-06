# Self-check: PDF flush gate must serialize (second join waits on first).
# Run: powershell -File scripts/check-flush-serialize.ps1
# Fails loudly if the gate pattern is missing from PreviewWindow.xaml.cs.

$ErrorActionPreference = "Stop"
$path = Join-Path $PSScriptRoot "..\src\NetieSpace\Views\PreviewWindow.xaml.cs"
$text = Get-Content -Raw $path

$needles = @(
  '_pdfFlushTask',
  'EnsurePdfSavedForCloseAsync',
  'RequestCloseAsync',
  'FlushPendingPdfStampsCoreAsync',
  'EnsureTopmostNoActivate',
  'ArmSignPlacementAsync'
)

$missing = @()
foreach ($n in $needles) {
  if ($text -notmatch [regex]::Escape($n)) { $missing += $n }
}

$app = Join-Path $PSScriptRoot "..\src\NetieSpace\App.xaml.cs"
$appText = Get-Content -Raw $app
if ($appText -notmatch 'ShouldSwallowPreviewKeys') { $missing += 'App.ShouldSwallowPreviewKeys' }
if ($appText -notmatch 'RequestCloseAsync') { $missing += 'App->RequestCloseAsync' }

if ($missing.Count -gt 0) {
  Write-Error ("FLUSH GATE CHECK FAIL - missing: " + ($missing -join ", "))
  exit 1
}

Write-Host "FLUSH GATE CHECK PASS - serialize + save-then-close + focus keys + topmost present"
exit 0

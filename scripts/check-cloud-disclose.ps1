# Self-check: SEC-01 disclose-first (do not claim Space#3 closed)
$ErrorActionPreference = "Stop"
$fail = @()
$cs = Get-Content -Raw "D:\Space\src\NetieSpace\Views\CloudPathDisclosureWindow.cs"
foreach ($n in @("Cloud AI leaves this PC", "EnsureAcked", "Allow cloud AI")) {
  if ($cs -notlike "*$n*") { $fail += "disclose missing $n" }
}
$prefs = Get-Content -Raw "D:\Space\src\NetieSpace\Helpers\UserPrefs.cs"
if ($prefs -notlike "*CloudPathAcknowledged*") { $fail += "UserPrefs missing CloudPathAcknowledged" }
$pw = Get-Content -Raw "D:\Space\src\NetieSpace\Views\PreviewWindow.xaml.cs"
if ($pw -notlike "*EnsureCloudPathAcked*") { $fail += "PreviewWindow missing EnsureCloudPathAcked" }
if ($pw -notlike "*PaintCloudPathChip*") { $fail += "PreviewWindow missing PaintCloudPathChip" }
$app = Get-Content -Raw "D:\Space\src\NetieSpace\App.xaml.cs"
if ($app -notlike "*Cloud AI notice*") { $fail += "tray missing Cloud AI notice" }
$skill = "D:\Cortex\CortexOS\crew\skill_packs\fill-form.md"
if (-not (Test-Path $skill)) { $fail += "missing Cortex fill-form skill" }
else {
  $st = Get-Content -Raw $skill
  if ($st -notlike "*Never invent PII*") { $fail += "fill-form missing abstain" }
  if ($st -notlike "*DeepSeek Harness*") { $fail += "fill-form missing harness BAN" }
}
if ($fail.Count) { Write-Host "FAIL: $($fail -join ' | ')"; exit 1 }
Write-Host "OK: disclose-first + fill-form skill"
exit 0

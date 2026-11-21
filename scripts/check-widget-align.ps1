# Self-check: AcroForm / fill bbox alignment markers

$ErrorActionPreference = "Stop"
$v = "D:\Space\src\NetieSpace\Assets\pdfjs\netie-viewer.html"
$t = Get-Content -Raw $v
$need = @(
  "--scale-factor",
  "entry.annotDiv.style.width = entry.canvas.style.width",
  "entry.annotDiv.style.height = entry.canvas.style.height",
  "fillFieldRectAt",
  "layoutAllFillFields",
  "cover: true"
)
$fail = @()
foreach ($n in $need) {
  if ($t -notlike "*$n*") { $fail += $n }
}
if ($fail.Count) {
  Write-Host "FAIL missing: $($fail -join ' | ')"
  exit 1
}
Write-Host "OK: widget scale-factor + annot layer CSS size + fill hit-test + cover whiteout"
exit 0

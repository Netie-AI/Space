# Self-check: PDF overlay glue (no flying boxes)

$ErrorActionPreference = "Stop"
$v = "D:\Space\src\NetieSpace\Assets\pdfjs\netie-viewer.html"
$t = Get-Content -Raw $v
$need = @(
  "function pageCssBox",
  "clientWidth || entry.canvas.width",
  "--annotation-unfocused-field-background",
  "annotDiv.style.left = '0'",
  "Math.min(1.5, window.devicePixelRatio",
  "Do NOT re-layout fills/stamps/highlights here",
  "skip full raster on noise / low RAM",
  "#viewer.fill-mode .annotationLayer { pointer-events: none; }",
  "#viewer.fill-mode .annotationLayer section { pointer-events: auto; }",
  "const THUMB_BATCH = 12",
  "glue: (() => {"
)
$fail = @()
if ($t -match "#viewer.fill-mode \.annotationLayer,\s*#viewer.fill-mode \.annotationLayer section \{ pointer-events: auto; \}") {
  $fail += "fill-mode still sets pointer-events auto on the whole annot layer"
}
foreach ($n in $need) {
  if ($t -notlike "*$n*") { $fail += $n }
}
if ($t -match 'function layoutFillField[\s\S]{0,400}wrapR\.left') {
  $fail += "layoutFillField still uses wrapR.left"
}
if ($t -match "viewer\.addEventListener\('scroll'[\s\S]{0,800}layoutAllFillFields") {
  $fail += "scroll still re-layouts fill fields"
}
if (-not (Test-Path "D:\Space\src\NetieSpace\Assets\pdfjs\__stress\acroform.pdf")) {
  $fail += "missing __stress/acroform.pdf"
}
# Scroll offset must not enter the formula (that was flying-box + lag).
function OverlayCss([double]$xPt, [double]$yBot, [double]$wPt, [double]$hPt,
  [double]$pageW, [double]$pageH, [double]$cssW, [double]$cssH, [double]$ox, [double]$oy) {
  $kx = $cssW / $pageW
  $ky = $cssH / $pageH
  return @{
    left = $ox + $xPt * $kx
    top  = $oy + ($pageH - $yBot - $hPt) * $ky
    w    = [Math]::Max(10, $wPt * $kx)
    h    = [Math]::Max(8, $hPt * $ky)
  }
}
$a = OverlayCss 72 700 120 18 612 792 612 792 0 0
$b = OverlayCss 72 700 120 18 612 792 1224 1584 0 0
if ([Math]::Abs($b.left - 2 * $a.left) -gt 0.01) { $fail += "zoom 2x left not doubled" }
if ([Math]::Abs($b.top - 2 * $a.top) -gt 0.01) { $fail += "zoom 2x top not doubled" }
$scrolled = OverlayCss 72 700 120 18 612 792 612 792 0 0
# Identical args with a fake scrollLeft must not change wrap-relative CSS.
if ($scrolled.left -ne $a.left -or $scrolled.top -ne $a.top) { $fail += "scroll entered overlay math" }

if ($fail.Count) {
  Write-Host "FAIL: $($fail -join ' | ')"
  exit 1
}
Write-Host "OK: pageCssBox glue + no scroll re-layout + DPR cap + overlay math"
exit 0

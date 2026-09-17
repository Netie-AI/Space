# Self-check: offline Markdown + CSV editor assets and PreviewWindow wiring
$ErrorActionPreference = "Stop"
$root = "D:\Space"
$md = Join-Path $root "src\NetieSpace\Assets\markdown"
$bin = Join-Path $root "src\NetieSpace\bin\Debug\net8.0-windows\Assets\markdown"
$cs = Join-Path $root "src\NetieSpace\Views\PreviewWindow.xaml.cs"
$proj = Join-Path $root "src\NetieSpace\NetieSpace.csproj"
$fail = @()

foreach ($f in @("md-editor.html", "csv-editor.html", "marked.min.js", "NOTICE.txt", "katex\katex.min.js", "katex\auto-render.min.js", "katex\katex.min.css")) {
  if (-not (Test-Path (Join-Path $md $f))) { $fail += "src missing $f" }
  if (-not (Test-Path (Join-Path $bin $f))) { $fail += "bin missing $f" }
}

$csText = Get-Content -Raw $cs
foreach ($n in @("netie.markdown", "csv-editor.html", "md-editor.html", "setCsv", "setMarkdown", "RotateImageButton_Click", "TryOpenForEdit")) {
  if ($csText -notlike "*$n*") { $fail += "PreviewWindow missing $n" }
}

$img = Get-Content -Raw (Join-Path $root "src\NetieSpace\Services\ImageEditService.cs")
if ($img -notlike "*Rotate90Cw*") { $fail += "ImageEditService missing Rotate90Cw" }
$lo = Get-Content -Raw (Join-Path $root "src\NetieSpace\Services\LibreOfficePreviewService.cs")
if ($lo -notlike "*TryOpenForEdit*") { $fail += "LibreOfficePreviewService missing TryOpenForEdit" }

$projText = Get-Content -Raw $proj
if ($projText -notlike "*Assets\markdown\**\*") { $fail += "csproj missing markdown Content Include" }

$mdHtml = Get-Content -Raw (Join-Path $md "md-editor.html")
foreach ($n in @("marked.min.js", "btnSplit", "id=`"src`"", "id=`"prev`"", "syncFrom", "toggleTask", "gfm: true", "btnWidth", "wrapSel", "btnTheme", "paintCount", "bar-off", "renderMathInElement", "katex.min.js", "btnLh", "btnFont", "--imgmax", "mdSelection", "mdAsk", "paintOutline", "annotateSel", "eqToMark", "btnSide", "btnAnnot")) {
  if ($mdHtml -notlike "*$n*") { $fail += "md-editor missing $n" }
}

$csvHtml = Get-Content -Raw (Join-Path $md "csv-editor.html")
foreach ($n in @("parseCsv", "id=`"grid`"", "setCsv", "textEdit", "addRow", "btnAddCol")) {
  if ($csvHtml -notlike "*$n*") { $fail += "csv-editor missing $n" }
}

if ($fail.Count) {
  Write-Host "FAIL: $($fail -join ' | ')"
  exit 1
}
$katex = Join-Path $root "scripts\check-katex-offline.mjs"
$mdCheck = Join-Path $root "scripts\check-md-editor.mjs"
if (Get-Command node -ErrorAction SilentlyContinue) {
  & node $katex
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  & node $mdCheck
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Write-Host "OK: markdown+csv assets in src/bin + PreviewWindow wiring"
exit 0

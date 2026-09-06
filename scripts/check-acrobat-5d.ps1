# Self-check: Acrobat 5d gap markers on D:\Space

$ErrorActionPreference = "Stop"
$root = "D:\Space\src\NetieSpace"
$fail = @()

function Assert-Contains($path, $needle, $label) {
  $t = Get-Content -Raw $path
  if ($t -notlike "*$needle*") { $script:fail += $label }
}

Assert-Contains "$root\Services\PdfEditService.cs" "CreateBlankPdf" "CreateBlankPdf"
Assert-Contains "$root\Views\PreviewWindow.xaml.cs" "PdfFillAndSign_Click" "PdfFillAndSign_Click"
Assert-Contains "$root\Views\PreviewWindow.xaml.cs" '"fillsign"' "fillsign mode"
Assert-Contains "$root\Views\PreviewWindow.xaml.cs" "Open in Netie Space" "Open in Netie Space status"
Assert-Contains "$root\Views\PreviewWindow.xaml" "PdfFillAndSign_Click" "rail Fill&Sign"
Assert-Contains "$root\Views\PreviewWindow.xaml" "PdfCreateBlank_Click" "rail Blank PDF"
Assert-Contains "$root\Assets\pdfjs\netie-viewer.html" "not full Acrobat OCR" "honest OCR banner"

if ($fail.Count -gt 0) {
  Write-Host "FAIL: $($fail -join ', ')"
  exit 1
}
Write-Host "OK: Acrobat 5d markers present"
exit 0

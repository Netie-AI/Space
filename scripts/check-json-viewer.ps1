# Self-check: JSON peek tree assets (row 51). Does not touch PreviewWindow.
$ErrorActionPreference = "Stop"
$root = "D:\Space"
$fail = @()

$htmlPath = Join-Path $root "src\NetieSpace\Assets\code-viewer\json-viewer.html"
$csPath = Join-Path $root "src\NetieSpace\Services\ContentParserService.cs"
if (-not (Test-Path $htmlPath)) { $fail += "src missing json-viewer.html" }
if (-not (Test-Path $csPath)) { $fail += "src missing ContentParserService.cs" }

$html = Get-Content -Raw $htmlPath
foreach ($n in @(
    "id=`"q`"",
    "id=`"tree`"",
    "id=`"src`"",
    "language-json",
    "btnCopy",
    "btnPretty",
    "btnRaw",
    "btnExpand",
    "btnCollapse",
    "__NETIE_SOURCE__",
    "NETIE_JSON_VIEW"
)) {
    if ($html -notlike "*$n*") { $fail += "json-viewer missing $n" }
}

foreach ($n in @("press Space", "Drag ", "How to")) {
    if ($html -like "*$n*") { $fail += "json-viewer how-to copy: $n" }
}

$cs = Get-Content -Raw $csPath
foreach ($n in @("WrapJsonHtml", "json-viewer.html", ".jsonl", ".ndjson")) {
    if ($cs -notlike "*$n*") { $fail += "ContentParserService missing $n" }
}

if ($fail.Count) {
    Write-Host "FAIL: $($fail -join ' | ')"
    exit 1
}
Write-Host "OK: json-viewer.html + WrapJsonHtml"
exit 0

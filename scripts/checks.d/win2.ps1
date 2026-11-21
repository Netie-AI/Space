# Lane win2: HTML to PDF, HTML to image and PDF redaction through the built exe.
# One chain: html -> pdf (WebView2), html -> png, then the generated PDF is redacted and rendered to a picture.
$w2 = Join-Path $Out "win2-input"
New-Item -ItemType Directory -Force -Path $w2 | Out-Null
$html = Join-Path $w2 "netie-proof.html"
@'
<!doctype html><html><head><meta charset="utf-8"><title>proof</title>
<link rel="stylesheet" href="http://example.invalid/site.css"></head>
<body style="margin:0;font:32px Arial">
<div>Quarterly note. The code word is NetieRedactProof and it must disappear.</div>
<div>This second line stays readable after redaction of the page below.</div>
<div style="position:absolute;left:100px;top:200px;width:300px;height:100px;background:#ff0000"></div>
<img src="http://example.invalid/logo.png">
</body></html>
'@ | Set-Content -Encoding UTF8 -Path $html

function Copy-ToolOutputs([string]$Id, [string]$Prefix) {
    $json = Join-Path $Out ("run-" + $Id + ".json")
    $doc = Get-Content -Raw $json | ConvertFrom-Json
    $copied = @()
    foreach ($o in @($doc.outputs)) {
        $dest = Join-Path $Out ($Prefix + (Split-Path $o -Leaf))
        Copy-Item -LiteralPath $o -Destination $dest -Force
        $copied += $dest
    }
    return $copied
}

Invoke-Tool "pdf-from-html" @($html)
$pdfCopy = Copy-ToolOutputs "pdf-from-html" "win2-"
Invoke-Tool "img-html-to-image" @($html) @("--opt", "width=900")
$null = Copy-ToolOutputs "img-html-to-image" "win2-"

$htmlPdf = $pdfCopy | Select-Object -First 1
Invoke-Tool "pdf-redact" @($htmlPdf) @("--opt", "text=NetieRedactProof", "--opt", "color=#000000")
$redacted = Copy-ToolOutputs "pdf-redact" "win2-" | Select-Object -First 1
$bytes = [System.IO.File]::ReadAllBytes($redacted)
$ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
# A picture-only file has no font resource at all, and the input page (text) does.
$inAscii = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($htmlPdf))
Check "redacted pdf has no font, the input has" (($ascii -notmatch "/Font") -and ($inAscii -match "/Font")) $redacted

# A picture of the redacted page to look at.
Invoke-Tool "pdf-to-jpg" @($redacted) @("--opt", "dpi=100")
$null = Copy-ToolOutputs "pdf-to-jpg" "win2-redacted-"

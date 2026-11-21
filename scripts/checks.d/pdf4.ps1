# Lane pdf4: pdf-repair on a damaged copy of $pdfIn, pdf-sign-self (typed name) on $pdfIn.
# Everything lands in $Out\pdf4 so a person can open it from the artifact.
$p4 = Join-Path $Out "pdf4"
New-Item -ItemType Directory -Force $p4 | Out-Null

function P4-Tool([string]$Id, [string]$Tag, [string[]]$Inputs, [string[]]$Extra = @()) {
    $dir = Join-Path $p4 $Tag
    $res = Join-Path $p4 ($Tag + ".json")
    $a = @("--tool", $Id)
    foreach ($i in $Inputs) { $a += @("--in", $i) }
    $a += @("--out", $dir, "--json", $res) + $Extra
    $r = Invoke-App $a 300
    $ok = $false; $outs = @()
    if (Test-Path $res) {
        $doc = Get-Content -Raw $res | ConvertFrom-Json
        $ok = [bool]$doc.ok
        foreach ($o in @($doc.outputs)) { if (Test-Path $o) { $outs += $o } }
    }
    return @{ Code = $r.Code; Ok = $ok; Outputs = $outs; Err = $r.Err }
}

function P4-Text([string]$Pdf, [string]$Tag) {
    $t = P4-Tool "pdf-extract-text" $Tag @($Pdf)
    if ($t.Outputs.Count -lt 1) { return "" }
    return (Get-Content -Raw $t.Outputs[0])
}

# --- pdf-repair: cut the file just before its cross-reference table ---
$orig = [IO.File]::ReadAllBytes($pdfIn)
$latin = [Text.Encoding]::GetEncoding(28591).GetString($orig)
$cut = $latin.LastIndexOf("xref", $latin.LastIndexOf("startxref") - 1)
Check "pdf4 damage point found" ($cut -gt 100) "cut=$cut of $($orig.Length)"
$damaged = Join-Path $p4 "Q3_Report_damaged.pdf"
[IO.File]::WriteAllBytes($damaged, [byte[]]$orig[0..($cut - 1)])

$refused = P4-Tool "pdf-rotate" "damaged-rotate" @($damaged)
Check "pdf4 damaged file is refused by an ordinary tool" (($refused.Code -ne 0) -and (-not $refused.Ok)) "exit=$($refused.Code)"

$rep = P4-Tool "pdf-repair" "repair" @($damaged)
Check "pdf4 pdf-repair" (($rep.Code -eq 0) -and $rep.Ok -and ($rep.Outputs.Count -eq 1)) "exit=$($rep.Code) ok=$($rep.Ok) outputs=$($rep.Outputs.Count) $($rep.Err)"
if ($rep.Outputs.Count -eq 1) {
    Copy-Item $rep.Outputs[0] (Join-Path $p4 "Q3_Report_repaired.pdf") -Force
    $again = P4-Tool "pdf-rotate" "repaired-rotate" @($rep.Outputs[0])
    Check "pdf4 repaired file opens in the PDF engine" (($again.Code -eq 0) -and $again.Ok) "exit=$($again.Code) $($again.Err)"
    $before = (P4-Text $pdfIn "text-original") -replace "\s+", " "
    $after = (P4-Text $rep.Outputs[0] "text-repaired") -replace "\s+", " "
    Check "pdf4 repaired text equals original" (($before.Trim().Length -gt 0) -and ($before.Trim() -eq $after.Trim())) "chars=$($before.Length)/$($after.Length)"
}
$nothing = P4-Tool "pdf-repair" "repair-intact" @($pdfIn)
Check "pdf4 intact file is refused as nothing to repair" (($nothing.Code -ne 0) -and (-not $nothing.Ok) -and ($nothing.Outputs.Count -eq 0)) "exit=$($nothing.Code)"

# --- pdf-sign-self: typed name and a date line on the last page ---
$sig = P4-Tool "pdf-sign-self" "sign" @($pdfIn) @("--opt", "name=Ada", "--opt", "date=true")
Check "pdf4 pdf-sign-self" (($sig.Code -eq 0) -and $sig.Ok -and ($sig.Outputs.Count -eq 1)) "exit=$($sig.Code) ok=$($sig.Ok) outputs=$($sig.Outputs.Count) $($sig.Err)"
if ($sig.Outputs.Count -eq 1) {
    Copy-Item $sig.Outputs[0] (Join-Path $p4 "Q3_Report_signed.pdf") -Force
    $txt = P4-Text $sig.Outputs[0] "text-signed"
    $origTokens = @((P4-Text $pdfIn "text-original2") -split "\s+" | Where-Object { $_ })
    $missing = @($origTokens | Where-Object { -not $txt.Contains($_) })
    Check "pdf4 signed copy shows the name and a date" (($txt -match "Ada") -and ($txt -match "\d{4}-\d{2}-\d{2}")) "name: $($txt -match 'Ada') date: $($txt -match '\d{4}-\d{2}-\d{2}')"
    Check "pdf4 signed copy keeps the original text" (($origTokens.Count -gt 0) -and ($missing.Count -eq 0)) "tokens=$($origTokens.Count) missing=$($missing.Count)"
}
$bad = P4-Tool "pdf-sign-self" "sign-bad-name" @($pdfIn) @("--opt", "name=Zhang$([char]0x4E09)")
Check "pdf4 pdf-sign-self refuses text outside Latin-1" (($bad.Code -ne 0) -and (-not $bad.Ok) -and ($bad.Outputs.Count -eq 0)) "exit=$($bad.Code)"

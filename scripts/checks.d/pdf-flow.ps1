# Lane pdf-flow (wave 6): the office-worker flow behind the visual PDF panels, run through the real exe.
# Three documents (an invoice, a 12-page contract with two blank pages, a one-page receipt) are merged in a dragged
# order, organized (blank pages deleted, the scan rotated), then signed at a clicked spot. Each result is read back
# page by page (pdf-extract-text puts one page per line). The panels themselves are rendered with --snapshot
# --files / --state and the line they print is asserted: pictures drawn, pages, blanks, plan.
# Everything lands in $Out\pdf-flow, which CI uploads.
$flow = Join-Path $Out "pdf-flow"
New-Item -ItemType Directory -Force $flow | Out-Null

function Flow-NewPdf([string]$Path, [string[]]$Pages) {
    $n = $Pages.Count
    $kids = (@(0..($n - 1)) | ForEach-Object { "$(4 + 2 * $_) 0 R" }) -join " "
    $objs = New-Object System.Collections.Generic.List[string]
    $objs.Add("<< /Type /Catalog /Pages 2 0 R >>")
    $objs.Add("<< /Type /Pages /Kids [$kids] /Count $n >>")
    $objs.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
    for ($i = 0; $i -lt $n; $i++) {
        $objs.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents $(5 + 2 * $i) 0 R /Resources << /Font << /F1 3 0 R >> >> >>")
        $text = $Pages[$i]
        $stream = ""
        if ($text) { $stream = "BT /F1 24 Tf 72 700 Td ($text) Tj ET" }
        $objs.Add("<< /Length $($stream.Length) >>`nstream`n$stream`nendstream")
    }
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append("%PDF-1.4`n")
    $offsets = New-Object System.Collections.Generic.List[int]
    for ($k = 0; $k -lt $objs.Count; $k++) {
        $offsets.Add($sb.Length)
        [void]$sb.Append("$($k + 1) 0 obj`n$($objs[$k])`nendobj`n")
    }
    $xref = $sb.Length
    [void]$sb.Append("xref`n0 $($objs.Count + 1)`n0000000000 65535 f `n")
    foreach ($o in $offsets) { [void]$sb.Append(("{0:D10} 00000 n `n" -f $o)) }
    [void]$sb.Append("trailer`n<< /Size $($objs.Count + 1) /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n")
    [IO.File]::WriteAllBytes($Path, [Text.Encoding]::ASCII.GetBytes($sb.ToString()))
}

function Flow-Tool([string]$Id, [string]$Tag, [string[]]$Inputs, [string[]]$Extra = @()) {
    $dir = Join-Path $flow $Tag
    $res = Join-Path $flow ($Tag + ".json")
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

# pdf-extract-text writes one page per line, so line k is page k when every page is a single line of text.
function Flow-Pages([string]$Pdf, [string]$Tag) {
    $t = Flow-Tool "pdf-extract-text" $Tag @($Pdf)
    if ($t.Outputs.Count -lt 1) { return $null }
    $raw = [IO.File]::ReadAllText($t.Outputs[0])
    return @($raw -split "`n" | ForEach-Object { $_.Trim() })
}

function Flow-Same($Got, [string[]]$Want) {
    if ($null -eq $Got) { return $false }
    return ((@($Got) -join "|") -ceq ($Want -join "|"))
}

$invoice = Join-Path $flow "invoice.pdf"
$contract = Join-Path $flow "contract.pdf"
$receipt = Join-Path $flow "receipt.pdf"
Flow-NewPdf $invoice @("INV-01")
$conPages = @(1..12 | ForEach-Object { "CON-{0:D2}" -f $_ })
$conPages[3] = ""    # page 4 is blank
$conPages[8] = ""    # page 9 is blank
Flow-NewPdf $contract $conPages
Flow-NewPdf $receipt @("REC-01")
Check "flow fixtures written" ((Test-Path $invoice) -and (Test-Path $contract) -and (Test-Path $receipt)) $flow
Check "flow contract reads back as 12 pages with two blanks" (Flow-Same (Flow-Pages $contract "src-contract") (@(1..12 | ForEach-Object { if (($_ -eq 4) -or ($_ -eq 9)) { "" } else { "CON-{0:D2}" -f $_ } }))) "fixture"

# --- Merge PDF: the strip order is the page order (receipt dragged to the front) ---
$m = Flow-Tool "pdf-merge" "merge" @($receipt, $invoice, $contract)
Check "flow merge in strip order" (($m.Code -eq 0) -and $m.Ok -and ($m.Outputs.Count -eq 1)) "exit=$($m.Code) ok=$($m.Ok) $($m.Err)"
if ($m.Outputs.Count -eq 1) {
    $want = @("REC-01", "INV-01") + @(1..12 | ForEach-Object { if (($_ -eq 4) -or ($_ -eq 9)) { "" } else { "CON-{0:D2}" -f $_ } })
    $got = Flow-Pages $m.Outputs[0] "text-merge"
    Check "flow merged pages in order (14, PdfPig text per page)" (Flow-Same $got $want) "got=$(@($got) -join ',')"
}

# --- Organize: delete the blanks, drag the receipt first and rotate it (the plan the page grid writes) ---
$plan = "3:1@90;1:1;2:1-3;2:5-8;2:10-12"
$o = Flow-Tool "pdf-organize" "organize" @($invoice, $contract, $receipt) @("--opt", "plan=$plan")
Check "flow organize" (($o.Code -eq 0) -and $o.Ok -and ($o.Outputs.Count -eq 1)) "exit=$($o.Code) ok=$($o.Ok) $($o.Err)"
if ($o.Outputs.Count -eq 1) {
    Copy-Item $o.Outputs[0] (Join-Path $flow "organized.pdf") -Force
    $want = @("REC-01", "INV-01", "CON-01", "CON-02", "CON-03", "CON-05", "CON-06", "CON-07", "CON-08", "CON-10", "CON-11", "CON-12")
    $got = Flow-Pages $o.Outputs[0] "text-organize"
    Check "flow organized: 12 pages, blanks gone, receipt first" (Flow-Same $got $want) "got=$(@($got) -join ',')"
    $latin = [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($o.Outputs[0]))
    $rot = [regex]::Matches($latin, "/Rotate\s+90").Count
    Check "flow organized: exactly the scan is rotated" ($rot -eq 1) "rotate90 count=$rot"

    # --- Sign the result at a clicked spot (the page preview click becomes at=x,y) ---
    $s = Flow-Tool "pdf-sign-self" "sign" @($o.Outputs[0]) @("--opt", "name=Ada", "--opt", "at=62,88", "--opt", "pages=12")
    Check "flow sign at a clicked spot" (($s.Code -eq 0) -and $s.Ok -and ($s.Outputs.Count -eq 1)) "exit=$($s.Code) ok=$($s.Ok) $($s.Err)"
    if ($s.Outputs.Count -eq 1) {
        Copy-Item $s.Outputs[0] (Join-Path $flow "signed.pdf") -Force
        $st = Flow-Tool "pdf-extract-text" "text-sign" @($s.Outputs[0])
        $txt = ""
        if ($st.Outputs.Count -eq 1) { $txt = [IO.File]::ReadAllText($st.Outputs[0]) }
        $ada = [regex]::Matches($txt, "Ada").Count
        Check "flow signed: the name is on the last page only" (($ada -eq 1) -and ($txt.IndexOf("Ada") -gt $txt.IndexOf("CON-12"))) "ada=$ada"
    }
}
$bad = Flow-Tool "pdf-sign-self" "sign-bad-at" @($invoice) @("--opt", "name=Ada", "--opt", "at=140,5")
Check "flow sign refuses a spot outside the page" (($bad.Code -ne 0) -and (-not $bad.Ok) -and ($bad.Outputs.Count -eq 0)) "exit=$($bad.Code)"

# --- Remove pages: the two blanks the grid selected ---
$rm = Flow-Tool "pdf-remove-pages" "remove" @($contract) @("--opt", "pages=4,9")
Check "flow remove the two blank pages" (($rm.Code -eq 0) -and $rm.Ok -and ($rm.Outputs.Count -eq 1)) "exit=$($rm.Code) $($rm.Err)"
if ($rm.Outputs.Count -eq 1) {
    $want = @("CON-01", "CON-02", "CON-03", "CON-05", "CON-06", "CON-07", "CON-08", "CON-10", "CON-11", "CON-12")
    Check "flow remove leaves 10 pages in order" (Flow-Same (Flow-Pages $rm.Outputs[0] "text-remove") $want) "pages"
}

# --- The panels, drawn headless: the strip with 3 files, the grid with 12 pages, the signature preview ---
function Flow-Snap([string]$Name, [string]$View, [string[]]$Files, [string]$State = "") {
    $png = Join-Path $flow ($Name + ".png")
    $a = @("--app", "pdf", "--snapshot", $png, "--view", $View, "--files", ($Files -join "|"))
    if ($State) { $a += @("--state", $State) }
    $r = Invoke-App $a 120
    $size = 0
    if (Test-Path $png) { $size = (Get-Item $png).Length }
    $line = ""
    $found = @($r.Out -split "`n" | Where-Object { $_ -like "PANEL *" })
    if ($found.Count -gt 0) { $line = $found[0].Trim() }
    return @{ Code = $r.Code; Size = $size; Line = $line; Err = $r.Err }
}

$snap = Flow-Snap "merge-strip-3-files" "pdf-merge" @($invoice, $contract, $receipt)
Check "flow snapshot merge strip" (($snap.Code -eq 0) -and ($snap.Size -gt 20480) -and ($snap.Line -match "PANEL merge files=3 pages=14 thumbs=3 ")) "exit=$($snap.Code) bytes=$($snap.Size) line=[$($snap.Line)] $($snap.Err)"

$snap = Flow-Snap "organize-grid-12-pages" "pdf-organize" @($contract) "select=5;rotate=90;select=2,3"
Check "flow snapshot organize grid 12 pages, blanks flagged, one rotated, two selected" (($snap.Code -eq 0) -and ($snap.Size -gt 20480) -and ($snap.Line -match "PANEL organize files=1 pages=12 thumbs=12 blank=2 selected=2 plan=1:1-4;1:5@90;1:6-12")) "exit=$($snap.Code) bytes=$($snap.Size) line=[$($snap.Line)] $($snap.Err)"

$snap = Flow-Snap "organize-grid-blanks-selected" "pdf-organize" @($contract) "selectblank"
Check "flow snapshot select blank pages finds the two" (($snap.Code -eq 0) -and ($snap.Line -match "pages=12 thumbs=12 blank=2 selected=2 ")) "exit=$($snap.Code) line=[$($snap.Line)] $($snap.Err)"

$snap = Flow-Snap "remove-grid-blanks-deleted" "pdf-remove-pages" @($contract) "selectblank;delete"
Check "flow snapshot remove grid after deleting the blanks" (($snap.Code -eq 0) -and ($snap.Line -match "PANEL remove files=1 pages=10 thumbs=12 blank=0 ")) "exit=$($snap.Code) line=[$($snap.Line)] $($snap.Err)"

$snap = Flow-Snap "rotate-grid" "pdf-rotate" @($contract) "select=1,2;rotate=90"
Check "flow snapshot rotate grid" (($snap.Code -eq 0) -and ($snap.Line -match "PANEL rotate files=1 pages=12 thumbs=12 ") -and ($snap.Line -match "plan=1:1-2@90;1:3-12")) "exit=$($snap.Code) line=[$($snap.Line)] $($snap.Err)"

$snap = Flow-Snap "sign-place" "pdf-sign-self" @($contract) "page=3;at=62,88"
Check "flow snapshot sign placement preview" (($snap.Code -eq 0) -and ($snap.Size -gt 20480) -and ($snap.Line -match "PANEL sign files=1 pages=12 page=3 thumbs=1 at=62,88 on=3")) "exit=$($snap.Code) bytes=$($snap.Size) line=[$($snap.Line)] $($snap.Err)"

$snap = Flow-Snap "merge-empty-state" "pdf-merge" @((Join-Path $flow "not-a-pdf.txt"))
Check "flow snapshot merge with a file that is not a PDF stays empty" (($snap.Code -eq 0) -and ($snap.Line -match "PANEL merge files=0 pages=0 thumbs=0 ")) "exit=$($snap.Code) line=[$($snap.Line)] $($snap.Err)"

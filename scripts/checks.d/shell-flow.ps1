# Lane shell-flow (wave 6): the shared shell as a person meets it.
# Headless pictures of the real window tree (--snapshot) plus an audit of the names UI Automation would read
# (--audit): a file in hand, the quick-search palette, the help sheet, the drop overlay, dark mode, first-run and
# recent files, an unavailable tool in plain words. Pictures land in $Out\shell-flow for a person to open.
$sf = Join-Path $Out "shell-flow"
New-Item -ItemType Directory -Force -Path $sf | Out-Null
$sfPdf = Join-Path $root "tests\fixtures\preview\Q3_Report.pdf"
$sfPdf2 = Join-Path $root "tests\fixtures\preview\Contract_Lease.pdf"
$sfPng = Join-Path $root "tests\fixtures\preview\Portrait_product.png"

function Invoke-Shot([string]$Name, [string[]]$Extra) {
    $png = Join-Path $sf ($Name + ".png")
    $audit = Join-Path $sf ($Name + "-audit.txt")
    $r = Invoke-App (@("--snapshot", $png, "--audit", $audit) + $Extra)
    $size = 0
    if (Test-Path $png) { $size = (Get-Item $png).Length }
    $text = ""
    if (Test-Path $audit) { $text = [string](Get-Content -Raw $audit) }
    $controls = -1; $unnamed = -1
    if ($text -match "controls=(\d+) unnamed=(\d+)") { $controls = [int]$Matches[1]; $unnamed = [int]$Matches[2] }
    Check "shell $Name picture" (($r.Code -eq 0) -and ($size -gt 10240)) "exit=$($r.Code) bytes=$size $($r.Err)"
    Check "shell $Name has no unnamed control" (($controls -gt 0) -and ($unnamed -eq 0)) "controls=$controls unnamed=$unnamed $((($text -split "`n") | Where-Object { $_ -like 'UNNAMED*' }) -join '; ')"
    return @{ Png = $png; Text = $text; Code = $r.Code }
}

# True when the audit lists a control of this type with this exact name. The audit line is "ok Type x:Name "name"".
function Has-Named([string]$Text, [string]$Type, [string]$Name) {
    return [bool]($Text -match ('(?m)^\S+\s+' + $Type + '\s+(\S+\s+)?"' + [regex]::Escape($Name) + '"'))
}

function Get-PixelRgb([string]$Png, [int]$X, [int]$Y) {
    Add-Type -AssemblyName System.Drawing
    $bmp = [System.Drawing.Bitmap]::new($Png)
    try { $c = $bmp.GetPixel($X, $Y); return @([int]$c.R, [int]$c.G, [int]$c.B) } finally { $bmp.Dispose() }
}

# 1. A file in hand: "Suggested for <file>" leads the home page, and the page below is the full grid.
$h = Invoke-Shot "shell-home-file" @("--app", "pdf", "--with-file", $sfPdf)
Check "shell suggested heading names the file" ($h.Text -match 'text "Suggested for Q3_Report.pdf"') ""
Check "shell suggested tiles are named by the tool" ((Has-Named $h.Text "Button" "Rotate PDF") -or (Has-Named $h.Text "Button" "Merge PDF")) ""

# 2. Recent files and recent tools come from a throw-away folder (never this PC's own history).
$r1 = Invoke-Shot "shell-home-recents" @("--app", "pdf", "--seed-recents", "$sfPdf;$sfPdf2")
Check "shell recent files section" (($r1.Text -match 'text "Recent files"') -and ($r1.Text -match 'text "Recent tools"')) ""
Check "shell recent file rows say when" ($r1.Text -match 'Q3_Report.pdf, (Just now|\d+ min ago|Today)') ""

# 3. First run: an empty home says what to do instead of showing nothing.
$first = Invoke-Shot "shell-home-first-run" @("--app", "image", "--seed-recents", "none")
Check "shell first run says what to do" (($first.Text -match 'text "Start with a file"') -and (Has-Named $first.Text "Button" "Open a file")) ""

# 4. The quick-search palette: Ctrl+K opens it, 2 or 3 letters land on a tool.
$p = Invoke-Shot "shell-palette" @("--app", "pdf", "--overlay", "palette", "--query", "rot")
Check "shell palette lists the match" (Has-Named $p.Text "ListBoxItem" "Rotate PDF") ""
Check "shell palette box is named" (Has-Named $p.Text "TextBox" "Search tools and recent files") ""

# 5. Help (F1) and the whole-window drop overlay.
$hp = Invoke-Shot "shell-help" @("--app", "pdf", "--overlay", "help")
Check "shell help lists the keys" (($hp.Text -match 'Ctrl\+K or Ctrl\+P') -and ($hp.Text -match 'Alt\+Left') -and ($hp.Text -match 'F1')) ""
$dp = Invoke-Shot "shell-drop-overlay" @("--app", "pdf", "--overlay", "drop")
Check "shell drop overlay says what to do" ($dp.Text -match 'text "Drop a file to open it in Netie PDF"') ""

# 6. Light and dark: the same page, two palettes. The paper pixel proves the theme was really applied.
$light = Invoke-Shot "shell-home-light" @("--app", "pdf", "--theme", "light")
$dark = Invoke-Shot "shell-home-dark" @("--app", "pdf", "--theme", "dark")
# x=6 is the sidebar's left margin: no text or tile is ever drawn there.
$lp = Get-PixelRgb $light.Png 6 400
$dpx = Get-PixelRgb $dark.Png 6 400
Check "shell light paper is light" (($lp[0] -gt 235) -and ($lp[1] -gt 235) -and ($lp[2] -gt 235)) "rgb=$($lp -join ',')"
Check "shell dark paper is dark" (($dpx[0] -lt 60) -and ($dpx[1] -lt 60) -and ($dpx[2] -lt 60)) "rgb=$($dpx -join ',')"
$null = Invoke-Shot "shell-image-dark" @("--app", "image", "--theme", "dark", "--with-file", $sfPng)
$null = Invoke-Shot "shell-video-dark" @("--app", "video", "--theme", "dark")

# 7. A tool page in both themes: every control named, the save folder and back button reachable.
$tl = Invoke-Shot "shell-tool-light" @("--app", "pdf", "--view", "pdf-rotate", "--with-file", $sfPdf)
$td = Invoke-Shot "shell-tool-dark" @("--app", "pdf", "--view", "pdf-rotate", "--with-file", $sfPdf, "--theme", "dark")
Check "shell tool page buttons are named" ((Has-Named $tl.Text "Button" "Back to all tools") -and (Has-Named $tl.Text "Button" "Change the save folder") -and (Has-Named $tl.Text "Button" "Browse")) ""

# 8. An unavailable tool says what it needs in a sentence; the engine's own words stay out of the page (they live in a closed fold).
$listJson = Join-Path $sf "tools.json"
$null = Invoke-App @("--list-tools", "--json", $listJson)
$down = $null
if (Test-Path $listJson) {
    $doc = Get-Content -Raw $listJson | ConvertFrom-Json
    $down = @($doc.tools | Where-Object { ($_.app -eq "pdf") -and (-not $_.available) }) | Select-Object -First 1
}
if ($down) {
    $u = Invoke-Shot "shell-tool-unavailable" @("--app", "pdf", "--view", $down.id)
    Check "shell unavailable tool reads as a sentence" (($u.Text -match 'text "This tool ') -and ($u.Text -notmatch 'text "engine missing')) "tool=$($down.id) reason=$($down.reason)"
    Check "shell unavailable tool has one way forward" (($u.Text -match '(?m)^\S+\s+Button\s+(\S+\s+)?"Back to all tools"') -or ($u.Text -match '(?m)^\S+\s+Button\s+(\S+\s+)?"Use .+ instead"')) "tool=$($down.id)"
} else {
    Check "shell unavailable tool exists on this machine" $true "every pdf tool is available here, nothing to look at"
}

# 9. The 68-tool floor still stands (the shell must not hide a tool).
$avail = 0
if (Test-Path $listJson) { $avail = @((Get-Content -Raw $listJson | ConvertFrom-Json).tools | Where-Object { $_.available }).Count }
Check "shell keeps the tool floor" ($avail -ge 68) "available=$avail"

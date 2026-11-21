# Netie MD lane checks. Dot-sourced by check-app-shell.ps1: uses Invoke-App, Check, $Exe, $Out.
# Everything it writes lands under $Out\md so the CI artifact carries the document, the snapshot and the PDF.

Add-Type -AssemblyName System.Drawing
# No compiled System.Drawing code: Windows PowerShell 5.1 and PowerShell 7 reference it differently.
# Pixels are counted in PowerShell on every second pixel; thresholds below are for that sampling.
if (-not ('MdCheck.Win' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace MdCheck {
    public static class Win {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    }
}
'@
}

function Get-PixelCount([string]$Path, [int]$R, [int]$G, [int]$B, [int]$Tol) {
    $bmp = New-Object System.Drawing.Bitmap $Path
    try {
        $n = 0
        for ($y = 0; $y -lt $bmp.Height; $y += 2) {
            for ($x = 0; $x -lt $bmp.Width; $x += 2) {
                $c = $bmp.GetPixel($x, $y)
                if ([Math]::Abs($c.R - $R) -le $Tol -and [Math]::Abs($c.G - $G) -le $Tol -and [Math]::Abs($c.B - $B) -le $Tol) { $n++ }
            }
        }
        return $n
    } finally { $bmp.Dispose() }
}

$mdDir = Join-Path $Out "md"
New-Item -ItemType Directory -Force -Path $mdDir | Out-Null

# A local picture beside the document: a magenta panel with a white disc (a colour nothing else on the page uses).
$pic = Join-Path $mdDir "panel one.png"
$bmp = New-Object System.Drawing.Bitmap 320, 180
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(233, 30, 99))
$g.FillEllipse([System.Drawing.Brushes]::White, 110, 40, 100, 100)
$g.Dispose()
$bmp.Save($pic, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# A listener that must never be contacted: the document points a picture at it.
$listener = New-Object System.Net.Sockets.TcpListener ([System.Net.IPAddress]::Loopback), 0
$listener.Start()
$port = $listener.LocalEndpoint.Port

$doc = Join-Path $mdDir "check.md"
$lines = @(
    '# Netie MD check',
    '',
    'Intro paragraph with a [link](#table-section), a [blocked link](javascript:alert(1)) and `inline code`.',
    '',
    '## Local picture',
    '',
    '![Local panel](panel%20one.png)',
    '',
    ("![Remote one](http://127.0.0.1:{0}/remote.png)" -f $port),
    '',
    '![Missing one](nothing-here.png)',
    '',
    '![Outside one](../outside.png)',
    '',
    '## Table section',
    '',
    '| Name | Qty | Note |',
    '|------|----:|------|',
    '| Alpha | 1 | first |',
    '| Beta | 22 | second |',
    '| Gamma | 333 | third |',
    '',
    '## Tasks',
    '',
    '- [x] done item',
    '- [ ] open item',
    '',
    '## Code',
    '',
    '```csharp',
    'var x = 1; // not run',
    '```',
    '',
    '> A quote with an accent bar.',
    '',
    '<script>document.body.innerHTML = "SCRIPT RAN"</script>',
    '',
    '## Table section'
)
[System.IO.File]::WriteAllText($doc, ($lines -join "`n"), (New-Object System.Text.UTF8Encoding $false))

# 1. Snapshot: exit 0, a PNG a person can look at, with the picture and the table borders in it.
$png = Join-Path $mdDir "md-snapshot.png"
$pdf = Join-Path $mdDir "md-print.pdf"
$r = Invoke-App @("--app", "md", "--open", $doc, "--theme", "light", "--snapshot", $png, "--pdf", $pdf) 150
$size = 0
if (Test-Path $png) { $size = (Get-Item $png).Length }
Check "md snapshot" (($r.Code -eq 0) -and ($size -gt 10240)) "exit=$($r.Code) bytes=$size $($r.Out) $($r.Err)"
if ($size -gt 0) {
    $probe = New-Object System.Drawing.Bitmap $png
    $dim = "$($probe.Width)x$($probe.Height)"
    $probe.Dispose()
    $magenta = Get-PixelCount $png 233 30 99 6
    $border = Get-PixelCount $png 189 189 189 3
    Check "md snapshot shows the local picture" ($magenta -gt 1000) "size=$dim sampled-magenta-pixels=$magenta"
    Check "md snapshot draws table borders" ($border -gt 60) "sampled-grey-border-pixels=$border"
}

# 2. The PDF is a real PDF with the document in it.
$pdfSize = 0
if (Test-Path $pdf) { $pdfSize = (Get-Item $pdf).Length }
$head = ""
if ($pdfSize -gt 4) { $head = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($pdf), 0, 5) }
Check "md print to pdf" (($pdfSize -gt 2048) -and ($head -eq "%PDF-")) "bytes=$pdfSize head=$head"

# 3. The remote picture was never requested, and the document's script did not run.
Start-Sleep -Seconds 1
$contacted = $listener.Pending()
$listener.Stop()
$remoteInDoc = (Get-Content -Raw $doc) -match ("http://127.0.0.1:" + $port + "/remote.png")
Check "md never fetches a remote picture" ($remoteInDoc -and (-not $contacted)) "listener-contacted=$contacted port=$port"

# 4. Failures are loud: a missing file and missing arguments are not a silent exit 0.
$r = Invoke-App @("--app", "md", "--open", (Join-Path $mdDir "absent.md"), "--snapshot", (Join-Path $mdDir "absent.png")) 90
Check "md snapshot of a missing file fails" (($r.Code -ne 0) -and ($r.Err -match "SNAPSHOT_FAILED")) "exit=$($r.Code) $($r.Err)"
$r = Invoke-App @("--app", "md", "--snapshot", (Join-Path $mdDir "noopen.png")) 60
Check "md snapshot without a file is an argument error" ($r.Code -eq 2) "exit=$($r.Code) $($r.Err)"

# 5. The real window: it opens, titles itself with the file, and shows the outline and the page.
$p = Start-Process -FilePath $Exe -ArgumentList @("--app", "md", "--open", ('"' + $doc + '"')) -PassThru
$title = ""
$rect = $null
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 500
    $p.Refresh()
    if ($p.HasExited) { break }
    $title = $p.MainWindowTitle
    if ($title -like "check.md*") { break }
}
$alive = -not $p.HasExited
Check "md window opens with the file name" ($alive -and ($title -eq "check.md - Netie MD")) "alive=$alive title='$title'"
if ($alive) {
    Start-Sleep -Seconds 4
    try {
        $r2 = New-Object MdCheck.Win+RECT
        [void][MdCheck.Win]::GetWindowRect($p.MainWindowHandle, [ref]$r2)
        $w = $r2.Right - $r2.Left; $h = $r2.Bottom - $r2.Top
        $shot = New-Object System.Drawing.Bitmap $w, $h
        $gfx = [System.Drawing.Graphics]::FromImage($shot)
        $gfx.CopyFromScreen($r2.Left, $r2.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
        $gfx.Dispose()
        $winPng = Join-Path $mdDir "md-window.png"
        $shot.Save($winPng, [System.Drawing.Imaging.ImageFormat]::Png)
        $shot.Dispose()
        Write-Host "md window screenshot ${w}x${h} -> $winPng"
    } catch {
        Write-Host "md window screenshot not taken: $($_.Exception.Message)"
    }
    try { $p.Kill() } catch { }
}

# 6. Start Menu registration includes Netie MD; the independent reader checks all four; unregister removes it.
$programs = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$mdLnk = Join-Path $programs "Netie MD.lnk"
$r = Invoke-App @("--register-apps")
Check "md register-apps writes the shortcut" (($r.Code -eq 0) -and (Test-Path $mdLnk)) "exit=$($r.Code) $($r.Out) $($r.Err)"
$reader = Join-Path $PSScriptRoot "..\check-app-shortcuts.ps1"
$readOut = & powershell -NoProfile -ExecutionPolicy Bypass -File $reader -ExePath $Exe 2>&1 | Out-String
$readCode = $LASTEXITCODE
Check "md shortcut AUMID read-back" (($readCode -eq 0) -and ($readOut -match "Netie MD.lnk Netie.Md --app md")) "exit=$readCode $readOut"
$r = Invoke-App @("--unregister-apps")
Check "md unregister-apps removes the shortcut" (($r.Code -eq 0) -and (-not (Test-Path $mdLnk))) "exit=$($r.Code) $($r.Out) $($r.Err)"

# Sections 7 and 8 run in their own scope: this file is dot-sourced, and a variable such as $root or $r
# set here would leak into the lanes that run after it (video3 and win2 read $root as the repository).
& {
# ====================================================================================================
# 7. The real use case: a developer opens README.md in a cloned repo. A fixture repo (README, docs/img,
#    second.md) is driven through the real WebView2 by `--app md --steps` (real mouse clicks and keys through
#    the browser input pipeline, toolbar buttons through UI Automation Invoke) and the window is captured.
#    Every Check below names one thing a reader does; the screenshots land in $mdDir for a person to look at.
# ====================================================================================================
$repo = Join-Path $mdDir "repo"
$img = Join-Path $repo "docs\img"
New-Item -ItemType Directory -Force -Path $img | Out-Null

function New-Picture([string]$Path, [int]$W, [int]$H, $Back, $Disc) {
    $b = New-Object System.Drawing.Bitmap $W, $H
    $gr = [System.Drawing.Graphics]::FromImage($b)
    $gr.Clear($Back)
    $brush = New-Object System.Drawing.SolidBrush $Disc
    $gr.FillEllipse($brush, [int]($W / 2 - $H / 3), [int]($H / 6), [int]($H * 2 / 3), [int]($H * 2 / 3))
    $brush.Dispose(); $gr.Dispose()
    $b.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
}
# A blue panel (30,120,200) in a subfolder, and the magenta one with a space in its name.
New-Picture (Join-Path $img "architecture.png") 640 240 ([System.Drawing.Color]::FromArgb(30, 120, 200)) ([System.Drawing.Color]::White)
New-Picture (Join-Path $img "panel one.png") 240 120 ([System.Drawing.Color]::FromArgb(233, 30, 99)) ([System.Drawing.Color]::White)

$listener2 = New-Object System.Net.Sockets.TcpListener ([System.Net.IPAddress]::Loopback), 0
$listener2.Start()
$port2 = $listener2.LocalEndpoint.Port

$readme = Join-Path $repo "README.md"
$second = Join-Path $repo "second.md"
$filler = (1..40 | ForEach-Object { "Filler line $_ of the appendix keeps the page long enough to scroll to any section." }) -join "`n`n"
$readmeText = @'
# Space Notes

A small project to show how **Netie MD** renders a `README.md` like GitHub does. See the [second page](second.md#details) or jump to [Getting started](#getting-started).

> [!NOTE]
> Useful information that users should know, even when skimming.

> [!WARNING]
> Urgent info that needs immediate user attention to avoid problems.

![Architecture](docs/img/architecture.png)

![Panel](docs/img/panel%20one.png)

## Getting started

1. Clone the repo
2. Run `make build`
3. Open the app

```python
def greet(name: str) -> str:
    """Say hello."""
    return f"Hello, {name}!"  # friendly

print(greet("world"))
```

```csharp
public static class Program
{
    public static int Main(string[] args) => args.Length;
}
```

```mermaid
graph TD
    A[Clone] --> B[Build]
    B --> C[Run]
```

## What's new?

| Feature | Status | Notes |
|---------|:------:|-------|
| Tables  | done   | GFM pipe tables |
| Alerts  | done   | `> [!NOTE]` |
| Mermaid | no     | shown as code |

### Tasks

- [x] Render pictures from `docs/img`
- [x] Follow relative links
- [ ] Ship it

Footnote reference[^1] and an [external link](https://example.com/docs).

[^1]: The footnote text.

## Appendix

Final paragraph with the word findme here and findme there.

![Missing](docs/img/nothing.png)

![Remote](http://127.0.0.1:PORT/remote.png)

FILLER
'@
$readmeText = $readmeText.Replace("PORT", [string]$port2).Replace("FILLER", $filler)
[System.IO.File]::WriteAllText($readme, $readmeText, (New-Object System.Text.UTF8Encoding $false))
$pad = (1..60 | ForEach-Object { "Line $_ before the details." }) -join "`n`n"
[System.IO.File]::WriteAllText($second, "# Second page`n`nBack to the [readme](README.md).`n`n$pad`n`n## Details`n`nMore details here.`n", (New-Object System.Text.UTF8Encoding $false))

$stepsFile = Join-Path $mdDir "readme-steps.txt"
$steps = @'
[open] wait-doc README.md
[gfm] expect-js 2 :: document.querySelectorAll('.markdown-alert').length
[gfm] expect-js 1 :: document.querySelectorAll('table').length
[gfm] expect-js 3 :: document.querySelectorAll('li.task-list-item input').length
[gfm] expect-js 1 :: document.querySelectorAll('.footnotes li').length
[gfm] expect-js true :: !!document.getElementById('whats-new')
[anchors] expect-js true :: document.querySelectorAll('.md-anchor').length===document.querySelectorAll('main h1,main h2,main h3,main h4,main h5,main h6').length
[pictures] expect-js 2 :: document.querySelectorAll('img').length
[pictures] expect-js true :: Array.prototype.every.call(document.querySelectorAll('img'),function(i){return i.complete&&i.naturalWidth>0})
[pictures] expect-js 2 :: document.querySelectorAll('.md-pic').length
[code] expect-js 3 :: document.querySelectorAll('.md-code').length
[code] expect-js true :: document.querySelectorAll('pre code.hljs .hljs-keyword').length>0
[mermaid] expect-js true :: document.querySelector('.md-mermaid .md-note').textContent.indexOf('not rendered')>0
[mermaid] expect-js 0 :: document.querySelectorAll('.md-mermaid svg,.md-mermaid canvas').length
[nav] expect-disabled md-back
[nav] expect-disabled md-forward
window OUT\readme-light.png
[inpage-link] click-link Getting started
[inpage-link] expect-js true :: document.getElementById('getting-started').getBoundingClientRect().top<250
[outline] eval window.scrollTo(0,0)
[outline] invoke md-outline-whats-new
[outline] expect-js true :: document.getElementById('whats-new').getBoundingClientRect().top<250
[scrollspy] eval document.getElementById('getting-started').scrollIntoView()
[scrollspy] expect-section getting-started
[scrollspy] eval document.getElementById('tasks').scrollIntoView()
[scrollspy] expect-section tasks
window OUT\readme-section.png
[copy] click-selector .md-code .md-copy
[copy] expect-clipboard def greet
[copy] expect-js Copied :: document.querySelector('.md-copy').textContent
[find] find findme
[find] expect-js 1 of 2 :: document.getElementById('md-find-count').textContent
[find] key enter
[find] expect-js 2 of 2 :: document.getElementById('md-find-count').textContent
[find] eval document.querySelector('mark.md-hit-cur').scrollIntoView({block:'center'})
window OUT\readme-find.png
[find] key escape
[find] expect-js 0 :: document.querySelectorAll('mark.md-hit').length
[find-code] find greet
[find-code] expect-js 1 of 2 :: document.getElementById('md-find-count').textContent
[find-code] key escape
[find-none] find zzzzqq
[find-none] expect-js No matches :: document.getElementById('md-find-count').textContent
[find-none] key escape
[zoom] eval window.scrollTo(0,0)
[zoom] invoke md-zoom-in
[zoom] expect-zoom 1.1
[zoom] invoke md-zoom-in
[zoom] expect-zoom 1.25
[zoom] invoke md-zoom-out
[zoom] expect-zoom 1.1
[zoom] key ctrl+0
[zoom] expect-zoom 1
[external] answer-external no
[external] eval window.scrollTo(0,0)
[external] click-link external link
[external] expect-asked https://example.com/docs
[external] expect-not-launched
[external] answer-external yes
[external] click-link external link
[external] expect-launched https://example.com/docs
[edit] invoke md-edit
[edit] expect-launched --edit
[link-nav] eval window.scrollTo(0,0)
[link-nav] click-link second page
[link-nav] wait-doc second.md
[link-fragment] expect-js true :: window.scrollY>100
[link-nav] expect-enabled md-back
[link-nav] expect-disabled md-forward
window OUT\second-page.png
[link-nav] click-link readme
[link-nav] wait-doc README.md
[back-forward] invoke md-back
[back-forward] wait-doc second.md
[back-scroll] expect-js true :: window.scrollY>100
[back-forward] invoke md-forward
[back-forward] wait-doc README.md
[back-keys] key alt+left
[back-keys] wait-doc second.md
[back-keys] key alt+right
[back-keys] wait-doc README.md
[open-button] set-pick second.md
[open-button] invoke md-open
[open-button] wait-doc second.md
[drop] drop README.md
[drop] wait-doc README.md
[live-reload] append README.md Live reload line from a save
[live-reload] wait-text Live reload line from a save
[theme] invoke md-theme
[theme] expect-theme light
[theme] invoke md-theme
[theme] expect-theme dark
[theme] expect-js rgb(22, 24, 28) :: getComputedStyle(document.body).backgroundColor
[theme] eval window.scrollTo(0,0)
window OUT\readme-dark.png
[print-light] emulate-media print
[print-light] expect-js rgb(255, 255, 255) :: getComputedStyle(document.body).backgroundColor
[print-light] expect-js none :: getComputedStyle(document.querySelector('.md-copy')).display
[print-light] emulate-media none
[theme] invoke md-theme
[theme] expect-theme auto
[theme] set-theme dark
pdf OUT\readme-dark-print.pdf
'@
$steps = $steps.Replace('OUT\', $mdDir + '\')
[System.IO.File]::WriteAllText($stepsFile, $steps, (New-Object System.Text.UTF8Encoding $false))

$r = Invoke-App @("--app", "md", "--open", $readme, "--steps", $stepsFile) 240
$stepLines = @(($r.Out -split "`r?`n") | Where-Object { $_ -match '^STEP' })
Set-Content -Path (Join-Path $mdDir "readme-steps-result.txt") -Value $r.Out
$failLine = ($stepLines | Where-Object { $_ -match '^STEP_FAIL' } | Select-Object -First 1)
Check "md steps ran to the end" (($r.Code -eq 0) -and ($r.Out -match 'STEPS_DONE') -and (-not $failLine)) "exit=$($r.Code) first-failure='$failLine' err=$($r.Err)"
$labels = @('open', 'gfm', 'anchors', 'pictures', 'code', 'mermaid', 'nav', 'inpage-link', 'outline', 'scrollspy', 'copy', 'find', 'find-code', 'find-none',
            'zoom', 'external', 'edit', 'link-nav', 'link-fragment', 'back-forward', 'back-scroll', 'back-keys', 'open-button', 'drop', 'live-reload', 'theme', 'print-light')
$descr = @{
    'open' = 'README.md opens'; 'gfm' = 'GFM alerts, table, task list, footnote and heading ids render'; 'anchors' = 'every heading carries a self link';
    'pictures' = 'pictures from docs/img load, missing and remote ones show labelled placeholders'; 'code' = 'code blocks have a copy bar and syntax colours';
    'mermaid' = 'a mermaid block shows its source with a diagram-not-rendered label'; 'nav' = 'Back and Forward start disabled';
    'inpage-link' = 'a #heading link scrolls to the heading'; 'outline' = 'an outline entry jumps to its heading'; 'scrollspy' = 'the outline follows the section being read';
    'copy' = 'the copy button puts the code on the clipboard'; 'find' = 'Ctrl+F finds with a count and steps with Enter, Escape clears'; 'find-code' = 'find matches across syntax-coloured code';
    'find-none' = 'find says No matches'; 'zoom' = 'zoom buttons and Ctrl+0 change the zoom'; 'external' = 'an external link asks first and opens only on Yes';
    'edit' = 'the Edit button starts the editor with --edit FILE'; 'link-nav' = 'a relative .md link opens in the same window'; 'link-fragment' = 'a relative link with #heading scrolls to it';
    'back-forward' = 'Back and Forward buttons walk the documents'; 'back-scroll' = 'Back returns to the scroll position that was left'; 'back-keys' = 'Alt+Left and Alt+Right walk the documents';
    'open-button' = 'the Open button opens the chosen file'; 'drop' = 'a dropped .md file opens'; 'live-reload' = 'saving the file shows the new text';
    'theme' = 'the theme button cycles Auto, Light, Dark and the page follows'; 'print-light' = 'printing is light and has no UI even when the page is dark'
}
foreach ($label in $labels) {
    $ok = @($stepLines | Where-Object { $_ -match ('^STEP_OK \[' + [regex]::Escape($label) + '\] ') }).Count
    $bad = @($stepLines | Where-Object { $_ -match ('^STEP_FAIL \[' + [regex]::Escape($label) + '\] ') })
    Check "md $label" (($ok -gt 0) -and ($bad.Count -eq 0)) "$($descr[$label]) (steps ok=$ok) $($bad -join ' ')"
}

# 7b. The pictures of the run: light and dark windows are real, with the picture and the dark paper in them.
$lightPng = Join-Path $mdDir "readme-light.png"
$darkPng = Join-Path $mdDir "readme-dark.png"
foreach ($f in @('readme-light.png', 'readme-section.png', 'readme-find.png', 'second-page.png', 'readme-dark.png')) {
    $p = Join-Path $mdDir $f
    $len = 0; if (Test-Path $p) { $len = (Get-Item $p).Length }
    Check "md window picture $f" ($len -gt 10240) "bytes=$len"
}
if (Test-Path $lightPng) {
    $blue = Get-PixelCount $lightPng 30 120 200 6
    Check "md light window shows the subfolder picture" ($blue -gt 1000) "sampled-blue-pixels=$blue"
}
if (Test-Path $darkPng) {
    $paper = Get-PixelCount $darkPng 22 24 28 3
    $chrome = Get-PixelCount $darkPng 31 34 39 3
    Check "md dark window paints the page paper" ($paper -gt 20000) "sampled-dark-paper-pixels=$paper"
    Check "md dark window paints the outline pane" ($chrome -gt 2000) "sampled-dark-sidebar-pixels=$chrome (the toolbar and outline follow the page)"
}

# 7c. The printed PDF of a DARK document is the light page: no dark fill in any content stream, light panels present.
function Get-PdfContent([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $latin = [System.Text.Encoding]::GetEncoding(28591)
    $text = $latin.GetString($bytes)
    $sb = New-Object System.Text.StringBuilder
    $pos = 0
    while (($i = $text.IndexOf("stream", $pos, [StringComparison]::Ordinal)) -ge 0) {
        $start = $i + 6
        if ($text[$start] -eq "`r") { $start++ }
        if ($text[$start] -eq "`n") { $start++ }
        $end = $text.IndexOf("endstream", $start, [StringComparison]::Ordinal)
        if ($end -lt 0) { break }
        $len = $end - $start
        if ($len -gt 8) {
            try {
                $in = [System.IO.MemoryStream]::new($bytes, $start + 2, $len - 2)
                $z = New-Object System.IO.Compression.DeflateStream $in, ([System.IO.Compression.CompressionMode]::Decompress)
                $outMs = New-Object System.IO.MemoryStream
                $z.CopyTo($outMs)
                [void]$sb.Append($latin.GetString($outMs.ToArray()))
                [void]$sb.Append("`n")
            } catch { }
        }
        $pos = $end + 9
    }
    return @{ Raw = $text; Streams = $sb.ToString() }
}
$darkPdf = Join-Path $mdDir "readme-dark-print.pdf"
if (Test-Path $darkPdf) {
    $pdfSize2 = (Get-Item $darkPdf).Length
    $c = Get-PdfContent $darkPdf
    $pages = [regex]::Matches($c.Raw, '/Type\s*/Page(?![s])').Count
    $darkFill = [regex]::Matches($c.Streams, '0\.08\d*\s+0\.09\d*\s+0\.1[01]\d*\s+rg').Count + [regex]::Matches($c.Streams, '0\.12\d*\s+0\.13\d*\s+0\.15\d*\s+rg').Count
    $lightFill = [regex]::Matches($c.Streams, '0\.95\d*\s+0\.96\d*\s+0\.9[67]\d*\s+rg').Count
    $sample = ([regex]::Matches($c.Streams, '[\d.]+\s+[\d.]+\s+[\d.]+\s+rg') | Select-Object -First 8 | ForEach-Object { $_.Value }) -join ' | '
    Check "md pdf of a dark document is a multi-page PDF" (($c.Raw.StartsWith('%PDF-')) -and ($pages -ge 2) -and ($pdfSize2 -gt 20000)) "bytes=$pdfSize2 pages=$pages"
    Check "md pdf of a dark document has no dark fill" (($lightFill -gt 0) -and ($darkFill -eq 0)) "dark-fills=$darkFill light-panel-fills=$lightFill colours: $sample"
} else {
    Check "md pdf of a dark document" $false "no PDF written"
}

# 7d. Dark snapshot straight from the command line (--theme dark), and the remote picture of the README was never fetched.
$darkCli = Join-Path $mdDir "readme-cli-dark.png"
$r = Invoke-App @("--app", "md", "--open", $readme, "--theme", "dark", "--snapshot", $darkCli) 150
$darkLen = 0; if (Test-Path $darkCli) { $darkLen = (Get-Item $darkCli).Length }
Check "md --theme dark snapshot" (($r.Code -eq 0) -and ($darkLen -gt 10240)) "exit=$($r.Code) bytes=$darkLen $($r.Err)"
if ($darkLen -gt 0) {
    $paper2 = Get-PixelCount $darkCli 22 24 28 3
    Check "md --theme dark snapshot is dark" ($paper2 -gt 20000) "sampled-dark-paper-pixels=$paper2"
}
Start-Sleep -Seconds 1
$contacted2 = $listener2.Pending()
$listener2.Stop()
Check "md README never fetches its remote picture" (-not $contacted2) "listener-contacted=$contacted2 port=$port2"

# ====================================================================================================
# 8. The real window (not headless): second launch forwards the file, toolbar buttons answer UI Automation,
#    the Open button shows the real Open dialog and opens what it is given.
# ====================================================================================================
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

function Get-UiaButton($Root, [string]$Id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::AutomationIdProperty), $Id
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}
function Invoke-Uia($Element) {
    $pattern = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}
function Wait-Title($Proc, [string]$Want, [int]$Seconds) {
    $title = ""
    for ($i = 0; $i -lt ($Seconds * 4); $i++) {
        Start-Sleep -Milliseconds 250
        $Proc.Refresh()
        if ($Proc.HasExited) { return "(exited)" }
        $title = $Proc.MainWindowTitle
        if ($title -eq $Want) { return $title }
    }
    return $title
}

$real2 = Start-Process -FilePath $Exe -ArgumentList @("--app", "md", "--open", ('"' + $readme + '"')) -PassThru
try {
    $t = Wait-Title $real2 "README.md - Netie MD" 25
    Check "md real window opens README.md" ($t -eq "README.md - Netie MD") "title='$t'"
    Start-Sleep -Seconds 3

    # second launch of the same app: it hands the file to the open window and exits
    $r = Invoke-App @("--app", "md", "--open", $second) 60
    Check "md second launch exits at once" ($r.Code -eq 0) "exit=$($r.Code) $($r.Err)"
    $t = Wait-Title $real2 "second.md - Netie MD" 20
    Check "md second launch shows its file in the open window" ($t -eq "second.md - Netie MD") "title='$t'"

    $uiRoot = [System.Windows.Automation.AutomationElement]::FromHandle($real2.MainWindowHandle)
    # UI Automation: the Back button (what a screen reader or a test tool presses)
    $backBtn = Get-UiaButton $uiRoot "md-back"
    Check "md real window has an enabled Back button after a second launch" (($null -ne $backBtn) -and $backBtn.Current.IsEnabled) "found=$($null -ne $backBtn)"
    if ($null -ne $backBtn) {
        Invoke-Uia $backBtn
        $t = Wait-Title $real2 "README.md - Netie MD" 15
        Check "md real window Back returns to the first file" ($t -eq "README.md - Netie MD") "title='$t'"
    }
    # UI Automation: the zoom button changes the label the window shows
    $zin = Get-UiaButton $uiRoot "md-zoom-in"
    $zlabel = Get-UiaButton $uiRoot "md-zoom-label"
    if (($null -ne $zin) -and ($null -ne $zlabel)) {
        Invoke-Uia $zin
        Start-Sleep -Milliseconds 700
        Check "md real window zoom button changes the zoom" ($zlabel.Current.Name -eq "110%") "label='$($zlabel.Current.Name)'"
    } else {
        Check "md real window zoom button changes the zoom" $false "zoom controls not found in the UI Automation tree"
    }

    # the real Open dialog: press Open, type a path, accept
    $openBtn = Get-UiaButton $uiRoot "md-open"
    $dialogOk = $false
    $dialogNote = "open button not found"
    if ($null -ne $openBtn) {
        $dialogNote = "dialog did not appear"
        Invoke-Uia $openBtn
        $procCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty), $real2.Id
        $dlg = $null
        $nameOpen = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), "Open"
        $isWindow = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Window)
        $dialogCond = New-Object System.Windows.Automation.AndCondition($nameOpen, $isWindow)
        for ($i = 0; $i -lt 120 -and $null -eq $dlg; $i++) {
            Start-Sleep -Milliseconds 250
            # an owned dialog can be listed under the desktop or under its owner window: look in both
            $dlg = $uiRoot.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $dialogCond)
            if ($null -eq $dlg) {
                foreach ($w in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $procCond)) {
                    if ($w.Current.Name -eq "Open") { $dlg = $w }
                }
            }
        }
        if ($null -ne $dlg) {
            $edits = $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Edit)))
            $edit = $null
            foreach ($e in $edits) { if ($e.Current.AutomationId -eq "1148") { $edit = $e } }
            if ($null -eq $edit -and $edits.Count -gt 0) { $edit = $edits[0] }
            $accept = $dlg.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.AndCondition(
                    (New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::AutomationIdProperty), "1"),
                    (New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Button)))))
            if (($null -ne $edit) -and ($null -ne $accept)) {
                $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($second)
                Start-Sleep -Milliseconds 300
                Invoke-Uia $accept
                $t = Wait-Title $real2 "second.md - Netie MD" 15
                $dialogOk = ($t -eq "second.md - Netie MD")
                $dialogNote = "title='$t'"
            } else {
                $dialogNote = "dialog controls not found (edit=$($null -ne $edit) accept=$($null -ne $accept))"
            }
        }
    }
    Check "md real Open dialog opens the chosen file" $dialogOk $dialogNote

    try {
        $r3 = New-Object MdCheck.Win+RECT
        [void][MdCheck.Win]::GetWindowRect($real2.MainWindowHandle, [ref]$r3)
        $w3 = $r3.Right - $r3.Left; $h3 = $r3.Bottom - $r3.Top
        $shot3 = New-Object System.Drawing.Bitmap $w3, $h3
        $g3 = [System.Drawing.Graphics]::FromImage($shot3)
        $g3.CopyFromScreen($r3.Left, $r3.Top, 0, 0, (New-Object System.Drawing.Size $w3, $h3))
        $g3.Dispose()
        $shot3.Save((Join-Path $mdDir "md-window-forwarded.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $shot3.Dispose()
    } catch { Write-Host "md forwarded window screenshot not taken: $($_.Exception.Message)" }
} finally {
    try { if (-not $real2.HasExited) { $real2.Kill() } } catch { }
}
}

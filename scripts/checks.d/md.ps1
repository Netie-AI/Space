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
$r = Invoke-App @("--app", "md", "--open", $doc, "--snapshot", $png, "--pdf", $pdf) 150
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

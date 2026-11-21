# Lane W6-3 image flow: the small-shop job on the built exe. Twenty product photos are cropped square, resized to 1080,
# watermarked bottom-right and compressed under 300 KB, and every output is read back (size, pixels, bytes) the way the
# shop owner receives it. Then the visual states are rendered headless (crop box, watermark pad, size in KB, batch strip
# with a damaged file) so a person can open the PNGs from the artifact.
# Dot-sourced by check-app-shell.ps1: uses Invoke-App, Check, $Exe, $Out.
$fl = Join-Path $Out "image-flow"
$flIn = Join-Path $fl "photos in"
New-Item -ItemType Directory -Force -Path $flIn | Out-Null

# Windows PowerShell keeps these types in System.Drawing; PowerShell 7 (CI) splits them over System.Drawing.Common and
# System.Drawing.Primitives. Reference whichever assemblies hold the types in this session.
Add-Type -AssemblyName System.Drawing
$drawRefs = @([System.Drawing.Bitmap].Assembly.Location, [System.Drawing.Color].Assembly.Location, [System.Drawing.Imaging.ImageFormat].Assembly.Location) | Select-Object -Unique
Add-Type -ReferencedAssemblies $drawRefs -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class NetieFlowPhoto
{
    // A photograph-like picture: a smooth gradient with a little noise, dark enough that a white mark reads on it.
    public static void Make(string path, int w, int h, int seed)
    {
        using (var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
        {
            var rect = new Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            var bytes = new byte[data.Stride * h];
            var rnd = new Random(seed);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * data.Stride + x * 3;
                    int g = 30 + (x * 90 / w) + (y * 50 / h);
                    bytes[i] = (byte)Math.Max(0, Math.Min(255, g + 20 + rnd.Next(-5, 6)));
                    bytes[i + 1] = (byte)Math.Max(0, Math.Min(255, g + (seed % 5) * 8 + rnd.Next(-5, 6)));
                    bytes[i + 2] = (byte)Math.Max(0, Math.Min(255, g + 40 - (y * 30 / h) + rnd.Next(-5, 6)));
                }
            }
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
            bmp.UnlockBits(data);
            bmp.Save(path, ImageFormat.Jpeg);
        }
    }

    public static int[] Size(string path)
    {
        using (var img = Image.FromFile(path)) { return new int[] { img.Width, img.Height }; }
    }

    // How many pixels in the box differ by more than thr in any channel between two pictures of the same size.
    public static int CountDiff(string a, string b, int x0, int y0, int x1, int y1, int thr)
    {
        using (var ba = new Bitmap(a))
        using (var bb = new Bitmap(b))
        {
            int n = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    Color pa = ba.GetPixel(x, y), pb = bb.GetPixel(x, y);
                    if (Math.Abs(pa.R - pb.R) > thr || Math.Abs(pa.G - pb.G) > thr || Math.Abs(pa.B - pb.B) > thr) n++;
                }
            return n;
        }
    }
}
'@

function IF-Tool([string]$Id, [string]$Tag, [string[]]$Inputs, [string[]]$Extra = @()) {
    $dir = Join-Path $fl $Tag
    $res = Join-Path $fl ($Tag + ".json")
    $a = @("--tool", $Id)
    foreach ($i in $Inputs) { $a += @("--in", $i) }
    $a += @("--out", $dir, "--json", $res) + $Extra
    $r = Invoke-App $a 600
    $ok = $false; $outs = @()
    if (Test-Path $res) {
        $doc = Get-Content -Raw $res | ConvertFrom-Json
        $ok = [bool]$doc.ok
        foreach ($o in @($doc.outputs)) { if (Test-Path $o) { $outs += $o } }
    }
    return @{ Code = $r.Code; Ok = $ok; Outputs = $outs; Err = $r.Err }
}

# 1. Twenty photos: portrait and landscape, as a phone and a camera make them.
$photos = @()
for ($i = 1; $i -le 20; $i++) {
    $p = Join-Path $flIn ("product {0:00}.jpg" -f $i)
    if ($i % 4 -eq 0) { [NetieFlowPhoto]::Make($p, 1200, 1600, $i) } else { [NetieFlowPhoto]::Make($p, 1600, 1200, $i) }
    $photos += $p
}
Check "flow 20 photos made" ($photos.Count -eq 20 -and (@($photos | Where-Object { Test-Path $_ }).Count -eq 20)) "count=$($photos.Count)"

# 2. Crop each one square (one box shape for every photo: the largest centred square).
$crop = IF-Tool "img-crop" "1-crop" $photos @("--opt", "aspect=1:1", "--opt", "box=0.5,0.5,1")
$allSquare = $crop.Ok -and ($crop.Outputs.Count -eq 20)
foreach ($o in $crop.Outputs) { $s = [NetieFlowPhoto]::Size($o); if ($s[0] -ne $s[1] -or $s[0] -lt 1100) { $allSquare = $false } }
Check "flow crop 1:1 gives 20 squares" $allSquare "exit=$($crop.Code) ok=$($crop.Ok) outputs=$($crop.Outputs.Count) $($crop.Err)"

# 3. Resize to the Instagram preset: exactly 1080 x 1080.
$rs = IF-Tool "img-resize" "2-resize" $crop.Outputs @("--opt", "mode=pixels", "--opt", "width=1080", "--opt", "height=1080", "--opt", "fit=cover")
$all1080 = $rs.Ok -and ($rs.Outputs.Count -eq 20)
foreach ($o in $rs.Outputs) { $s = [NetieFlowPhoto]::Size($o); if ($s[0] -ne 1080 -or $s[1] -ne 1080) { $all1080 = $false } }
Check "flow resize preset gives 20 pictures of 1080 x 1080" $all1080 "exit=$($rs.Code) ok=$($rs.Ok) outputs=$($rs.Outputs.Count) $($rs.Err)"

# 4. Watermark bottom-right: ink where it was asked for and nowhere near the opposite corner.
$wm = IF-Tool "img-watermark" "3-watermark" $rs.Outputs @("--opt", "text=Netie Shop", "--opt", "position=bottom-right", "--opt", "color=#FFFFFF", "--opt", "opacity=100", "--opt", "scale=30")
$wmOk = $wm.Ok -and ($wm.Outputs.Count -eq 20)
$inkMin = [int]::MaxValue; $strayMax = 0
if ($wmOk) {
    for ($i = 0; $i -lt 20; $i++) {
        $before = $rs.Outputs[$i]; $after = $wm.Outputs[$i]
        $ink = [NetieFlowPhoto]::CountDiff($before, $after, 540, 540, 1080, 1080, 45)
        $stray = [NetieFlowPhoto]::CountDiff($before, $after, 0, 0, 540, 540, 45)
        if ($ink -lt $inkMin) { $inkMin = $ink }
        if ($stray -gt $strayMax) { $strayMax = $stray }
    }
    $wmOk = ($inkMin -gt 300) -and ($strayMax -eq 0)
}
Check "flow watermark pixels are bottom-right only" $wmOk "ok=$($wm.Ok) outputs=$($wm.Outputs.Count) inkMin=$inkMin strayMax=$strayMax $($wm.Err)"

# 5. Compress each under 300 KB without changing the size.
$cp = IF-Tool "img-compress" "4-compress" $wm.Outputs @("--opt", "level=low", "--opt", "target_kb=300")
$under = $cp.Ok -and ($cp.Outputs.Count -eq 20)
$maxKb = 0; $minKb = 99999
foreach ($o in $cp.Outputs) {
    $kb = (Get-Item $o).Length / 1024.0
    if ($kb -gt $maxKb) { $maxKb = $kb }
    if ($kb -lt $minKb) { $minKb = $kb }
    $s = [NetieFlowPhoto]::Size($o)
    if ($kb -gt 300 -or $s[0] -ne 1080 -or $s[1] -ne 1080) { $under = $false }
}
Check "flow compress puts every file under 300 KB at 1080 x 1080" $under ("ok={0} outputs={1} minKb={2:N0} maxKb={3:N0} {4}" -f $cp.Ok, $cp.Outputs.Count, $minKb, $maxKb, $cp.Err)

# 6. A target that cannot be met fails loudly and writes nothing (the run is the person's answer, not a silent bigger file).
$tight = IF-Tool "img-compress" "5-compress-impossible" @($photos[0]) @("--opt", "target_kb=1")
Check "flow compress impossible target fails and says why" ((-not $tight.Ok) -and ($tight.Outputs.Count -eq 0) -and (($tight.Err + (Get-Content -Raw (Join-Path $fl "5-compress-impossible.json") -ErrorAction SilentlyContinue)) -match "under 1 KB")) "ok=$($tight.Ok) outputs=$($tight.Outputs.Count)"

# 7. The visual states, headless. Each one is a real tool page with real photos; the PNGs are in the artifact.
function IF-Snap([string]$Name, [string]$View, [string[]]$Files, [string[]]$Opts, [string[]]$Extra = @()) {
    $png = Join-Path $fl ($Name + ".png")
    $a = @("--app", "image", "--snapshot", $png, "--view", $View, "--flow-files", ($Files -join "|"))
    foreach ($o in $Opts) { $a += @("--flow-opt", $o) }
    $a += $Extra
    $r = Invoke-App $a 180
    $size = 0; if (Test-Path $png) { $size = (Get-Item $png).Length }
    return @{ Code = $r.Code; Size = $size; Out = $r.Out; Err = $r.Err; Png = $png }
}

$empty = Join-Path $fl "empty-crop.png"
$null = Invoke-App @("--app", "image", "--snapshot", $empty, "--view", "img-crop")
$emptySize = 0; if (Test-Path $empty) { $emptySize = (Get-Item $empty).Length }

$first3 = @($photos[0], $photos[3], $photos[1])
$s = IF-Snap "flow-crop-box" "img-crop" $first3 @("aspect=1:1", "box=0.46,0.5,0.8")
Check "flow snapshot crop box" (($s.Code -eq 0) -and ($s.Size -gt 20000) -and ($s.Size -ne $emptySize)) "exit=$($s.Code) bytes=$($s.Size) empty=$emptySize $($s.Err)"

$s = IF-Snap "flow-watermark-pad" "img-watermark" $first3 @("text=Netie Shop", "position=bottom-right", "color=#FFFFFF", "opacity=100", "scale=30")
Check "flow snapshot watermark preview" (($s.Code -eq 0) -and ($s.Size -gt 20000)) "exit=$($s.Code) bytes=$($s.Size) $($s.Err)"

$s = IF-Snap "flow-compress-size" "img-compress" @($crop.Outputs[0], $crop.Outputs[1]) @("level=low", "target_kb=300")
Check "flow snapshot compress shows size" (($s.Code -eq 0) -and ($s.Size -gt 20000)) "exit=$($s.Code) bytes=$($s.Size) $($s.Err)"

# The strip with a state per photo: five real photos and one damaged file, run for real.
$broken = Join-Path $flIn "broken.jpg"
Set-Content -Path $broken -Value "this is not a picture" -Encoding ASCII
$batchOut = Join-Path $fl "batch-out"
$batchFiles = @($photos[0], $photos[1], $broken, $photos[2], $photos[3], $photos[4])
$s = IF-Snap "flow-batch-strip" "img-resize" $batchFiles @("mode=pixels", "width=1080", "height=1080", "fit=cover") @("--flow-run", "--flow-out", $batchOut)
$states = @($s.Out -split "`r?`n" | Where-Object { $_ -like "FLOW_STATE *" })
$doneN = @($states | Where-Object { $_ -like "*|Done|*" }).Count
$failed = @($states | Where-Object { $_ -like "*|Failed|*" })
$reasonOk = ($failed.Count -eq 1) -and ($failed[0] -like "FLOW_STATE broken.jpg|Failed|*") -and ($failed[0] -match "not a readable|damaged")
$outs = @(Get-ChildItem -Path $batchOut -Filter *.jpg -ErrorAction SilentlyContinue)
$sizesOk = $outs.Count -eq 5
foreach ($f in $outs) { $d = [NetieFlowPhoto]::Size($f.FullName); if ($d[0] -ne 1080 -or $d[1] -ne 1080) { $sizesOk = $false } }
Check "flow batch strip: 5 done, the damaged one failed with its reason, outputs are 1080 square" (($s.Code -eq 0) -and ($s.Size -gt 20000) -and ($doneN -eq 5) -and $reasonOk -and $sizesOk) ("exit={0} bytes={1} done={2} failed={3} outputs={4} {5} {6}" -f $s.Code, $s.Size, $doneN, $failed.Count, $outs.Count, ($failed -join ";"), $s.Err)

# The old tool pages must still draw for tools the panel does not take over.
$s = IF-Snap "flow-untouched-dpi" "img-dpi" @($photos[0]) @()
Check "flow snapshot of a tool without a panel still renders" (($s.Code -eq 0) -and ($s.Size -gt 10000)) "exit=$($s.Code) bytes=$($s.Size) $($s.Err)"

# Lane S50-E5 video3: vid-meme, vid-stop-motion, vid-screen-record on the built exe, then the
# FFmpeg that scripts/download-ffmpeg.ps1 fetches (tools\ffmpeg) against the same tool battery.
# Dot-sourced by check-app-shell.ps1: uses Invoke-App, Invoke-Tool, Check, $Exe, $Out, $root, $clip.

function V3-Probe([string]$ProbeExe, [string]$File) {
    $j = & $ProbeExe -v error -print_format json -show_format -show_streams -i $File | Out-String | ConvertFrom-Json
    $v = @($j.streams | Where-Object { $_.codec_type -eq 'video' })[0]
    $fps = 0.0
    if ($v -and $v.avg_frame_rate -match '^(\d+)/(\d+)$' -and [double]$Matches[2] -gt 0) { $fps = [double]$Matches[1] / [double]$Matches[2] }
    return @{ Dur = [double]$j.format.duration; Fps = $fps; W = [int]$v.width; H = [int]$v.height; Tag = [string]$j.format.tags.encoder }
}

# Runs one tool through the exe (Invoke-Tool asserts exit 0, ok:true, output present) and returns its first output path.
function V3-Run([string]$Id, [string[]]$Inputs, [string[]]$Extra = @()) {
    Invoke-Tool $Id $Inputs $Extra
    $res = Join-Path $Out ("run-" + $Id + ".json")
    if (-not (Test-Path $res)) { return $null }
    $doc = Get-Content -Raw $res | ConvertFrom-Json
    return @($doc.outputs)[0]
}

function V3-Near([string]$Name, [double]$Actual, [double]$Want, [double]$Tol) {
    Check $Name ([math]::Abs($Actual - $Want) -le $Tol) "got $Actual, want $Want +-$Tol"
}

# The same ten tools, read back with the given ffprobe. $Tag names the ffmpeg build in the check names.
function V3-Battery([string]$Tag, [string]$ProbeExe, [string]$In) {
    $second = Join-Path $Out "clip two.mp4"
    Copy-Item $In $second -Force
    $srt = Join-Path $Out "v3 subs.srt"
    Set-Content -Path $srt -Encoding ASCII -Value "1`r`n00:00:00,500 --> 00:02:00,000`r`nTop subtitle`r`n"
    $seen = @{}

    $o = V3-Run "vid-trim" @($In) @("--opt", "start=0.5", "--opt", "end=2")
    $p = V3-Probe $ProbeExe $o; $seen["trim"] = $p
    V3-Near "$Tag vid-trim duration" $p.Dur 1.5 0.2; V3-Near "$Tag vid-trim fps" $p.Fps 25 0.1

    $o = V3-Run "vid-merge" @($In, $second)
    $p = V3-Probe $ProbeExe $o; $seen["merge"] = $p
    V3-Near "$Tag vid-merge duration" $p.Dur 6 0.3; V3-Near "$Tag vid-merge fps" $p.Fps 25 0.1

    $o = V3-Run "vid-loop" @($In) @("--opt", "count=2")
    $p = V3-Probe $ProbeExe $o; $seen["loop"] = $p
    V3-Near "$Tag vid-loop duration" $p.Dur 6 0.3; V3-Near "$Tag vid-loop fps" $p.Fps 25 0.1

    $o = V3-Run "vid-stabilize" @($In)
    $p = V3-Probe $ProbeExe $o; $seen["stabilize"] = $p
    V3-Near "$Tag vid-stabilize duration" $p.Dur 3 0.2; V3-Near "$Tag vid-stabilize fps" $p.Fps 25 0.1

    $o = V3-Run "vid-subtitles" @($In, $srt) @("--opt", "position=top-center")
    $p = V3-Probe $ProbeExe $o; $seen["subtitles"] = $p
    V3-Near "$Tag vid-subtitles top duration" $p.Dur 3 0.2; V3-Near "$Tag vid-subtitles top fps" $p.Fps 25 0.1

    $o = V3-Run "vid-add-text" @($In) @("--opt", "text=Netie 8.1")
    $p = V3-Probe $ProbeExe $o; $seen["add-text"] = $p
    V3-Near "$Tag vid-add-text duration" $p.Dur 3 0.2; V3-Near "$Tag vid-add-text fps" $p.Fps 25 0.1

    $o = V3-Run "vid-to-gif" @($In) @("--opt", "fps=10", "--opt", "width=160")
    $p = V3-Probe $ProbeExe $o; $seen["to-gif"] = $p
    V3-Near "$Tag vid-to-gif duration" $p.Dur 3 0.4; V3-Near "$Tag vid-to-gif fps" $p.Fps 10 0.5
    Check "$Tag vid-to-gif width" ($p.W -eq 160) "width=$($p.W)"

    $o = V3-Run "vid-speed" @($In) @("--opt", "speed=200")
    $p = V3-Probe $ProbeExe $o; $seen["speed"] = $p
    V3-Near "$Tag vid-speed duration" $p.Dur 1.5 0.2; V3-Near "$Tag vid-speed fps" $p.Fps 25 0.1

    $o = V3-Run "vid-resize" @($In) @("--opt", "width=160", "--opt", "height=0")
    $p = V3-Probe $ProbeExe $o; $seen["resize"] = $p
    V3-Near "$Tag vid-resize duration" $p.Dur 3 0.2; V3-Near "$Tag vid-resize fps" $p.Fps 25 0.1
    Check "$Tag vid-resize width" ($p.W -eq 160) "width=$($p.W) height=$($p.H)"

    $o = V3-Run "vid-meme" @($In) @("--opt", "top=One does not simply", "--opt", "bottom=Check a build")
    $p = V3-Probe $ProbeExe $o; $seen["meme"] = $p
    V3-Near "$Tag vid-meme duration" $p.Dur 3 0.2; V3-Near "$Tag vid-meme fps" $p.Fps 25 0.1
    Check "$Tag vid-meme size" (($p.W -eq 320) -and ($p.H -eq 180)) "size=$($p.W)x$($p.H)"

    foreach ($k in $seen.Keys) { Write-Host ("BATTERY {0} {1}: dur={2:N3} fps={3:N2} size={4}x{5} encoder={6}" -f $Tag, $k, $seen[$k].Dur, $seen[$k].Fps, $seen[$k].W, $seen[$k].H, $seen[$k].Tag) }
    return $seen
}

if (-not (Get-Variable clip -ErrorAction SilentlyContinue) -or -not (Test-Path $clip)) {
    Check "video3 fixture" $false "the 3 second clip from check-app-shell.ps1 is missing"
    return
}
$pathProbe = (Get-Command ffprobe -ErrorAction SilentlyContinue)
$pathFfmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue)
Check "video3 PATH ffmpeg and ffprobe" (($null -ne $pathProbe) -and ($null -ne $pathFfmpeg)) "ffmpeg and ffprobe must be on PATH for the baseline battery"
Write-Host ("PATH ffmpeg: " + (& $pathFfmpeg.Source -version | Select-Object -First 1))
$bundledDir = Join-Path $root "tools\ffmpeg"
Check "video3 no bundled ffmpeg yet" (-not (Test-Path (Join-Path $bundledDir "ffmpeg.exe"))) "tools\ffmpeg\ffmpeg.exe already exists, the PATH baseline would not be the PATH build"

# ---- 1. vid-meme on a still and on the clip (decoded-frame checks are in the xunit suite) ----
$icon3 = Join-Path $root "src\NetieSpace\Assets\netie-icon.png"
$memeOut = V3-Run "vid-meme" @($icon3) @("--opt", "top=Top line", "--opt", "bottom=Bottom line")
Check "vid-meme still gives a png" ($memeOut -like "*.png") $memeOut
$blank = Invoke-App @("--tool", "vid-meme", "--in", $clip, "--out", (Join-Path $Out "meme-blank"), "--opt", "top=   ")
Check "vid-meme blank caption refused" (($blank.Code -eq 2) -and -not (Test-Path (Join-Path $Out "meme-blank"))) "exit=$($blank.Code) $($blank.Err)"

# ---- 2. vid-stop-motion: length kept, fewer distinct pictures (mpdecimate) ----
$sm = V3-Run "vid-stop-motion" @($clip) @("--opt", "fps=5")
$smp = V3-Probe $pathProbe.Source $sm
V3-Near "vid-stop-motion duration" $smp.Dur 3 0.2
V3-Near "vid-stop-motion fps" $smp.Fps 25 0.1
$distinct = @(& $pathFfmpeg.Source -v error -i $sm -map 0:v:0 -vf mpdecimate -f framemd5 - | Where-Object { $_ -and -not $_.StartsWith('#') }).Count
Check "vid-stop-motion distinct pictures" (($distinct -ge 14) -and ($distinct -le 16)) "distinct=$distinct (5 per second over 3 s is 15)"
Copy-Item $sm (Join-Path $Out "v3-stop-motion.mp4") -Force

# ---- 3. vid-screen-record through the exe: 2 seconds on this runner ----
function V3-Record([string]$Name, [string[]]$Extra) {
    $dir = Join-Path $Out ("rec-" + $Name)
    $res = Join-Path $Out ("rec-" + $Name + ".json")
    $r = Invoke-App (@("--tool", "vid-screen-record", "--out", $dir, "--json", $res) + $Extra) 120
    $doc = $null
    if (Test-Path $res) { $doc = Get-Content -Raw $res | ConvertFrom-Json }
    if ($doc -and $doc.ok -and (Test-Path @($doc.outputs)[0])) {
        $p = V3-Probe $pathProbe.Source @($doc.outputs)[0]
        Write-Host ("SCREEN {0}: dur={1:N3} fps={2:N2} size={3}x{4}" -f $Name, $p.Dur, $p.Fps, $p.W, $p.H)
        return @{ Ok = $true; P = $p; Path = @($doc.outputs)[0] }
    }
    $why = "exit=$($r.Code) err=$($r.Err) json=" + $(if ($doc) { ($doc | ConvertTo-Json -Compress) } else { "none" })
    Write-Host "SCREEN $Name UNVERIFIED: $why"
    Set-Content -Path (Join-Path $Out ("screen-record-$Name-error.txt")) -Value $why
    return @{ Ok = $false; Why = $why }
}
$full = V3-Record "full" @("--opt", "seconds=2", "--opt", "fps=15")
if ($full.Ok) {
    V3-Near "vid-screen-record duration" $full.P.Dur 2 0.5
    Check "vid-screen-record frame size" (($full.P.W -ge 64) -and ($full.P.H -ge 64) -and ($full.P.W % 2 -eq 0) -and ($full.P.H % 2 -eq 0)) "size=$($full.P.W)x$($full.P.H)"
    Copy-Item $full.Path (Join-Path $Out "v3-screen-full.mp4") -Force
    $reg = V3-Record "region" @("--opt", "seconds=2", "--opt", "fps=15", "--opt", "region=16,16,320,200", "--opt", "cursor=false")
    Check "vid-screen-record region size" ($reg.Ok -and ($reg.P.W -eq 320) -and ($reg.P.H -eq 200)) $(if ($reg.Ok) { "size=$($reg.P.W)x$($reg.P.H)" } else { $reg.Why })
    if ($reg.Ok) { V3-Near "vid-screen-record region duration" $reg.P.Dur 2 0.5 }
} else {
    Write-Host "UNVERIFIED vid-screen-record: this runner has no capturable desktop or gdigrab failed, see above"
}
$bad = Invoke-App @("--tool", "vid-screen-record", "--out", (Join-Path $Out "rec-bad"), "--opt", "seconds=0")
Check "vid-screen-record seconds=0 refused" (($bad.Code -eq 2) -and ($bad.Err -match "seconds")) "exit=$($bad.Code) $($bad.Err)"

# ---- 4. The battery on the PATH build, then on the bundled build from scripts\download-ffmpeg.ps1 ----
$base = V3-Battery "pathbuild" $pathProbe.Source $clip

$dl = Join-Path $root "scripts\download-ffmpeg.ps1"
$got = $false
for ($try = 1; $try -le 3 -and -not $got; $try++) {
    try { & $dl; $got = Test-Path (Join-Path $bundledDir "ffmpeg.exe") } catch { Write-Host "download attempt $try failed: $_" }
}
Check "download-ffmpeg.ps1 installed tools\ffmpeg" ($got -and (Test-Path (Join-Path $bundledDir "ffprobe.exe"))) "tools\ffmpeg\ffmpeg.exe / ffprobe.exe missing"
if ($got) {
    try {
        $bff = Join-Path $bundledDir "ffmpeg.exe"; $bfp = Join-Path $bundledDir "ffprobe.exe"
        $verText = (& $bff -version | Out-String)
        $verLine = ($verText -split "`n")[0].Trim()
        Write-Host "BUNDLED ffmpeg -version: $verLine"
        ($verText -split "`n" | Select-Object -First 12) | ForEach-Object { Write-Host ("  " + $_.TrimEnd()) }
        Set-Content -Path (Join-Path $Out "bundled-ffmpeg-version.txt") -Value $verText
        $avf = [regex]::Match($verText, 'libavformat\s+(\d+)\.\s*(\d+)\.\s*(\d+)')
        $lavf = "Lavf" + $avf.Groups[1].Value + "." + $avf.Groups[2].Value + "." + $avf.Groups[3].Value

        $bundled = V3-Battery "bundled" $bfp $clip
        # The exe used the bundled build when the muxer tag in its outputs is the bundled libavformat, not the PATH one.
        $pathVer = (& $pathFfmpeg.Source -version | Out-String)
        $pathAvf = [regex]::Match($pathVer, 'libavformat\s+(\d+)\.\s*(\d+)\.\s*(\d+)')
        $pathLavf = "Lavf" + $pathAvf.Groups[1].Value + "." + $pathAvf.Groups[2].Value + "." + $pathAvf.Groups[3].Value
        Write-Host "muxer tags: bundled=$lavf path=$pathLavf"
        Check "exe uses the bundled build" (($lavf -ne $pathLavf) -and ($bundled["trim"].Tag -eq $lavf) -and ($base["trim"].Tag -eq $pathLavf)) "trim tag on bundled run=$($bundled['trim'].Tag) want $lavf; on PATH run=$($base['trim'].Tag) want $pathLavf"
        foreach ($k in $bundled.Keys) {
            $a = $base[$k]; $b = $bundled[$k]
            $same = ([math]::Abs($a.Dur - $b.Dur) -le 0.1) -and ([math]::Abs($a.Fps - $b.Fps) -le 0.05) -and ($a.W -eq $b.W) -and ($a.H -eq $b.H)
            Write-Host ("DIFF {0}: path dur={1:N3} fps={2:N2} {3}x{4} | bundled dur={5:N3} fps={6:N2} {7}x{8} | {9}" -f $k, $a.Dur, $a.Fps, $a.W, $a.H, $b.Dur, $b.Fps, $b.W, $b.H, $(if ($same) { "same" } else { "DIFFERENT" }))
        }
        foreach ($id in @("vid-trim", "vid-merge", "vid-loop", "vid-stabilize", "vid-subtitles", "vid-add-text", "vid-to-gif", "vid-speed", "vid-resize", "vid-meme")) {
            $d = Join-Path $Out ("run-" + $id)
            if (Test-Path $d) { Get-ChildItem $d -File | Select-Object -Last 1 | ForEach-Object { Copy-Item $_.FullName (Join-Path $Out ("v3-bundled-" + $id + $_.Extension)) -Force } }
        }
    } finally {
        # Leave the workspace as it was for the lane checks that run after this one.
        Remove-Item $bundledDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

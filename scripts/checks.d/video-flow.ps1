# Lane W6-4 video-flow: the filmstrip quick edit, on the built exe and real files.
#   1. a 2-minute clip is trimmed the way the page does it (first 8 s and last 5 s off): output length within 0.2 s of the handles;
#   2. mute + speed + caption in one run: length, a silent muted section, sound around it, and the caption really on the picture;
#   3. WhatsApp preset on a busy clip bigger than 16 MB comes out under 16 MB;
#   4. GIF preset: 6 s selection gives 480 px wide, about 72 frames;
#   5. Reel and YouTube preset shapes;
#   6. --snapshot of the filmstrip page in three states (handles, presets, gif), rendered by CI so a person can look at them.
# Dot-sourced by check-app-shell.ps1: uses Invoke-App, Invoke-Tool, Check, $Exe, $Out, $root.
# Runs before video3.ps1 (name order), which rewrites PATH: this file only reads ffmpeg and ffprobe from PATH.

$vf = Join-Path $Out "flow"
New-Item -ItemType Directory -Force -Path $vf | Out-Null
$vfFfmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue
$vfProbe = Get-Command ffprobe -ErrorAction SilentlyContinue
Check "video-flow ffmpeg and ffprobe on PATH" (($null -ne $vfFfmpeg) -and ($null -ne $vfProbe)) "needed to make and read the fixtures"
if (($null -eq $vfFfmpeg) -or ($null -eq $vfProbe)) { return }

function VF-Probe([string]$File) {
    $j = & $vfProbe.Source -v error -print_format json -show_format -show_streams -i $File | Out-String | ConvertFrom-Json
    $v = @($j.streams | Where-Object { $_.codec_type -eq 'video' })[0]
    $a = @($j.streams | Where-Object { $_.codec_type -eq 'audio' })
    return @{ Dur = [double]$j.format.duration; W = [int]$v.width; H = [int]$v.height; Audio = ($a.Count -gt 0); Size = (Get-Item $File).Length }
}

function VF-Frames([string]$File) {
    $n = & $vfProbe.Source -v error -count_frames -select_streams v:0 -show_entries stream=nb_read_frames -of csv=p=0 -i $File | Out-String
    return [int]($n.Trim())
}

function VF-Near([string]$Name, [double]$Actual, [double]$Want, [double]$Tol) {
    Check $Name ([math]::Abs($Actual - $Want) -le $Tol) ("got {0:N3}, want {1} +-{2}" -f $Actual, $Want, $Tol)
}

# Mean volume of a part of a file, in dB; -200 for digital silence.
function VF-Volume([string]$File, [double]$From, [double]$Len) {
    $err = & $vfFfmpeg.Source -hide_banner -ss $From -t $Len -i $File -vn -af volumedetect -f null - 2>&1 | Out-String
    if ($err -match 'mean_volume:\s*(-?[\d.]+|-inf) dB') {
        if ($Matches[1] -eq '-inf') { return -200.0 }
        return [double]::Parse($Matches[1], [System.Globalization.CultureInfo]::InvariantCulture)
    }
    return 0.0
}

function VF-Run([string[]]$Inputs, [string[]]$Opts) {
    $extra = @()
    foreach ($o in $Opts) { $extra += @("--opt", $o) }
    Invoke-Tool "vid-flow" $Inputs $extra
    $res = Join-Path $Out "run-vid-flow.json"
    if (-not (Test-Path $res)) { return $null }
    $doc = Get-Content -Raw $res | ConvertFrom-Json
    return @($doc.outputs)[0]
}

# ---- fixtures: a 2-minute screen-recording stand-in with sound, and a busy clip bigger than the WhatsApp limit ----
$rec = Join-Path $vf "rec 120.mp4"
& $vfFfmpeg.Source -hide_banner -loglevel error -y -f lavfi -i "testsrc2=size=640x360:rate=15:duration=120" -f lavfi -i "sine=frequency=440:sample_rate=44100:duration=120" `
    -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -shortest $rec
Check "video-flow fixture 2 min clip" ((Test-Path $rec) -and ((VF-Probe $rec).Dur -gt 119)) $rec

$busy = Join-Path $vf "busy 60.mp4"
& $vfFfmpeg.Source -hide_banner -loglevel error -y -f lavfi -i "testsrc2=size=1280x720:rate=30,noise=alls=35:allf=t+u" -f lavfi -i "sine=frequency=330:sample_rate=44100" `
    -t 60 -c:v libx264 -preset ultrafast -b:v 6M -pix_fmt yuv420p -c:a aac $busy
$busyBytes = if (Test-Path $busy) { (Get-Item $busy).Length } else { 0 }
Check "video-flow fixture busy clip is over the WhatsApp limit" ($busyBytes -gt 16000000) "bytes=$busyBytes"

# ---- 1. trim: first 8 s and last 5 s off ----
$o = VF-Run @($rec) @("in=8", "out=115")
if ($o) {
    $p = VF-Probe $o
    VF-Near "video-flow trim duration matches the handles (8 s .. 115 s)" $p.Dur 107 0.2
    Check "video-flow trim keeps the sound" $p.Audio ("audio=" + $p.Audio)
    Check "video-flow trim keeps the picture size" (($p.W -eq 640) -and ($p.H -eq 360)) "size=$($p.W)x$($p.H)"
}

# ---- 2. mute + speed + caption in one run ----
$plain = VF-Run @($rec) @("in=8", "out=115", "mute_from=40", "mute_to=50", "speed_from=60", "speed_to=70", "speed=200")
$capped = VF-Run @($rec) @("in=8", "out=115", "mute_from=40", "mute_to=50", "speed_from=60", "speed_to=70", "speed=200", "caption=Launch demo")
if ($plain) {
    $p = VF-Probe $plain
    # 107 s selection, 10 s of it at 200 percent: 107 - 10 + 5 = 102 s.
    VF-Near "video-flow speed section length (107 - 10 + 5)" $p.Dur 102 0.4
    # Source 40..50 s is output 32..42 s (the selection starts at 8 s).
    $muted = VF-Volume $plain 34 6
    $before = VF-Volume $plain 10 8
    $after = VF-Volume $plain 80 8
    Check "video-flow muted section is silent" ($muted -lt -50) "mean volume $muted dB"
    Check "video-flow sound before the muted section" ($before -gt -30) "mean volume $before dB"
    Check "video-flow sound after the muted section" ($after -gt -30) "mean volume $after dB"
}
if ($plain -and $capped) {
    $fa = Join-Path $vf "frame-plain.raw"; $fb = Join-Path $vf "frame-caption.raw"
    foreach ($pair in @(@($plain, $fa), @($capped, $fb))) {
        & $vfFfmpeg.Source -hide_banner -loglevel error -y -ss 20 -i $pair[0] -frames:v 1 -vf "scale=320:180,format=gray" -f rawvideo $pair[1]
    }
    $ba = [System.IO.File]::ReadAllBytes($fa); $bb = [System.IO.File]::ReadAllBytes($fb)
    $diff = 0
    if ($ba.Length -eq $bb.Length) { for ($i = 0; $i -lt $ba.Length; $i++) { if ([math]::Abs($ba[$i] - $bb[$i]) -gt 24) { $diff++ } } }
    Check "video-flow caption is drawn on the picture" (($ba.Length -eq $bb.Length) -and ($diff -gt 150)) "pixels changed=$diff of $($ba.Length)"
}

# ---- 3. WhatsApp preset ----
$o = VF-Run @($busy) @("preset=whatsapp", "in=2", "out=57")
if ($o) {
    $p = VF-Probe $o
    Check "video-flow WhatsApp preset is under 16 MB" ($p.Size -lt 16000000) "bytes=$($p.Size) (source $busyBytes)"
    Check "video-flow WhatsApp preset is a real encode, not a shrunk-to-nothing file" ($p.Size -gt 5000000) "bytes=$($p.Size)"
    VF-Near "video-flow WhatsApp preset keeps the selection length" $p.Dur 55 0.3
    Check "video-flow WhatsApp preset keeps the sound" $p.Audio ("audio=" + $p.Audio)
}

# ---- 4. GIF preset: 6 s ----
$o = VF-Run @($rec) @("preset=gif", "in=60", "out=66")
if ($o) {
    $p = VF-Probe $o
    $frames = VF-Frames $o
    Check "video-flow GIF is a .gif" ($o -like "*.gif") $o
    Check "video-flow GIF frame count (6 s at 12 fps = 72)" (($frames -ge 69) -and ($frames -le 75)) "frames=$frames"
    Check "video-flow GIF is 480 px wide" ($p.W -eq 480) "width=$($p.W)"
    VF-Near "video-flow GIF length" $p.Dur 6 0.5
}

# ---- 5. Reel and YouTube shapes ----
$o = VF-Run @($rec) @("preset=reel", "in=10", "out=14")
if ($o) { $p = VF-Probe $o; Check "video-flow Reel preset is 1080x1920" (($p.W -eq 1080) -and ($p.H -eq 1920)) "size=$($p.W)x$($p.H)" }
$o = VF-Run @($rec) @("preset=youtube", "in=10", "out=14")
if ($o) { $p = VF-Probe $o; Check "video-flow YouTube preset keeps a small clip's size" (($p.W -eq 640) -and ($p.H -eq 360)) "size=$($p.W)x$($p.H)" }

# A bad selection says why and writes nothing.
$bad = Invoke-App @("--tool", "vid-flow", "--in", $rec, "--out", (Join-Path $vf "bad"), "--opt", "in=500")
Check "video-flow selection past the end fails with the reason" (($bad.Code -eq 1) -and ($bad.Err -match "'in'")) "exit=$($bad.Code) $($bad.Err)"
Check "video-flow failed run leaves no file" (-not (Test-Path (Join-Path $vf "bad")) -or (@(Get-ChildItem (Join-Path $vf "bad") -File -ErrorAction SilentlyContinue).Count -eq 0)) "bad folder holds files"

# ---- 6. Snapshots of the filmstrip page ----
$hashes = @{}
foreach ($state in @("handles", "presets", "gif")) {
    $png = Join-Path $Out ("video-flow-" + $state + ".png")
    $r = Invoke-App @("--app", "video", "--snapshot", $png, "--view", "vid-trim", "--flow-file", $rec, "--flow-state", $state) 240
    $size = if (Test-Path $png) { (Get-Item $png).Length } else { 0 }
    Check "video-flow snapshot $state" (($r.Code -eq 0) -and ($size -gt 30000)) "exit=$($r.Code) bytes=$size $($r.Err)"
    if ($size -gt 0) { $hashes[$state] = (Get-FileHash -Algorithm SHA256 $png).Hash }
}
Check "video-flow snapshots show three different states" (@($hashes.Values | Select-Object -Unique).Count -eq 3) "hashes: $($hashes.Values -join ' ')"
$noPng = Join-Path $Out "video-flow-nope.png"
$r = Invoke-App @("--app", "video", "--snapshot", $noPng, "--view", "vid-trim", "--flow-file", $rec, "--flow-state", "nope") 240
Check "video-flow snapshot of an unknown state fails loudly" (($r.Code -eq 1) -and ($r.Err -match "nope")) "exit=$($r.Code) $($r.Err)"

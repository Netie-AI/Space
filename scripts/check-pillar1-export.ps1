# ffprobe a Windows Export. Expect ~3.1s (1.6 + 2.0 - 0.5 wipe).
# Does not replace the human Select->Split->Drag->Delete->Fade->Export clicks.
param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Path)) {
    Write-Error "missing file: $Path"
    exit 2
}
$ffprobe = Get-Command ffprobe -ErrorAction SilentlyContinue
if (-not $ffprobe) {
    Write-Error "ffprobe not on PATH"
    exit 2
}
$dur = & ffprobe -v error -show_entries format=duration -of csv=p=0 -- $Path
$d = [double]$dur
$ok = ($d -ge 2.8) -and ($d -le 3.4)
$msg = "duration={0:N3}s expect 2.8..3.4 ({1})" -f $d, $(if ($ok) { 'OK' } else { 'FAIL' })
Write-Host $msg
if (-not $ok) { exit 1 }

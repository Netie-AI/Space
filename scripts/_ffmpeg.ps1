# Shared locate for playbook scripts. install.bat puts ffmpeg in tools/ffmpeg
# (gitignored) and does not add it to PATH.
$script:NetieRoot = Split-Path $PSScriptRoot -Parent

function Get-NetieTool {
    param([Parameter(Mandatory)][string]$Name)
    $dir = Join-Path $script:NetieRoot 'tools\ffmpeg'
    foreach ($n in @("$Name.exe", $Name)) {
        $p = Join-Path $dir $n
        if (Test-Path -LiteralPath $p) { return $p }
    }
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Get-NetieFfmpeg { Get-NetieTool ffmpeg }
function Get-NetieFfprobe { Get-NetieTool ffprobe }

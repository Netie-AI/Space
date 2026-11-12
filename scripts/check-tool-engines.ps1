#Requires -Version 5.1
<#
.SYNOPSIS
  Shipping-build licence gate: no AGPL engine may be inside a package.
.DESCRIPTION
  Scans a package folder (the publish output, dist\NetieSpace) for banned engines by package,
  directory and binary name, never by file contents (a PDF written by MuPDF is not the engine).
  Banned: PyMuPDF, pymupdf4llm, Ghostscript, OnlyOffice.
  Prints  BANNED <engine> <path>  per hit and  OK <n> entries scanned, 0 banned  on a pass.
  Notice rule: when tools\ffmpeg\ffmpeg.exe is in the package, THIRD-PARTY-NOTICES.txt must sit at
  the package root and mention FFmpeg and GPL.
  There is no override switch. A dev build that needs PyMuPDF runs from the repo; it is not a package.
.PARAMETER Dist
  Folder to scan.
.PARAMETER SelfTest
  Plants one marker per banned engine in a temp folder and asserts the scan fails naming all four,
  then asserts a clean folder (with a xrefstream-mupdf.pdf decoy) passes, then the notice rule.
  Exit 0 only when every half held.
.NOTES
  Exit codes: 0 pass, 1 banned engine / missing notice / bad arguments.
  Runs under Windows PowerShell 5.1 and pwsh. ASCII only.
#>
param(
    [string]$Dist,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

# Names are matched against a single file or directory name, anchored. First match wins, so
# pymupdf4llm is tested before the broader pymupdf prefix.
$script:Rules = @(
    @{ Engine = 'pymupdf4llm'; Pattern = '^pymupdf4llm' },
    @{ Engine = 'PyMuPDF';     Pattern = '^pymupdf' },
    @{ Engine = 'PyMuPDF';     Pattern = '^fitz(-.*)?$'; DirOnly = $true },
    @{ Engine = 'PyMuPDF';     Pattern = '^(_mupdf|mupdfcpp|libmupdf)' },
    @{ Engine = 'Ghostscript'; Pattern = '^(gswin32c?|gswin64c?|gsdll32|gsdll64)\.(exe|dll)$' },
    @{ Engine = 'OnlyOffice';  Pattern = '^(x2t\.exe$|onlyoffice|documentserver)' }
)

# A METADATA file inside a *.dist-info names its package on a "Name:" line (renamed folders still hit).
$script:MetadataNames = @(
    @{ Engine = 'pymupdf4llm'; Pattern = '^Name:\s*pymupdf4llm\s*$' },
    @{ Engine = 'PyMuPDF';     Pattern = '^Name:\s*pymupdf\s*$' }
)

function Find-BannedEngines {
    param([string]$Path)

    $hits = New-Object System.Collections.ArrayList
    $entries = @(Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue)
    foreach ($entry in $entries) {
        $name = $entry.Name
        $isDir = $entry.PSIsContainer
        foreach ($rule in $script:Rules) {
            if ($rule.DirOnly -and -not $isDir) { continue }
            if ($name -match $rule.Pattern) {
                [void]$hits.Add([pscustomobject]@{ Engine = $rule.Engine; Path = $entry.FullName })
                break
            }
        }

        if (-not $isDir -and $name -ceq 'METADATA') {
            $head = @(Get-Content -LiteralPath $entry.FullName -TotalCount 40 -ErrorAction SilentlyContinue)
            foreach ($rule in $script:MetadataNames) {
                $found = $false
                foreach ($line in $head) {
                    if ($line -match $rule.Pattern) { $found = $true; break }
                }
                if ($found) {
                    [void]$hits.Add([pscustomobject]@{ Engine = $rule.Engine; Path = $entry.FullName })
                    break
                }
            }
        }
    }

    return [pscustomobject]@{ Scanned = $entries.Count; Hits = @($hits) }
}

function Test-Notice {
    param([string]$Path)

    $ffmpeg = Join-Path (Join-Path (Join-Path $Path 'tools') 'ffmpeg') 'ffmpeg.exe'
    if (-not (Test-Path -LiteralPath $ffmpeg)) { return $null }

    $notice = Join-Path $Path 'THIRD-PARTY-NOTICES.txt'
    if (-not (Test-Path -LiteralPath $notice)) {
        return "NOTICE missing: $ffmpeg is in the package but THIRD-PARTY-NOTICES.txt is not at $Path"
    }
    $text = Get-Content -LiteralPath $notice -Raw
    if ($text -notmatch 'FFmpeg' -or $text -notmatch 'GPL') {
        return "NOTICE incomplete: $notice must mention FFmpeg and GPL"
    }
    return $null
}

function Invoke-Check {
    param([string]$Path, [switch]$Quiet)

    $result = Find-BannedEngines -Path $Path
    $noticeProblem = Test-Notice -Path $Path
    $failed = $false

    foreach ($hit in $result.Hits) {
        if (-not $Quiet) { Write-Host "BANNED $($hit.Engine) $($hit.Path)" }
        $failed = $true
    }
    if ($noticeProblem) {
        if (-not $Quiet) { Write-Host $noticeProblem }
        $failed = $true
    }
    if (-not $failed -and -not $Quiet) {
        Write-Host "OK $($result.Scanned) entries scanned, 0 banned"
    }

    return [pscustomobject]@{ Passed = (-not $failed); Hits = $result.Hits; Notice = $noticeProblem; Scanned = $result.Scanned }
}

function New-TempDir {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('netie-engine-gate-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    return $dir
}

function Write-Marker {
    param([string]$Root, [string]$Relative, [string]$Content = 'x')

    # Markers are written with backslashes; pwsh on Linux needs the native separator.
    $full = Join-Path $Root ($Relative.Replace('\', [string][System.IO.Path]::DirectorySeparatorChar))
    New-Item -ItemType Directory -Path (Split-Path $full -Parent) -Force | Out-Null
    Set-Content -LiteralPath $full -Value $Content -Encoding ASCII
}

function Invoke-SelfTest {
    $ok = $true
    $dirs = @()
    try {
        # Half 1: one marker per banned engine, the scan must fail and name exactly those four.
        $bad = New-TempDir; $dirs += $bad
        Write-Marker $bad 'site-packages\pymupdf-1.0.dist-info\METADATA' "Metadata-Version: 2.1`nName: PyMuPDF`nVersion: 1.0"
        Write-Marker $bad 'site-packages\pymupdf4llm-1.0.dist-info\METADATA' "Metadata-Version: 2.1`nName: pymupdf4llm`nVersion: 1.0"
        Write-Marker $bad 'gs\gsdll64.dll'
        Write-Marker $bad 'oo\x2t.exe'
        $r = Invoke-Check -Path $bad -Quiet
        $engines = @($r.Hits | ForEach-Object { $_.Engine } | Sort-Object -Unique)
        $wanted = @('Ghostscript', 'OnlyOffice', 'PyMuPDF', 'pymupdf4llm') | Sort-Object
        if ($r.Passed -or (($engines -join ',') -ne ($wanted -join ','))) {
            Write-Host "SELFTEST planted-markers FAIL: passed=$($r.Passed) engines=$($engines -join ',')"
            $ok = $false
        } else {
            Write-Host "SELFTEST planted-markers PASS: failed as required, named $($engines -join ', ')"
        }

        # Half 1b: a renamed dist-info is still caught through its METADATA Name line.
        $renamed = New-TempDir; $dirs += $renamed
        Write-Marker $renamed 'sp\totally-innocent.dist-info\METADATA' "Name: PyMuPDF`nVersion: 9"
        $r = Invoke-Check -Path $renamed -Quiet
        if ($r.Passed -or @($r.Hits | Where-Object { $_.Engine -eq 'PyMuPDF' }).Count -lt 1) {
            Write-Host 'SELFTEST renamed-metadata FAIL'
            $ok = $false
        } else {
            Write-Host 'SELFTEST renamed-metadata PASS: METADATA Name line caught a renamed folder'
        }

        # Half 2: a clean folder with a MuPDF-written decoy PDF must pass.
        $clean = New-TempDir; $dirs += $clean
        Write-Marker $clean 'Fixtures\xrefstream-mupdf.pdf' '%PDF-1.5 produced by MuPDF'
        Write-Marker $clean 'NetieSpace.exe'
        $r = Invoke-Check -Path $clean -Quiet
        if (-not $r.Passed) {
            Write-Host "SELFTEST clean-folder FAIL: flagged $($r.Hits.Count) entries"
            $ok = $false
        } else {
            Write-Host "SELFTEST clean-folder PASS: $($r.Scanned) entries scanned, decoy not flagged"
        }

        # Half 3: the notice rule.
        $noNotice = New-TempDir; $dirs += $noNotice
        Write-Marker $noNotice 'tools\ffmpeg\ffmpeg.exe'
        $r = Invoke-Check -Path $noNotice -Quiet
        $withNotice = New-TempDir; $dirs += $withNotice
        Write-Marker $withNotice 'tools\ffmpeg\ffmpeg.exe'
        Write-Marker $withNotice 'THIRD-PARTY-NOTICES.txt' 'FFmpeg is licensed under the GPL.'
        $r2 = Invoke-Check -Path $withNotice -Quiet
        if ($r.Passed -or -not $r2.Passed) {
            Write-Host "SELFTEST notice-rule FAIL: without=$($r.Passed) (want False) with=$($r2.Passed) (want True)"
            $ok = $false
        } else {
            Write-Host 'SELFTEST notice-rule PASS: ffmpeg without notice fails, with notice passes'
        }
    } finally {
        foreach ($d in $dirs) {
            Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    if ($ok) { Write-Host 'SELFTEST OK'; return 0 }
    Write-Host 'SELFTEST FAILED'
    return 1
}

if ($SelfTest) {
    exit (Invoke-SelfTest)
}

if (-not $Dist) {
    Write-Host 'Usage: check-tool-engines.ps1 -Dist <folder> | -SelfTest'
    exit 1
}
if (-not (Test-Path -LiteralPath $Dist -PathType Container)) {
    Write-Host "Dist folder not found: $Dist"
    exit 1
}

$final = Invoke-Check -Path (Resolve-Path -LiteralPath $Dist).Path
if ($final.Passed) { exit 0 }
exit 1

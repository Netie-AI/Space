# Temporary stress test mirroring ExplorerFileService.ResolveNamesUnderDesktop
# (probe extensions, prefer video on ambiguity, strip " - Shortcut")

$ErrorActionPreference = 'Stop'

$DesktopHiddenProbeExtensions = @(
    '.mp4', '.mkv', '.mov', '.avi', '.webm', '.wmv', '.m4v', '.mpeg', '.mpg',
    '.hevc', '.h265', '.mp3', '.wav', '.flac', '.m4a', '.aac',
    '.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.tif', '.tiff',
    '.pdf', '.docx', '.doc', '.pptx', '.ppt', '.xlsx', '.xls',
    '.txt', '.md', '.csv', '.json', '.xml', '.html', '.htm',
    '.zip', '.7z', '.rar', '.lnk'
)

$DesktopPreferVideoExt = [System.Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase
)
@(
    '.mp4', '.webm', '.mkv', '.avi', '.mov', '.wmv', '.m4v', '.mpeg', '.mpg',
    '.hevc', '.h265', '.h264', '.3gp', '.flv', '.ts', '.m2ts', '.mts', '.ogv', '.vob'
) | ForEach-Object { [void]$DesktopPreferVideoExt.Add($_) }

function Sanitize-DesktopUiName([string]$name) {
    $clean = $name.Trim()
    $shortcutSuffix = ' - Shortcut'
    if ($clean.EndsWith($shortcutSuffix, [StringComparison]::OrdinalIgnoreCase)) {
        $clean = $clean.Substring(0, $clean.Length - $shortcutSuffix.Length).TrimEnd()
    }
    return $clean
}

function Resolve-NamesUnderDesktop {
    param(
        [string[]]$Names,
        [string]$DesktopDir
    )
    if (-not $Names -or $Names.Count -eq 0) { return $null }
    if (-not (Test-Path -LiteralPath $DesktopDir)) { return $null }

    foreach ($name in $Names) {
        $clean = Sanitize-DesktopUiName $name
        if ([string]::IsNullOrWhiteSpace($clean)) { continue }

        $path = Join-Path $DesktopDir $clean
        if (Test-Path -LiteralPath $path) { return $path }

        # Extension-hidden labels: try common extensions before stem scan
        if (-not [System.IO.Path]::HasExtension($clean)) {
            foreach ($ext in $DesktopHiddenProbeExtensions) {
                $probed = Join-Path $DesktopDir ($clean + $ext)
                if (Test-Path -LiteralPath $probed) { return $probed }
            }
        }

        # Stem / filename match - prefer unique, else prefer video when ambiguous
        try {
            $matches = @(Get-ChildItem -LiteralPath $DesktopDir -Force | Where-Object {
                $fileName = $_.Name
                $stem = [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
                [string]::Equals($fileName, $clean, [StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals($stem, $clean, [StringComparison]::OrdinalIgnoreCase)
            } | ForEach-Object { $_.FullName })

            if ($matches.Count -eq 1) { return $matches[0] }
            if ($matches.Count -gt 1) {
                $video = $matches | Where-Object {
                    $DesktopPreferVideoExt.Contains([System.IO.Path]::GetExtension($_))
                } | Select-Object -First 1
                if ($null -ne $video) { return $video }
            }
        }
        catch {
            # skip
        }
    }
    return $null
}

function Assert-Case {
    param(
        [string]$Label,
        [string]$FixtureDir,
        [string[]]$CreateFiles,
        [string]$InputName,
        [string]$ExpectedFileName
    )

    New-Item -ItemType Directory -Force -Path $FixtureDir | Out-Null
    foreach ($f in $CreateFiles) {
        $full = Join-Path $FixtureDir $f
        New-Item -ItemType File -Force -Path $full | Out-Null
    }

    $resolved = Resolve-NamesUnderDesktop -Names @($InputName) -DesktopDir $FixtureDir
    $actualName = if ($resolved) { [System.IO.Path]::GetFileName($resolved) } else { '<null>' }
    $ok = [string]::Equals($actualName, $ExpectedFileName, [StringComparison]::OrdinalIgnoreCase)
    $status = if ($ok) { 'PASS' } else { 'FAIL' }
    Write-Host ("{0}: {1}  (input='{2}' expected='{3}' got='{4}')" -f $status, $Label, $InputName, $ExpectedFileName, $actualName)
    return $ok
}

$root = Join-Path ([System.IO.Path]::GetTempPath()) ("netie_stress_desktop_resolve_" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $root | Out-Null
$pass = 0
$fail = 0

try {
    Write-Host "Fixture root: $root"
    Write-Host ""

    $r1 = Assert-Case -Label 'Demo.mp4 only' `
        -FixtureDir (Join-Path $root 'case1') `
        -CreateFiles @('Demo.mp4') `
        -InputName 'Demo' `
        -ExpectedFileName 'Demo.mp4'
    if ($r1) { $pass++ } else { $fail++ }

    $r2 = Assert-Case -Label 'Demo.mp4+Demo.txt prefer video' `
        -FixtureDir (Join-Path $root 'case2') `
        -CreateFiles @('Demo.mp4', 'Demo.txt') `
        -InputName 'Demo' `
        -ExpectedFileName 'Demo.mp4'
    if ($r2) { $pass++ } else { $fail++ }

    $r3 = Assert-Case -Label 'Report.pdf+Report.docx (probe .pdf first)' `
        -FixtureDir (Join-Path $root 'case3') `
        -CreateFiles @('Report.pdf', 'Report.docx') `
        -InputName 'Report' `
        -ExpectedFileName 'Report.pdf'
    if ($r3) { $pass++ } else { $fail++ }

    $r4 = Assert-Case -Label 'Movie - Shortcut -> Movie.lnk' `
        -FixtureDir (Join-Path $root 'case4') `
        -CreateFiles @('Movie.lnk') `
        -InputName 'Movie - Shortcut' `
        -ExpectedFileName 'Movie.lnk'
    if ($r4) { $pass++ } else { $fail++ }

    Write-Host ""
    Write-Host ("Summary: {0} PASS, {1} FAIL" -f $pass, $fail)
    if ($fail -gt 0) { exit 1 } else { exit 0 }
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Cleaned up fixture root."
    }
}

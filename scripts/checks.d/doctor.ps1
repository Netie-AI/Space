# Lane S50-E2: honest availability. --doctor must give every engine a definite state and
# agree with --list-tools about every tool whose engine is missing.

$docJson = Join-Path $Out "doctor.json"
$listJson = Join-Path $Out "doctor-list-tools.json"

$d = Invoke-App @("--doctor", "--json", $docJson) 60
Check "doctor exits 0" ($d.Code -eq 0) "exit=$($d.Code) $($d.Err)"
$d.Out | Set-Content -Path (Join-Path $Out "doctor.txt") -Encoding UTF8
Check "doctor json written" (Test-Path $docJson) $docJson

$l = Invoke-App @("--list-tools", "--json", $listJson) 60
Check "list-tools for doctor compare" (($l.Code -eq 0) -and (Test-Path $listJson)) "exit=$($l.Code) $($l.Err)"

$bad = Invoke-App @("--doctor", "--tool", "pdf-ocr") 30
Check "doctor with bad arguments exits 2" ($bad.Code -eq 2) "exit=$($bad.Code) $($bad.Err)"

if ((Test-Path $docJson) -and (Test-Path $listJson)) {
    $doc = Get-Content $docJson -Raw | ConvertFrom-Json
    $list = Get-Content $listJson -Raw | ConvertFrom-Json
    $engines = @($doc.engines)

    $indefinite = @($engines | Where-Object { ($_.present -isnot [bool]) -or ((-not $_.present) -and (-not $_.reason)) })
    Check "every engine has a definite state" (($engines.Count -gt 0) -and ($indefinite.Count -eq 0)) "engines=$($engines.Count) indefinite=$($indefinite.Count)"

    $timedOut = @($engines | Where-Object { $_.reason -and ($_.reason -match "timed out") })
    Check "no probe timed out" ($timedOut.Count -eq 0) (($timedOut | ForEach-Object { $_.name }) -join ", ")

    $byId = @{}
    foreach ($t in $list.tools) { $byId[$t.id] = $t }
    $mismatch = New-Object System.Collections.Generic.List[string]
    $compared = 0
    foreach ($e in ($engines | Where-Object { -not $_.present })) {
        foreach ($id in @($e.toolIds)) {
            $t = $byId[$id]
            if ($null -eq $t) { $mismatch.Add("$id not in list-tools"); continue }
            if ($t.available) { $mismatch.Add("$id listed available but engine '$($e.name)' is missing"); continue }
            if (($t.reason -eq "cloud only") -or ($t.reason -eq "needs Space#3 disclose")) { continue }
            $compared++
            if ($t.reason -ne $e.reason) { $mismatch.Add("$id reason '$($t.reason)' vs doctor '$($e.reason)'") }
        }
    }
    Check "missing engines agree with list-tools" ($mismatch.Count -eq 0) "compared=$compared; $($mismatch -join '; ')"

    $present = @($engines | Where-Object { $_.present }).Count
    $missing = @($engines | Where-Object { -not $_.present }).Count
    "doctor: engines=$($engines.Count) present=$present missing=$missing" | Add-Content -Path (Join-Path $Out "doctor.txt")
}

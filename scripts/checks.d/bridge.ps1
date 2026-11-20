# Send-to-phone through the real exe (Space #3: nothing leaves the PC without --yes).
# 1. Without --yes: disclosure line, nothing sent, exit 2, json status not_confirmed.
# 2. With --yes and AirGPT not running (a closed loopback port): honest failure, exit 1, json status not_running.
# 3. Never the tray: the process exits on its own in both cases.

$sendIn = Join-Path $Out "bridge-send.txt"
"netie bridge check" | Set-Content -Path $sendIn -Encoding ASCII
$noYesJson = Join-Path $Out "bridge-no-yes.json"
$closedJson = Join-Path $Out "bridge-not-running.json"

$prevUrl = $env:AIRGPT_URL
try {
    # Port 9 (discard) is closed on the runner: AirGPT is certainly not there.
    $env:AIRGPT_URL = "http://127.0.0.1:9"

    $r = Invoke-App @("--send-to-phone", $sendIn, "--json", $noYesJson) 60
    $status = ""
    if (Test-Path $noYesJson) { $status = (Get-Content $noYesJson -Raw | ConvertFrom-Json).status }
    Check "send-to-phone without --yes sends nothing" (($r.Code -eq 2) -and ($r.Out -match "Not sent\.") -and ($status -eq "not_confirmed")) "exit=$($r.Code) status=$status out=$($r.Out.Trim()) $($r.Err)"

    $r = Invoke-App @("--send-to-phone", $sendIn, "--yes", "--json", $closedJson) 60
    $status = ""
    if (Test-Path $closedJson) { $status = (Get-Content $closedJson -Raw | ConvertFrom-Json).status }
    Check "send-to-phone with AirGPT down fails honestly" (($r.Code -eq 1) -and ($status -eq "not_running")) "exit=$($r.Code) status=$status $($r.Err.Trim())"

    $r = Invoke-App @("--send-to-phone") 60
    Check "send-to-phone without a file is a usage error" ($r.Code -eq 2) "exit=$($r.Code) $($r.Err.Trim())"
}
finally {
    $env:AIRGPT_URL = $prevUrl
}

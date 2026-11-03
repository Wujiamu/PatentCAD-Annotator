param(
    [string]$PackageDirectory = "",
    [string]$AcCoreConsole = "",
    [string]$OutputRoot = "",
    [int]$TimeoutSeconds = 120
)

# R-028 verification: does a real AutoCAD host accept, persist and re-read the
# PATBRACE Xrecord group codes 1000/1040/1070? The documented Xrecord DXF
# contract lists object codes 1-369 (with exclusions), so 1000/1040/1070 are
# outside that range. Only a real host round trip can settle whether it works.
#
# Two Core Console runs on the same DWG:
#   run A: create a brace at a constant non-zero elevation, SAVE, QUIT
#   run B: reopen the saved DWG and read the brace back, QUIT without saving
# A PASS needs: creation succeeded, the saved file reopens, and the re-read
# definition matches what was written (including the Z elevation).

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) { throw "PackageDirectory is required" }
if ([string]::IsNullOrWhiteSpace($AcCoreConsole)) { throw "AcCoreConsole is required" }
if (-not (Test-Path -LiteralPath $AcCoreConsole)) { throw "Core Console not found: $AcCoreConsole" }
$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$dll = Join-Path $PackageDirectory "PatentMarker.dll"
if (-not (Test-Path -LiteralPath $dll)) { throw "Package DLL missing: $dll" }

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path ([IO.Path]::GetTempPath()) ("PatentMarker-r028-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$OutputRoot = (Resolve-Path -LiteralPath $OutputRoot).Path
$runId = (Get-Date -Format "yyyyMMdd-HHmmss") + "-" + ([guid]::NewGuid().ToString("N").Substring(0, 8))
$dllHash = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash

function Invoke-CoreScript([string]$scrPath, [string]$dwgPath, [string]$tag) {
    $argString = '/i "' + $dwgPath + '" /s "' + $scrPath + '" /l en-US'
    $stdout = Join-Path $OutputRoot "$tag.stdout.txt"
    $stderr = Join-Path $OutputRoot "$tag.stderr.txt"
    $proc = Start-Process -FilePath $AcCoreConsole -ArgumentList $argString -PassThru `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden
    if (-not $proc.WaitForExit($TimeoutSeconds * 1000)) {
        try { $proc.Kill() } catch { }
        throw "Core Console timed out after $TimeoutSeconds s during $tag"
    }
    # -PassThru objects keep a stale ExitCode unless the handle is refreshed.
    $proc.WaitForExit()
    $exitCode = $proc.ExitCode
    if ($null -eq $exitCode) { $exitCode = -1 }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Stdout = $stdout
        Stderr = $stderr
        Text = (Get-Content -LiteralPath $stdout -Encoding Unicode -ErrorAction SilentlyContinue) -join "`n"
    }
}

# ---- Run A: create + save ---------------------------------------------------
$dwg = Join-Path $OutputRoot "r028-brace.dwg"
$scrA = Join-Path $OutputRoot "runA.scr"
@(
    'FILEDIA 0',
    'CMDDIA 0',
    ('NETLOAD "' + $dll + '"'),
    'PATBRACE',
    '10,10,2',
    '10,30,2',
    '6,20,2',
    'ZOOM E',
    'QSAVE',
    'QUIT',
    'Y'
) | Set-Content -LiteralPath $scrA -Encoding ascii
# A brand new empty drawing: Core Console needs an existing file to open.
# Create one by opening a fresh template and saving it first.
$seed = Join-Path $OutputRoot "seed.dwg"
$scrSeed = Join-Path $OutputRoot "runSeed.scr"
@('FILEDIA 0', 'CMDDIA 0', ('SAVEAS 2018 "' + $dwg + '"'), 'QUIT', 'Y') |
    Set-Content -LiteralPath $scrSeed -Encoding ascii
$seedRun = Invoke-CoreScript $scrSeed "" "seed"
if (-not (Test-Path -LiteralPath $dwg)) { throw "seed run did not create the drawing: $dwg" }

$runA = Invoke-CoreScript $scrA $dwg "runA"
$dwgHashBefore = (Get-FileHash -LiteralPath $dwg -Algorithm SHA256).Hash

# ---- Run B: reopen and read back -------------------------------------------
$scrB = Join-Path $OutputRoot "runB.scr"
@(
    'FILEDIA 0',
    'CMDDIA 0',
    ('NETLOAD "' + $dll + '"'),
    'PATGEOMVERIFY',
    'QUIT',
    'Y'
) | Set-Content -LiteralPath $scrB -Encoding ascii
$runB = Invoke-CoreScript $scrB $dwg "runB"

$report = Join-Path $PackageDirectory "PatentMarker-geom-verify.txt"
$reportText = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { "" }

$evidence = [ordered]@{
    run_id = $runId
    package_directory = $PackageDirectory
    candidate_dll_sha256 = $dllHash
    accoreconsole = $AcCoreConsole
    drawing = $dwg
    drawing_sha256_after_save = $dwgHashBefore
    runA_exit = $runA.ExitCode
    runB_exit = $runB.ExitCode
    runA_stdout = $runA.Stdout
    runB_stdout = $runB.Stdout
    report = $report
    report_present = (Test-Path -LiteralPath $report)
}
# Judge the round trip on the brace assertions only: this probe deliberately
# uses a drawing with no text annotations, so PATGEOMVERIFY reports texts=0 and
# overall FAIL. What matters for R-028 is that the brace created in run A is
# re-read intact in run B, including its elevation.
$bracePass = 0
$braceFail = 0
$bracePlanarPass = $false
foreach ($line in ($reportText -split "`n")) {
    if ($line -match "^\s+\[PASS\]\s+B\d") { $bracePass++ }
    elseif ($line -match "^\s+\[FAIL\]\s+B\d") { $braceFail++ }
    if ($line -match "\[PASS\]\s+B0c planar-contract") { $bracePlanarPass = $true }
}
$summaryLine = ""
foreach ($line in ($reportText -split "`n")) { if ($line -match "^SUMMARY:") { $summaryLine = $line.Trim() } }
# Judge on the business assertions, not on Core Console's exit code: the
# console exits 0 even when a command fails, and its ExitCode is not always
# observable through Start-Process. The report itself is the evidence.
$verdict = if ($braceFail -eq 0 -and $bracePass -ge 10 -and $bracePlanarPass -and
    $reportText -match "braces=1 passed=1 failed=0 unreadable=0") { "PASS" } else { "FAIL" }

$evidence = [ordered]@{
    run_id = $runId
    package_directory = $PackageDirectory
    candidate_dll_sha256 = $dllHash
    accoreconsole = $AcCoreConsole
    drawing = $dwg
    drawing_sha256_after_save = $dwgHashBefore
    runA_exit = $runA.ExitCode
    runB_exit = $runB.ExitCode
    runA_stdout = $runA.Stdout
    runB_stdout = $runB.Stdout
    report = $report
    report_present = (Test-Path -LiteralPath $report)
    brace_assertions_passed = $bracePass
    brace_assertions_failed = $braceFail
    planar_contract_pass = $bracePlanarPass
    report_summary = $summaryLine
    overall_report_result = $(if ($reportText -match "(?m)^RESULT: (\w+)") { $matches[1] } else { "" })
    r028_verdict = $verdict
    note = "overall PATGEOMVERIFY RESULT is FAIL only because this drawing has no text annotations (texts=0, requiredTexts=1); brace assertions are the R-028 target"
}
$evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputRoot "r028-evidence.json") -Encoding UTF8

Write-Output ("EVIDENCE|{0}" -f (Join-Path $OutputRoot "r028-evidence.json"))
Write-Output ("RUN_A_EXIT|{0}" -f $runA.ExitCode)
Write-Output ("RUN_B_EXIT|{0}" -f $runB.ExitCode)
Write-Output ("DRAWING|{0}|sha256={1}" -f $dwg, $dwgHashBefore)
Write-Output ("REPORT_PRESENT|{0}" -f (Test-Path -LiteralPath $report))
Write-Output ("BRACE_ASSERTIONS|passed={0} failed={1} planar={2}" -f $bracePass, $braceFail, $bracePlanarPass)
Write-Output ("OVERALL_REPORT_RESULT|{0}" -f $evidence.overall_report_result)
Write-Output ("R028_VERDICT|{0}" -f $verdict)
if ($reportText -ne "") {
    foreach ($line in ($reportText -split "`n")) {
        if ($line -match '^(RESULT|SUMMARY|braces|texts)') { Write-Output ("REPORT|{0}" -f $line.Trim()) }
    }
}

param([string]$ProjectRoot = ".")

# G23 / R-035: the wrapper must only accept a TRX that belongs to THIS run.
#
# The probe replays both attribution strategies against the same situation:
# a results directory that already holds yesterday's all-passing results.trx,
# and a run that exits 0 without producing any new TRX. The old strategy
# (search by file name, take newest) accepts the stale file; the new strategy
# (read the exact per-run path) refuses it.
#
# This is an L1 attribution check of the decision logic, not a VSTest run.

$ErrorActionPreference = "Stop"
$root = (Resolve-Path -LiteralPath $ProjectRoot).Path
$work = Join-Path $root "docs\test-evidence\repair-review-20261102\G23\attribution-probe"
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Path $work -Force | Out-Null

$staleBody = @'
<?xml version="1.0" encoding="UTF-8"?>
<TestRun><ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" /></ResultSummary></TestRun>
'@

function New-ResultsDir([string]$name, [switch]$WithStale) {
    $dir = Join-Path $work $name
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    if ($WithStale) {
        [IO.File]::WriteAllText((Join-Path $dir "results.trx"), $staleBody, (New-Object Text.UTF8Encoding($false))
        )
    }
    return $dir
}

# The pre-fix strategy: look for results.trx anywhere under the results dir,
# take the most recently written one.
function Resolve-OldWay([string]$results) {
    $trx = Get-ChildItem -LiteralPath $results -Filter "results.trx" -Recurse |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $trx) { return $null }
    return $trx.FullName
}

# The fixed strategy: this run's TRX has a name carrying this run's id.
function Resolve-NewWay([string]$results, [string]$runId) {
    $path = Join-Path $results ("results-" + $runId + ".trx")
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    return $path
}

function Counters([string]$trxPath) {
    [xml]$run = Get-Content -LiteralPath $trxPath
    $c = $run.TestRun.ResultSummary.Counters
    return [pscustomobject]@{ total = [int]$c.total; executed = [int]$c.executed; passed = [int]$c.passed }
}

function AllPass($c) {
    return ($c.total -gt 0 -and $c.executed -eq $c.total -and $c.passed -eq $c.total)
}

$passed = 0
$failed = 0
function Assert($name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Output ("PASS|" + $name + "|" + $detail); $script:passed++ }
    else { Write-Output ("FAIL|" + $name + "|" + $detail); $script:failed++ }
}

# --- Case 1: stale root-level TRX, no new TRX produced ----------------------
$d1 = New-ResultsDir "root-stale" -WithStale
$old = Resolve-OldWay $d1
$new = Resolve-NewWay $d1 ([guid]::NewGuid().ToString("N"))
Assert "root_stale_old_strategy_accepts" ($null -ne $old) ("old=" + $old)
Assert "root_stale_new_strategy_refuses" ($null -eq $new) ("new=" + $new)
if ($old) {
    $c = Counters $old
    Assert "root_stale_would_report_all_pass" (AllPass $c) ("total=" + $c.total + " passed=" + $c.passed)
}

# --- Case 2: stale TRX in a previous run's sub-directory --------------------
$d2 = New-ResultsDir "nested-stale" -WithStale
$sub = Join-Path $d2 "20261101-030000"
New-Item -ItemType Directory -Path $sub -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $sub "results.trx"), $staleBody, (New-Object Text.UTF8Encoding($false)))
$old2 = Resolve-OldWay $d2
$new2 = Resolve-NewWay $d2 ([guid]::NewGuid().ToString("N"))
Assert "nested_stale_old_strategy_accepts" ($null -ne $old2) ("old=" + $old2)
Assert "nested_stale_new_strategy_refuses" ($null -eq $new2) ("new=" + $new2)

# --- Case 3: this run did produce its own TRX -> both must accept -----------
$d3 = New-ResultsDir "own-result"
$runId = [guid]::NewGuid().ToString("N")
$ownBody = @'
<?xml version="1.0" encoding="UTF-8"?>
<TestRun><ResultSummary outcome="Completed"><Counters total="48" executed="48" passed="48" failed="0" /></ResultSummary></TestRun>
'@
[IO.File]::WriteAllText((Join-Path $d3 ("results-" + $runId + ".trx")), $ownBody, (New-Object Text.UTF8Encoding($false)))
$own = Resolve-NewWay $d3 $runId
Assert "own_result_accepted" ($null -ne $own) ("new=" + $own)
if ($own) {
    $c = Counters $own
    Assert "own_result_all_pass" (AllPass $c) ("total=" + $c.total + " executed=" + $c.executed + " passed=" + $c.passed)
}

# --- Case 4: this run produced a zero-count TRX -> must not count as pass ---
$d4 = New-ResultsDir "zero-count"
$runId4 = [guid]::NewGuid().ToString("N")
$zeroBody = @'
<?xml version="1.0" encoding="UTF-8"?>
<TestRun><ResultSummary outcome="Completed"><Counters total="0" executed="0" passed="0" failed="0" /></ResultSummary></TestRun>
'@
[IO.File]::WriteAllText((Join-Path $d4 ("results-" + $runId4 + ".trx")), $zeroBody, (New-Object Text.UTF8Encoding($false)))
$zero = Resolve-NewWay $d4 $runId4
Assert "zero_count_rejected" ($null -ne $zero -and -not (AllPass (Counters $zero))) ("total=" + (Counters $zero).total)

# --- Case 5: this run produced a partial-execution TRX ---------------------
$d5 = New-ResultsDir "partial"
$runId5 = [guid]::NewGuid().ToString("N")
$partialBody = @'
<?xml version="1.0" encoding="UTF-8"?>
<TestRun><ResultSummary outcome="Completed"><Counters total="48" executed="47" passed="47" failed="0" /></ResultSummary></TestRun>
'@
[IO.File]::WriteAllText((Join-Path $d5 ("results-" + $runId5 + ".trx")), $partialBody, (New-Object Text.UTF8Encoding($false)))
$p5 = Resolve-NewWay $d5 $runId5
Assert "partial_execution_rejected" ($null -ne $p5 -and -not (AllPass (Counters $p5))) ("executed=" + (Counters $p5).executed)

Write-Output ("SUMMARY: passed=" + $passed + " failed=" + $failed)
if ($failed -gt 0) { exit 1 }
if ($passed -eq 0) { Write-Output "SKIP/BLOCKED: no assertion executed"; exit 2 }
exit 0

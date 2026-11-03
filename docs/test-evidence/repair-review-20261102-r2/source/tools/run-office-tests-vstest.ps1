
# Runs a net48 Office code-test project through the real VSTest host.
#
# Why a copy: this workspace root carries a `Everyone: Deny DeleteSubdirectoriesAndFiles`
# ACL left by a sandbox run. Under that ACL, `dotnet vstest <dll>` inside the
# workspace reports "no available tests" and still exits 0 - the exact fake-green
# shape R-030 is about. Copying the built output to a neutral directory makes the
# same assembly discover and run every test, including the `[Theory]`/`[InlineData]`
# cases that the project's FactRunner cannot execute.
#
# The copy is a test transport detail, not a product change: the assembly, its
# dependencies and the test code are byte-identical to the build output.

param(
    [Parameter(Mandatory = $true)][string]$TestProject,
    [string]$ResultsDirectory = ""
)
$ErrorActionPreference = "Stop"
$project = (Resolve-Path -LiteralPath $TestProject).Path
$projectDir = Split-Path -Parent $project
$assemblyName = [IO.Path]::GetFileNameWithoutExtension($project)

dotnet build $project -c Release --nologo -v quiet | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Test project build failed: $project" }

$outDir = Join-Path $projectDir "bin\Release\net48"
if (-not (Test-Path -LiteralPath $outDir)) { throw "Build output not found: $outDir" }

$staging = Join-Path ([IO.Path]::GetTempPath()) ("PatentMarker-VSTest-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    Get-ChildItem -LiteralPath $outDir -Force | Copy-Item -Destination $staging -Recurse -Force
    $dll = Join-Path $staging ($assemblyName + ".dll")
    if (-not (Test-Path -LiteralPath $dll)) { throw "Test assembly not copied: $dll" }
    Write-Output ("ASSEMBLY_SHA256=" + (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLower())

    $results = if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) { $staging } else { $ResultsDirectory }
    New-Item -ItemType Directory -Path $results -Force | Out-Null
    # A stale TRX left in the results directory must never be read as this run's
    # result (R-035), so the file name carries this run's unique id and we only
    # read that exact path.
    $runId = [guid]::NewGuid().ToString("N")
    $trxName = "results-$runId.trx"
    dotnet vstest $dll --logger "trx;LogFileName=$trxName" "--ResultsDirectory:$results" | Out-Host
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw "VSTest failed for $assemblyName (exit $code)" }

    $trxPath = Join-Path $results $trxName
    if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
        throw ("VSTest exited 0 but produced no result for this run: $trxPath " +
            "(stale results from earlier runs are not this run's evidence)")
    }
    [xml]$run = Get-Content -LiteralPath $trxPath
    $c = $run.TestRun.ResultSummary.Counters
    # A run that discovered nothing is not a pass (R-030).
    if ([int]$c.total -le 0 -or [int]$c.executed -ne [int]$c.total -or
        [int]$c.passed -ne [int]$c.total) {
        throw ("$assemblyName did not execute and pass every test: " +
            "total=$($c.total) executed=$($c.executed) passed=$($c.passed) failed=$($c.failed)")
    }
    Write-Output ("PASS|VSTEST|{0}|{1}/{2}|trx={3}" -f $assemblyName, $c.passed, $c.total, $trxPath)
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}

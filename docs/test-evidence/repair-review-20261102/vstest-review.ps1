param([string]$ProjectRoot)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ProjectRoot).Path
$fixture=Join-Path $PSScriptRoot ('vstest-fixture-'+[guid]::NewGuid().ToString('N'))
$project=Join-Path $fixture 'Fixture.csproj'
$output=Join-Path $fixture 'bin/Release/net48'
$tempRoot=Join-Path $fixture 'temp'
New-Item -ItemType Directory -Path $output,$tempRoot | Out-Null
[IO.File]::WriteAllText($project,'<Project />')
[IO.File]::WriteAllText((Join-Path $output 'Fixture.dll'),'synthetic file: dotnet is injected, never executed')
$trxText='<TestRun><ResultSummary><Counters total="1" executed="1" passed="1" failed="0" /></ResultSummary></TestRun>'
$oldTemp=$env:TEMP; $oldTmp=$env:TMP
$env:TEMP=$tempRoot; $env:TMP=$tempRoot
$total=0; $fail=0
# The known zero-discovery exit-0 failure is injected at the external CLI seam.
# Production result selection/counter checking and all filesystem work run intact.
function dotnet {
    if($args[0] -eq 'vstest' -and $emitFresh) {
        $resultArg=@($args | Where-Object { $_ -like '--ResultsDirectory:*' })[0]
        $destination=$resultArg.Substring('--ResultsDirectory:'.Length)
        [IO.File]::WriteAllText((Join-Path $destination 'results.trx'),$trxText)
    }
    $global:LASTEXITCODE=0
    'INJECTED|dotnet|' + $args[0] + '|fresh-trx=' + $emitFresh
}
try {
    foreach($mode in @('empty-no-run','stale-no-run','fresh-pass')) {
        $results=Join-Path $fixture $mode
        New-Item -ItemType Directory -Path $results | Out-Null
        if($mode -eq 'stale-no-run') {
            [IO.File]::WriteAllText((Join-Path $results 'results.trx'),$trxText)
            (Get-Item -LiteralPath (Join-Path $results 'results.trx')).LastWriteTime=[DateTime]::Now.AddDays(-1)
        }
        $emitFresh=$mode -eq 'fresh-pass'
        $accepted=$false
        try {
            & (Join-Path $root 'tools/run-office-tests-vstest.ps1') -TestProject $project -ResultsDirectory $results *> (Join-Path $fixture ($mode+'.txt'))
            $accepted=$true
        }
        catch { $_.Exception.Message | Add-Content -LiteralPath (Join-Path $fixture ($mode+'.txt')) }
        $ok=$accepted -eq $emitFresh
        $total++; if(-not $ok){$fail++}
        $mark=if($ok){'PASS'}else{'FAIL'}
        "$mark|$mode|expected-accepted=$emitFresh|actual-accepted=$accepted|artifact=$fixture"
    }
}
finally{$env:TEMP=$oldTemp; $env:TMP=$oldTmp}
"SUMMARY|total=$total|failed=$fail"
if($fail){exit 1}

param([string]$ProjectRoot='.')
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ProjectRoot).Path
$fixture=Join-Path $PSScriptRoot ('vstest-fixture-'+[guid]::NewGuid().ToString('N'))
$out=Join-Path $fixture 'bin/Release/net48';$tmp=Join-Path $fixture 'temp'
New-Item -ItemType Directory -Path $out,$tmp | Out-Null
$project=Join-Path $fixture 'Fixture.csproj';[IO.File]::WriteAllText($project,'<Project />');[IO.File]::WriteAllText((Join-Path $out 'Fixture.dll'),'synthetic CLI seam fixture')
$oldTemp=$env:TEMP;$oldTmp=$env:TMP;$env:TEMP=$tmp;$env:TMP=$tmp
$pass=0;$fail=0
function Check([string]$Id,[bool]$Ok,[string]$Detail){if($Ok){$script:pass++}else{$script:fail++};$(if($Ok){'PASS'}else{'FAIL'})+'|'+$Id+'|'+$Detail}
function dotnet{
    if($args[0] -eq 'build'){$global:LASTEXITCODE=0;return}
    $global:LASTEXITCODE=if($mode -eq 'cli-error'){7}else{0}
    if($mode -in @('stale-root','stale-child','no-new','cli-error')){return}
    $resultArg=@($args|Where-Object{$_ -like '--ResultsDirectory:*'})[0]
    $logger=@($args|Where-Object{$_ -like 'trx;LogFileName=*'})[0]
    $dest=$resultArg.Substring('--ResultsDirectory:'.Length)
    $name=$logger.Substring('trx;LogFileName='.Length)
    $total=2;$executed=2;$passed=2
    if($mode -eq 'zero'){$total=0;$executed=0;$passed=0}
    if($mode -eq 'partial'){$executed=1;$passed=1}
    $xml=if($mode -eq 'bad-xml'){'<invalid'}else{"<TestRun><ResultSummary><Counters total=`"$total`" executed=`"$executed`" passed=`"$passed`" failed=`"0`" /></ResultSummary></TestRun>"}
    [IO.File]::WriteAllText((Join-Path $dest $name),$xml)
}
try{
    foreach($mode in @('stale-root','stale-child','no-new','fresh','zero','partial','cli-error','bad-xml','default-fresh')){
        $results=Join-Path $fixture $mode;New-Item -ItemType Directory -Path $results | Out-Null
        if($mode -like 'stale-*'){
            $old=if($mode -eq 'stale-child'){Join-Path $results 'old/results.trx'}else{Join-Path $results 'results.trx'}
            New-Item -ItemType Directory -Path (Split-Path -Parent $old) -Force | Out-Null
            [IO.File]::WriteAllText($old,'<TestRun><ResultSummary><Counters total="1" executed="1" passed="1" /></ResultSummary></TestRun>')
        }
        $accepted=$false;$log=Join-Path $fixture ($mode+'.txt')
        $arguments=@{TestProject=$project};if($mode -ne 'default-fresh'){$arguments.ResultsDirectory=$results}
        try{& (Join-Path $root 'tools/run-office-tests-vstest.ps1') @arguments *> $log;$accepted=$true}catch{$_ | Out-String | Add-Content -LiteralPath $log}
        $expected=$mode -in @('fresh','default-fresh')
        Check ('T1-'+$mode) ($accepted -eq $expected) ("accepted=$accepted|expected=$expected")
        if($accepted){
            $line=@(Get-Content -LiteralPath $log|Where-Object{$_ -like 'PASS|VSTEST|*'})[0]
            $trx=[regex]::Match($line,'\|trx=(.+)$').Groups[1].Value
            Check ('T2-artifact-'+$mode) (Test-Path -LiteralPath $trx -PathType Leaf) ('trx='+$trx)
        }
    }
}finally{$env:TEMP=$oldTemp;$env:TMP=$oldTmp}
"SUMMARY|passed=$pass|failed=$fail"
if($fail){exit 1}

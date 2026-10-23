param([string]$FindingsPath='', [string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if(-not $FindingsPath){$FindingsPath=Join-Path $repo 'docs/acceptance-findings.json'}
$work=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $repo ('_paneltest/acceptance-gate-selftest-'+[guid]::NewGuid().ToString('N'))}
if(Test-Path -LiteralPath $work){throw 'Self-test output already exists; preserve previous evidence'}
New-Item -ItemType Directory -Path $work|Out-Null
$renderer=Join-Path $repo 'tools/render-acceptance-report.ps1'
$cases=@('actual-result','synthetic-blocked','synthetic-finding-blocked','blocked-finding-stored-pass','missing-matrix-result','invalid-matrix-result','synthetic-host-fail','synthetic-pass','wrong-stored-verdict','changed-evidence-hash','stale-source-id','forged-source-binding','changed-verification-tool')
$baseline=Get-Content $FindingsPath -Raw -Encoding utf8|ConvertFrom-Json
if($baseline.build_source_manifest){$cases+=@('changed-build-snapshot','wrong-build-archive')}
if($baseline.repeatability.full_collections){$cases+=@('missing-repeat-run','changed-test-selector')}
$rows=foreach($case in $cases){
    $data=Get-Content $FindingsPath -Raw -Encoding utf8|ConvertFrom-Json
    $expected=switch($data.overall_result){'PASS'{0};'FAIL'{1};'SKIP/BLOCKED'{3};default{throw 'Unknown acceptance result'}}
    switch($case){
        'synthetic-blocked' {$data.findings=@($data.findings|Where-Object result -eq 'PASS');$data.negative_matrix=@();foreach($hostRow in $data.host_matrix){$hostRow.result='SKIP/BLOCKED'};$data.overall_result='SKIP/BLOCKED';$expected=3}
        'synthetic-finding-blocked' {$data.findings=@($data.findings|Where-Object result -eq 'PASS');$data.findings[0].status='fixed_pending_verification';$data.findings[0].result='SKIP/BLOCKED';$data.negative_matrix=@();foreach($hostRow in $data.host_matrix){$hostRow.required=$false};$data.overall_result='SKIP/BLOCKED';$expected=3}
        'blocked-finding-stored-pass' {$data.findings=@($data.findings|Where-Object result -eq 'PASS');$data.findings[0].status='fixed_pending_verification';$data.findings[0].result='SKIP/BLOCKED';$data.negative_matrix=@();foreach($hostRow in $data.host_matrix){$hostRow.required=$false};$data.overall_result='PASS';$expected=2}
        'missing-matrix-result' {$data.findings=@($data.findings|Where-Object result -eq 'PASS');$data.negative_matrix=@([pscustomobject]@{case='missing result';required=$true;detail='synthetic missing required result'});foreach($hostRow in $data.host_matrix){$hostRow.required=$false};$data.overall_result='PASS';$expected=2}
        'invalid-matrix-result' {$data.findings=@($data.findings|Where-Object result -eq 'PASS');$data.negative_matrix=@([pscustomobject]@{case='invalid result';required=$true;result='UNKNOWN';detail='synthetic invalid required result'});foreach($hostRow in $data.host_matrix){$hostRow.required=$false};$data.overall_result='PASS';$expected=2}
        'synthetic-host-fail' {$data.findings=@($data.findings|Where-Object result -eq 'PASS');$data.negative_matrix=@();foreach($hostRow in $data.host_matrix){$hostRow.required=$false};$data.host_matrix[0].required=$true;$data.host_matrix[0].result='FAIL';$data.overall_result='FAIL';$expected=1}
        'synthetic-pass' {$data.findings=@($data.findings|Where-Object result -eq 'PASS');$data.negative_matrix=@();foreach($hostRow in $data.host_matrix){$hostRow.required=$false};$data.overall_result='PASS';$expected=0}
        'wrong-stored-verdict' {$data.overall_result=if($data.overall_result -eq 'PASS'){'FAIL'}else{'PASS'};$expected=2}
        'stale-source-id' {$data.source_snapshot_id='0'*64;$expected=2}
        'forged-source-binding' {$data.findings=@($data.findings|Where-Object result -eq 'PASS');$data.negative_matrix=@();foreach($hostRow in $data.host_matrix){$hostRow.required=$false};$data.overall_result='PASS';$data.source_snapshot_id='0'*64;$data.source_manifest.sha256='0'*64;$expected=2}
        'changed-verification-tool' {$data.verification_tools[0].sha256='0'*64;$expected=2}
        'changed-build-snapshot' {$data.build_source_snapshot_id='0'*64;$expected=2}
        'wrong-build-archive' {$data.build_source_archive='docs/nonexistent-source-archive';$expected=2}
        'missing-repeat-run' {$data.repeatability.full_collections[0].runs=@($data.repeatability.full_collections[0].runs | Select-Object -First 2);$expected=2}
        'changed-test-selector' {$data.repeatability.full_collections[0].test_selectors[0]='Nonexistent.Test';$expected=2}
        'changed-evidence-hash' {
            $index=Get-Content (Join-Path $repo $data.evidence_index.path) -Raw -Encoding utf8|ConvertFrom-Json
            $index.files[0].sha256='0'*64;$indexPath=Join-Path $work 'tampered-index.json';$index|ConvertTo-Json -Depth 8|Set-Content $indexPath -Encoding utf8
            $data.evidence_index.path=[IO.Path]::GetRelativePath($repo,$indexPath).Replace('\','/');$data.evidence_index.sha256=(Get-FileHash $indexPath).Hash.ToLowerInvariant();$expected=2
        }
    }
    $file=Join-Path $work ($case+'.json');$data|ConvertTo-Json -Depth 20|Set-Content $file -Encoding utf8
    $psi=[Diagnostics.ProcessStartInfo]::new();$psi.FileName=(Get-Process -Id $PID).Path;$psi.UseShellExecute=$false;$psi.CreateNoWindow=$true;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
    foreach($arg in @('-NoProfile','-File',$renderer,'-FindingsPath',$file,'-CheckOnly')){[void]$psi.ArgumentList.Add($arg)}
    $p=[Diagnostics.Process]::Start($psi);$stdout=$p.StandardOutput.ReadToEndAsync();$stderr=$p.StandardError.ReadToEndAsync()
    if(-not $p.WaitForExit(60000)){$p.Kill($true);throw "Self-test timeout: $case"};$code=$p.ExitCode
    $out=$stdout.GetAwaiter().GetResult();$err=$stderr.GetAwaiter().GetResult();$p.Dispose()
    [IO.File]::WriteAllText((Join-Path $work ($case+'.stdout.txt')),$out,[Text.UTF8Encoding]::new($false));[IO.File]::WriteAllText((Join-Path $work ($case+'.stderr.txt')),$err,[Text.UTF8Encoding]::new($false))
    [ordered]@{case=$case;expected_exit=$expected;actual_exit=$code;result=$(if($expected -eq $code){'PASS'}else{'FAIL'});scope='gate self-test with synthetic copies; never an actual release PASS'}
}
$rows|ConvertTo-Json -Depth 4|Set-Content (Join-Path $work 'results.json') -Encoding utf8
$rows|ForEach-Object{Write-Output ($_.case+' '+$_.result+' expected='+$_.expected_exit+' actual='+$_.actual_exit)}
Write-Output ('SELFTEST_DIRECTORY='+$work)
if($rows.result -contains 'FAIL'){exit 1}

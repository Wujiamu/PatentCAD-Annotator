param([Parameter(Mandatory=$true)][string]$RunDirectory,[string]$FindingsPath='')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$runRoot=(Resolve-Path -LiteralPath $RunDirectory).Path
$run=Get-Content (Join-Path $runRoot 'run.json') -Raw -Encoding utf8|ConvertFrom-Json
$evidence=Join-Path $runRoot 'evidence'
function Rel([string]$path){[IO.Path]::GetRelativePath($repo,$path).Replace('\','/')}
function Ref([string]$path){[ordered]@{path=(Rel $path);sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}}
function CaseRuns([string]$phase,[string]$case){
    @(1..$run.repetitions|ForEach-Object{
        $iteration=$_;$path=Join-Path $evidence "cases/$phase/$iteration/$case/case-result.json"
        $result=Get-Content $path -Raw -Encoding utf8|ConvertFrom-Json
        $command=$run.commands|Where-Object id -eq "$phase-$iteration-$case"
        $stdout=Join-Path $runRoot $command.stdout
        $keys=Get-Content $stdout -Encoding utf8|Where-Object {$_ -match 'accepted=|RESULT:|\[FAIL\] (B9|T3)|CASE='}
        [ordered]@{iteration=$iteration;exit_code=$command.exit_code;result=$result.result;key_output=($keys -join ' ');command_record=(Rel (Join-Path $evidence "commands/$phase-$iteration-$case/command.json"));evidence=(Ref $stdout);case_result=(Ref $path)}
    })
}
function ReportRuns([string]$phase,[string]$case){
    @(1..$run.repetitions|ForEach-Object{
        $iteration=$_;$folder=Join-Path $evidence "report-cases/$phase/$iteration"
        $path=Join-Path $folder 'results.json';$row=Get-Content $path -Raw -Encoding utf8|ConvertFrom-Json|Where-Object case -eq $case
        [ordered]@{iteration=$iteration;exit_code=$row.exit_code;result=$row.result;key_output=('expected_acceptance='+$row.expected_acceptance+' actual_acceptance='+$row.actual_acceptance);command_record=(Rel $path);evidence=(Ref $path);stdout=(Ref (Join-Path $folder ($case+'.stdout.txt')));stderr=(Ref (Join-Path $folder ($case+'.stderr.txt')));case_result=(Ref $path);fixture=(Ref (Join-Path $folder ($case+'.txt')))}
    })
}
function Finding([string]$id,[string]$severity,[string]$component,[string]$expected,[string]$actual,[string]$risk,[string[]]$files,[string[]]$selectors,[object[]]$before,[object[]]$after,[string[]]$repro,[string[]]$related){
    $verified=$before.Count -ge 3 -and $after.Count -ge 3 -and @($before|Where-Object result -ne 'FAIL').Count -eq 0 -and @($after|Where-Object result -ne 'PASS').Count -eq 0
    [ordered]@{finding_id=$id;severity=$severity;component=$component;status=$(if($verified){'original_case_independently_verified'}else{'open'});result=$(if($verified){'PASS'}else{'FAIL'});expected=$expected;actual=$actual;repair_files=$files;repair_source_sha256=@($files|ForEach-Object{[ordered]@{path=$_;sha256=(Get-FileHash (Join-Path $run.post_fix.root $_)).Hash.ToLowerInvariant()}});repro_command=$repro;test_selectors=$selectors;pre_fix_runs=$before;post_fix_runs=$after;pre_fix_evidence=@($before|ForEach-Object{$_.evidence});post_fix_evidence=@($after|ForEach-Object{$_.evidence});evidence=[ordered]@{before=@($before|ForEach-Object{$_.evidence});after=@($after|ForEach-Object{$_.evidence})};residual_risk=$risk;related_findings=$related}
}
$probeCommand='dotnet build tools/AcceptanceProbe/AcceptanceProbe.csproj -c Release "-p:ContractAssemblyPath=<SNAPSHOT>/cad-plugin/RuntimeContract.Tests/bin/Release/net8.0/PatentMarker.RuntimeContract.2010.Tests.dll" -o <BIN>'
$fullReplay='pwsh -NoProfile -File tools/run-acceptance-evidence.ps1 -PreFixRoot "'+$run.pre_fix.root+'" -PostFixRoot "'+$run.post_fix.root+'" -OutputDirectory "./docs/test-evidence/acceptance-assets-REPLAY" -Repetitions 3 -Seed '+$run.randomization_seed
$selectors=$run.test_selectors
$testCommand='dotnet test "'+$run.post_fix.root+'/cad-plugin/RuntimeContract.Tests/PatentMarker.RuntimeContract.2010.Tests.csproj" -c Release --nologo --filter "FullyQualifiedName={0}"'
$findings=@(
    (Finding 'A01' 'P1' 'PatBraceAssertions' 'Reject original 11-point sharp elbows with all bulges zero' 'Before: accepted in 3/3; after: rejected in 3/3 (B9 FAIL)' '仅检查采样顶点；未证明弧段/bulge、闭合状态或点间连续曲率；低密度轮廓接受是当前实现行为，规格是否允许尚待确认。' @('cad-plugin/Shared/Commands/PatBraceAssertions.cs') @($selectors[0],$selectors[2]) (CaseRuns pre sharp) (CaseRuns post sharp) @($fullReplay,($testCommand -f $selectors[0])) @()),
    (Finding 'A02' 'P1' 'PatGeomVerifyCommand' 'Reject actual text moved to (2000,1000,0) while recorded chain unchanged' 'Before: T3 PASS in 3/3; after: T3a/T3 FAIL and command throws in 3/3' '原文字移动反例已关闭；实际前一顶点仍未核对，见 R2-01。真实 MLeader Location/TextLocation 和容差误报、2007 Leader 分支仍缺宿主证据。' @('cad-plugin/Shared/Commands/PatGeomVerifyCommand.cs','cad-plugin/Shared/Commands/PatGeomTextAssertions.cs','cad-plugin/Shared/Cad/PatEntityHelper.cs') @($selectors[1]) (CaseRuns pre moved-text) (CaseRuns post moved-text) @($fullReplay,($testCommand -f $selectors[1])) @('R2-01')),
    (Finding 'A03' 'P1' 'verify-geometry.ps1' 'Reject same incomplete report missing B0c/ASSERTIONS/REQUIRED and wrong MVID' 'Before: report accepted (exit 0) in 3/3; after: rejected (exit 1) in 3/3' 'MVID 依赖 manifest；生成正确不等于实际加载。计数矛盾与单对象额外重复断言仍未拒绝，见 R2-02、A03-DUP。真实宿主报告未执行。' @('tools/verify-geometry.ps1') @('acceptance-report-cases.ps1::original-A03','verify-geometry.ps1::-SelfTest') (ReportRuns pre original-A03) (ReportRuns post original-A03) @($fullReplay,'pwsh -NoProfile -File tools/verify-geometry.ps1 -SelfTest') @('R2-02','A03-DUP')),
    (Finding 'R2-01' 'P1' 'PatGeomVerifyCommand actual previous vertex' 'Changing actual previous vertex to (10,100,0) must fail endpoint check' 'Before and after: unchanged Xrecord previous vertex produces false PASS in 3/3' '当前开放；实际引线链与存储链一致性未形成闭环，不能仅通过文字位置检查放行。' @('cad-plugin/Shared/Commands/PatGeomVerifyCommand.cs') @('AcceptanceProbe::moved-previous (no permanent xUnit selector yet)') (CaseRuns pre moved-previous) (CaseRuns post moved-previous) @($fullReplay) @('A02')),
    (Finding 'R2-02' 'P2' 'verify-geometry.ps1 object totals' 'Reject braces=0 passed=1 and equivalent text-count contradiction' 'Before and after: contradictory totals accepted in 3/3' '当前开放；对象总数、通过数、逐对象断言数量未交叉核验。' @('tools/verify-geometry.ps1') @('acceptance-report-cases.ps1::braces-zero-passed-one','acceptance-report-cases.ps1::texts-zero-passed-one') (ReportRuns pre braces-zero-passed-one) (ReportRuns post braces-zero-passed-one) @($fullReplay) @('A03')),
    (Finding 'A03-DUP' 'P2' 'verify-geometry.ps1 per-object assertion multiplicity' 'Reject extra B9 PASS in a one-brace report even if ASSERTIONS count is updated' 'Before and after: additional duplicate PASS accepted in 3/3' '当前开放；多对象重复相同 ID 本来合法，修复应按对象绑定断言，不能全局禁止重复 ID。' @('tools/verify-geometry.ps1') @('acceptance-report-cases.ps1::duplicate-pass') (ReportRuns pre duplicate-pass) (ReportRuns post duplicate-pass) @($fullReplay) @('A03'))
)
$matrix=[Collections.Generic.List[object]]::new()
if(Test-Path (Join-Path $evidence 'cases/post/1/translated-large/case-result.json')){
    $findings+=@(
        (Finding 'R3-01' 'P1' 'PatEntityHelper actual previous vertex at large WCS coordinates' 'A valid annotation translated by +10000000 WCS X must retain PASS' 'Valid large-coordinate annotation rejected' 'PointsClose uses absolute WCS magnitude, so the scan may stop at an earlier vertex. Repeated/equivalent coordinates and the 10000-vertex cap also need an index/count contract.' @('cad-plugin/Shared/Cad/PatEntityHelper.cs') @('AcceptanceProbe::translated-large') (CaseRuns pre translated-large) (CaseRuns post translated-large) @($fullReplay) @('R2-01')),
        (Finding 'R3-02' 'P1' 'Leader report producer/parser integration' 'Accept an actual PASS report containing a valid product Leader + MText' 'Legacy report produced successfully but rejected by the new parser' 'Successful Leader branch emits no LEADER header; do not infer end-to-end report acceptance from command RESULT alone.' @('cad-plugin/Shared/Commands/PatGeomVerifyCommand.cs','tools/verify-geometry.ps1') @('acceptance-report-cases.ps1::actual-legacy','AcceptanceProbe::legacy-leader') (ReportRuns pre actual-legacy) (ReportRuns post actual-legacy) @($fullReplay) @('A03')),
        (Finding 'R3-03' 'P2' 'Flat report total accounting' 'Reject total=2 passed=1 failed=0 unreadable=0 for either target class' 'Flat compatibility path still accepts unaccounted targets' 'Structured accounting does not close the flat-report branch; sum of outcomes must account for every required target.' @('tools/verify-geometry.ps1') @('acceptance-report-cases.ps1::flat-unaccounted-braces','acceptance-report-cases.ps1::flat-unaccounted-texts') (ReportRuns pre flat-unaccounted-braces) (ReportRuns post flat-unaccounted-braces) @($fullReplay) @('R2-02')),
        (Finding 'R3-04' 'P2' 'Per-object required assertion status and empty blocks' 'Reject a required B9 marked N/A in one object, and reject an unverified object block' 'Required assertion identity is checked without its PASS status; blocks with zero assertions are skipped' 'A PASS in another object must not satisfy a missing/NA check; no-assertion product blocks cannot be omitted from accounting.' @('tools/verify-geometry.ps1') @('acceptance-report-cases.ps1::structured-required-na','acceptance-report-cases.ps1::structured-empty-object') (ReportRuns pre structured-required-na) (ReportRuns post structured-required-na) @($fullReplay) @('A03'))
    )
    foreach($finding in $findings){
        $finding.actual='Before: '+$finding.pre_fix_runs[0].key_output+'; after: '+$finding.post_fix_runs[0].key_output
        if($finding.finding_id -eq 'R2-01'){$finding.test_selectors=@('PatentMarker.RuntimeContract.Tests.PatGeomVerifyTests.MovedPreviousVertexWithoutChainUpdate_IsRejectedByT3');$finding.residual_risk='Original moved-previous case is fixed; translation regression remains R3-01.'}
        if($finding.finding_id -eq 'R2-02'){$finding.residual_risk='Original total=0/passed=1 case is fixed; unaccounted flat targets remain R3-03.'}
        if($finding.finding_id -eq 'A03-DUP'){$finding.residual_risk='Original per-block duplicate case is fixed; required-status and empty-block validation remain R3-04.'}
        if($finding.finding_id -eq 'A02'){$finding.residual_risk='Moved text and moved previous-vertex originals now rejected; valid large-coordinate MLeader is falsely rejected (R3-01). Host semantics/tolerance remain unverified.'}
        if($finding.finding_id -eq 'A03'){$finding.residual_risk='Original missing-fields, contradictory-count and duplicate cases are fixed; R3-02/R3-03/R3-04 remain. Manifest identity still does not prove actual host loading.'}
    }
}
foreach($case in @('sharp','baseline','moved-text','moved-previous','mtext-only','textlocation-only','record-only','all-synchronized','geometry-matrix')+@($(if(Test-Path (Join-Path $evidence 'cases/post/1/translated-large/case-result.json')){'translated-large';'legacy-leader'}))){
    $results=CaseRuns post $case;$result=if(@($results|Where-Object result -eq 'FAIL').Count){'FAIL'}elseif(@($results|Where-Object result -eq 'SKIP/BLOCKED').Count){'SKIP/BLOCKED'}else{'PASS'}
    $matrix.Add([ordered]@{case=$case;required=$true;result=$result;detail='3 repetitions, isolated children; see evidence/cases/post. Geometry matrix: 5 definitions × 4 sample densities.'})
}
$reportCases=@(Get-Content (Join-Path $evidence 'report-cases/post/1/results.json') -Raw -Encoding utf8|ConvertFrom-Json|Where-Object case -ne 'baseline'|ForEach-Object case)
foreach($case in $reportCases){
    $results=ReportRuns post $case;$result=if(@($results|Where-Object result -eq 'FAIL').Count){'FAIL'}else{'PASS'}
    $matrix.Add([ordered]@{case=$case;required=$true;result=$result;detail='Identical fixture and rejection oracle for pre/post snapshots; three repetitions'})
}
foreach($gap in @('A01 尖角位置变化（原始尖角之外）','真正旧报告文件重用/runner 陈旧目录拒绝（区别于旧 run ID）','File.Replace 强制允许/拒绝、锁定/只读、Hidden/System 属性','File.Move 兜底的 ACL、ADS 与元数据保留','备份存在/缺失和真实恢复路径')){$matrix.Add([ordered]@{case=$gap;required=$true;result='SKIP/BLOCKED';detail='本轮未执行，不由常规测试或相邻用例外推'})}
$perf=@(1..$run.repetitions|ForEach-Object{(Get-Content (Join-Path $evidence "cases/post/$_/scale/case-result.json") -Raw -Encoding utf8|ConvertFrom-Json).measurements})
$measurements=foreach($group in ($perf|Group-Object vertices)){$numbers=@($group.Group.elapsed_ms|Sort-Object);[ordered]@{requested_scale=([int]$group.Name+1);actual_vertices=[int]$group.Name;runs=$numbers.Count;min_ms=[math]::Round($numbers[0],4);median_ms=[math]::Round($numbers[[int][math]::Floor($numbers.Count/2)],4);max_ms=[math]::Round($numbers[-1],4);result=$(if(@($group.Group|Where-Object accepted -eq $false).Count){'FAIL'}else{'PASS'})}}
$hosts=foreach($edition in @('2007','2010','2013','2015','2025')){
    $manifestPath=Join-Path $evidence "candidates/$edition/PatentMarker-release.txt";$manifest=@{};foreach($line in Get-Content $manifestPath -Encoding utf8){if($line -match '^([^#=]+)=(.*)$'){$manifest[$matches[1]]=$matches[2]}}
    [ordered]@{host=$(if($edition -eq '2025'){'AutoCAD 2026 x64 R25.1.60.0.0'}else{'AutoCAD '+$edition});required=$true;core_console=$(if($edition -eq '2025'){'C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe (installed; not run)'}else{'not available'});candidate_path=(Join-Path $run.post_fix.root ('acceptance-stage/'+$edition+'/PatentMarker.dll'));candidate_identity=($manifest['artifact.sha256']+' / '+$manifest['artifact.mvid']);candidate_manifest=(Ref $manifestPath);drawing_sha256=$null;netload_path=$null;assembly_path=$null;assembly_sha256=$null;assembly_mvid=$null;accore_exit_code=$null;host_result=$null;actual_load='SKIP/BLOCKED: not run';patdoctor='SKIP/BLOCKED';patgeomverify='SKIP/BLOCKED / exit=null / RESULT=null';gui='SKIP/BLOCKED';result='SKIP/BLOCKED'}
}
$candidateOriginPath=Join-Path $evidence 'candidates/origin.json'
$candidateOrigin=if(Test-Path $candidateOriginPath){Get-Content $candidateOriginPath -Raw -Encoding utf8|ConvertFrom-Json}else{$null}
if($candidateOrigin.candidate_root){foreach($hostRow in $hosts){$edition=if($hostRow.host -like '*2026*'){'2025'}else{($hostRow.host -split ' ')[1]};$hostRow.candidate_path=$candidateOrigin.candidate_root+'/'+$edition+'/PatentMarker.dll'}}
$realHostPath=Join-Path $evidence 'real-host/host-summary.json'
if(Test-Path $realHostPath){
    $realHost=Get-Content $realHostPath -Raw -Encoding utf8|ConvertFrom-Json
    foreach($record in $realHost.hosts){
        $row=$hosts|Where-Object {if($record.edition -eq '2025'){$_.host -like '*2026*'}else{$_.host -eq ('AutoCAD '+$record.edition)}}
        foreach($property in $record.PSObject.Properties){if($property.Name -ne 'edition'){$row[$property.Name]=$property.Value}}
    }
    foreach($finding in $realHost.findings){$findings+=$finding}
    foreach($check in $realHost.checks){$matrix.Add($check)}
    if(@($realHost.checks|Where-Object {$_.case -eq 'real-runner-stale-directory' -and $_.result -eq 'PASS'}).Count){$matrix=@($matrix|Where-Object {$_.case -ne '真正旧报告文件重用/runner 陈旧目录拒绝（区别于旧 run ID）'})}
}
$restored=@(Get-ChildItem (Join-Path $evidence 'cases') -Recurse -Filter 'case-result.json'|ForEach-Object{Get-Content $_.FullName -Raw -Encoding utf8|ConvertFrom-Json}|Where-Object environment_restored -eq $false).Count -eq 0
$trxResults=foreach($trx in Get-ChildItem (Join-Path $evidence 'trx') -Filter '*.trx'){
    [xml]$xml=Get-Content $trx.FullName -Raw -Encoding utf8;$counter=$xml.TestRun.ResultSummary.Counters
    [ordered]@{file=(Rel $trx.FullName);total=[int]$counter.total;executed=[int]$counter.executed;passed=[int]$counter.passed;failed=[int]$counter.failed;notExecuted=[int]$counter.notExecuted;result=$(if([int]$counter.total -eq 5 -and [int]$counter.passed -eq 5 -and [int]$counter.failed -eq 0 -and [int]$counter.notExecuted -eq 0){'PASS'}else{'FAIL'})}
}
if(@($trxResults).Count -ne 12 -or @($trxResults|Where-Object result -ne 'PASS').Count){throw 'Selected standard test collection incomplete or failed'}
$repeatSets=foreach($iteration in 1..$run.repetitions){@((Get-Content (Join-Path $evidence "post-$iteration-order.json") -Raw -Encoding utf8|ConvertFrom-Json)|Sort-Object) -join ','}
$sameSets=@($repeatSets|Sort-Object -Unique).Count -eq 1
$businessSignatures=foreach($iteration in 1..$run.repetitions){
    $signatures=@(Get-ChildItem (Join-Path $evidence "cases/post/$iteration") -Recurse -Filter 'case-result.json'|ForEach-Object{$case=Get-Content $_.FullName -Raw -Encoding utf8|ConvertFrom-Json;$case.case+'='+$case.result})
    $signatures+=@(Get-Content (Join-Path $evidence "report-cases/post/$iteration/results.json") -Raw -Encoding utf8|ConvertFrom-Json|ForEach-Object{$_.case+'='+$_.result})
    ($signatures|Sort-Object) -join ';'
}
$sameResults=@($businessSignatures|Sort-Object -Unique).Count -eq 1
$risks=@(
    @{id='A01-continuity';status='SKIP/BLOCKED';description='B9 只检查采样顶点，不证明连续曲率或非零 bulge/闭合轮廓；2 点/段低密度样本通过属于实测行为，是否符合产品规格未确认'},
    @{id='A02-host-tolerance';status='SKIP/BLOCKED';description='T3a/T3b 的 1e-6×max(1,H) 容差在真实 MLeader 的 Location/TextLocation 语义下是否误报未验证'},
    @{id='A03-identity';status='SKIP/BLOCKED';description='manifest/SHA/MVID 正确仅证明候选文件身份，不等于宿主实际 NETLOAD 到该路径'},
    @{id='IO-move-metadata';status='SKIP/BLOCKED';description='2025 File.Move overwrite fallback 对 ACL、备用数据流、加密/压缩等元数据保留尚未强制故障注入验证；127 项常规测试不是此分支覆盖证据'},
    @{id='host-editions';status='SKIP/BLOCKED';description='2007 Leader 与四版 MLeader 只有源码模拟覆盖；各自真实宿主和安装后事件/UI 未验证'},
    @{id='scale-upper-bound';status='SKIP/BLOCKED';description='实测上限为单个 4999 顶点轮廓的 L1 断言，非整图最大产品实体数量；真实报告生成、内存峰值及宿主超时边界未测，BuildPoints 新采样参数未设硬上限'}
)
Copy-Item -LiteralPath (Join-Path $runRoot 'run.json') -Destination (Join-Path $evidence 'run.json') -Force
$indexed=@(Get-ChildItem $evidence -File -Recurse|Sort-Object FullName|ForEach-Object{[ordered]@{path=(Rel $_.FullName);bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
$index=[ordered]@{schema_version=1;created=[DateTimeOffset]::Now.ToString('o');scope='all files under this run/evidence, including historical copies, tooling, command metadata and immutable run.json; index/findings/generated report excluded to avoid circular hashes';files=$indexed}
$indexPath=Join-Path $runRoot 'evidence-index.json';$index|ConvertTo-Json -Depth 5|Set-Content $indexPath -Encoding utf8
$verificationTools=@('tools/run-acceptance-evidence.ps1','tools/acceptance-report-cases.ps1','tools/assemble-acceptance-findings.ps1','tools/render-acceptance-report.ps1','tools/test-acceptance-report.ps1','tools/AcceptanceProbe/AcceptanceProbe.csproj','tools/AcceptanceProbe/Program.cs')|ForEach-Object{[ordered]@{path=$_;sha256=(Get-FileHash (Join-Path $repo $_)).Hash.ToLowerInvariant()}}
$data=[ordered]@{schema_version=1;report_id=$run.report_id;generated_at=[DateTimeOffset]::Now.ToString('o');verification_actor=$run.verification_actor;operator=$run.operator;reviewer=$run.reviewer;independence_statement=$run.independence_statement;source_snapshot_id=$run.post_fix.source_snapshot_id;source_manifest=(Ref (Join-Path $runRoot $run.post_fix.manifest));verification_tools=@($verificationTools);pre_fix_source_snapshot_id=$run.pre_fix.source_snapshot_id;source=$run.post_fix;pre_fix_source=$run.pre_fix;toolchain=('PowerShell '+$run.powershell+'; .NET SDK '+$run.sdk+'; same Windows 10 x64 build 19045; real host not started');candidate_statement=$candidateOrigin.statement;overall_result=$(if(@($findings|Where-Object result -eq 'FAIL').Count -or @($matrix|Where-Object {$_.required -and $_.result -eq 'FAIL'}).Count){'FAIL'}elseif(@($hosts|Where-Object {$_.required -and $_.result -ne 'PASS'}).Count -or @($matrix|Where-Object {$_.required -and $_.result -eq 'SKIP/BLOCKED'}).Count){'SKIP/BLOCKED'}else{'PASS'});findings=$findings;evidence_index=(Ref $indexPath);host_matrix=@($hosts);negative_matrix=@($matrix);residual_risks=$risks;performance=[ordered]@{statement='L1 geometry assertions only; 257 reference points per corner, default 8 cubic samples/section. Approximate requested sizes 100/1000/5000 yield 99/999/4999 vertices. Cold JIT/order effects included; no host throughput claim.';measurements=@($measurements);max_tested_braces_per_case=1;host_max_product_entities=$null;report_generation_only_ms=$null;simulated_command_with_report_ms=@(1..$run.repetitions|ForEach-Object{(Get-Content (Join-Path $evidence "cases/post/$_/baseline/case-result.json") -Raw -Encoding utf8|ConvertFrom-Json).command_elapsed_ms})};repeatability=[ordered]@{repetitions=$run.repetitions;order_randomized=$run.order_randomized;seed=$run.randomization_seed;same_case_set=$sameSets;same_business_result_set=$sameResults;probe_environment_restored=$restored;parent_PATGEOM_restored=$run.parent_environment_restored;selected_tests=@($trxResults);selection_order_note='Probe cases shuffled/recorded; report variants use fixed order and isolated verifier children; xUnit internal order not randomized; existing DisableTestParallelization retained';cleanup='Fresh child per probe; reports intentionally retained; harness binaries in _paneltest, no user documents/processes touched';order_dependence_observed=$(if($sameResults){'No business-result differences across three recorded permutations; does not prove all orders'}else{'Business-result differences detected; inspect repeats'})}}
if(Test-Path $realHostPath){
    $data.toolchain='PowerShell '+$run.powershell+'; .NET SDK '+$run.sdk+'; Windows 10 x64 build 19045; AutoCAD 2026 x64 R25.1.60.0.0 / .NET runtime 8.0.28'
    $data.verification_scope=$realHost.verification_scope
    $data.delivery_statement=$realHost.delivery_statement
    $data.real_host_evidence=Ref $realHostPath
    foreach($risk in $data.residual_risks){
        if($risk.id -eq 'A03-identity'){$risk.status='PASS';$risk.description='本轮 AutoCAD 2026 PATDOCTOR 与几何报告实载路径/SHA/MVID 匹配候选；此身份 PASS 不代表几何/GUI 通过，也不外推旧宿主。'}
        if($risk.id -eq 'A02-host-tolerance'){$risk.status='FAIL';$risk.description='真实 AutoCAD 2026 正常 PATMARK 创建对象在 T3 失败，见 R3-06；模拟通过不能替代实载几何契约。'}
        if($risk.id -eq 'host-editions'){$risk.description='2026 实载身份和冷启动已执行；Core Console 崩溃、完整宿主 T3 FAIL；2007/2010/2013/2015 本机没有安装证据，仍 BLOCKED。'}
    }
}
if(-not $FindingsPath){$FindingsPath=Join-Path $repo 'docs/acceptance-findings.json'}
$data|ConvertTo-Json -Depth 18|Set-Content -LiteralPath $FindingsPath -Encoding utf8
Write-Output ('Assembled findings and '+$indexed.Count+' indexed evidence files; overall FAIL retained.')

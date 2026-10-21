param(
    [string]$FindingsPath='',
    [string]$OutputPath='',
    [switch]$CheckOnly
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if(-not $FindingsPath){$FindingsPath=Join-Path $repo 'docs/acceptance-findings.json'}
if(-not $OutputPath){$OutputPath=Join-Path $repo 'docs/acceptance-current.md'}
function Read-ProjectFile([string]$relative){
    $full=[IO.Path]::GetFullPath((Join-Path $repo $relative))
    if(-not $full.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw "Evidence outside repository: $relative"}
    return $full
}
function Cell($value){if($value -is [DateTime] -or $value -is [DateTimeOffset]){return $value.ToString('o')};return ([string]$value).Replace('|','\|').Replace("`r",' ').Replace("`n",' ')}
function Evidence-Link($item){if($null -eq $item){return 'SKIP/BLOCKED'};return ('['+[IO.Path]::GetFileName($item.path)+']('+([IO.Path]::GetRelativePath((Split-Path ([IO.Path]::GetFullPath($OutputPath)) -Parent),(Read-ProjectFile $item.path))).Replace('\','/')+') SHA-256 `'+$item.sha256+'`')}
try {
    $data=Get-Content -LiteralPath $FindingsPath -Raw -Encoding utf8|ConvertFrom-Json
    if($data.schema_version -ne 1){throw 'Unsupported findings schema'}
    foreach($field in @('report_id','generated_at','verification_actor','independence_statement','source_snapshot_id','source','evidence_index','findings','host_matrix','negative_matrix','residual_risks','repeatability')){if($null -eq $data.$field){throw "Missing $field"}}
    $indexPath=Read-ProjectFile $data.evidence_index.path
    if((Get-FileHash -LiteralPath $indexPath).Hash -ne $data.evidence_index.sha256){throw 'Evidence index SHA-256 mismatch'}
    $index=Get-Content -LiteralPath $indexPath -Raw -Encoding utf8|ConvertFrom-Json
    $indexed=@{}
    foreach($entry in $index.files){
        if($indexed.ContainsKey($entry.path)){throw "Duplicate indexed path: $($entry.path)"}
        $file=Read-ProjectFile $entry.path
        if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing indexed evidence: $($entry.path)"}
        if((Get-FileHash -LiteralPath $file).Hash -ne $entry.sha256){throw "Evidence changed: $($entry.path)"}
        if((Get-Item -LiteralPath $file).Length -ne $entry.bytes){throw "Evidence size changed: $($entry.path)"}
        $indexed[$entry.path]=$entry.sha256
    }
    if($data.source_manifest.sha256 -ne $data.source_snapshot_id -or -not $indexed.ContainsKey($data.source_manifest.path) -or $indexed[$data.source_manifest.path] -ne $data.source_manifest.sha256){throw 'Source snapshot/manifest binding invalid'}
    $sourceFiles=Get-Content (Read-ProjectFile $data.source_manifest.path) -Raw -Encoding utf8|ConvertFrom-Json
    # Reporting infrastructure is versioned separately below; changes to a
    # formatter/assembler do not redefine the compiled product snapshot.
    $verificationPaths=@($data.verification_tools.path)
    foreach($source in $sourceFiles){if($source.path -in $verificationPaths){continue};if((Get-FileHash -LiteralPath (Read-ProjectFile $source.path)).Hash -ne $source.sha256){throw "Stale acceptance for current source: $($source.path)"}}
    foreach($tool in $data.verification_tools){if((Get-FileHash -LiteralPath (Read-ProjectFile $tool.path)).Hash -ne $tool.sha256){throw "Verification tool changed: $($tool.path)"}}
    if ($data.build_source_manifest) {
        if (-not $data.build_source_archive -or -not $data.build_source_snapshot_id) { throw 'Incomplete build-source binding' }
        $buildManifest = Read-ProjectFile $data.build_source_manifest.path
        if ((Get-FileHash $buildManifest).Hash -ne $data.build_source_manifest.sha256 -or
            $data.build_source_manifest.sha256 -ne $data.build_source_snapshot_id -or
            -not $indexed.ContainsKey($data.build_source_manifest.path)) { throw 'Build-source manifest binding invalid' }
        $buildFiles = @(Get-Content $buildManifest -Raw -Encoding utf8 | ConvertFrom-Json)
        if ($buildFiles.Count -ne @($sourceFiles).Count) { throw 'Build/root source sets differ' }
        foreach ($entry in $buildFiles) {
            $current = @($sourceFiles | Where-Object path -eq $entry.path)
            if ($current.Count -ne 1) { throw 'Build/root source paths differ' }
            $archived = Read-ProjectFile ($data.build_source_archive + '/' + $entry.path)
            if ((Get-FileHash $archived).Hash -ne $entry.sha256) { throw "Build-source archive changed: $($entry.path)" }
            # Git's CRLF checkout conversion changes byte identities, but must
            # not permit any other source-content difference. Latin1 is used
            # as a reversible byte mapping, not as the C# source encoding.
            $rootBytes = [Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes((Read-ProjectFile $entry.path))).Replace("`r`n","`n")
            $buildBytes = [Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes($archived)).Replace("`r`n","`n")
            if ($rootBytes -cne $buildBytes) { throw "Current/build content differs beyond CRLF/LF: $($entry.path)" }
        }
    }
    $ids=@{}
    foreach($finding in $data.findings){
        foreach($field in @('finding_id','severity','component','status','result','expected','actual','repro_command','test_selectors','evidence','residual_risk')){if($null -eq $finding.$field){throw "$($finding.finding_id): missing $field"}}
        if($ids.ContainsKey($finding.finding_id)){throw 'Duplicate finding ID'};$ids[$finding.finding_id]=$true
        if($finding.result -notin @('PASS','FAIL','SKIP/BLOCKED')){throw 'Invalid result enum'}
        if($finding.status -eq 'open' -and $finding.result -ne 'FAIL'){throw 'Open finding cannot pass'}
        if($finding.status -eq 'original_case_independently_verified'){
            if($finding.result -ne 'PASS'){throw 'Verified case must PASS'}
            if(@($finding.pre_fix_runs).Count -lt 3 -or @($finding.post_fix_runs).Count -lt 3){throw 'Verified case requires three before/after runs'}
            if(@($finding.pre_fix_runs|Where-Object{$_.result -ne 'FAIL'}).Count -or @($finding.post_fix_runs|Where-Object{$_.result -ne 'PASS'}).Count){throw 'Red/green run mapping invalid'}
        }
        foreach($reference in @($finding.evidence.before)+@($finding.evidence.after)){
            if($null -eq $reference){continue}
            if(-not $indexed.ContainsKey($reference.path) -or $indexed[$reference.path] -ne $reference.sha256){throw "Unindexed finding evidence: $($reference.path)"}
        }
    }
    if(-not $data.repeatability.same_case_set -or -not $data.repeatability.same_business_result_set -or -not $data.repeatability.probe_environment_restored -or -not $data.repeatability.parent_PATGEOM_restored){throw 'Repeatability or environment isolation assertion failed'}
    if ($data.repeatability.full_collections) {
        $collections = @($data.repeatability.full_collections)
        if (($collections.version | Sort-Object) -join ',' -ne '2007,2010,2013,2015') { throw 'Required repeated runtime collections missing' }
        foreach ($collection in $collections) {
            $expectedNames = @($collection.test_selectors | Sort-Object)
            if ($expectedNames.Count -eq 0 -or @($expectedNames | Select-Object -Unique).Count -ne $expectedNames.Count -or
                @($collection.runs).Count -lt 3) { throw 'Incomplete repeated test collection' }
            foreach ($run in $collection.runs) {
                if ($run.exit_code -ne 0 -or $run.result -ne 'PASS' -or -not $indexed.ContainsKey($run.stdout.path) -or
                    $indexed[$run.stdout.path] -ne $run.stdout.sha256) { throw 'Repeated test run failed or unindexed' }
                $output = [IO.File]::ReadAllLines((Read-ProjectFile $run.stdout.path))
                $actualNames = @($output | Where-Object { $_.StartsWith('PASS ') } | ForEach-Object { $_.Substring(5) } | Sort-Object)
                if (($actualNames -join "`n") -cne ($expectedNames -join "`n") -or
                    @($output | Where-Object { $_ -match '^(FAIL |SKIP )' }).Count -or
                    @($output | Where-Object { $_ -eq ('SUMMARY: passed='+$expectedNames.Count+' failed=0 skipped=0') }).Count -ne 1) {
                    throw 'Repeated test names/counts/results differ'
                }
            }
        }
    } elseif (@($data.repeatability.selected_tests).Count -ne 12 -or @($data.repeatability.selected_tests|Where-Object {$_.result -ne 'PASS' -or $_.executed -ne 5 -or $_.notExecuted -ne 0}).Count) { throw 'Selected test collection incomplete or failed' }
    # A missing matrix assertion, timeout or skipped test cannot become a release PASS.
    foreach($hostRow in $data.host_matrix){if($hostRow.result -notin @('PASS','FAIL','SKIP/BLOCKED')){throw 'Invalid host result enum'}}
    foreach($matrixRow in $data.negative_matrix){if($matrixRow.result -notin @('PASS','FAIL','SKIP/BLOCKED')){throw 'Missing or invalid matrix result enum'}}
    $hasFail=@($data.findings|Where-Object{$_.result -eq 'FAIL'}).Count -gt 0 -or @($data.negative_matrix|Where-Object{$_.required -and $_.result -eq 'FAIL'}).Count -gt 0 -or @($data.host_matrix|Where-Object{$_.required -and $_.result -eq 'FAIL'}).Count -gt 0
    $hasBlocked=@($data.findings|Where-Object{$_.result -eq 'SKIP/BLOCKED'}).Count -gt 0 -or @($data.host_matrix|Where-Object{$_.required -and $_.result -ne 'PASS'}).Count -gt 0 -or @($data.negative_matrix|Where-Object{$_.required -and $_.result -eq 'SKIP/BLOCKED'}).Count -gt 0
    $result=if($hasFail){'FAIL'}elseif($hasBlocked){'SKIP/BLOCKED'}else{'PASS'}
    if($data.overall_result -ne $result){throw "Stored result '$($data.overall_result)' disagrees with derived '$result'"}
    if(-not $CheckOnly){
        $lines=[Collections.Generic.List[string]]::new()
        $lines.Add('# 修复后验收报告');$lines.Add('');$lines.Add('<!-- Generated from docs/acceptance-findings.json. Do not edit by hand. -->')
        $lines.Add('');$lines.Add('**整体结论：'+$result+'。** 逐项结论见下表；原始场景的 PASS 不替代其关联剩余项及真实宿主验收，未执行项不计为通过。')
        $lines.Add('');$lines.Add('## 运行身份与独立性');$lines.Add('')
        foreach($field in @('report_id','generated_at','verification_actor','operator','reviewer','independence_statement','source_snapshot_id')){$lines.Add('- **'+$field+'**：'+(Cell $data.$field))}
        $lines.Add('- 源码 commit / tree / dirty：`'+$data.source.commit+'` / `'+$data.source.tree+'` / `'+$data.source.dirty+'`。')
        if ($data.build_source_manifest) {
            $lines.Add('- 实际构建副本 snapshot：`'+$data.build_source_snapshot_id+'`；当前工作区 snapshot 与构建副本分别记录原始字节哈希，仅允许并逐文件核验 CRLF/LF 差异。实际构建源码完整归档于 `'+$data.build_source_archive+'`。')
        }
        $lines.Add('- diff SHA-256：`'+$data.source.diff_sha256+'`；未跟踪源码由 source manifest 单独覆盖，dirty diff 本身不能标识完整候选。')
        if($data.historical_evidence_reconciliation){$lines.Add('- 历史日志完整性：两份旧候选运行日志曾追加内容；原始长度前缀与旧索引 SHA 完全一致，另存原始字节及追加后完整副本。旧现场文件及旧索引未改写；本轮绑定恢复副本，不声称旧候选目录始终只读。核对记录：'+(Evidence-Link $data.historical_evidence_reconciliation))}
        $lines.Add('- 红绿源码链：修复前 snapshot `'+$data.pre_fix_source_snapshot_id+'` → 修复后 snapshot `'+$data.source_snapshot_id+'`；修复前 diff SHA-256 `'+$data.pre_fix_source.diff_sha256+'`。')
        $lines.Add('- 同一命令判定：'+$(if($data.oracle_statement){[string]$data.oracle_statement}else{'两阶段调用同一 AcceptanceProbe 和 acceptance-report-cases 驱动器，夹具与判定标准不变；仅替换源码/程序集路径。报告脚本旧接口没有 ExpectedMvid 参数时省略该参数，完整实际 argv 保存在各命令记录。'}))
        $lines.Add('- 工具链：'+(Cell $data.toolchain));$lines.Add('- '+$(if($data.verification_scope){[string]$data.verification_scope}else{'本轮是 L0/L1/模拟命令验收，未安装或启动真实 CAD/Office。'}))
        $lines.Add('');$lines.Add('## 原始快照 → 修复源码 → 同一反例');$lines.Add('')
        $lines.Add('| ID | 原始反例/结果 | 修复文件 | 精确测试选择器 | 修复后结果 | 证据 SHA-256（首组） | 剩余风险 |')
        $lines.Add('|---|---|---|---|---|---|---|')
        foreach($f in $data.findings){
            $before=if(@($f.evidence.before).Count){Evidence-Link $f.evidence.before[0]}else{'无历史前证据'}
            $after=if(@($f.evidence.after).Count){Evidence-Link $f.evidence.after[0]}else{'未执行'}
            $original=if($f.original_counterexample){[string]$f.original_counterexample+'：'+[string]$f.pre_fix_outcome}else{[string]$f.actual}
            $lines.Add('| '+$f.finding_id+' / '+$f.severity+' | '+(Cell $original)+' | '+(Cell ($f.repair_files -join ', '))+' | '+(Cell ($f.test_selectors -join '; '))+' | '+$f.result+' / '+$f.status+' | '+$before+' → '+$after+' | '+(Cell $f.residual_risk)+' |')
        }
        foreach($f in $data.findings){
            $lines.Add('');$lines.Add('### '+$f.finding_id+'：'+$f.component);$lines.Add('');$lines.Add('预期：'+$f.expected);$lines.Add('');$lines.Add('状态：'+$f.status+'；结果：'+$f.result+'；关联项：'+($f.related_findings -join ', '))
            if($f.before_source_snapshot_id -or $f.after_source_snapshot_id){
                $lines.Add('');$lines.Add('本项源码快照：修复前 `'+$f.before_source_snapshot_id+'` → 本轮 `'+$f.after_source_snapshot_id+'`。历史 A01–A03 与本轮 MLeader 故障注入分别保留各自修复前快照，不共用一个旧源码身份。')
            }
            foreach($source in $f.repair_source_sha256){$lines.Add('- 修复源码 `'+$source.path+'`，SHA-256 `'+$source.sha256+'`。')}
            $lines.Add('');$lines.Add('可复制命令（PowerShell 7；工作目录为项目根）：');$lines.Add('');$lines.Add('```powershell');foreach($command in $f.repro_command){$lines.Add($command)};$lines.Add('```')
            $lines.Add('');$lines.Add('| 阶段/次数 | 退出码 | 判定 | 关键输出 | 命令记录 |');$lines.Add('|---|---|---|---|---|')
            foreach($stage in @('pre_fix_runs','post_fix_runs')){foreach($run in $f.$stage){$lines.Add('| '+$stage+' / '+$run.iteration+' | '+$run.exit_code+' | '+$run.result+' | '+(Cell $run.key_output)+' | '+(Cell $run.command_record)+' |')}}
        }
        $lines.Add('');$lines.Add('## 重复性、隔离与清理');$lines.Add('')
        if($data.repeatability.full_collections){
            $lines.Add('| Runtime 集合 | 每轮测试数 | 重复次数 | 原始日志（含精确测试名和哈希） |');$lines.Add('|---|---|---|---|')
            foreach($collection in $data.repeatability.full_collections){
                $logLinks=@($collection.runs|ForEach-Object {Evidence-Link $_.stdout})
                $lines.Add('| '+$collection.version+' | '+@($collection.test_selectors).Count+' | '+@($collection.runs).Count+' | '+($logLinks -join '<br>')+' |')
            }
            $lines.Add('');$lines.Add('完整选择器集合保存在 findings.repeatability.full_collections；生成器逐轮校验原始日志中的测试名、数量、退出码及零跳过。')
        }
        foreach($property in $data.repeatability.PSObject.Properties){if($property.Name -eq 'full_collections'){continue};$lines.Add('- '+$property.Name+'：'+(Cell ($property.Value|ConvertTo-Json -Compress -Depth 5)))}
        if($data.test_assemblies){$lines.Add('');$lines.Add('测试程序集（实际归档字节）：');$lines.Add('');foreach($assembly in $data.test_assemblies){$lines.Add('- '+(Evidence-Link $assembly))}}
        $lines.Add('');$lines.Add('## 负例和状态组合矩阵');$lines.Add('');$lines.Add('| 场景 | 必需 | 结果 | 实际覆盖/缺口 |');$lines.Add('|---|---|---|---|');foreach($row in $data.negative_matrix){$lines.Add('| '+(Cell $row.case)+' | '+$row.required+' | '+$row.result+' | '+(Cell $row.detail)+' |')}
        $lines.Add('');$lines.Add('## 真实宿主矩阵');$lines.Add('');$lines.Add([string]$data.candidate_statement);$lines.Add('');$lines.Add('| 宿主 | Core Console | 候选 DLL | SHA-256 / MVID | manifest SHA | DWG SHA | NETLOAD / 实载身份 | PATDOCTOR | PATGEOMVERIFY / exit / RESULT | 冷启动 | 命令 Undo/Redo | GUI | 结论 |');$lines.Add('|---|---|---|---|---|---|---|---|---|---|---|---|---|')
        foreach($row in $data.host_matrix){$lines.Add('| '+$row.host+' | '+(Cell $row.core_console)+' | '+(Cell $row.candidate_path)+' | '+(Cell $row.candidate_identity)+' | '+$row.candidate_manifest.sha256+' | '+(Cell $row.drawing_sha256)+' | '+(Cell $row.actual_load)+' | '+$row.patdoctor+' | '+(Cell $row.patgeomverify)+' | '+(Cell $row.cold_start)+' | '+(Cell $row.undo_redo_command)+' | '+$row.gui+' | '+$row.result+' |')}
        if($data.real_host_evidence){$lines.Add('');$lines.Add('真实宿主逐次命令、安装恢复、GUI 阻碍和最短复现：'+(Evidence-Link $data.real_host_evidence))}
        $lines.Add('');$lines.Add('## 性能与规模边界');$lines.Add('');$lines.Add((Cell $data.performance.statement));$lines.Add('');$lines.Add('| 请求规模 | 实测顶点数 | 重复次数 | 最小/中位/最大 ms | 断言结果 |');$lines.Add('|---|---|---|---|---|');foreach($row in $data.performance.measurements){$lines.Add('| '+$row.requested_scale+' | '+$row.actual_vertices+' | '+$row.runs+' | '+$row.min_ms+' / '+$row.median_ms+' / '+$row.max_ms+' | '+$row.result+' |')}
        $lines.Add('');$lines.Add('## 残余风险与未证明边界');$lines.Add('');foreach($risk in $data.residual_risks){$lines.Add('- **'+$risk.id+'**：'+$risk.description+'；当前状态：'+$risk.status)}
        $lines.Add('');$lines.Add('## 证据与自动放行规则');$lines.Add('');$lines.Add('- findings 真源：`docs/acceptance-findings.json`，本次 SHA-256 `'+(Get-FileHash -LiteralPath $FindingsPath).Hash.ToLowerInvariant()+'`。');$lines.Add('- 证据索引：'+$data.evidence_index.path+'，SHA-256 `'+$data.evidence_index.sha256+'`；覆盖 '+$index.files.Count+' 个证据文件，任何缺失/篡改均拒绝。');$lines.Add('- 候选 manifest、DLL、测试程序集、运行日志、源码快照、完整命令/cwd/环境/退出码在索引及 run.json 中；null 表示未取得，禁止解释成已通过。')
        $lines.Add('- 生成/校验：`pwsh -NoProfile -File tools/render-acceptance-report.ps1`。退出码：0=PASS，1=FAIL，2=资产或哈希无效，3=SKIP/BLOCKED。加 `-CheckOnly` 只核验，不写报告。')
        $lines.Add('- 历史验收与开发修复记录保留原快照，不再作为当前状态真源。'+$(if($data.delivery_statement){[string]$data.delivery_statement}else{'没有提交、推送、安装或覆盖旧部署 DLL。'}))
        [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath),($lines -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
    }
    Write-Output "ACCEPTANCE $result; evidence_integrity=PASS; report=$OutputPath"
    if($result -eq 'FAIL'){exit 1};if($result -eq 'SKIP/BLOCKED'){exit 3};exit 0
}catch{Write-Error $_ -ErrorAction Continue;exit 2}

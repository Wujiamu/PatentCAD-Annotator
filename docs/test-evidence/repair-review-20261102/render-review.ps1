param([string]$ProjectRoot)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ProjectRoot).Path
$data=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'review-findings.json') -Raw | ConvertFrom-Json
$envInfo=Get-Content -LiteralPath (Join-Path $PSScriptRoot $data.environment) -Raw | ConvertFrom-Json
foreach($finding in $data.findings){
    $log=[IO.File]::ReadAllText((Join-Path $PSScriptRoot $finding.evidence))
    foreach($selector in $finding.selectors){if(-not $log.Contains($selector)){throw "Missing failure selector: $selector"}}
}
$lines=[Collections.Generic.List[string]]::new()
function Line([string]$text=''){ $lines.Add($text) }
Line '# G15–G18 集成后复核（2026-11-02）'
Line
Line '结论：**新增 R-035；R-004／R-016／R-029／R-034 需要补修**。累计 35 个追踪编号，本轮 5 个待处理包 G19–G23。未改生产代码或部署包。'
Line
Line '机器真源：[review-findings.json](../test-evidence/repair-review-20261102/review-findings.json)。本报告由同目录 render-review.ps1 生成；实施边界见[修复计划第 12 节](../repair-plan.md)。旧失败已修复的结论保留，本轮相邻反例不能被旧 PASS 覆盖。'
Line
Line '## 复核候选与隔离'
Line
Line ('- HEAD：`'+$envInfo.head+'`，叠加当前工作树；状态见 working-tree-status.txt。29 个生产／工具源文件封存在 source/；没有把 HEAD 当作完整候选。')
Line ('- 复核角色：'+$data.reviewer)
Line ('- 时间：'+([DateTimeOffset]$envInfo.time).ToString('o')+'（'+$envInfo.timezone+'）；沙箱身份 `'+$envInfo.shell_identity+'`，主机隔离执行身份 `'+$envInfo.host_identity+'`。')
Line ('- 环境：'+$envInfo.os+' / '+$envInfo.architecture+'；PowerShell '+$envInfo.powershell+' / '+$envInfo.shell_framework+'。面板探针目标 net8.0-windows，仅用 CAD 桩；没有加载真实 CAD 插件。')
Line '- 合成 JSON、备份、安装目标与 TRX 均在本轮 evidence 目录；未调用真实注册表 API，未操作真实 DWG、Startup、Normal 或已打开的 Office/CAD。部署包／真实加载路径不适用于本轮 L1，未借用前轮包哈希声称宿主通过。'
Line
Line '| 清单 | SHA-256 |'
Line '|---|---|'
Line ('| [29 个源码快照](../test-evidence/repair-review-20261102/source-manifest.json) | `'+$data.source_manifest.sha256+'` |')
Line ('| [探针及边界桩](../test-evidence/repair-review-20261102/tool-manifest.json) | `'+$data.tool_manifest.sha256+'` |')
Line ('| [原始证据](../test-evidence/repair-review-20261102/evidence-manifest.json) | `'+$data.evidence_manifest.sha256+'` |')
Line
Line '## 实际检查'
Line
Line '| 入口 | 层级 | 本轮结果 |'
Line '|---|---|---|'
foreach($test in $data.tests){
    $counts=if($null -ne $test.passed){"$($test.passed) PASS / $($test.failed) FAIL"}else{'SKIP/BLOCKED'}
    Line ('| '+$test.id+' | '+$test.level+' | '+$counts+'；退出 '+$test.exit_code+'；['+$test.evidence+'](../test-evidence/repair-review-20261102/'+$test.evidence+') |')
}
Line
Line 'VBScript 完整探针的 27 PASS 中含 23 个模式文本检查，只有 7 个展示场景，不能把它们都称为业务测试；边界探针与完整探针的反例有重合，不将断言总数当成独立缺陷数量。'
Line
foreach($test in $data.tests){Line ('- **'+$test.id+'**：'+$test.limitation)}
Line
Line '四种恢复状态已按同一组件断言检查：好当前＋好备份、坏当前＋好备份、好当前＋坏备份、两者均坏。前三者中仅“好当前＋坏备份”的面板入口 FAIL；好当前＋坏备份的底层采用动作 PASS。相关连续改号被拦截、同一操作重试成功清除的对照也 PASS。'
Line
Line '## 可执行反例与责任包'
foreach($finding in $data.findings){
    Line
    Line ('### '+$finding.id+'（'+$finding.kind+'，'+$finding.priority+'，'+$finding.task+'）：'+$finding.title)
    Line
    Line ('- 位置：`'+($finding.locations -join '`、`')+'`。')
    Line ('- 最短场景：'+$finding.scenario)
    Line ('- 预期：'+$finding.expected)
    Line ('- 实际：'+$finding.actual)
    Line ('- 根因与价值：'+$finding.cause)
    Line ('- 修复边界：'+$finding.repair)
    Line ('- 精确选择器：`'+($finding.selectors -join '`、`')+'`；原始输出 `'+$finding.evidence+'`。')
    if($finding.deduplication){Line ('- 去重：'+$finding.deduplication)}
}
Line
Line '## 重跑命令'
Line
Line '从项目根执行。FAIL 探针退出 1 是本轮预期红灯；修复后使用相同场景和业务判定闭环。面板构建命令为 dotnet build docs/test-evidence/repair-review-20261102/palette-probe/Review.csproj -c Release。'
Line
Line '```powershell'
foreach($test in $data.tests | Where-Object {$_.status -ne 'SKIP/BLOCKED'}){Line $test.command}
Line '```'
Line
Line '## 已排除与未覆盖'
Line
foreach($item in $data.exclusions){Line ('- '+$item)}
foreach($item in $data.limits){Line ('- '+$item)}
Line '- 本轮只有修复前反例和对照，没有生产修复后的绿灯。旧任务包的作者自测不等同本轮独立宿主验收。'
Line
[IO.File]::WriteAllText((Join-Path $root 'docs/repair-work/review-20261102.md'),($lines -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
Write-Output 'PASS|review-report|5 findings; failure selectors present'

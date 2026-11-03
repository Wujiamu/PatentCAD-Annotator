param([string]$ProjectRoot='.')
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ProjectRoot).Path
$data=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'review-findings.json') -Raw | ConvertFrom-Json
$envInfo=Get-Content -LiteralPath (Join-Path $PSScriptRoot $data.environment) -Raw | ConvertFrom-Json
$prefix='../test-evidence/repair-review-20261102-r2/'
$lines=[Collections.Generic.List[string]]::new()
function Line([string]$s=''){$lines.Add($s)}
foreach($f in $data.findings){$log=[IO.File]::ReadAllText((Join-Path $PSScriptRoot $f.evidence));foreach($selector in $f.selectors){if(-not $log.Contains($selector)){throw ('Missing selector: '+$selector)}}}
Line '# G19–G23 集中复核（2026-11-02，第二轮）'
Line
Line '**4 项补修：R-004、R-034、R-029、R-035；没有新增编号。R-016 的本轮解析反例与扩展矩阵通过，不再分派 G19 补修。** 原场景通过与相邻组合失败分别记录。后续 G24–G27 见[修复计划第 13 节](../repair-plan.md)。'
Line
Line ('机器真源：[review-findings.json]('+$prefix+'review-findings.json)；本报告由 render-review.ps1 生成。')
Line
Line '## 范围与候选'
Line
Line $data.scope
Line
Line ('- HEAD：`'+$envInfo.head+'`＋工作树；['+$envInfo.source_files+'个源码快照]('+$prefix+'source-manifest.json)。源码不是仅该提交；结束时逐个哈希回读，生产文件未修改。')
Line ('- 开始：'+([DateTimeOffset]$envInfo.time).ToString('o')+'；沙箱 `'+$envInfo.identity+'`，主机 `'+$envInfo.host_identity+'`。PowerShell '+$envInfo.powershell+'，'+$envInfo.framework+'；'+$envInfo.os+'。')
Line ('- 面板探针运行于 '+$envInfo.palette_runtime+'；Word '+$envInfo.word_version+'，'+$envInfo.word_bitness+'。Word仅自建临时文档，探针后 WINWORD='+$envInfo.word_processes_after_probe+'。')
Line ('- 复核角色：'+$data.reviewer)
Line '- 安装集成仅使用 UUID 测试注册表根与本轮临时目录，保留并核对非产品哨兵，不修改真实加载项注册／安装目录。面板只使用 CAD 桩，不加载真实 CAD 部署 DLL。'
Line
Line ('源码／工具／原始证据 SHA-256 见[证据索引]('+$prefix+'review-index.json)、[工具清单]('+$prefix+'tool-manifest.json)、[证据清单]('+$prefix+'evidence-manifest.json)。实际测试程序集哈希、候选 dotm／五包 DLL 哈希另存在清单中，仅用于身份记录，不能推出真实宿主已加载。')
Line
Line '## 先列出的状态组合与结果'
Line
Line ('[原覆盖表]('+$prefix+'coverage-plan.md)在执行前建立；发现失败后仍完成同组组合，另核对实际调用方。')
Line
Line '| 组 | 结果 | 断言范围 |'
Line '|---|---|---|'
foreach($c in $data.coverage){Line ('| '+$c.group+' | '+$c.status+' | '+$c.detail+' |')}
Line
Line '面板最终 114 个断言中 90 PASS、24 FAIL；安装组合 56 PASS、4 FAIL；包装器 10 PASS、1 FAIL。29 个失败断言归并到上述 4 项，不按变体重复建项。C#／VBScript 的相同语料与23个模式检查不累计成独立业务场景。'
Line
Line '## 原修复保留的结论'
Line
foreach($p in $data.previous_fixes){Line ('- **'+$p.id+' / '+$p.status+'**：'+$p.scope)}
Line
Line '## 待补修项'
foreach($f in $data.findings){
    Line
    Line ('### '+$f.id+'（'+$f.priority+'，'+$f.task+'）：'+$f.title)
    Line
    Line ('- 位置：`'+($f.locations -join '`、`')+'`。')
    Line ('- 场景：'+$f.scenario)
    Line ('- 预期：'+$f.expected)
    Line ('- 实际：'+$f.actual)
    Line ('- 根因：'+$f.cause)
    Line ('- 最小边界：'+$f.repair)
    Line ('- 归因／去重：'+$f.classification)
    Line ('- ['+$f.evidence+']('+$prefix+$f.evidence+')；精确选择器 `'+($f.selectors -join '`、`')+'`。')
}
Line
Line '## 命令、退出码与证据层'
Line
Line '| 检查 | 层级 | 结果／退出码 | 原始记录 |'
Line '|---|---|---|---|'
foreach($t in $data.tests){
    $counts=if($null -ne $t.passed){"$($t.passed) PASS / $($t.failed) FAIL"}else{$t.status}
    Line ('| '+$t.id+' | '+$t.level+' | '+$counts+'；exit '+$t.exit_code+' | ['+$t.evidence+']('+$prefix+$t.evidence+') |')
}
Line
foreach($t in $data.tests){
    Line ('- **'+$t.id+'**：'+$t.limit+' 命令：`'+$t.command+'`。')
}
Line
Line '面板重跑前：`dotnet build docs/test-evidence/repair-review-20261102-r2/palette/Review.csproj -c Release`。本轮红灯退出 1 是业务断言失败；修复后保持同一输入、故障和判定，并把绿灯存入新目录，不能覆盖本次原始记录。'
Line
Line '## 排除与限制'
Line
foreach($s in $data.exclusions){Line ('- '+$s)}
foreach($s in $data.limits){Line ('- '+$s)}
Line '- 真实 CAD 宿主、Word 安装后启动链与其他 Office 版本／位数均为 SKIP/BLOCKED。本轮没有全支持范围发布结论，也没有把这些范围变成额外的本机旧宿主任务。'
Line
[IO.File]::WriteAllText((Join-Path $root 'docs/repair-work/review-20261102-r2.md'),($lines -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
'PASS|review-report|4 findings; selectors present'

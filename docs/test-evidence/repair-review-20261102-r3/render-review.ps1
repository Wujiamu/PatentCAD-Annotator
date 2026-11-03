param([string]$ProjectRoot='.')
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ProjectRoot).Path
$data=Get-Content (Join-Path $PSScriptRoot 'review-findings.json') -Raw|ConvertFrom-Json
$envInfo=Get-Content (Join-Path $PSScriptRoot $data.environment) -Raw|ConvertFrom-Json
$prefix='../test-evidence/repair-review-20261102-r3/'
foreach($f in $data.findings){$raw=[IO.File]::ReadAllText((Join-Path $PSScriptRoot $f.evidence));foreach($selector in $f.selectors){if(-not $raw.Contains($selector)){throw ('Missing failure: '+$selector)}}}
$out=[Collections.Generic.List[string]]::new()
function Line([string]$s=''){$out.Add($s)}
Line '# G24–G27 集中复核（2026-11-02，第三轮）'
Line
Line '**本轮保留1项P1补修：R-004 / G28；不新增编号。** 原有114项面板反例、G24/G25交付矩阵和302项代码测试通过；新的读取/解析交错暴露身份与模型错配，合成文件中已复现CAS接受虚构条目编辑。'
Line
Line ('机器真源：[review-findings.json]('+$prefix+'review-findings.json)。本报告由render-review.ps1生成；后续唯一责任包见[修复计划第14节](../repair-plan.md)。')
Line
Line '## 候选与方法'
Line
Line $data.scope
Line
Line ('- 开始：'+([DateTimeOffset]$envInfo.time).ToString('o')+'；HEAD `'+$envInfo.head+'`＋工作树。['+$envInfo.source_files+'个源码快照]('+$prefix+'source-manifest.json)；结束逐个回读，生产源未改。')
Line ('- 复核角色：'+$data.reviewer)
Line ('- 沙箱 `'+$envInfo.identity+'`；主机 `'+$envInfo.host_identity+'`；'+$envInfo.os+'，PowerShell '+$envInfo.powershell+' / '+$envInfo.framework+'；面板运行 '+$envInfo.palette_runtime+'。')
Line '- 只有本轮创建的合成JSON、控件、临时安装目录及UUID测试注册表根。未操作实际DWG、真实加载项注册或用户文档；未安装或探测旧四组CAD。'
Line ('- 身份与证据SHA-256见[索引]('+$prefix+'review-index.json)、[工具清单]('+$prefix+'tool-manifest.json)、[原始证据清单]('+$prefix+'evidence-manifest.json)。部署DLL/dotm仅记录候选哈希，不能推出宿主加载结论。')
Line
Line '## 覆盖结果'
Line
Line ('[执行前覆盖表]('+$prefix+'coverage-plan.md)包含17组；同源断言不重复计为缺陷，同一输入的多个入口不累计成独立业务场景。')
Line
Line '| 组 | 结果 | 范围 |'
Line '|---|---|---|'
foreach($c in $data.coverage){Line ('| '+$c.group+' | '+$c.status+' | '+$c.detail+' |')}
Line
Line '## 原修复的保留结论'
Line
foreach($p in $data.previous_fixes){Line ('- **'+$p.id+' / '+$p.status+'**：'+$p.scope)}
Line
foreach($f in $data.findings){
    Line ('## '+$f.id+'（'+$f.priority+'，'+$f.task+'）：'+$f.title)
    Line
    Line ('- 位置：`'+($f.locations -join '`、`')+'`。')
    Line ('- 最短场景：'+$f.scenario)
    Line ('- 应有结果：'+$f.expected)
    Line ('- 实际：'+$f.actual)
    Line ('- 根因：'+$f.cause)
    Line ('- 最小修复：'+$f.repair)
    Line ('- 归因：'+$f.classification)
    Line ('- 局部验证：'+$f.proof)
    Line ('- ['+$f.evidence+']('+$prefix+$f.evidence+')；选择器 `'+($f.selectors -join '`、`')+'`。')
    Line
}
Line '确定性边界的含义：生产Workflow源码未改；在它准备调用加载器时控制外部文件变化，仍用实际生产加载器解析。内部LoadBytes跨测试程序集不可见时仅通过反射转发；未执行的其他宿主方法用编译名占位，不参与ReadSnapshot路径。'
Line
Line '数据覆盖证明复用了实际NotifySelfWrite、TryApplyEdit和DictWriter.Write，合成前置没有待裁决备份；它证明底层CAS无法发现错配模型，没有声称绕过备份冲突门禁或完成真实CAD GUI端到端复现。文件持续变化时最后身份复核通常会拒绝；A→B→A则可能身份仍相同，故不能靠最后一次哈希检查代替同字节解析。'
Line
Line '## 命令与证据层'
Line
Line '| 检查 | 层级 | 结果／退出码 | 原始输出 |'
Line '|---|---|---|---|'
foreach($t in $data.tests){$n=if($null -ne $t.passed){"$($t.passed) PASS / $($t.failed) FAIL"}else{$t.status};Line ('| '+$t.id+' | '+$t.level+' | '+$n+'；exit '+$t.exit_code+' | ['+$t.evidence+']('+$prefix+$t.evidence+') |')}
Line
foreach($t in $data.tests){Line ('- **'+$t.id+'**：'+$t.limit+' 命令：`'+$t.command+'`。')}
Line
Line '快照探针构建：`dotnet build docs/test-evidence/repair-review-20261102-r3/snapshot/SnapshotProbe.csproj -c Release`。默认编译当前生产源码。局部假设只通过WorkflowSource属性指向本目录snapshot/hypothesis/DictPaletteWorkflow.cs，并输出到独立目录；两者使用同一SnapshotProbe.cs与85条判定。'
Line
Line '## 排除与未覆盖'
Line
foreach($s in $data.exclusions){Line ('- '+$s)}
foreach($s in $data.limits){Line ('- '+$s)}
Line '- 本轮没有生产修复后的绿灯。原通过记录支持对应场景，不构成整个缺陷类别或全支持范围已关闭。'
Line
[IO.File]::WriteAllText((Join-Path $root 'docs/repair-work/review-20261102-r3.md'),($out -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
'PASS|review-report|1 finding; selectors verified'

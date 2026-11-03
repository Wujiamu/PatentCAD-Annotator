# PatentMarker 待修复清单

更新日期：2026-11-02（Asia/Shanghai）  
用途：累计记录连续 review 的缺陷、可能的 Bug 和技术债，供后续逐项修复。各轮均不修改生产代码、安装器或部署包。

## 2026-11-02 G24–G27 后集中复核（当前，第三轮）

本轮按39个源码快照和17组覆盖表复核，**保留1项P1补修：R-004／G28，无新增编号，累计仍35个追踪编号**。原114项面板断言、G24/G25的160/100项矩阵、2025全套302项、两套完整隔离安装矩阵及TRX留存检查均通过。

- 新反例集中在共用`ReadSnapshot`：它先读字节计算身份，又按路径重新读取来解析，可能返回“A身份+B模型”。受控A→B→A交错中，实际生产写入器CAS接受虚构条目编辑，合成文件原内容被覆盖。25个同源失败断言归为一项，不按编码或调用方重复建项。
- [当前机器真源](test-evidence/repair-review-20261102-r3/review-findings.json)、[本轮报告](repair-work/review-20261102-r3.md)、[覆盖表](test-evidence/repair-review-20261102-r3/coverage-plan.md)与[计划第14节G28](repair-plan.md)。R-034刷新也共用此读取入口，统一由G28修，不另分派G25。
- 只在证据副本中将`Load(path)`换成已有`LoadBytes(path, bytes)`，相同85项断言全通过；**生产代码未改，缺陷尚未修复**。39个源文件结束哈希回读一致；局部假设不作为候选包绿灯。
- R-034、R-029、R-035的本轮相关实现复核通过，不再补修；真实宿主范围不由此关闭。VBA/解析源与上一轮一致，本轮未重复Word/2016组扩展测试。四组旧CAD仍仅列GitHub外部宿主验收。

## 2026-11-02 G19–G23 后集中复核（历史，第二轮）

先冻结受影响调用链并列 17 组状态组合，再执行检查。本轮 **无新增编号，4 项补修：R-004、R-034、R-029、R-035**；累计仍为 35 个追踪编号。29 个失败断言按责任归并，不按变体重复建项。**R-016 的双引擎扩展语料与实际 Word L2 均通过，不再分派补修。**

- 当前真源：[review-findings.json](test-evidence/repair-review-20261102-r2/review-findings.json)；[集中复核报告](repair-work/review-20261102-r2.md)；[覆盖表](test-evidence/repair-review-20261102-r2/coverage-plan.md)。后续唯一责任包为[计划第 13 节 G24–G27](repair-plan.md)，G24→G25 串行。
- 原修复通过的部分保留：解析 2016 组双引擎对照、实际 Word L2 17/17、Office 34/34 和48/48、两套现有完整隔离安装矩阵，以及五项只读同步／DOTM结构检查均通过。失败集中在提交后快照、重新确认、跨图纸状态、恢复部分成功、注册未动但文件已动和默认结果留存。
- 当前反例是源代码／组件层，恢复面板使用真实测试控件、安装新组合使用内存注册边界；没有真实 CAD 交互或新的安装后启动放行。未改生产源码、安装器、VBA、部署包或系统权限，37 个源文件哈希结束时回读一致。
- GitHub 外部宿主验收仍单列四组旧 CAD；不安排本机安装／探测。其他宿主范围和作者自测与本轮独立结果分开记录。

## 2026-11-02 G15–G18 后复核（历史，第一轮）

G15–G18 已有实施／集成记录。本轮重新检查后，确认 **新增 R-035；R-004／R-016／R-029／R-034 需继续补修**，累计 35 个追踪编号，本轮待处理 5 项。已修复的原场景保留，不把新反例重复建项。

- [本轮复核报告](repair-work/review-20261102.md)由 [review-findings.json](test-evidence/repair-review-20261102/review-findings.json) 生成；后续唯一责任包为 **G19–G23**，见[修复计划第 12 节](repair-plan.md)。G20→G21 串行交接，其他包文件范围独立。
- R-016 是新增 VBA 边界检查误定位；R-004 是单槽待同步记录的丢失；R-029 是升级暂存阶段失败漏分支；R-034 是备份合法性错误地约束了采用当前版本。各自归原编号补修。R-035 位于新增 VSTest 包装器，与原 FactRunner 的 R-030 分开。
- 本轮证据为 L0/L1。未改生产源码、VBA、安装器、部署包或系统权限。G16 的 VSTest 重跑因 testhost 无法打开父进程句柄而 BLOCKED，不计 PASS；独立组件探针已取得新反例，未做发布放行。
- R-012、R-028 的既有宿主核验结论不变；四组旧 CAD 仍只列 GitHub 外部宿主验收，不安排本机安装或探测。

## 2026-11-01 集成后复核（历史）

其他 Agent 已实施原任务包；本轮对集成后的新快照复核，确认 **新增 R-034，R-004／R-016／R-029 需补修**，累计编号 34 个，本轮待处理 4 项。连续表格漏项候选经真实 Word 取样排除，不建待办。

- 当前证据与分层结果见 [集成复核报告](repair-work/review-20261101.md)，本轮机器真源为 [review-findings.json](test-evidence/repair-review-20261101/review-findings.json)；后续唯一责任包为 G15–G18，见 [修复计划第 11 节](repair-plan.md)。
- 原条目保留初审历史和旧快照哈希；不能从标题推断原代码仍未修复。本轮只给出上述四项的补修结论，没有批量勾选其他未重验条目；各包此前的实施／验收范围以其末尾交接记录为准。
- 维持 R-012 原假设被真实 PPT 推翻、R-028 在 AutoCAD 2026 已核验且不迁移的结论。旧四组仍只列 GitHub 外部宿主验收，不因旧文档状态重新安排本机探测。
- 本轮未改生产源码、安装器或部署包；没有发布放行结论。

## 2026-10-31 初审基线与使用方式（历史）

- 源码基线：`b945b1a68fec26ada268b5a4ee3ac066386e96ab` 加当前未提交及未跟踪的工作树内容；结论针对当前文件，不代表该提交单独包含这些实现。
- 执行身份：`DESKTOP-3VJ2A3B\wjm`。审阅者为本次 Codex 主代理；没有独立验收者参与，也没有形成发布放行结论。
- 范围：五版 CAD 的标注、字典读写与缓存、面板编辑及调度；Word 导出、备份、保存事件与安装；PPT/Visio 的共享读取、绑定、刷新与标注扫描；构建、打包和现有测试的相关入口。属于风险导向 review，不是逐行穷尽审计。
- 第 2 轮（同日）：补查冲突后的继续编辑、解析命中顺序与频率、编号边界、旧版字符串读写、PATALIGN 旧 Leader 路径、Office 标注身份及原生编辑／安装错误分支。新增 R-013 至 R-021，并补强 R-012 的 L1 证据；第 1 轮 16 个源码哈希回读一致。
- 已对照 `docs/maintainability-repair-plan.md`、当前实现和既有验收记录。旧清单中已经实现的单源化、CI、图纸会话隔离等不重复列为未修复。
- P1：优先处理的功能阻断或数据丢失风险。P2：随后处理的错误状态、边界缺陷或维护债。
- “已复现”只适用于注明的证据层。静态发现保留具体触发条件，未经宿主复现不升级为“实机确认”。所有复选框仍未关闭。
- 修复前补齐现场部署包与实际加载 DLL 的路径、SHA-256、宿主版本/位数及最短失败步骤；修复后用同一反例取得红绿证据。已有历史 PASS 的范围限于当时的场景、快照和宿主，不覆盖本轮新增反例。
- 下列代码位置是当前工作树的仓库相对路径与行号；后续变动时按方法名重新定位。历史 `acceptance-findings.json` 未改写，本文件是新增 review 待办，不是替代它的自动验收报告。

## 2026-10-31 待办审定（历史执行口径）

已逐项核对价值、根因、调用方和文件依赖。33 个原编号保留作回归追踪，未发现可以直接删除的完全重复项；不将它们当作 33 个互不相关的并行任务。

- **31 项进入实施计划**；**R-028、R-031 先核验**，没有宿主失败证据时不按推断修改格式或几何算法。R-006 与冻结备份有行为耦合，归 G05 用最小已知输出基线一起修复，避免正常 Word 保存被新门禁误拦截。
- 已合并为 **14 个任务包**。同包统一实现，共享文件串行交接；每项只有一个责任包。逐项裁决、最小改法、协议不变量、任务文件边界、依赖与验收见 [详细修复计划](repair-plan.md)。
- 本节与总表的“审定处理／责任包”为当前口径；后文保留历史发现和证据。修复范围按计划收缩，不能将核验项计为已修复。
- 仍只更新文档，没有实施修复、派发 Agent、安装或发布。原清单、源码快照和用户已有工作树均保留。

## 连续复核记录

用户要求持续复核，直至某轮完成后没有新增待修复项。每轮检查现有清单并按根因去重；已知反例的新组合只补证据，不重复建项。“零新增”仅表示该轮记录的源码与检查范围内没有新发现，不代表缺陷清零或所有宿主通过。

| 轮次 | 检查重点 | 新增 | 累计 | 结论 |
|---|---|---:|---:|---|
| 1 | CAD/Word 数据保护、安装、Office 读取与扫描 | 12 | 12 | 继续复核 |
| 2 | 冲突继续编辑、解析与统计、对齐、字符串协议 | 9 | 21 | 继续复核 |
| 3 | 多文档收尾、撤销绑定、诊断、大括号、Visio 配对 | 7 | 28 | 继续复核 |
| 4 | 安装卸载失败恢复、测试入口门禁、PPT 旋转线条 | 3 | 31 | 继续复核 |
| 5 | 跨语言正则、段落与行边界、四组同步检查 | 2 | 33 | 继续复核 |
| 6 | 相邻解析状态、表格预处理、会话差异及跨入口核对 | 0 | 33 | 无新增，达到用户条件，停止本轮连续复核 |
| 7（集成后新快照） | 解析边界、恢复入口、改号重试、回滚二次失败 | 1 | 34 | R-034 新增；R-004／016／029 补修，不重复计数 |
| 8（G15–G18 后） | 重复编号子串、恢复动作矩阵、待同步生命周期、升级早期失败、结果新鲜度 | 1 | 35 | R-035 新增；R-004／016／029／034 补修 |
| 9（G19–G23 后） | 17组覆盖；提交上下文、热冷缓存、跨图纸、部分写入、回滚组合和结果留存 | 0 | 35 | 4项补修；R-016本轮PASS，不再补修；不将29个失败断言计为29个缺陷 |
| 10（G24–G27 后） | 原矩阵独立重跑；同字节快照与读取/解析交错 | 0 | 35 | R-004一项补修；其余原补修矩阵通过；局部单行假设85/85但生产未改 |

## 待办总表

| 完成 | 编号 | 优先级 | 类型／当前依据 | 问题 | 审定处理／责任包 |
|---|---|---|---|---|---|
| [ ] | R-001 | P1 | 用户现场反馈＋L1 算法复现 | AutoCAD 2007 文字落点持续被误判为拐点过近 | 实施／G03 |
| [ ] | R-002 | P1 | L1 生产解析／加载源码复现 | 2007/2010 把损坏 JSON 当成有效空字典或半截字典 | 实施／G04 |
| [ ] | R-003 | P1 | 静态发现，宿主待复现 | Word 清理旧主名字典前不检查 CAD 修改及恢复价值 | 实施／G05 |
| [ ] | R-004 | P1 | G24原场景通过；同字节快照L1交错FAIL | ReadSnapshot二次读盘可错配身份/模型，后续CAS可覆盖原内容 | 需补修／G28 |
| [ ] | R-005 | P1 | 静态发现，故障注入待做 | 同秒备份重名时先删除已有备份，再复制新备份 | 实施／G05 |
| [ ] | R-006 | P2 | 静态发现，Word 待复现 | 可见字典会把正常 Word 内容变化误判为外部手工修改 | 实施／G05 |
| [ ] | R-007 | P2 | 静态发现＋FSO API 局部复现 | Word 安装器不能把旧清单恢复到已存在的半写入清单上 | 实施／G08 |
| [ ] | R-008 | P2 | 静态发现，Office 待复现 | PPT/Visio 临时读失败可能被内容哈希缓存为持续失败 | 实施／G10 |
| [ ] | R-009 | P1 | 已知技术债，本轮复核 | CAD 按旧模型整文件写回，缺少外部变更前置检查 | 实施／G05 |
| [ ] | R-010 | P2 | 技术债＋L1 生产加载源码复现 | CAD 仅凭修改时间判断变化，会漏掉内容已经改变的字典 | 实施／G05 |
| [ ] | R-011 | P1 | 静态发现，目标宿主待验 | 2007 组安装／卸载只扫描 R17.0，未覆盖声明的 R17.1/R17.2 | 实施／G09 |
| [ ] | R-012 | P2 | 静态＋L1 扫描源码复现，PPT 待验 | PPT 漏标扫描不进入嵌套组合 | 实施／G11 |
| [ ] | R-013 | P1 | L1 生产状态／编辑源码复现 | 选择稍后裁决后继续编辑，会使未解决冲突从面板消失 | 实施／G05 |
| [ ] | R-014 | P2 | L1 生产扫描源码复现 | PPT 编号文字已改动或清空，漏标检查仍相信旧 Tags | 实施／G11 |
| [ ] | R-015 | P2 | L1 生产移动函数复现 | 旧 Leader 对齐跨象限后，文字附着方向仍停留在原侧 | 实施／G03 |
| [ ] | R-016 | P1 | G19已集成；最近独立复核2016组/Word L2通过，本轮源未变 | 编号边界原反例与扩展语料通过；未外推其他宿主 | 不再补修／保留前轮证据 |
| [ ] | R-017 | P2 | 静态调用链＋C# 前置命中复现 | Word 在去重后统计次数，出现次数和名称多数决失真 | 实施／G02 |
| [ ] | R-018 | P2 | L1 生产解析源码复现 | 混合格式的命中顺序按正则批次排列，偏离原文顺序 | 实施／G02 |
| [ ] | R-019 | P2 | L1 生产读写源码复现 | 旧版 JSON 字符串转义会丢字符、制造非法 JSON 或重复换行 | 实施／G04 |
| [ ] | R-020 | P2 | 对齐测量技术债，静态复核 | 旧 Leader 的字高固定 3.5、宽度固定估算，布局判断失准 | 实施／G03 |
| [ ] | R-021 | P2 | 跨语言技术债＋COM／比较器复现 | Word 按大小写分开编号，CAD／Office 却把它们视为同号 | 实施／G02 |
| [ ] | R-022 | P2 | L1 生产设置源码复现 | 关闭非活动图纸也会重置当前图纸的运行设置 | 实施／G07 |
| [ ] | R-023 | P2 | 静态发现，Word 待验 | 显式关闭自动导出后，再打开面板会重新启用 | 实施／G05 |
| [ ] | R-024 | P2 | L1 生产刷新函数复现 | 同一文档撤销／更改绑定后，Office 面板继续使用缓存路径 | 实施／G10 |
| [ ] | R-025 | P2 | L1 生产几何／实体函数复现 | 大括号接受不同 Z 端点，但二维实体只保留起点高程 | 实施／G13 |
| [ ] | R-026 | P2 | L1 生产诊断源码复现 | 原生编辑同步的 FAIL 日志未计入最近错误 | 实施／G07 |
| [ ] | R-027 | P2 | L1 生产扫描源码复现 | Visio 同 ID 的残缺标注会借用其他标签组成有效配对 | 实施／G12 |
| [ ] | R-028 | P2 | 官方契约与源码冲突，宿主待验 | 大括号 Xrecord 使用了文档支持范围外的 xdata 组码 | 先核验／G13 |
| [ ] | R-029 | P2 | G26已集成；本轮组件60/60及两套完整隔离矩阵通过 | 原文件/注册跨阶段失败与日志状态对照通过 | 本轮PASS／不再补修 |
| [ ] | R-030 | P2 | 原始 FactRunner 负对照复现 | 没执行任何 Fact 的测试程序集仍返回成功 | 实施／G01 |
| [ ] | R-031 | P2 | L1 生产端点函数复现，PPT 待验 | 待核验：PPT 旋转线条的编号锚点是否正确 | 先核验／G11 |
| [ ] | R-032 | P1 | 实际 VBScript 正则引擎＋生产 C# 对照 | 跨语言正则语义不一致，VBA 会在大写 Z 处截断标记段落 | 实施／G02 |
| [ ] | R-033 | P2 | L1 生产解析源码复现 | 裸列表正则跨行拼接普通段落与孤立数字 | 实施／G02 |
| [ ] | R-034 | P2 | G25已集成；旧114与G25矩阵100/100通过 | 部分写入/重复失败后的刷新通过；共用快照问题归R-004 | 本轮PASS／不另分派 |
| [ ] | R-035 | P2 | G27已集成；留存/归属15/15及真实34/48通过 | 默认/显式成功及失败结果返回后可复核 | 本轮PASS／不再补修 |

## 各项说明

### R-001：2007 文字落点与避让区域互相冲突

- 范围：2007 组的 `Leader + MText` 创建链路，三点和无限点共用此实现。
- 用户反馈：选择标注点和拐点后，选择文字位置即出现 `ArgumentException: 引线拐点过于靠近文字，请调整拾取点。`；拉远文字仍然失败。现场 DLL、加载路径、哈希及位数尚未取得，不能声称已完成现场包复现。
- 位置：`cad-plugin/Shared/Commands/PatMarkCommand.cs:280、304`；`PatLeaderTextAttachment.cs:84、145`；`PatTextPathRouting.cs:18、45、50`。
- 根因：末端按 `0.4 × 字高` 向文字落点回缩；避让框横向扩展 `0.2 × 字高`，纵向扩展 `max(0.8, 0.4 × 字高)`。程序生成的末端在某些较陡方向下位于扩展框内，所有通向它的候选边均被排除。这个末端被当作必须保留的拾取点，最终报“拐点过近”。
- 本轮 L1：使用当前 `Retract`、`Route` 原码及最小 `Point3d` 桩，构造朝向相符的文字框。字高 3.5，方向 75°/105°/255°/285°，距离 100 和 10000 均 FAIL，错误与现场文字一致；0°、45°对照均 PASS。文字框不是 AutoCAD 实测字体边界。
- 修复方向：统一文字末端和避让框的几何约束，区分用户拐点与生成的文字侧末端，保留真实穿字检查。
- 关闭标准：上述远距离反例由红变绿；补齐四象限、较陡／水平／垂直方向、不同字高、三点／无限点 Enter／显式文字点、直线／样条；真实拾取点位于文字内部仍拒绝。2007 实机结论由 GitHub 外部宿主验收回传。

### R-002：损坏 JSON 被识别为有效数据

- 范围：2007、2010。两版 `SimpleJson.cs` 本轮 SHA-256 相同。
- 位置：`cad-plugin/2007/PatentMarker/IO/SimpleJson.cs:16、24、89、115、137`；`IO/DictEntry.cs:294`；共享 `cad-plugin/Shared/IO/DictConflict.cs:117、128、135`。2010 同路径实现相同。
- 触发：输入截断、空文件、根节点类型不对或尾随垃圾；解析器不要求消费完整输入，也不要求对象／数组／字符串完整闭合，非对象根节点直接返回空对象。
- 本轮 L1：直接编译生产 `SimpleJson` 与 2007 `DictLoader`，仅用桩替代宿主、配置及日志。`{ invalid json }}}`、空文件、字符串根节点及尾随垃圾均返回非空空模型；截断的 `{"entries":[{"number":"301","name":"sample"}` 返回含 1 项的模型，均为 FAIL。合法 `{"entries":[]}` 对照 PASS。
- 影响：损坏数据可能替换缓存／Diff 基线；“恢复 CAD”只检查 `Load` 是否为 null，因而存在把损坏备份作为空／部分字典写回并删除备份的路径。该恢复全链路尚未运行。
- 相邻边界：2013/2015 的 `DictEntry.cs:318` 与 2025 的 `DictEntry.cs:306` 会把顶层 JSON `null` 转为空模型，需在同一轮明确无效根节点与合法空字典的契约，避免只修旧解析器语法。
- 关闭标准：五版对无效语法／根节点一致失败，保留合法旧字典兼容性；损坏备份恢复必须保留当前 JSON、备份字节和属性；真实空 entries 仍能正常读取。测试必须覆盖旧版实际解析器。

### R-003：旧 Word 主名字典被无条件删除

- 范围：共用 Word VBA，因此影响 CAD、PPT、Visio 使用的字典生产端。
- 位置：`vba/AutoExport.bas:411`、`CleanupOrphanWordDict:822`，尤其 `844–845`。
- 触发：`说明.docx` 原来导出 `说明.dict.json`；CAD 已修改该旧字典。后来同目录出现／选定不同主名 DWG，Word 成功导出新主名 JSON 后清理旧字典。
- 问题：清理只核对精确主名与新旧路径不同，随后清属性并 `Kill`；未读取旧文件的 CAD 标记、未检查是否含待裁决内容、未建立可恢复副本，也未证明这个文件属于本次导出的历史产物。`BackupIfCadModified` 检查的是新 `outPath`，不能保护即将删除的旧文件。
- 影响：CAD 独有修改可能在路径切换时丢失；即使当前没有 CAD 标记，也需要确认旧路径所有权与恢复价值。
- 当前审定／关闭标准（G05）：采用更小的修复——停止自动删除旧 Word 主名字典，保留原字节、完整属性并诊断路径；不新建所有权追踪系统。用“旧文件含 CAD 独有项＋新 DWG 成功导出”红绿验证，断言旧文件仍在、其他主名字典不变。
- 当前证据：L0 静态调用链；未启动 Word 或修改任何实际用户文档。

### R-004：改号部分失败可能被当作正常同步

- 2026-11-02 第三轮：原G24矩阵160/160、旧面板114/114均通过；**ReadSnapshot所称“同一份字节”尚未实际落实**。`File.ReadAllBytes`仅用于SHA，`DictLoader.Load(path)`再次读盘；读取与解析之间改写可产生A身份+B模型。在A→B→A合成反例中，登记缓存后实际DictWriter的CAS仍接受虚构302→303，原文件被覆盖。有效/合法空/坏JSON/坏UTF8/BOM×四种交错，85项中25FAIL；证据副本只换为`LoadBytes(path, bytes)`后85/85。转G28，修共享入口一次，覆盖重试、重新确认与裁决刷新，不再拆新任务。下文为历史。
- 2026-11-02 第二轮：G21原反例通过，**当前剩余20个失败断言合为三部分**：①保存后Reload再重建提交身份，会把外部版本或MISSING当成自己刚提交的版本；同Document换路径也未拦；②重新确认配最新文件身份却用旧模型查新号，坏字典/缺新号也会调用CAD；③按钮和确认位未按图纸刷新，关闭复用历史路径的另一文档还能清掉原图纸欠账。转G24一次处理，并覆盖实际模态编辑调用方、热冷缓存、A/B切换及另存为后关闭。详见第二轮报告，下文为历史。
- 2026-11-02 复核：G17 的字典快照与相关改号拦截已生效；**单槽位仍允许无关改号，其成功／零匹配会清掉 301→302 旧任务，失败会覆盖成 401→402；首次同步因内容漂移拒绝也不登记欠账**。四个 L1 反例归本项，转 G21，不新增映射队列。详见本轮报告和 `palette-review-final.txt`。下文为历史。
- 2026-11-01 复核：原部分失败报告／事务处理已有实现；**重试上下文与连续改号仍有反例，转 G17 补修**。见本轮报告及 rename-probe；以下保留初审故障历史，不表示原成功计数修复无效。
- 范围：五版 CAD 共享实体改号与面板编辑。
- 位置：`cad-plugin/Shared/Cad/PatEntityHelper.cs:278、390、409、427、443`；`Shared/Palette/DictPaletteCadService.cs:27`；`DictPaletteControl.cs:762、774、786`；`EditEntryDialog.cs:190`。
- 触发：同号有多个 PAT 标注，其中一项不能写入，例如锁定图层或实体写入异常；用户在面板修改编号。
- 问题：对话框先写 JSON 并关闭；随后改图纸。改号循环及底层 setter 吞掉单实体异常，只返回成功数；上层仍提交其他实体并显示“同步了 N 个”，无法区分“没有旧号”和“匹配实体修改失败”。整个 CAD 事务失败时也只有状态提示，没有补偿或明确的待处理记录。
- 影响：JSON 已为新号，部分图纸实体仍是旧号；用户可能只看到正常的同步数量。
- 修复方向：保留匹配数、成功数和失败对象／原因；部分失败必须明确报告，并提供与现有写回流程匹配的补偿或重试方案，无需扩建通用分布式事务框架。
- 关闭标准：同号两处、一处受控拒写时，不能报告完整成功；核对 JSON、两处实体、缓存和后续重试。锁定图层及事务提交失败的真实宿主场景尚待复现。

### R-005：同秒备份重名时先毁掉旧备份

- 范围：Word 覆盖 CAD 修改／可见 JSON 手工修改前的备份。
- 位置：`vba/AutoExport.bas:784–793`，`BackupIfCadModified`。
- 触发：同一字典一秒内再次需要备份，生成相同 `.word-yyyymmdd-hhnnss.bak`；删除旧备份后，新 `FileCopy` 因权限、占用或其他 I/O 故障失败。
- 问题：已有备份被先清属性、删除，复制失败返回 False 时无法恢复；这与本函数“先建立新备份再清理旧备份”的保护目标相冲突。
- 影响：主字典覆盖会被阻止，但既有 CAD 历史恢复点已经丢失。不能把“阻止导出”当作“所有备份均保住”。
- 当前审定／关闭标准（G05）：与 R-013 统一为未裁决备份不覆盖；优先保留现有命名与 FindWordBackup 规则，不先新增后缀协议。已有恢复点不能先删后复制，后续修改也不能滚动清掉它；具体状态处理见计划。同秒重入、复制拒绝、隐藏属性反例须保留旧备份字节与完整属性。
- 当前证据：L0 静态发现；未执行 Word 同秒故障注入。

### R-006：显示字典后，正常 Word 修改也会制造冲突备份

- 当前审定：实施（G05）。冻结备份与待裁决门禁若不区分正常 Word 变化，会将误报放大成额外保存拦截；因此同包维护最小已知输出基线，复用现有比较逻辑，不单独建设同步／版本服务。无基线或基线失败时保守保护并如实诊断，不能承诺任何异常情况下都零误报。
- 位置：`vba/AutoExport.bas:371–377、712–755`，`IsVisibleDictContentChanged`。
- 触发：用户打开 JSON 可见开关，不手工编辑 JSON；只在 Word 中修改一个有效编号／名称并保存。
- 问题：函数把“磁盘旧 JSON”和“本次新 Word 导出”直接比较，仅抹除提取时间。正常的 Word 业务变化也会使二者不同，被 `manualEditDetected` 当作外部修改强制备份。
- 影响：CAD 会发现无 CAD 标记的新 JSON 加 Word 备份，从而提示本不需要的裁决；选择恢复时还可能退回旧 Word 字典。
- 修复方向／关闭标准：按最后一次已知导出内容判定磁盘是否被外部改动，不能用本次新内容充当旧基线。验证“只改 Word 不产生伪冲突；只改可见 JSON 会备份；两边都改进入裁决”；换进程后的基线策略需明确。
- 当前证据：L0 静态确认比较逻辑；真实 Word/CAD 组合流程尚未复现。

### R-007：半写入所有权清单阻止安装回滚

- 位置：`word-addin/install-vba.vbs:264–293`；现有隔离测试 `tools/verify-vba-installer.ps1:73`。
- 触发：升级时旧清单已改名备份；新清单创建成功，但后续 `WriteLine`／`Close` 失败，留下空／部分写入的新清单。
- 问题：错误分支直接 `fso.MoveFile manifestBackupPath, manifestPath`，没有处理自己刚创建的残留目标；FSO 不覆盖已有目标。恢复错误又被 `On Error Resume Next` 吞掉，提示仍称 previous add-in restored。
- 本轮局部复现：仅在临时目录调用同一个 FSO MoveFile API，预置旧备份与半写入目标，得到 `0x800A003A (CTL_E_FILEALREADYEXISTS)`；目标仍为半写入内容，旧备份仍存在。该证据不等于完整安装器故障复现。
- 测试缺口：现有测试锁住原清单，在备份旧清单时即失败，没有进入“新清单已经创建后写入失败”的分支。
- 修复方向／关闭标准：清单先暂存并验证，再激活；所有回滚步骤都读取错误并验证旧文件／清单实际恢复。隔离 Startup 注入中途写入／关闭失败，核对旧产品字节、旧清单字节、非产品哨兵及诊断；不得使用真实 Startup 做破坏性试验。

### R-008：Office 把瞬时读取失败缓存为内容错误

- 范围：PPT、Visio 的字典自动刷新。
- 位置：`office-com-addin/src/PatentOffice.PowerPoint/PaletteForm.cs:309–318、337–345`；`PatentOffice.Visio/PaletteForm.cs:241–250、266–274`。
- 触发：文件哈希可读，随后解析读取时发生一次临时 I/O／共享冲突；故障解除后文件内容与哈希不变。Visio 还会在 catch 中重新取哈希，可能把随后已修复文件的哈希关联到之前的失败。
- 问题：`_lastAttemptSucceeded=false` 与哈希绑定；后续相同哈希直接返回旧失败，不再尝试读取。没有区分确定性的内容解析错误与可以恢复的临时 I/O 错误。
- 影响：有效字典恢复可读后仍暂停标注。PPT 可用“重新加载”强制刷新；Visio 当前主要需要重新绑定或切换文档等方式重置状态。
- 修复方向／关闭标准：只缓存确定的内容解析失败；临时 I/O 失败在后续轮询恢复，不引入后台服务。注入一次读取失败后恢复权限／解除占用，保持字节不变，断言自动恢复；损坏 JSON 则保持可诊断的失败状态。
- 当前证据：L0 静态状态机发现；没有启动 PPT/Visio 或进行 COM 故障注入。

### R-009：旧模型整文件写回缺少外部版本核对

- 类型：已有并发技术债，本轮核对当前实现仍存在。
- 位置：`cad-plugin/Shared/Palette/EditEntryDialog.cs:133、190`；`PasteRecognizeDialog.cs:195、203`；`Shared/IO/PatSettings.cs:142`；各版 `IO/DictWriter.cs` 的 `Write`。
- 场景：在 CAD 打开条目编辑对话框，保留旧模型 A；期间 Word 保存产生 B；随后 CAD 将旧模型 A 的局部编辑作为整份字典写回，覆盖 B 中无关的新项／修改。CAD 两个进程使用同一字典时同样存在旧快照写回问题。
- 现状：唯一临时名和原子替换已经实现，不再把旧“固定 .tmp”问题列为待办；但写回接口没有调用者读取时的内容版本／哈希，也没有覆盖前的外部变化核对。
- 影响：完整合法的新 JSON 仍可能丢更新；单文件替换的完整性不能证明这个场景被保护。
- 当前审定／修复方向（G05）：编辑／裁决／粘贴等入口携带读取版本；与 R-010 共用同一次读取的内容身份，Word/CAD 共同参与短持有锁以覆盖检查到替换窗口。外部变化后阻止静默覆盖并要求重载或裁决；保留轻量文件通信，不声称 JSON–DWG 事务或阻止第三方编辑器写入。
- 关闭标准：A 读 → B 写 → A 保存的可控交错下，B 的新增条目不得静默丢失；同时验证 Word 备份检查至最终替换之间的写入窗口。当前仅静态确认缺少前置条件，未做宿主并发测试。

### R-010：只用文件修改时间判断变化

- 范围：五版 CAD 读取缓存。
- 位置：2007/2010 `IO/DictEntry.cs:160、187–194`；2013/2015 `IO/DictEntry.cs:178、206–216`；2025 `IO/DictEntry.cs:166、203–210`。
- 场景：外部工具恢复文件时保留原时间戳，或文件系统时间粒度使两次内容变更时间相同；缓存会被继续视为有效。
- 本轮 L1：使用生产 2007 加载器读入编号 301，将磁盘改为 602 后恢复同一修改时间；`IsFileChanged=False`，再次按当前图纸加载仍为 301，直接读磁盘为 602，结果 FAIL。宿主、配置、日志使用桩。
- 影响：面板和命令可能继续使用旧字典；若随后编辑写回，会放大 R-009 的丢更新风险。
- 修复方向／关闭标准：引入适当的内容身份核对并沿用 `NotifySelfWrite`，不要求每次轮询反复反序列化。同时间戳、同长度但不同内容的替换能被发现；自写不产生伪 Diff；A/B 图纸缓存隔离不退化。
- 当前限制：不是网络文件系统实测；本轮没有测量新增哈希检查的性能。

### R-011：2007 组安装范围小于支持声明

- 位置：`PatentMarker-2007-deploy/install-2007.vbs:160、373`；`uninstall-2007.vbs:77、231`；对照 `cad-plugin/2007/README.md:3、13` 与 `doctor-2007.vbs:52`。
- 问题：README 声明 AutoCAD 2007/2008/2009（R17.0–R17.2），doctor 也枚举三个注册表版本；安装器及卸载器把扫描根和相关 Support 目录固定为 R17.0。
- 影响：仅安装 2008／2009 时可能找不到目标；混装时不会逐一处理 R17.1／R17.2 的配置。现有 `tools/check-installer-contract.ps1` 的 2007 契约仅要求 R17.0，因此不能捕获缺口。
- 修复方向／关闭标准：按当前支持声明对称枚举 R17.0/R17.1/R17.2，并保留 HKCU/HKLM、产品及配置去重；补齐隔离注册表契约和外部宿主安装／卸载验证。实际 SDK／运行时兼容性另需目标环境证据，不能由注册表枚举通过外推。
- 当前证据：L0 静态交叉核对；本机没有探测、安装或启动旧 CAD。

### R-012：PPT 嵌套组合可能被漏标扫描忽略

- 位置：`office-com-addin/src/PatentOffice.PowerPoint/PowerPointAnnotations.cs:99–140`，`GetMarkedNumbers`。
- 条件：有效的产品“直线＋编号”组合仍保留身份 Tags，但被放入包含其他图形的外层组合。
- 问题：扫描只遍历 `slide.Shapes` 顶层；外层组合不满足恰好两项“直线＋文本框”时直接跳过，不遍历其 `GroupItems`。
- 影响：仍可见的已有标注可能被报为漏标。需在真实 PPT 中确认用户组合操作后的 Tags 和层级，再把该条件升级为实机确认。
- 修复方向／关闭标准：对组合层级递归扫描，沿用现有产品身份与形态校验，避免普通用户组合被算作 PAT 标注；覆盖单层／多层组合、重复编号、普通文字和已破坏产品组合。没有必要引入新的标注存储格式。
- 第 1 轮证据：L0 静态发现；PPT 代码测试当前仅接入共享字典测试，没有覆盖该扫描场景。
- 第 2 轮补充：直接编译生产 `PowerPointAnnotations.cs`，用内存形状／集合桩传入保留 Tags 的嵌套产品组合；顶层有效组合对照 PASS，嵌套组合返回 `marked_301=False`，结果 FAIL。该 L1 证据不代表真实 PPT 组合操作已经验证。

### R-013：待裁决状态被后续 CAD 编辑清掉

- 范围：五版 CAD 的冲突识别、条目编辑／新增／删除与粘贴写回。
- 位置：`cad-plugin/Shared/IO/DictConflict.cs:73–79`；各版 `DictWriter` 的 `MarkCadModified`；`Shared/Palette/DictPaletteControl.cs:659、735、859`。
- 场景：Word 已覆盖一次 CAD 修改并留下备份，用户选择“稍后”；之后在 CAD 中编辑当前 Word 字典的一个名称。
- 根因：编辑会设置 `modified_by=cad`，而 `IsPendingConflict` 只要看到这个标记就返回 False，即使原备份仍在、用户也从未裁决。编辑入口没有拦住这个状态组合。
- 本轮 L1：直接执行生产 `IsPendingConflict` 与 `TryApplyEdit`，`before=True → edited=True → after=False`，备份仍存在，结果 FAIL。探针只验证状态与模型修改，未调用文件写入或 GUI。
- 影响：面板裁决按钮会被禁用，未处理的旧 CAD 内容失去提示；后续 Word 再备份当前 CAD 修改并清理旧备份时，还可能使原待裁决版本失去恢复入口。后半段覆盖流程尚未运行。
- 修复方向／关闭标准：待裁决状态不能由无关编辑的 CAD 标记隐式清除；明确保留待裁决来源或在处理前限制写回。覆盖“稍后→改名称／编号／新增／删除／粘贴→重开面板”，冲突持续可见，原备份保留到用户明确裁决。
- 与 R-009 的区别：本项是顺序操作就能触发的状态丢失，不需要并发写入。

### R-014：PPT 可见编号与身份标签不一致仍算已标注

- 位置：`office-com-addin/src/PatentOffice.PowerPoint/PowerPointAnnotations.cs:99–140`，`GetMarkedNumbers`／`IsPatentAnnotationGroup`。
- 场景：用户在 PPT 中直接把产品组合内的编号 `301` 改成 `999`，或删除文本内容；组合 Tags 仍为 `PATNUMBER=301`。
- 根因：形态检查只要求两项“直线＋文本框”，随后直接采用组合 Tags 的编号；没有读取文本框的实际文字。Visio 的扫描已有可见文字与身份编号的比对，PPT 未做对应检查。
- 本轮 L1：生产扫描源码＋内存形状桩，文字为 `301` 的对照 PASS；文字改为 `999` 或空串后，仍返回原编号 301 已标注，两项均 FAIL。
- 影响：漏标检查可能漏报实际缺失的原编号，用户看到的图面与统计结论不一致。真实 PPT 的文本编辑／保存后 Tags 行为仍需确认。
- 关闭标准：有效组合的可见文字须与身份编号按 `NumberIdentity` 一致；不一致或空文字不能报旧号已完成，并提供可诊断状态。覆盖修改文字、清空、普通组合和重复同号的有效标注，不误读用户普通文字。

### R-015：旧 Leader 对齐后没有重算文字附着角

- 范围：2007 新建的独立 `Leader + MText`，以及其他四版中仍可读取／对齐的旧 Leader。
- 位置：`cad-plugin/Shared/Commands/PatAlignCommand.cs:383–400`；MLeader 组各版 `Commands/PatAlignCommand.cs` 的旧 Leader 分支（2025 为 `429–437`）；共享 `PatLeaderTextAttachment.Get`／`SetTextEndpoint`。
- 根因：`MoveTarget` 只改 MText.Location 并更新末端，不按新文字位置与稳定拐点重算 Attachment。创建时选好的象限在跨侧对齐后继续保留。
- 本轮 L1：编译当前生产 `MoveTarget`、`Get`、`Retract`、`SetTextEndpoint` 函数，用桩替代事务、实体及关联读取。稳定拐点 `(0,0)`，原文字 `(10,10)` 为 BottomLeft；移到 `(-10,-10)` 后应为 TopRight，实际仍为 BottomLeft，函数却返回成功，结果 FAIL；同象限移动对照 PASS。
- 影响：文字可能朝向引线一侧展开，末段进入文字区域或间距异常。证据限函数层，不是 AutoCAD 提交／重生成后的实测。
- 关闭标准：四象限跨侧、水平／垂直边界的对齐后附着角、文字位置、末端间距和穿字检查一致；保留旧原生 Annotation 关联的兼容行为。旧 CAD 组交 GitHub 外部宿主验收。

### R-016：编号在前时被截成后面的数字部分

- 2026-11-02 第二轮：**G19通过本轮独立复核**。2016组C#与实际VBScript语料一致，Word16.0/64位真实BuildModel L2 17/17；编号前置/名称前置/两种括号、重复数字、编号各形态、分隔符、代表空白和五/六位边界均有对照。不再重开补修；通过层级不外推其他Word/CAD宿主，也不替代安装放行。下文为历史反例。
- 2026-11-02 复核：G15 已拒绝名称在前的超长编号；**新增 `InStr(1, m.Value, number)` 会命中名称中较早的同文数字**。`壳体X123 支架 123、` 等三例 C# 均正确，实际 VBScript 完整提取均 0 命中。转 G19 补修编号起点定位；不能撤销完整编号左边界，也不扩大为名称语法重写。实际 Word BuildModel 本轮未运行。下文为历史。
- 2026-11-01 复核：S1／10-1 及编号在前的超长反例已通过；**名称在前的“底座123456、”仍得到 23456／底座1，转 G15 补修**。C# 和实际 VBScript 模式均失败，不能将原 G02 覆盖范围外推到所有排列。
- 范围：五版 CAD 粘贴识别的共享解析器及 Word VBA 的对应规则。
- 位置：`cad-plugin/Shared/IO/MarkingTextParser.cs:52、56、98`；`vba/Patterns.bas:45、49`；`vba/DictModel.bas:34`。
- 根因：旧格式／编号在前的捕获从 `\d{1,5}` 开始，没有覆盖已声明的字母前缀和连字符子编号，也没有阻止从较长编号中间起配；新格式规则则支持这些形式。
- 本轮 L1：生产 C# `ExtractAll("S1底座，")` 得到编号 `1`；`10-1支架；` 得到 `1`；`S1(底座)、` 也得到 `1`，三项均 FAIL。名称在前的 `底座S1、` 对照正确得到 `S1`。
- 影响：合法编号被改写，多个子编号可能被并入同一个 `1`；即使决定不支持某种排列，也应明确拒绝，不能静默接受尾部数字。
- 关闭标准：编号在前／名称在前／括号／表格对支持形式识别一致，保留完整编号；补齐 `S1`、`10-1`、`123A1` 及超过长度上限的边界，防止中间截取。VBA 现仅静态核对相同模式，后续须真实 Word 与共享语料对照。

### R-017：先去重使 Word 频率统计失去依据

- 位置：`vba/Patterns.bas:94、139–153`；`vba/DictModel.bas:55、61–85、111、204–212`；对应 C# `MarkingTextParser.cs:250、298`。
- 根因：`Patterns.ExtractAll` 先按 `(number,name)` 去重，`BuildModel` 再逐项累加 occurrences 和 nameCounts。同一对编号／名称的重复已经丢掉，每个不同名称最多只贡献一次，“MostFrequent”无法按实际出现频率择名。
- 反例：`1支架，1底座，1底座，` 应能保留“总出现 3 次、底座出现 2 次”的依据；前置去重仅剩两项，后续按当前消费者逻辑会得到 occurrences=2、名称支架。
- 本轮依据：生产 C# 对应提取器实际返回两项；本轮回放 VBA 消费者的计数／择名逻辑得到上述错误结果。VBA 的生产调用链经静态核对，未在 Word 执行 `BuildModel`，不能记为 Word L2。
- 修复方向／关闭标准：为模型统计保留原始命中或携带频率；保持去重展示／粘贴的现有契约，避免直接取消去重而制造重复条目。真实 VBA 验证重复同名、同号不同名、频率相同的稳定择名和 conflicts 候选，C# 对照不能自己生成权威期望。

### R-018：混合格式输出按规则批次重新排序

- 位置：`cad-plugin/Shared/IO/MarkingTextParser.cs:211–251、298`；`vba/Patterns.bas:41–95`；Word 模型按这些命中首次插入组装条目。
- 根因：先追加所有旧格式，再追加所有新格式和裸列表；虽然保存了 Position，最终没有按原文位置稳定排序。“保持首次出现顺序”实际只保留了规则批次内部的顺序。
- 本轮 L1：生产 C# 提取 `支架2、1底座，`，预期按原文为 `2,1`，实际为 `1,2`，结果 FAIL。VBA 对应批次追加顺序相同，尚未进行 Word 实测。
- 影响：混用格式时字典和面板顺序变化，名称频率相同时还会改变 R-017 的择名优先项；VBA/C# 输出相同也不能证明符合原文顺序。
- 关闭标准：保留既有重叠命中优先级后，按获胜命中的 Position 稳定排序；覆盖普通／括号／新格式／裸列表交错、相同名称及紧邻分隔符，核对 number/name/顺序。

### R-019：旧版字符串转义与合法 JSON 不一致

- 范围：2007/2010 JSON 读写；两版 `DictWriter.cs` 本轮字节相同。Word `JsonWriter.bas:96–105` 也有未转义其余控制字符的相邻边界，但当前 Word 模型可达输入未验证，不据此宣称 Word 已复现。
- 位置：旧版 `IO/SimpleJson.cs:150–171`；`IO/DictWriter.cs:148–162`。
- 本轮 L1 反例：合法 `{"name":"A\bB\fC"}` 被生产解析器读为 `AbBfC`，丢掉退格／换页语义；名称含 U+000B 时，生产序列化器把控制字符原样写入，严格 JSON 读取拒绝；CRLF 被分别转成两个 `\n`，回读为两个 LF，而 Word 对应转义约定把 CRLF 合为一个 LF。三项均 FAIL。
- 业务路径：可见 JSON 允许手工编辑；其中合法转义字符被旧版 CAD 读入后，再编辑其他条目整文件写回，就可能改坏既有字符串，甚至生成不能被其他宿主读取的 JSON。
- 与 R-002 的区别：本项关注合法输入的值保真与生成端合法性；R-002 关注损坏输入被接受。
- 关闭标准：按约定完整处理 U+0000–U+001F 与所有 JSON 转义；明确 CRLF 规范化规则并保持 VBA/C# 一致；用严格解析器做生成端对照，逐字符核对合法转义往返，不破坏 `modified_by` 的现有备份识别格式。

### R-020：旧 Leader 对齐测量仍使用固定估算

- 类型：当前生产路径的布局技术债，独立回归断言；与 R-015、R-001 归 G03 同包实现，复用同一关联文字测量和定位语义。
- 位置：`cad-plugin/Shared/Commands/PatAlignCommand.cs:324–333、365–367`；MLeader 组各版旧 Leader 分支（2025 为 `342–348、391–393`）；布局使用处为 GapPerTextHeight／ColGapPerTextHeight 及紧凑排列判断。
- 现状：能定位关联 MText，却仍把旧 Leader 的 TextHeight 写死为 3.5、Width 设为 `2 × TextHeight`；不读取关联文字的实际字高和可用宽度。独立 MText／新 MLeader 分支已经读取相应真实属性。
- 影响：用户使用非默认字高或较长编号时，空间是否足够、排列间距和第二列偏移会按错误尺寸计算，存在重叠或不必要溢出风险。仅静态复核，未执行实际图面布局验收。
- 修复方向／关闭标准：复用已存在的关联读取，优先真实字高／宽度，确实不可读取时才有诊断地估算；验证字高 1/3.5/10、长编号、混合字高、线／框模式及原生／扩展字典关联，不扩建通用排版引擎。

### R-021：Word 与消费端对同号身份的判定不同

- 类型：跨语言协议技术债。
- 位置：`vba/DictModel.bas:57–73` 的 entries 字典未设置 CompareMode；对照 `cad-plugin/Shared/IO/NumberIdentity.cs:25–34`，该比较器已被 CAD／Office 使用。
- 场景：同一段出现 `1342A` 和 `1342a`，名称又不同。Word 以区分大小写的键分成两条，不会作为同号不同名生成同一组冲突；CAD／Office 扫描和改号却按同号处理，CAD 合并写回还可能合掉其中一条。
- 本轮机制复现：与 VBA 相同的 `Scripting.Dictionary` 默认 CompareMode=0，两种大小写可分别插入，Count=2；生产 `NumberIdentity.AreEqual` 对同一对输入返回 True，结果 FAIL。没有在 Word 中运行完整模型导出。
- 修复方向／关闭标准：明确 Word 按消费端的编号身份归组，同时保留首次编号原文、顺序与冲突候选；不擅自把编号转大写或整数。用两种大小写同名／异名、前后空白、`1342A/1342A1` 的真实 VBA→JSON→各消费端链路对照验证。

### R-022：释放非活动图纸时重置活动设置

- 位置：`cad-plugin/Shared/IO/PatSettings.cs:56–60`；`Shared/Palette/PatPaletteCommand.cs:117–126`。
- 根因：`Release(drawingPath)` 删除指定图纸项后，无条件把 `_current` 换成新默认对象，没有核对释放的是不是当前设置所属图纸。关闭文档处理器对每个关闭文档都会调用它。
- 本轮 L1：先创建 A/B 设置，再激活 A（字高 7.25、箭头开启），随后释放 B；当前对象从 A 变成默认对象，字高 3.5、箭头关闭，结果 FAIL。A 的字典项仍在，属于再次激活前的当前设置错误，并非已经证明持久化数据被删除。
- 影响：若关闭的是非活动图纸且没有再次触发 A 的激活，后续标注可能使用与 A 面板显示不同的默认设置。真实 CAD 事件顺序待验。
- 关闭标准：释放非当前图纸不动 Current；关闭当前图纸按明确生命周期清理。覆盖 A 活动时关闭 B、关闭活动文档后切换、同名未保存文档和重复释放，不串用设置。

### R-023：重开 Word 面板会撤销用户的关闭选择

- 位置：`vba/AutoExport.bas:14–16、23–28、78–84`；`vba/PatentDictPanel.frm:31–46`。
- 场景：在面板取消“保存 Word 时自动导出”，关闭面板，再通过同一用户宏重开面板；整个过程处于同一 Word 进程。
- 根因：取消勾选会释放钩子并置 `m_enabled=False`；`ShowPatentDictPanel` 对所有 False 状态直接执行 `InitializeAutoExport("PanelOpen")`，不能区分首次初始化和用户显式禁用。
- 影响：查看面板就重新打开保存钩子，用户的关闭选择失效。现有 L2 测试只覆盖直接开关及处理器调用，没有验证“关闭→关闭窗体→通过用户入口重开”。
- 当前依据：L0 静态调用链，未在 Word 操作窗体。
- 关闭标准：同进程重开面板保留显式关闭状态；首次启动／初始化失败恢复与 VBA Reset 的状态须分别验证，不能通过取消启动初始化绕过既定自动启动放行流程。

### R-024：Office 绑定的撤销结果未反映到面板

- 范围：PPT/Visio；本轮函数探针在 Visio 路径执行，PPT 的相同缓存条件经静态核对。
- 位置：Visio `PaletteForm.cs:172–216、433–441`、`VisioDocumentBinding.cs:93–103`；PPT `PaletteForm.cs:167–210、403–414`。
- 根因：只有文档对象／文档键变化时才重新 Resolve 绑定。绑定保存在宿主文档里，可以由撤销等动作改变；同一对象和路径未变时，面板仍使用 `_boundPath`。
- 本轮 L1：直接编译生产 Visio `RefreshHostState`、文档键及绑定解析函数，以内存文档／控件桩模拟“A→绑定 B→文档绑定撤销回 A”；初次读取 A 对照 PASS，文档绑定已为 A 后刷新仍读 B，结果 FAIL。没有执行真实 Undo，也没有加载宿主文档。
- 影响：当前会话使用 B，保存重开后却按文档记录读取 A；标注和漏标检查可能针对错误字典。本项不同于 R-008 的文件读取失败缓存。
- 关闭标准：同一文档撤销／重做绑定、外部修改产品绑定、另存为及文档切换后，面板、实际读取路径与文档记录一致；选择编号后上下文变化须阻止沿用旧选择。真实 PPT/Visio Undo 另验。

### R-025：大括号非共面参数被二维实体静默压平

- 位置：`cad-plugin/Shared/Commands/PatBraceGeometry.cs:110–123、292–310`；`PatBraceEntity.cs:26–31、40–71`。
- 根因：定义与点生成接受 Top.Z、Bottom.Z 不同；实体只写二维 Point2d，再把整个 Polyline.Elevation 设为 Top.Z。Xrecord 的 Bottom.Z 却保留原值。
- 本轮 L1：生产几何和 ApplyGeometry/SetGeometry 原码＋二维 Polyline 桩。Top.Z=2、Bottom.Z=2 对照 PASS；Bottom.Z=5 时函数接受参数，实际末点 Z=2、记录末点 Z=5，结果 FAIL。
- 影响：三维对象捕捉产生不同高程时，实际图形、记录参数和后续编辑／验证不一致；本轮未验证 AutoCAD 的实际拾取坐标转换。
- 关闭标准：按二维产品边界明确拒绝非共面输入，或在写入前一致规范化定义与图形，不静默制造两套坐标；保留恒定非零高程，验证创建、编辑、保存重开及无部分实体残留。

### R-026：FAIL 日志绕过最近错误统计

- 位置：`cad-plugin/Shared/Diagnostics/PatDiagnostics.cs:78–92`；`Shared/Cad/PatNativeEditTracker.cs:118`；`Shared/Diagnostics/PatDoctorCommand.cs:55`。
- 根因：RawLog 分类识别 error/failed/fatal/exception，不识别单独的 FAIL；原生编辑同步 catch 正是记录 `Native annotation tracker FAIL: ...`。异常说明本身不含另外几个词时不会进入错误缓冲。
- 本轮 L1：生产 OnRawLog 对 `Native annotation tracker FAIL: injected` 的错误数为 0，结果 FAIL；FAILED 错误对照记为 1，正常 PATDOCTOR START 对照保持 0，均 PASS。
- 影响：完整日志中已有同步失败，PATDOCTOR 的 recent errors 却可能显示 0；这不证明失败已经恢复。
- 关闭标准：统一显式失败的记录入口／分类，保留自检正常汇总的排除规则；覆盖原生同步失败和普通失败，避免把编号、路径中的偶然英文子串当成业务异常。

### R-027：Visio 配对没有逐个消耗标注部件

- 位置：`office-com-addin/src/PatentOffice.Visio/VisioAnnotations.cs:150–175`；面板 `PaletteForm.cs:403–415` 显示损坏标注数量。
- 条件：复制后部件沿用同一个 AnnotationId，一组有效引线／标签之外留下一个孤立引线或标签。
- 根因：同一 ID 中只要能找到任何有效配对，就设置 foundValidPair；每个标签可被多个引线重复借用。只剩“有效部件但数量不平衡”时不会增加 MalformedCount。
- 本轮 L1：生产扫描源码＋项目已有 FakeVisio 类型。1 引线／1 标签、单独孤线及 2 引线／2 标签对照 PASS；2 引线／1 标签和 1 引线／2 标签都返回损坏数 0，预期识别孤立部件，结果 FAIL。
- 影响：面板可能在仍有残缺产品标注时显示全部通过。真实 Visio 复制／删除后的身份保留情况仍待验。
- 关闭标准：配对时核对／消耗双方部件，或更新复制身份策略；保留完整复制与合法同号多处标注，孤立部件必须报告。不得把“同号多处合法”变成共享同一标签即可隐藏断件。

### R-028：大括号 Xrecord 组码超出官方支持契约

- 当前审定：先核验（G13）。不直接修改历史记录格式；如需迁移须在同包先兼容 V1 读取，核验通过的单宿主也不外推旧组。
- 类型：SDK 契约技术债／可能的兼容性 Bug，未作实际宿主失败结论。
- 位置：`cad-plugin/Shared/Commands/PatBraceEntity.cs:100–109、154–161` 使用 1000、1040、1070 存储及读取 Xrecord；模拟 TypedValue 没有校验这些取值。
- 官方依据：Autodesk 将 Xrecord 的支持组码规定为常规对象组码 1–369（排除特定保留码），区别于 xdata 组码；当前写法超出该范围。参见 [Autodesk XRECORD DXF 契约](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-24668FAF-AE03-41AE-AFA4-276C3692827F.htm)及 [ObjectARX Xrecord 说明](https://help.autodesk.com/cloudhelp/2022/ENU/OARX-DevGuide/files/GUID-0B9BDDE0-CDB7-4235-A2B7-8BD1FA82503F.htm)。
- 风险：源码可编译、模拟读写相同整数并不能证明真实宿主接受、保存及重开后保留这些码。既有宿主 PASS 仅按其实际包与断言范围保留，不据文档冲突推翻其已发生的观测，也不外推旧版本兼容性。
- 关闭标准：用候选包在隔离 DWG 中创建、读回、保存、重开并再次编辑，记录实际组码与错误；若需改成标准码，读取器同时兼容既有图纸格式。不得仅改写入端破坏历史参数。旧四组仍只走 GitHub 外部宿主验收。

### R-029：Office 卸载失败的“恢复”可能只恢复残缺目录

- 2026-11-02 第三轮：G26的组件60/60、两套完整隔离安装矩阵独立通过，原注册未写/路径已变/恢复失败及日志状态组合未见新反例。不再补修；不将隔离安装推成真实Office启动验收。下文为历史。
- 2026-11-02 第二轮：G22的“完整旧目录未动”已通过，**注册未触及＋旧目录已移动＋恢复Move失败**仍错误保留启用注册。两套各旧目录已移走/新目录已部署两格，L1共4FAIL；实际目标文件已缺失，日志仍称“保持停用”。既有完整隔离安装矩阵独立通过，不能覆盖新组合。转G26，区分文件路径变化与注册是否写过，不退回无条件清早期注册。下文为历史。
- 2026-11-02 复核：G18 的文件完整后恢复注册项顺序保留；**两套升级脚本把“旧目录尚未移动”误认为“原来没有安装”**。暂存复制／校验或移动旧目录失败时，catch 先清注册项，再因完整旧目录仍存在而跳过恢复。两个内存注册表边界的原码 catch 回放 FAIL；实际注册表未动。转 G22，只补升级早期状态，不重开已通过的卸载顺序场景。下文为历史。
- 2026-11-01 复核：完整恢复副本已实现；**文件恢复失败前就恢复注册项的问题仍在，转 G18 补修**。两套 catch 回放均失败；升级残片分支仅静态确认。不得将本轮桩回放称为真实注册表／卸载实测。
- 位置：`office-com-addin/uninstall-office-addin.ps1` 的删除备份／catch 恢复分支；`uninstall-visio-addin.ps1` 对应分支；`OfficeAddin.Common.psm1:239–246`、`VisioAddin.Common.psm1:215–218`。
- 根因：卸载先重命名目录，再递归删除；Remove-Item 在报错前可能已经删掉部分文件。catch 恢复注册表、把剩余目录改回原名，不能还原已经删掉的 DLL／所有权清单。
- 本轮 L1：在本轮临时目录预置三个合成产品文件，使用实际 `Remove-VisioSiblingDirectory`，分别锁住三个文件。锁住产品 DLL 后删除报错，但只剩该 DLL；锁住 Newtonsoft.Json.dll 后只剩两个文件，均 FAIL；锁住清单时三个文件仍在，对照 PASS。未操作真实安装目录或任何注册表项。
- 影响：完整卸载流程的 catch 可能恢复指向残缺目录的注册项，需要重新安装才能恢复。完整脚本未执行，该后果由其恢复链路推导，不冒充已做安装器端到端故障注入。
- 关闭标准：失败后能够恢复完整文件和注册项，或明确保留可恢复的卸载状态而不宣称回滚成功；用每个文件被锁及目录清理失败矩阵核对全部哈希。升级回滚中“新目录半删除，因目录仍存在而不恢复旧目录”的同类分支也须覆盖。沿用现有隔离目录、所有权清单和路径边界，不扩建通用事务服务。

### R-030：FactRunner 的零用例结果会变成绿灯

- 位置：`tools/FactRunner/Program.cs` 的测试枚举及末尾返回；`tools/run-fact-tests.ps1` 仅转发进程退出码。
- 根因：没有任何符合条件的 Fact 时，passed=failed=skipped=0；只判断 failures.Count，最后返回 0，没有“必须执行业务断言”的门禁。
- 本轮反例：为负对照构建不含测试的 net8.0 DLL，用当前原始 FactRunner 执行，输出 `SUMMARY: passed=0 failed=0 skipped=0`，退出码 0，结果 FAIL。该程序集为本轮临时合成文件，没有修改测试工具。
- 影响：测试选择错误／空测试程序集可能被脚本汇总为成功。当前有效程序集的实际执行结果仍保留，不因为这个负对照否认其断言。
- 关闭标准：零测试、全跳过及不支持的测试形态必须明确 SKIP/BLOCKED 或非零；至少一个正常同步 Fact 的正对照仍正确执行。当前工具仅支持它实际实现的同步 Fact 子集，不把它描述为完整 xUnit 运行器。
- 探针说明：第一次用当前 PowerShell 的 Add-Type 生成的负对照依赖 .NET 10，不能由 net8.0 运行器加载，属于探针前置条件错误；改为真正 net8.0 构建后才取得上述零用例反例，前一次没有计为产品失败。

### R-031：PPT 旋转线条的编号锚点是否正确（待核验）

- 当前审定：先核验（G11）。原构造对象不能证明真实 PPT 的 Rotation／Flip／边框组合；先记录真实属性与端点。若方向已由其他属性表达，应撤回缺陷推断，不为模型反例硬套旋转公式。
- 位置：`office-com-addin/src/PatentOffice.PowerPoint/PowerPointAnnotations.cs:186–198`；调用方 `MarkSelectedLine` 按计算出的 startX/startY 放置编号。
- 待验证的根因推断：端点只由 Left/Top/Width/Height 和 HorizontalFlip/VerticalFlip 推算，既不读取 Rotation，也不限制输入旋转角。Microsoft 明确 Rotation 表示绕 z 轴的旋转角度，见 [Shape.Rotation 文档](https://learn.microsoft.com/en-us/office/vba/api/powerpoint.shape.rotation)；但该定义不足以证明构造线对象的其他属性与实际宿主完全相同。
- 本轮 L1：生产 GetLineEndpoints 原码＋内存直线。水平线边框 `(10,20,100,0)` 围绕中心旋转 180°后，起点应从 `(10,20)` 到 `(110,20)`，边框不变；实际仍返回 `(10,20)`，结果 FAIL。0°普通线及水平翻转对照 PASS。
- 影响推断：对已旋转的原生线标注时，编号可能放到错误的一端。探针验证几何计算，不是 PowerPoint COM 属性读回；真实 Rotation／Flip／边框组合仍需宿主验收，不将构造对象当成实机证据。
- 关闭标准：核对真实宿主的形状坐标语义并正确应用旋转与翻转，覆盖 0/90/180/270°、斜线和水平／垂直翻转，验证编号锚点与箭头目标端的关系；普通未旋转线不退化。

### R-032：相同正则文本在 VBA／C# 中含义不同

- 位置：`vba/DictModel.bas:186–200` 的段落结束表达式；对照 `cad-plugin/Shared/IO/MarkingTextParser.cs:87、154–166`。
- 根因：VBA 使用 VBScript.RegExp，却复制了 .NET 的 `\Z` 文本结束写法；实际 VBScript 引擎把本例中的它匹配到字面大写 Z，C# 则匹配文末。仅比较模式字符串相同，不能证明两个引擎语义相同。
- 本轮证据：从生产 VBA 文件提取原始模式，直接交给本机 VBScript.RegExp，再按生产消费者的边界选择规则截取；生产 C# `ExtractMarkingSection` 作对照。输入 `附图标记说明：1底座，2Z形支架，3外壳。` 时，VBA 路线只保留 `1底座，2Z`，C# 保留完整段落，结果 FAIL；无 Z 和小写 z 的对照 PASS。
- 影响：有效名称“Z形支架”可能导致当前及后续条目丢失。没有在 Word 执行完整 BuildModel／保存事件，不将正则机制证据升级为 Word L2 或部署包结论。
- 关闭标准：使用两种引擎实际等价的文末／空段落边界；补齐大写 Z、大小写名称、CR/LF/CRLF、句号及空行先后关系，并审计共享模式的字符类语义。真实 VBA 与 C# 对 number/name/顺序一致，不能只让两个模式文本一致。

### R-033：每行一条的裸列表会跨越换行取编号

- 位置：`cad-plugin/Shared/IO/MarkingTextParser.cs:68–70、236–246`；`vba/Patterns.bas:81` 的对应模式。
- 根因：名称与编号之间、行首／行尾使用 `\s*`，包含换行；启用 Multiline 只改变锚点语义，不能约束中间空白停在本行。
- 第 5 轮 L1：生产 C# `ExtractAll("普通段落\n123\n")` 返回编号 123，按“每行一条、同一行名称＋编号”的现有说明应不匹配，结果 FAIL；`支架 123`、`底座 1\n支架 2` 两个对照 PASS。
- 第 6 轮补证：C# 的 CR、CRLF 变体仍 FAIL；从 VBA 提取生产模式交给实际 VBScript.RegExp，按生产规则统一换行后，LF、CRLF 两个跨行样本均误匹配，正常两行列表对照 PASS。只是补强相同根因，不新增编号；真实 Word 用户路径未执行。
- 影响：在标记段落或兼容全文扫描中，独立段落和下一行序号／页码可能被拼成不存在的标注。该问题不同于 R-016 的完整编号被截断，也不同于 R-018 的输出重排。
- 关闭标准：明确按行的空白边界，保留空格、Tab、正常多行列表和 Word 表格预处理后的输入；普通文字与下一段数字不拼接。若产品决定支持跨行名称／编号，应先明确触发格式与排除页码／段落号的规则，再更新契约。

### R-034：当前 JSON 损坏时，合法备份也无法从面板恢复

- 2026-11-02 第三轮：G25独立矩阵100/100与旧A3通过，重复失败后的实际写入事实、视图刷新、PATCHECK保持和不串图纸已通过；不再分派本项。刷新使用的共用ReadSnapshot存在新交错反例，统一归R-004/G28修，不重复登记为R-034。下文为历史。
- 2026-11-02 第二轮：G20四格入口与动作均通过，原P1阻断已解决；**残留为部分写入后的面板状态问题，当前优先级P2**。基线失败或备份删除失败时实际JSON和缓存已恢复成201，选择稍后后面板仍301；再次点击失败再稍后同样出现，自动刷新两次也不纠正。转G25，以实际已写状态刷新且保留失败信息，不误报裁决成功。下文为历史。
- 2026-11-02 复核：原“坏当前＋好备份”在本轮生产组件矩阵 PASS；**反向状态“好当前＋坏备份”被面板的 `!selection.BackupParsed` 全局 return 拦住，连采用 Word 版也无法选择**。同一快照的底层 `KeepSelectedVersion` PASS。转 G20，将可解析性限制放到对应动作，稍后仍保留文件，不自动裁决。下文是 11 月 1 日故障历史，末尾“未修复”指当时快照。
- 位置：`cad-plugin/Shared/Palette/DictPaletteControl.cs:639–675、685–691`；共享 `DictConflict.cs:86–117` 的双身份校验；五版 DictLoader 的缓存保留／显式重载路径。
- 场景：已经加载合法 A，并有合法 Word 备份；当前 JSON 被编辑成坏文本。轮询保留 A 的缓存；此时点击裁决采用缓存 A 的身份，和坏文件的实际字节不符，确认恢复被 CONTENT_CHANGED 拒绝。点击重载清空缓存后，模型为 null，裁决按钮被禁用，仍无法选择有效备份。
- 根因：普通写回需要成功解析模型的限制被带到了恢复入口。恢复动作应绑定用户所见当前文件的实际字节与所选备份，而不是要求当前文件本来就能成功解析。
- 本轮 L1：生产 2007 加载器／共享严格解析器、ReadSelected 与 UpdateConflictState 原码；合法当前文件对照 PASS，坏文件热缓存与重载两例 FAIL；用坏文件真实字节身份进行底层选择校验的对照 PASS。备份可解析、文件未被改写。其他四版同型路径静态核对，未做真实 CAD 点击。
- 修复／关闭：归 G16。允许为明确的“恢复合法备份”建立独立恢复快照，仍在确认时按原文档、路径、当前／备份身份和产品锁校验；“采用当前版本”及普通编辑不可接受坏模型。覆盖热／冷缓存、重载、坏 UTF-8、坏备份、操作后文件变化和写回后的辅助步骤失败。
- 证据：`docs/test-evidence/repair-review-20261101/recovery-probe.cs`、`recovery-probe.txt`，哈希及限制见本轮机器 findings。当前未修复。

### R-035：VSTest 包装器可把历史 TRX 当成本次通过

- 2026-11-02 第三轮：G27归属/留存矩阵15/15，含默认/显式的成功及失败结果；真实VSTest34/34、48/48已保留TRX。本项不再补修，不修系统ACL或另造运行器。下文为历史。
- 2026-11-02 第二轮：G23结果归属修复通过，实际VSTest34/34、48/48证据已保存。**省略ResultsDirectory时，新TRX与DLL同在staging，被finally删除；工具返回的证据路径立即失效**。完整生产包装器的默认/显式目录对照FAIL/PASS，转G27。属于同一工具的证据生命周期缺口，不否定实际测试通过，也不另增编号。下文为历史。
- 位置：`tools/run-office-tests-vstest.ps1:39–54`；固定 `results.trx` 后递归取最新文件。
- 场景：显式 `ResultsDirectory` 留有旧的通过 TRX；本次 VSTest 因零发现等原因退出 0，却不生成新结果。包装器没有约束结果来源，采纳旧计数并报 `PASS|VSTEST|...`。
- 本轮 L1：完整生产包装脚本，外部 dotnet 边界注入“退出 0，不产新 TRX”。无旧文件对照正确拒绝，新文件对照正确通过；旧文件场景 FAIL。日志及旧／新 TRX 均在本轮 evidence 的 `vstest-fixture-*`，未据此否定前轮真实 48/48、34/34 结果。
- 去重：原 R-030 修复 FactRunner 的零断言退出值；本项是新包装器使用历史文件，修复文件和门禁独立，归 G23。
- 关闭标准：本次唯一 TRX 的确切路径必须存在，随后才校验非零及完整执行计数；旧根文件、旧子目录、两工程共用结果目录及本次零输出均不得复用历史绿灯。继续使用真实 VSTest，不另造测试运行器。

## 第 1 轮实际检查与证据

| 检查 | 证据层 | 结果 | 限制 |
|---|---|---|---|
| 文字末端／避让算法 12 个构造场景 | L1 | 4 PASS、8 FAIL | 原码算法＋几何桩，文字框为构造值 |
| 旧版生产加载器 6 个 JSON 样本 | L1 | 1 PASS、5 FAIL | 2007 原码；2010 解析器字节相同，未另跑宿主 |
| 同时间戳内容变化 | L1 | FAIL | 2007 原码＋本轮临时文件，未测网络盘 |
| 所有权清单目标已存在时 MoveFile 恢复 | API 局部检查 | FAIL | 未运行完整安装器，没有操作真实 Startup |
| 其他静态发现的宿主／故障注入回归 | 未执行 | SKIP/BLOCKED | 本轮为 review 与记录；后续按各条关闭标准补验 |

探针运行目录：`C:\Users\wjm\AppData\Local\Temp\PatentMarker-review-20261031-6df49539c8ff44b2a189e6277644f91d`。目录仅含本轮创建的脱敏样本和输出，不是今后构建或测试的依赖。为避免依赖临时目录，下方保留关键结果。

```text
FAIL|routing|height=3.5|angle=75|distance=100|引线拐点过于靠近文字，请调整拾取点。
FAIL|routing|height=3.5|angle=75|distance=10000|引线拐点过于靠近文字，请调整拾取点。
PASS|valid_empty|expected=valid|actual=model entries=0
FAIL|invalid_syntax|expected=reject|actual=model entries=0
FAIL|empty_input|expected=reject|actual=model entries=0
FAIL|wrong_root|expected=reject|actual=model entries=0
FAIL|truncated|expected=reject|actual=model entries=1
FAIL|trailing_garbage|expected=reject|actual=model entries=0
FAIL|same_timestamp|IsFileChanged=False|cached=301|disk=602
FAIL|manifest_partial_rollback|old_backup_exists=True|active_content=partially-written-new-manifest|move_error=0x800A003A (CTL_E_FILEALREADYEXISTS)
```

执行方式：PowerShell `Add-Type` 内存编译生产路由／缩进原码，以及生产 2007 `SimpleJson`／`DictEntry` 原码；宿主边界替换为最小桩。FSO 检查仅调用 `Scripting.FileSystemObject.MoveFile`。路由与 FSO 探针因反例返回退出码 1；加载器探针执行完毕退出码 0，但各业务断言如上为 FAIL，不能计为测试通过。

已保存输出 SHA-256：

| 输出 | SHA-256 |
|---|---|
| `loader-probe.txt` | `56b9c414ff9afbc8f1901e02dc25b1de3d33104314d0cdedb88319226aef4065` |
| `manifest-api-probe.txt` | `19a8962d9647fcccf6f68c3c7df8df0f751121e474a321d6b72d47ccd3cd3519` |

## 宿主边界与下一轮验收

- 第 1 轮没有运行全量单测或构建插件；第 4 轮补跑的现有测试及环境限制见该轮记录。连续 review 均未安装候选包或启动 Word/CAD/PPT/Visio；反例 FAIL 是定位依据，不是修复后的验收结果。
- **GitHub 外部宿主验收**：2007/2010/2013/2015 四组只通过 GitHub 候选包交给目标环境。R-001、R-002、R-004、R-011 等涉及旧宿主的结论须单列外部回传，不能成为本机安装／测试待办，也不能以编译或模拟通过代替实际旧宿主通过。
- Word 安装／保存相关修复按项目既定 L2、隔离安装、正常新进程黑盒与真实 Startup 放行流程执行；PPT/Visio 需对应宿主确认。保留未执行项，不外推到其他年份或 Office 位数。
- `save_as_immediate_export=false`、CAD 不自动回写 Word、合法同号多处标注、删除字典项不删除实体等现有明确约定未当作新 Bug。纯旧版宿主证据缺口也未伪装成新代码缺陷。
- 建议处理次序：先 R-001；随后 R-002/R-003/R-005 的数据保护，再处理 R-004/R-009；其余按实际使用范围安排，不为本清单启动全面重构。

## 关键源码快照 SHA-256

以下哈希用于定位本轮审阅的实际文件；不是部署包或已加载程序集的哈希。

| 文件 | SHA-256 |
|---|---|
| `cad-plugin/Shared/Commands/PatMarkCommand.cs` | `fbd1bdffa6d64fc88e6e9a7a5aed0b172416d557a3de68ca5312a1ca3c1302a6` |
| `cad-plugin/Shared/Commands/PatLeaderTextAttachment.cs` | `de18d5e78f7e7aa02172595f0aab7a558a26b2163d4bfaf8e8a14e72fd6b7d60` |
| `cad-plugin/Shared/Commands/PatTextPathRouting.cs` | `3683e82c09f050f9c1092f3d5c5c80ea4d40b50fe34bc80151db65a010d12acc` |
| `cad-plugin/2007/PatentMarker/IO/SimpleJson.cs` | `2db514843e617f6bd3afc6a57ce8874b576c7fabb83c6a84f1c85ca1f216c6e0` |
| `cad-plugin/2007/PatentMarker/IO/DictEntry.cs` | `1f29a68d71d22637ce03cdf960176c5ddd85930bcfb36de20846958ff7859020` |
| `vba/AutoExport.bas` | `8033a30dc71bd4198a918dc4ac5a9222d30e81fce9afd7af0355928eb85d3543` |
| `word-addin/install-vba.vbs` | `78cc1b5d6b7bf01c00a09ac77b2b22d413c404670f78b69224523266f66cc9a8` |
| `cad-plugin/Shared/Cad/PatEntityHelper.cs` | `e9e561084547307fd8760389c74a2a1099bbfa6f0da25c2f8bf66a016961c497` |
| `cad-plugin/Shared/Palette/DictPaletteControl.cs` | `74789f46434db8f4dcaad34485e9273753de519b6e434bc4e9b6cc949b18fab7` |
| `cad-plugin/Shared/Palette/EditEntryDialog.cs` | `153a9199399a82e88d19dddf5885139f17d5854debb5e5e0cdeee02f612804e3` |
| `cad-plugin/Shared/IO/PatSettings.cs` | `c5f64a76dc48c8bb3e8420b70df8c438a587a72bff3ebc7f826279ddd784a666` |
| `office-com-addin/src/PatentOffice.PowerPoint/PaletteForm.cs` | `97b4238d8f0fab93da66deec289519101b0888f548a850d399c8470378aab682` |
| `office-com-addin/src/PatentOffice.Visio/PaletteForm.cs` | `509003b4f7e90ebfa3da5ea0d87fa7ed16f5a43d1ca329432a15fa42daae9e3b` |
| `office-com-addin/src/PatentOffice.PowerPoint/PowerPointAnnotations.cs` | `25b4c9012cb740abe1ebd2e75bc157c03ac2b575c2b6074a8511f0696b82c1d7` |
| `PatentMarker-2007-deploy/install-2007.vbs` | `0162006ae2c0161a9fdbe7a0b2ca2c978ec83a1d429b650a0d0222ef1e362ac8` |
| `PatentMarker-2007-deploy/uninstall-2007.vbs` | `f30467f7c8b7d0580ef5adfbffec7b48edfef072f4db93d378f82e2b9dddf568` |

## 第 2 轮实际检查与证据

本轮新增 9 项，累计 21 项；R-012 只补证据，不重复计数。没有修复或关闭任何条目。复核原生编辑和安装分支时，没有把未接入用户路径的辅助代码或已明确排除的原生命令另列为用户 Bug。

| 检查 | 证据层 | 结果 | 限制 |
|---|---|---|---|
| 冲突继续编辑、编号识别、顺序、频率前置、字符串读写共 10 个探针 | L1／消费者逻辑回放 | 1 PASS、9 FAIL | Word 频率消费者为回放＋静态分析，不是 VBA 宿主运行；文件写入边界未调用 |
| PPT 顶层／嵌套组合、文字被改／清空共 4 个探针 | L1 | 1 PASS、3 FAIL | 使用生产扫描源码和内存形状，不是 PPT COM 实机 |
| 旧 Leader 同象限／跨象限移动共 2 个探针 | L1 | 1 PASS、1 FAIL | 生产函数原码＋实体／事务桩，不涵盖 AutoCAD 重生成 |
| Word 字典键与生产编号比较器的身份差异 | COM 机制＋L1 | FAIL | 运行 Scripting.Dictionary，不启动 Word |
| 旧 Leader 布局尺寸、完整 Word/PPT/CAD 用户路径 | 未执行 | SKIP/BLOCKED | 根据条目安排后续验收；旧 CAD 仅走 GitHub 外部宿主验收 |

共 17 个诊断场景，3 PASS、14 FAIL；这些是反例定位结果，不是修复后的测试成绩。三组探针及编号身份检查退出码均为 1。未运行全量测试、构建、安装或 GUI。

本轮临时目录：`C:\Users\wjm\AppData\Local\Temp\PatentMarker-review2-20261031-fa1252a91f1f4252952e5586663f08fd`。没有新增正式测试工程或依赖，也没有把临时文件变成干净检出的前置条件。

```text
FAIL|pending_after_edit|before=True|edited=True|after=False|backup_exists=True
FAIL|prefix_number_first|expected=S1|actual=1
FAIL|hyphen_number_first|expected=10-1|actual=1
FAIL|prefix_parentheses|expected=S1|actual=1
PASS|prefix_name_first_control|expected=S1|actual=S1
FAIL|mixed_format_order|expected=2,1|actual=1,2
FAIL|frequency_consumer_replay|expected_occurrences=3|actual=2|expected_name=底座|actual=支架
FAIL|valid_escape_read|expected_U0008_U000C=true|actual=AbBfC
FAIL|serialize_control_char|strict_json=false|raw_U000B_present=True
FAIL|crlf_roundtrip|LF_count=2|expected_normalized_LF_count=1
PASS|ppt_scan|visible=301|nested=False|expected_marked_301=True|actual=True
FAIL|ppt_scan|visible=999|nested=False|expected_marked_301=False|actual=True
FAIL|ppt_scan|visible=<empty>|nested=False|expected_marked_301=False|actual=True
FAIL|ppt_scan|visible=301|nested=True|expected_marked_301=True|actual=False
PASS|legacy_align|target=(20,20)|moved=True|expected_attachment=BottomLeft|actual=BottomLeft
FAIL|legacy_align|target=(-10,-10)|moved=True|expected_attachment=TopRight|actual=BottomLeft
FAIL|cross_language_number_identity|VBA_dictionary_compare_mode=0|VBA_entries=2|CAD_numbers_equal=True
```

输出 SHA-256：

| 输出 | SHA-256 |
|---|---|
| `core-probes.txt` | `b725f242ad32b1768354c41485c0d1f8486ea445d02feed821328d3fa5f71895` |
| `ppt-probes.txt` | `46cf4bd28f74d83839fa674205195af03b9ba6028a73969fa1b7a58576abdb24` |
| `align-probes.txt` | `c042068a93476f5803d3b6f888dfb8f0db4e6c3aa8f67a435cbf0b2fcef8ca1e` |
| `identity-probe.txt` | `75cf2c55586caeb04b0b7d0b5103292fbeb80d518236e08b3e53e393c518635d` |

第 2 轮补充源码快照（上一轮已有哈希且未变化的文件不重复）：

| 文件 | SHA-256 |
|---|---|
| `cad-plugin/Shared/IO/DictConflict.cs` | `8e6c58f1a4a2b9e0e1fc3484952e2a31abfe1e009db34aa61f86ec35f6ba8a04` |
| `cad-plugin/Shared/IO/MarkingTextParser.cs` | `a88f75539fbcd207989bb62cd73e3aaab0152396d10098c754ec4be828368562` |
| `cad-plugin/Shared/IO/NumberIdentity.cs` | `ebb70ae6653c071bf944a855680be80a1c118c8329378fcb034c1c3e6bf1bc14` |
| `cad-plugin/Shared/Commands/PatAlignCommand.cs` | `7aeca1ec88904cff449d36261c3aa4aa73fcb39c30b08945f8aad5b2437f615c` |
| `cad-plugin/2025/PatentMarker/Commands/PatAlignCommand.cs` | `132b093f729e847aa14ebe99ed42081067b158730a81ebb29bbde6e0fbc8493d` |
| `cad-plugin/2007/PatentMarker/IO/DictWriter.cs` | `aeceed67952b7b5af9aac60719827456517096b9d1183acdbc615210dbb0863c` |
| `cad-plugin/2010/PatentMarker/IO/DictWriter.cs` | `aeceed67952b7b5af9aac60719827456517096b9d1183acdbc615210dbb0863c` |
| `vba/Patterns.bas` | `5b70419063334b3e85a6a1321e9c8eb0f0fab21d1de795e24b510655b33f6b1a` |
| `vba/DictModel.bas` | `dac15b2162394aea5dfb2e693c567fd943bf9bdb2ffc57e6b613b793214ceed5` |
| `vba/JsonWriter.bas` | `0ed7661b042c5ae8638ce8f115c2ba2900daa53f957f36b93e369c1d1f353676` |

后续优先把 R-013、R-016 加入数据保护／编号回归，再按范围处理其余项目。PPT／VBA／AutoCAD 实机结论仍须按各条证据层补齐，不能由本轮探针外推。

## 第 3 轮实际检查与证据

新增 R-022 至 R-028，共 7 项，累计 28 项；有新增，继续复核。检查了设置释放、面板开关、绑定撤销、诊断分类、Brace 参数／实体和 Visio 配对；原生编辑白名单外的任意第三方变换未当成已承诺功能。

- 核心探针 6 项：3 PASS、3 FAIL；Visio 配对 5 项：3 PASS、2 FAIL；绑定刷新 2 项：1 PASS、1 FAIL。合计 13 项，7 PASS、6 FAIL。生产函数原码以最小边界桩执行，未启动任何宿主。
- R-023 为静态调用链；R-028 为官方 SDK 文档与源码契约核对，两项宿主结果均为 SKIP/BLOCKED，不伪称实测失败。
- 临时探针初次编译缺少包装类命名空间引用，未执行断言；补齐探针引用后才取得上述结果。此编译问题不是产品发现，没有计入 7 项新增。
- 目录：`C:\Users\wjm\AppData\Local\Temp\PatentMarker-review-loop-20261031-4dbd14740110460a9b80e1fa0ba7d27b`。已保存可重编译的 `round3-*-probe.cs` 与输出；各组包含 FAIL，退出码 1。真实文件写入／撤销／UI 与保存重开尚未验收。

```text
FAIL|release_inactive_drawing|before_same=True|after_same=False|expected_height=7.25|actual_height=3.5|actual_arrow=False
FAIL|diagnostic|message=Native annotation tracker FAIL: injected|expected_error=True|count=0
FAIL|brace_elevation|recorded_bottom_z=5|actual_bottom_z=2
FAIL|visio_pair_cardinality|lines=2|labels=1|expected_malformed=1|actual=0|marked=1
FAIL|visio_pair_cardinality|lines=1|labels=2|expected_malformed=1|actual=0|marked=1
FAIL|binding_undo_same_document|document_binding=A|palette_read=B
```

## 第 4 轮实际检查与证据

新增 R-029 至 R-031，共 3 项，累计 31 项；有新增，继续复核。另复核了配置、选中／漏标／面板会话、Office 所有权检查和注册项备份恢复，没有把已覆盖根因的新组合重复建项。

- 实际文件删除故障 3 项：1 PASS、2 FAIL；PPT 端点 3 项：2 PASS、1 FAIL；空 Fact 程序集负对照：FAIL。7 项反例／对照中共 3 PASS、4 FAIL。
- 2025 便携测试与 2007 模拟契约均已编译成功。VSTest 在业务测试运行前因 Win32Exception(5) 访问父进程句柄被环境拒绝而中止，两次均记 SKIP/BLOCKED，退出码 1；不是账号／仓库权限不足，也不是业务用例断言失败。
- 采用项目现有 FactRunner，并按原脚本把运行器文件放到相应测试输出目录：2025 实际执行 139 PASS、0 FAIL、0 SKIP；2007 模拟契约实际执行 60 PASS、0 FAIL、0 SKIP，均退出码 0。它们不是 AutoCAD 真实宿主，不覆盖本轮新反例，也不替代 VSTest 的完整执行语义。
- 没有新增依赖或测试工程到仓库。测试只产生被忽略的 bin/obj 以及本轮临时证据；生产代码未改。

关键输出：

```text
FAIL|uninstall_failure_restoration|locked=PatentOffice.Visio.dll|delete_failed=True|expected_remaining=3|actual_remaining=1|files=PatentOffice.Visio.dll
FAIL|uninstall_failure_restoration|locked=Newtonsoft.Json.dll|delete_failed=True|expected_remaining=3|actual_remaining=2|files=Newtonsoft.Json.dll,PatentOffice.Visio.dll
SUMMARY: passed=0 failed=0 skipped=0
FAIL|zero_fact_gate|expected_nonzero_exit=true|actual_exit=0
FAIL|ppt_rotated_line|rotation=180|flipH=False|expected_start=(110,20)|actual_start=(10,20)
SUMMARY: passed=139 failed=0 skipped=0
SUMMARY: passed=60 failed=0 skipped=0
```

证据目录沿用第 3 轮临时目录：`round4-uninstall.txt`、`round4-endpoint.txt`、`round4-empty-facts-net8.txt`、`round4-facts-portable.txt`、`round4-facts-leader.txt`；VSTest 中止日志和 TRX 亦保留。卸载探针只删除本轮创建且已校验父路径的合成目录，没有触碰真实产品文件或注册表。

## 第 5 轮实际检查与证据

新增 R-032、R-033，累计 33 项；有新增，继续复核。

- 正则／解析探针 6 项：4 PASS、2 FAIL。使用生产 C#、实际 VBScript.RegExp 和从 VBA 提取的原始段落表达式；没有运行 Word。
- `check-version-sync.ps1`、`sync-mleader-group.ps1 -Check`、`vba-sync.ps1 -Check`、`sync-word-addin.ps1 -Check` 均 PASS，工具退出码 0；后两项明确输出 no writes。同步通过只说明对应源码／资产一致，不说明逻辑正确或 dotm 已在宿主中执行。
- 首次提取 VBA 模式的临时 PowerShell 探针把单个字符串按字符取首项，未运行业务断言；修正为数组后才获得本轮结果。没有将探针错误作为产品发现或 PASS。
- 证据：同一临时目录的 `round5-regex.txt` 和四个 `round5-*-sync.txt`；同步脚本使用 Write-Host 的部分输出仅见原工具执行记录，文件不是完整控制台转录。

```text
FAIL|vba_section_end|body=1底座，2Z形支架，3外壳。|VBA_slice=1底座，2Z|CS_slice=1底座，2Z形支架，3外壳。
FAIL|bare_line_scope|case=separate_lines|expected=|actual=123
VBA sync check passed (no writes).
Word add-in sync check passed (no writes).
```

## 第 6 轮实际检查与证据（零新增）

本轮新增 **0 项**，累计 **33 项**。按用户“某一次复核没有新增待修复项即停止”的要求结束连续 review。没有修复或关闭任何项，也不将零新增解释为不存在其他缺陷。

| 检查范围 | 结果 | 对应结论／限制 |
|---|---|---|
| 14 个 C# 提取场景：空文本、编号／名称在前、两种括号、LF／CR／CRLF 裸列表、同号异名、前缀、子编号和混排 | 9 PASS、5 FAIL | 失败均为 R-016、R-018、R-033 的已知反例／换行变体 |
| 6 个 C# 段落场景：无标题、句号、三种空段落、大写 Z | 6 PASS | C# 行为符合该组断言；不替代 R-032 的 VBA 反例 |
| 3 个表格预处理场景：编号在前、名称在前、名称在前的 S1 | 3 PASS | 真实生产预处理＋解析；不涵盖 Word 表格对象和保存事件 |
| 5 个会话场景：差异类别、Removed 行、已删除行过滤、空白搜索词、清空状态 | 5 PASS | 生产 DictDiff／DictPaletteSession，使用生产模型定义；不涉及 UI |
| 编号比较器与字典键归一化 | PASS | 空白和大小写归一化；不能抵消 Word 的 R-021 |
| 3 个实际 VBScript 裸列表正则场景 | 1 PASS、2 FAIL | 两个 FAIL 补入 R-033；不是完整 VBA 运行 |
| 面板调度结束／取消／关闭清理、PAT 实体识别及全选／检查调用链、Office 读取与绑定恢复链路 | 静态核对 | 未发现清单之外的独立根因；不以静态阅读声称宿主路径 PASS |
| 前两轮 26 个源码 SHA-256 回读 | PASS | 全部一致；当前未提交及未跟踪的用户修改仍保留 |

共 **32 个业务断言：25 PASS、7 FAIL**。7 个 FAIL 分别归入 R-016（2）、R-018（1）、R-033（4）；新增独立问题为 0。命令退出码 1，与已有反例仍失败一致。宿主操作为 SKIP/BLOCKED，没有本机旧 CAD 验收任务。

执行入口：在项目根目录运行 `& '<证据目录>\round6-core-probe.ps1'`。脚本通过 Add-Type 编译 `round6-core-source.cs`（生产解析器、编号比较器、差异与会话原码、2007 模型定义；仅本地化格式字符串使用桩），并调用本机 VBScript.RegExp。输出：

```text
FAIL|parse_prefix_number_first|known=R-016|expected=S1|actual=1
FAIL|parse_mixed_order|known=R-018|expected=2,1|actual=1,2
FAIL|vb_bare_crossline_1|known=R-033|expected_count=0|actual_count=1
SUMMARY|passed=25|known_failed=7|new_failed=0
```

初次临时探针缺少命名空间引用，随后又发现探针对差异输出顺序的预期写错、计数器作用域不正确；校准后独立重新执行才取得上表的 32 项结果。初次输出保留为 `round6-harness-initial.txt`，其中的 NEW 标记与零计数属于探针错误，不是产品发现或有效测试结果。

结束核对时间：2026-10-31 21:27:55 +08:00；身份与提交仍为开头基线。项目源码、安装器、部署包、用户宿主文件均未因本轮 review 修改；仓库内交付物仍仅为本清单。后续修复仍须按各条关闭标准补足红绿证据及对应宿主层级，不从本轮 PASS 外推。

## 第 3—6 轮补充源码与证据快照

下列为审阅文件的 SHA-256，结合前两轮源码表定位当前工作树；不代表已安装 DLL／模板或发布包哈希。

| 文件 | SHA-256 |
|---|---|
| `cad-plugin/Shared/Diagnostics/PatDiagnostics.cs` | `3ef59da97a25bf4816565c272a88259721795dfba8305dd7592be8b538b5e40a` |
| `cad-plugin/Shared/Cad/PatNativeEditTracker.cs` | `a947c6273a308dab501f6c65b1e084cfcc03f98fa9543f4eacd707f5a5b36810` |
| `cad-plugin/Shared/Commands/PatBraceGeometry.cs` | `8ca5e55434dc3c512d920899910841af277de315f0c892fb245ffbe07caa659d` |
| `cad-plugin/Shared/Commands/PatBraceEntity.cs` | `b137f18c13a4e705f4c6268955317763cc0351a05ce98f1019f85118945bc010` |
| `vba/PatentDictPanel.frm` | `7d0d357d2f572cd89735603b3d08746c3be8989474639b585d732e5c0fdcde18` |
| `office-com-addin/src/PatentOffice.Visio/VisioAnnotations.cs` | `13d6a0affaeb4fefdf24f6f8bdda7095a77ce257a9dbec674e044a481f109b14` |
| `office-com-addin/src/PatentOffice.Visio/VisioDocumentBinding.cs` | `e85e829511feeb22a45013c363e49e0fff9e0121c0515de809da0c2355ba5d60` |
| `cad-plugin/Shared/Palette/PatPaletteCommand.cs` | `f5de720275ba1dc3cbda2c20e204df9e4774ca76510d9968b9532a4ccc94e273` |
| `cad-plugin/Shared/Diagnostics/PatDoctorCommand.cs` | `6255caea1ca89d24495a7f8f5d3ea02a2c6d29108c5fa54f2e22bd3e891f3338` |
| `office-com-addin/OfficeAddin.Common.psm1` | `1d48d2bd213a809a2176ab006ec2a365390066e44224805b954f623fd4135ae6` |
| `office-com-addin/VisioAddin.Common.psm1` | `c9f94ccac1c60433d1524f6981ec19c2c0eeb1d864784f830bf97398cbde6c09` |
| `office-com-addin/uninstall-office-addin.ps1` | `e635d2483124fb43bd43324ceede860b5da1e8e109db115d06667c0b81ba56a2` |
| `office-com-addin/uninstall-visio-addin.ps1` | `a2041054fa244d12fc078292960050b96ae4073c93088929fa871f9efe1c8dee` |
| `tools/FactRunner/Program.cs` | `6bf77dbe799999b0ee4199f9867225910ef915984e83bf5c3bd103f4571f654d` |
| `tools/run-fact-tests.ps1` | `386d3ecab7c2b2dfcc82008cddfb0aaa3634a63b419bc42bb28942da8ddcf192` |
| `cad-plugin/Shared/Palette/DictPaletteSession.cs` | `fe9f19bd2b440415974d13242afa1922649f68087aeec750261eecc2dfcc2d85` |
| `cad-plugin/Shared/Palette/DictPaletteWorkflow.cs` | `722667442e4945564cf739a78bf4daa9b1723fcf140533d8adc103d6fd94cc6c` |
| `cad-plugin/Shared/IO/DictDiff.cs` | `605207616a1903d59ad5d54d76ebaa7c65c0839d7ed942387562504861694526` |
| `office-com-addin/src/PatentOffice.Shared/DictionaryReader.cs` | `2272098c600fdfdb7102e02002975795cc4c2b777538a9bcbbae350ebbc015fd` |
| `office-com-addin/src/PatentOffice.PowerPoint/PresentationBinding.cs` | `da048be9d1dafc0f8b4f9de070b0e737e08d47fc16a7541bd7565b1136c5e7eb` |

以下证据均位于 `C:\Users\wjm\AppData\Local\Temp\PatentMarker-review-loop-20261031-4dbd14740110460a9b80e1fa0ba7d27b`。关键结果已在本文件保留；临时探针不成为项目构建依赖。

| 输出／探针 | SHA-256 |
|---|---|
| `round3-core.txt` | `7c190c8928ef9411dd91b70ab498dfc6a4b113c4d4571ee962030d7cbaa4a76b` |
| `round3-visio.txt` | `655462b689069ff4aa374df19ea6b5ed3b9f5cab8c0d2714c285b6651a0977c5` |
| `round3-binding.txt` | `7c572ac25bab1babcc9fcf950dc3ca0ab6de1574806d60d0dbdff4d5d5ad9b63` |
| `round4-uninstall.txt` | `5b347efe42aee2248418701e865e2991087723cd20a6dad3e7decef40d6d279a` |
| `round4-endpoint.txt` | `a4692fb4faf6d35abb1301239ec29e3d8f7bf97484e6ec291acc18c857b8ea17` |
| `round4-empty-facts-net8.txt` | `10e5541b0da12b5f85779d0805b3f066381b45627fb24869c9184c8c39902597` |
| `round4-facts-portable.txt` | `59cd010c5cac160ade562bdb0970ef4dc618a34db7ac898212473a3781a7c4fe` |
| `round4-facts-leader.txt` | `57d9d42d3d18888089cb307072e08f81bec1e8c58ebee336bb50d97802ed8cbf` |
| `round4-portable-tests.txt` | `1c4092b27d7b5ff679b445a01cfd580286cd9711ebe81a1e3eb72fe8286b8968` |
| `round4-leader-tests.txt` | `fecadee67b59f7f89913203df7487a299695934e9819d60491b31068efcc2f67` |
| `round5-regex.txt` | `271243441638e1c1af956c9439962b603927f27a2ee33cf41b7a96e6dd12f7c8` |
| `round5-vba-sync.txt` | `510883ddbcdbf4a40a8c71bfcf72f9ff3e8abcd2749c7e4e1cb58fcf1538b9a6` |
| `round5-word-sync.txt` | `b7f7ddd22d6644abf98c78b57f643a5e0adb71c08bd023621f90cf5bf5c79ce8` |
| `round6-core.txt` | `337aefa7408e4768e20eee6090fb71641b603a34a3f5af480289cdbd68704264` |
| `round6-core-source.cs` | `a644e4f5e3799816efe412b0ba349238c8d45516e6a0008d56c832ad05161d48` |
| `round6-core-probe.ps1` | `cd8afb5a75c564c0244774c64cf4152f0dc694936c1f678bed044a77a7f85a90` |


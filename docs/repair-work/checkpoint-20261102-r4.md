# 修复任务接续记录（第四轮，2026-11-02）

基线 HEAD：`b945b1a68fec26ada268b5a4ee3ac066386e96ab`，叠加前三轮已集成内容与用户原有未提交改动。本轮未创建任何提交，未推送。

## 本轮范围

按 `docs/repair-plan.md` 第 12 节，处理复核报告列出的 **5 项**：R-016 补修（G19）、R-034 补修（G20）、R-004 补修（G21）、R-029 补修（G22）、新增 R-035（G23）。未重新分派 G01–G18，未因文档旧状态再动 R-012／R-028。

| 包 | 条目 | 结果 |
|---|---|---|
| G19 | R-016 | VBA 编号起点改为「名称捕获组之后」，修掉上一轮 G15 引入的误杀 |
| G20 | R-034 | 裁决对话框按动作判断可用性，坏备份不再挡住「采用 Word 版」 |
| G21 | R-004 | 存在欠账时拦截该图纸所有改号；漂移也登记欠账；补「显式放弃」入口 |
| G22 | R-029 | 升级早期失败保留完整未移动的旧安装与原注册项 |
| G23 | R-035 | VSTest 包装器只接受本次运行自己的 TRX |

## 最终门禁（全部在本轮实际执行）

- 2025 全套 **274 PASS / 0 FAIL**（基线 252 → +11 G20 → +11 G21）
- 四组模拟 83 / 96 / 96 / 96 PASS，0 FAIL
- Office 经 VSTest：Visio **48/48**、PPT **34/34**
- 五版编译通过（含 .NET 2.0）；`build.ps1 -Static` 退出 0
- 四个同步检查通过；五套 release manifest 匹配
- VBA L2：`verify-vba-export`、`test-vba-panel`、`test-g05-word`（FAILURES=0）、`test-g02-l2`（17/17）
- 隔离安装矩阵：G08 退出 0；PPT **44/0**；Visio **43/0**
- **真实 Word 16.0 64 位 L4**：`-ExtendedMatrix` 退出 0，`auto_export=true`、`normal_unchanged=true`，Startup dotm 与规范 dotm 同为 `21DAC67F…`

## 本轮的重要判断

1. **G21 的契约收敛替代了 G17 的规格**。§12.4 明确「存在欠账时拦截该图纸所有后续编号变更」，并指出 G17 探针里「无关 JSON 已写入」的 PASS「不是必须保留的规格」。因此 `G17RenameRetryTests.cs` 的两条断言被反转（其中一条改名），其余 17 条未动。这不是回归，是计划要求的规格变更，已记入 G21.md 契约差量。

2. **G21 发现第五条缺陷**（不在封存红灯里）：`DiscardPendingRename()` 原本**没有任何调用方**，计划要求的「或显式放弃」实际无入口。若不补，收紧守卫后会变成该图纸会话内再也改不了号的硬阻断。已新增 `_btnRenameDiscard` 按钮，文案走 `RenameSyncText`（未改 `Strings.cs`）。

3. **G20 的封存探针不能当绿灯**。`palette-probe/Review.cs` 没有消息泵：修复前坏备份那格事件提前 return 所以跑得完，修复后正常弹模态框就永久阻塞。实现者另写探针用 `Application.OpenForms` 驱动真实模态对话框与真实按钮，红绿两版编译同一份真实生产代码、零适配器。

4. **G22 的实现者自曝一次操作失误**：早期误用 `git checkout --` 冲掉了 G18 的未提交改动，随后用开工时归档的副本还原（SHA-256 校验一致）再打补丁。协调者已独立复跑 G18 全部断言（PPT 44 PASS、Visio 43 PASS）确认无丢失。

## 环境与流程备注

1. **`git checkout --` 会丢弃未提交改动**。本项目当前所有修复都在工作树里，任何还原动作必须用归档副本，不能用 git 还原。已在本轮两次提醒子代理。
2. **BOM 极易丢失**。Office 脚本编辑后必须核对前 3 字节为 `EF BB BF`，否则 PowerShell 5.1 会把中文错误信息读成乱码并解析失败。
3. **.NET 2.0 无 `System.Func`**。G17 曾因此让 2007 组 `CS0246`；本轮 G21 用真 `v2.0.50727\csc.exe` 验证（比 `-p:LangVersion=2` 更严）。
4. **本机无 pwsh 7**，只有 Windows PowerShell 5.1。无 BOM 的 UTF-8 脚本会被 5.1 误解码。
5. **工作区根有 `Everyone : Deny DeleteSubdirectoriesAndFiles` ACL**（沙箱遗留），导致 `dotnet vstest` 在该路径下发现 0 个测试却退出 0。`tools/run-office-tests-vstest.ps1` 复制到中性目录运行规避，并已加本次运行唯一 TRX 归属校验（G23）。

## 仍未关闭

- 真实 CAD 面板操作：G20／G21 的按钮、模态对话框视觉与布局、跨图纸真实事件链、真实 `DocumentLock`／`Transaction`。
- G21 的「显式放弃」**不可撤销**且无二次确认——这是本轮最需要用户判断的体验取舍。
- G21 欠账期间该图纸改号全部冻结，共号无关条目也需先完成或放弃。
- 四组旧宿主（2007/2010/2013/2015）GitHub 外部验收未回传；本机按约定未安装或探测。
- AutoCAD 2025 本体；32 位 Office、其他 Word 版本、ACL／权限拒绝、宏策略阻止、VBA Reset。
- G19–G23 均为自测，缺独立验收角色。

## 交付物

- 逐包交接：`docs/repair-work/G19.md`、`G20.md`、`G21.md`、`G22.md`、`G23.md`
- 证据：`docs/test-evidence/repair-review-20261102/{G19,G20,G21,G22,G23}/`
- 执行登记：`docs/repair-plan.md` §12.8

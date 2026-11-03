# 修复任务接续记录（第五轮，2026-11-02）

基线 HEAD：`b945b1a68fec26ada268b5a4ee3ac066386e96ab`，叠加前四轮已集成内容与用户原有未提交改动。本轮未创建任何提交，未推送。

## 本轮范围

按 `docs/repair-plan.md` 第 13 节，处理复核报告列出的 **4 项**：R-004（G24）、R-034（G25）、R-029（G26）、R-035（G27）。R-016 本轮复核通过（2016 组双引擎对照 + Word L2 17/17），未再分派。未重新分派 G01–G23。

| 包 | 条目 | 结果 |
|---|---|---|
| G24 | R-004 | 提交身份由写入方保存；规范路径与真实字节校验；入口与释放按 Document 归属 |
| G25 | R-034 | 部分写入后按真实磁盘刷新视图，累积会话事实不再被后一次点击抹掉 |
| G26 | R-029 | 四态模型区分「路径已变」「注册已写」「回读一致」，避免注册未写却留着指向缺失目录的注册项 |
| G27 | R-035 | 默认调用的 TRX 证据路径在返回后仍可读 |

## 最终门禁（全部在本轮实际执行）

- 2025 全套 **302 PASS / 0 FAIL**（274 → +18 G24 → +10 G25）
- 四组模拟 83 / 96 / 96 / 96 PASS，0 FAIL
- Office VSTest：Visio **48/48**、PPT **34/34**
- 五版编译通过（含 .NET 2.0）；`build.ps1 -Static` 退出 0
- 四个同步检查通过；五套 release manifest 匹配
- VBA L2：`verify-vba-export`、`test-vba-panel`、`test-g05-word`（FAILURES=0）、`test-g02-l2`（17/17）
- 隔离安装矩阵：PPT **127/0**、Visio **126/0**
- **真实 Word 16.0 64 位 L4**：`-ExtendedMatrix` 退出 0，`auto_export=true`、`normal_unchanged=true`，Startup dotm 与规范 dotm 同为 `21DAC67F…`

## 本轮的技术要点

1. **G24 的核心是把「提交事实」从读取方移到写入方**。原实现 `EditSelectedEntry` 先 `Reload` 再从新加载的模型取身份，外部合法 JSON 或 `MISSING` 可以在这个窗口冒充本次提交。改为在 `WriteBack()` 成功那一刻抓 `DictLoader.GetIdentity`，调用方只读 `CommitFact`。空身份 `ToContext()` 返回 null 且**不回读文件**——安全侧失败而非误同步。

2. **G25 的根因有两层**，只修一层不够：①只在 `DialogResult.OK/Yes` 刷新，而「写盘成功但基线失败」返回 `Succeeded=false`，两个分支都不进；②即使刷新也读不到变化，因为 `RestoreSelectedBackup` 写盘后立即 `NotifySelfWrite`，缓存身份已等于新文件，`IsFileChanged()` 为 false，轮询永远不重载。第三层是单次 `Outcome` 会被后续点击覆盖，所以引入只追加的 `ArbitrationSessionFact`。

3. **G26 把 `$registrationTouched` 从单一判据降为四态之一**。G22 用它同时决定「要不要清空」和「能不能只回读」，于是「路径已变但第一笔注册未写」时既不清空也不恢复，日志还说「保持停用」——注册项实际仍启用且 `CodeBase` 指向已不存在的目录。

4. **G27 是我上一轮引入的缺陷**：G23 加了唯一 TRX 名，但默认结果目录仍是 staging，`finally` 连同临时 DLL 一起删掉，打印的证据路径在返回后失效。G23 的验证一直显式传 `-ResultsDirectory`，正好绕开默认路径。

## 环境与流程备注

1. **`git checkout --` 会丢弃未提交改动**。本项目所有修复都在工作树里。本轮我再次对每个子代理明确禁止，并要求对照修复前代码时先复制到证据目录做快照。前几轮已有两个代理误用此法冲掉他人改动。
2. **BOM 极易丢失**。Office 脚本编辑后必须核对前 3 字节为 `EF BB BF`，否则 PowerShell 5.1 把中文错误信息读成乱码并解析失败。
3. **.NET 2.0 无 `System.Func`**，也不要用 `var`／自动属性。机器上有 `v2.0.50727\csc.exe`，比 `-p:LangVersion=2` 更严，已作为 Shared 改动门禁。
4. **工作区根有 `Everyone : Deny DeleteSubdirectoriesAndFiles` ACL**（沙箱遗留），`dotnet vstest` 在该路径下发现 0 个测试却退出 0。`tools/run-office-tests-vstest.ps1` 复制到中性目录运行规避，并已加唯一 TRX 归属校验（G23）与默认留存（G27）。
5. **`%TEMP%` 下打包工具的临时写入被拒**，需先把 `TEMP`／`TMP` 指向工作目录。

## 仍未关闭

- 真实 CAD 面板操作：G24／G25 的按钮、模态对话框视觉与布局、真实 `DocumentActivated` 事件链、真实 `DocumentLock`／`Transaction`、重启持久化。
- G25 部分写入后裁决入口仍可点（磁盘确有 `.bak`），再点得到 `CONTENT_CHANGED`；状态栏只给一句结论。
- G24 的 `ReadSnapshot` 与随后的 CAD 事务之间仍有窗口（既有「原子替换 ≠ 双写锁」边界，本轮不声称解决并发丢更新）。
- `ArbitrationSessionFact` 是内存结构，面板重开／图纸重载后历史消失（有意）。
- 四组旧宿主（2007/2010/2013/2015）GitHub 外部验收未回传；本机按约定未安装或探测。
- AutoCAD 2025 本体；32 位 Office、其他 Word 版本、ACL／权限拒绝、宏策略阻止、VBA Reset。
- G24–G27 均为自测，缺独立验收角色。

## 交付物

- 逐包交接：`docs/repair-work/G24.md`、`G25.md`、`G26.md`、`G27.md`
- 证据：`docs/test-evidence/repair-review-20261102-r2/{G24,G25,G26,G27}/`
- 执行登记：`docs/repair-plan.md` §13.7

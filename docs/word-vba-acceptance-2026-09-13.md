# Word VBA 修复验收记录（2026-09-13）

## 结论

本次候选在本机 Word 16.0 / Office x64 上通过源码宿主、隔离安装、真实 Startup、两次正常 Word 新进程、故障恢复、同步门禁、五版构建与发行暂存验收。结论只适用于下列候选字节和已执行场景；Word 2010、32 位 Office、宏策略阻止及 UI 专属分支仍为 BLOCKED，不得概括为“所有环境实际验证通过”。

## 候选与环境

| 项目 | 实际值 |
|---|---|
| 时间/时区 | 2026-09-13，Asia/Shanghai |
| 执行身份 | `desktop-3vj2a3b\codexsandboxoffline`；需要真实 Word 的命令在获准主机上下文执行 |
| Git 验收基线 | 验收开始时为 `master` @ `1a89004`；工作树另有用户原有未跟踪目录，始终未纳入本次候选 |
| Word | `C:\Program Files\Microsoft Office\Root\Office16\WINWORD.EXE`，Word 16.0 |
| Office | Click-to-Run `16.0.20326.20144`，x64，zh-cn，ProPlus2024Retail |
| 规范候选 | `word-addin\PatentMarker.dotm` |
| 实际加载文件 | `%APPDATA%\Microsoft\Word\STARTUP\PatentMarker.dotm` |
| 候选/五包/Startup SHA-256 | `2F3E4C46DAAC61225648EA5905507EAAC6EBF602947F074C93CE2CEF91457BC8` |
| Normal 路径 | `%APPDATA%\Microsoft\Templates\Normal.dotm` |
| Normal 验收前后 SHA-256 | `96DBA95BF4ECC3D955BA608F3077B0D3D68AE8FC6E1425C86E14B56EBD540D72`（不变） |

## 已执行验收

| 层级 | 场景与业务断言 | 结果 |
|---|---|---|
| L0 | `vba-sync.ps1 -Check`、`sync-word-addin.ps1 -Check`；五包逐字节一致 | PASS |
| L0 | 故障注入制造 VBA/Word 资产漂移及 DOTM 关系缺失；对应检查必须非零且不得写回 | PASS |
| L0 | DOTM 包含 `word/vbaProject.bin`、`word/vbaData.xml` 和关系；宏元数据恰为 `ShowPatentDictPanel`、`AutoExec`、`AutoExit` | PASS |
| L0 | Structure、Static、PowerShell AST 解析、`git diff --check` | PASS |
| L2 | 根 VBA 导入临时文档：路径映射、多 DWG 选择复用、保存事件与失败取消 | PASS |
| L2 | 8 个组件/9 个源文件导入；UserForm 类型、控件、3 个无参公开过程和手动导出 | PASS |
| L2 | 隔离 Startup：缺清单拒绝卸载、首次/重复安装、安装/卸载故障回滚、可恢复卸载、Normal/用户宏哨兵不变 | PASS |
| L4 | 正常 `WINWORD.EXE /q /n` 启动，不调用初始化器或手动导出；Startup 模板加载、AutoExec 挂钩、普通保存生成并解析 JSON | PASS |
| L4 | 第二个独立正常 Word 进程；run ID 不复用 | PASS |
| L4 | 进程退出前后比较诊断日志；退出阶段不得追加其他 run ID | PASS |
| L4 | 两文档同时打开，B 为活动文档时显式保存 A，只更新 A 的字典 | PASS |
| L4 | 程序化 Save As 改名/换目录：旧路径在 BeforeSave 更新；新路径随后一次普通保存导出 | PASS（即时新路径导出为 false） |
| L4 | 已有字典被独占锁定：旧字节/属性不变、Word 保存取消、错误文件存在、临时文件为 0；解锁后恢复 | PASS |
| L4 | 已有字典为只读：旧字节/属性不变、Word 保存取消、错误文件存在、临时文件为 0；清除只读后恢复 | PASS |
| L4 | Word 运行中，安装器与卸载器均返回失败，实际加载项仍存在 | PASS |
| 回归 | 2025 单元测试 `120/120`；2007/2010/2013/2015 模拟宿主各 `33/33` | PASS |
| 构建 | 2007/2010/2013/2015/2025 五版生产项目编译；2010/2013/2015/2025 SDK API 面检查 | PASS |
| 打包 | 全版本暂存；2013/2015 ILRepack；五包 DOTM 再验证；日志/LSP/备份/报告污染文件为 0 | PASS |

真实宿主运行 ID：

- 基础：`20260913-210320-74DFC3`
- 扩展矩阵：`20260913-210340-4EBEC3`

脱敏证据：

- L4 产物：`%TEMP%\PatentMarker-Installed-E2E-cffaa582c9be40a5ae225a5d75248037`
- 隔离安装产物：`%TEMP%\PatentMarker-Installer-Isolation-532c49b7364d4a49a40a22083d4336b8`
- Word 生命周期日志：`%LOCALAPPDATA%\PatentMarker\Logs\word-vba-20260913.tsv`
- 全版本发行暂存：`%TEMP%\PatentCAD-Annotator-release-20260913-210920`
- 同步/DOTM 门禁故障注入：`%TEMP%\PatentMarker-Sync-Gates-8584ac263130490d9ffc5633d7beb0cf`

## 验收机制实际捕获的红灯

扩展验收不是只在修复后补一个永远为绿的测试；它实际连续截获了以下缺陷：

1. 锁定字典时属性从 6 变为 0，证明失败回滚没有恢复 Hidden/System。
2. 新生成 dotm 能加载但 AutoExec 未执行；包对比定位到缺少 `vbaData.xml` 及 VBA 关系元数据。
3. 锁定写入后残留 `.dict.json.tmp-*`；日志定位到关闭已关闭 ADODB stream 的 3704 次生错误阻断清理。
4. Word 退出时出现仅含 `hook.release` 的新 run ID；新退出前后日志断言在旧候选上稳定红灯，修复后不再拆分同一宿主会话。

修复后，同一故障场景转绿，并被纳入固定 `-ExtendedMatrix` 放行命令。

## BLOCKED / 未覆盖

| 项目 | 原因与下一步 |
|---|---|
| Word 2010 真机 | 当前机器只有 Word 16.0；必须在 Word 2010 用同一候选哈希执行安装、正常重启、普通保存及锁定失败矩阵 |
| 32 位 Office | 当前 Office 为 x64；需在 x86 Office 复跑 L4，并核对 VBA7/Win32 条件声明 |
| 宏策略阻止 | 未更改用户安全策略；需在目标组织策略下验证 Startup 模板是否被信任或明确失败 |
| 交互式取消 Save As | 当前 Codex 会话没有原生 Word UI 控制面；需手工取消对话框并核对旧/新 JSON、文档状态和日志 |
| Alt+F8 宏列表截图 | 同上；源码探针确认 3 个无参过程，但没有把截图冒充自动化证据 |
| VBA Reset / VBE 中断 | 自动注入不可靠且可能改变待测状态；需在可控 UI 宿主中执行 Reset 后验证钩子状态与恢复策略 |
| ACL/目录权限拒绝 | 已覆盖文件锁和只读属性，尚未在隔离 Windows 身份/ACL 下拒绝目录创建或替换 |
| 旧 Normal 组件迁移 | 对 Normal VBProject 的只读清点返回 6068；为保护用户宏，本次不放宽 AccessVBOM、不自动删除任何旧组件 |

## 已知边界与警告

- `DocumentBeforeSave` 发生在 Save As 完成前。本机结果是 `save_as_immediate_export=false`；用户应在新路径再普通保存一次或手动导出。
- 2025 构建与测试有既存 WindowsBase 版本冲突、可空性警告；NuGet 漏洞源因受限网络产生 NU1900。它们没有导致本次构建或测试失败，但不等于警告已修复。
- AutoCAD 2026 可执行文件、Core Console、版本化 COM 注册和许可服务均存在；本次 Word/VBA 变更没有启动 CAD GUI，因此不把 CAD 交互记为本轮 PASS。

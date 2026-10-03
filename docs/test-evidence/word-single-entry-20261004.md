# Word 单一操作入口验证（2026-10-04）

## 失败契约与初始状态

- 时间：2026-10-04 00:28 起，Asia/Shanghai；执行身份为当前 Windows 用户 `wjm`。Windows 10 x64，Word 16.0.20430 x64。
- 源码基线：`b17962c`，开始时仅有用户已有的未跟踪实验目录；本次修改保留这些目录。完整本机产物在忽略目录 `test-results/word-entry-20261004/`。
- 修改前规范、2025 部署和真实 Startup 的 `PatentMarker.dotm` SHA-256：`2F3E4C46DAAC61225648EA5905507EAAC6EBF602947F074C93CE2CEF91457BC8`。实际加载位置：`%APPDATA%/Microsoft/Word/STARTUP/PatentMarker.dotm`。
- 初始状态：用户关闭已有 Word 后，在本轮拥有的新进程/临时文档测试。其他 Startup 模板（包括 `PatentTools.dotm`）及 Normal 均保留。
- 最短操作：正常启动 Word → 空白文档 → Alt+F8 → 项目入口 → 运行。预期只出现一个操作入口，打开面板选择事项。用户报告宏分散；修改前源码有三个公开无参过程（`ShowPatentDictPanel`、`AutoExec`、`AutoExit`）。最初只有源码/元数据证据，UI 现象未复现；最终界面结果另记，不用 L2 替代。

## 自动化结果

| 证据 | 结果 | 断言与产物 |
|---|---|---|
| L2 同场景红绿 | PASS | 原源码 `EXPECTED_1_PUBLIC_SUB_GOT_3`；候选只有 `ShowPatentDictPanel`，8 个组件、UserForm 类型、导出、开关与 JSON 显示/隐藏通过。`red-panel.log`、`green-panel.log` |
| 构建退出红绿 | PASS | 原源码/候选在 15 秒退出门失败；只把上限改为 60 秒后构建通过。没有强制终止用户 Word。`red-build.log`、`build-long-exit.log` |
| 源码导出 | PASS | `L2_SOURCE_IMPORT`；路径映射和保存钩子断言。`l2-export.log` |
| 隔离安装 | PASS | 首次/重复安装、拒绝缺清单卸载、安装/卸载故障回滚、可恢复卸载；Normal/用户宏哨兵零变化。`installer.log` |
| Static/同步/包 | PASS | 唯一入口、生命周期、三个发现元数据项、五包字节一致与同步故障注入；五版发行暂存逐包验证。`static.log`、`staging.log` |
| 本机 L4 安装后保存 | PASS | 两个正常 WINWORD `/q /n` 新进程；实际加载同一候选，AutoExec 自动挂钩，普通保存输出正确 UTF-8 JSON；多文档、Save As 后续保存、锁定/只读保护和恢复、运行中安装/卸载拒绝通过。`installed-e2e.log` |
| 最终宏列表/面板 UI | PASS | 01:13，实际 Alt+F8 列表中候选只有 `PatentMarkerAddin.AutoExport.ShowPatentDictPanel`；生命周期宏不显示，准确入口打开真实面板，控件及已挂钩状态通过。`ui-verification-retry.json/txt` |

- 候选、五包和真实 Startup dotm SHA-256：`01F9A47B837288534B1229EF3EFF14ED4424C2B0E66CE9F67E41BBF5A10FC713`。
- 基础 run ID：`20261004-005832-A5F5C2`；扩展矩阵 run ID：`20261004-005856-8C00C2`。诊断日志 `%LOCALAPPDATA%/PatentMarker/Logs/word-vba-20261004.tsv`。
- L4 脱敏测试产物：`%TEMP%/PatentMarker-Installed-E2E-54b8e5401c4a48ef99226d5dd15fab1a/`。隔离安装产物：`%TEMP%/PatentMarker-Installer-Isolation-e8bf637e8003483d9b209e1d935eda75/`。
- Normal 前后 SHA-256：`96DBA95BF4ECC3D955BA608F3077B0D3D68AE8FC6E1425C86E14B56EBD540D72`；非产品 Startup 文件清单/哈希一致。原有 `PatentTools.dotm` SHA-256：`5DBFB7D8954B2964B2B3B7AE460746BB250690543A8801C200D144B5BE4D34B7`。
- `save_as_immediate_export=false`：BeforeSave 先更新旧位置，新位置需要后续普通保存或面板手动导出。

## 最终界面范围

初次 Computer Use 在新建页被截图 `FrameArrived timed out` 与缺少坐标几何阻断，记为 SKIP/BLOCKED（`ui-verification.json/txt`）。后续以正常 `/q /n` Word 新进程准备一个空白文档；只读诊断确认实际 Startup 与自动启用的钩子后，COM 仅调用 `Documents.Add` 准备前置文档，未调用面板、初始化器或内部导出。之后通过原生 `@oai/sky`、真实 Alt+F8 和运行按钮快捷键完成入口及面板断言。GUI run ID：`20261004-011053-5FADC3`。

宏的位置为“所有的活动模板和文档”，实际完整列表：

- `Normal.AutoExport.ShowPatentDictPanel`（旧 Normal 入口，保留）
- `PatentMarkerAddin.AutoExport.ShowPatentDictPanel`（当前候选唯一入口）
- `专利文档工具箱`（其他模板入口，保留）

真实面板显示“专利标注字典工具”、手动导出按钮、保存 Word 时自动导出复选框、JSON 显示/隐藏复选框与“自动导出已挂钩”；未导出、切换开关或写入正文。主代理独立核对保存的完整状态树、选中宏、面板控件和候选/Startup/Normal/其他模板哈希。完成后本轮 Word 进程清零。

这是安装后宏列表和面板控件的辅助功能/键盘证据；截图及视觉布局为 SKIP/BLOCKED，不能记作截图验收。

## 未覆盖项

旧版 Word、32 位 Office、其他宏策略；交互式取消 Save As、ACL/目录权限拒绝、VBA Reset。本次未修改 UserForm 布局、CAD 代码、解析器或保存策略；不外推这些环境或功能。旧 Normal/其他加载项的操作宏不由本安装器清理。

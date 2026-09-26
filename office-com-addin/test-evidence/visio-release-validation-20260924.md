# Word → Visio 0.1.1.0 发布候选验收记录

## 环境与候选

- 时间：2026-09-24 21:25–21:41（北京时间）。执行身份：`本机用户`。
- 宿主：Windows 10 22H2 build 19045 x64；Visio 16.0.20430.20092 x64；.NET Framework 4.8.1 上加载面向 net40 的产品程序集。Office/Visio 2010 x86 和 Windows 7 未在本机验收。
- 源码：当前工作树含未提交 `office-com-addin/`；不要把本记录解读为特定 Git 提交的结果。没有修改 Word/CAD 生产代码。根目录 `README.md` 原有用户修改保留。
- 发布 ZIP（历史候选，已移出发布目录）：PatentMarker-WordVisio-0.1.1.0-20260924-214136.zip（本地原始产物未公开），SHA-256 `5EAB82B7FB5C5FBC4EBAFB4E63B6723B3C437EA93FD4600FA0B0B0DF9E5CFD7C`。精确清单和逐文件哈希 `verify-visio-package.ps1` PASS。包内有安装、卸载、依赖、MIT 许可证、Newtonsoft.Json 许可证及说明。
- 实际安装路径：`%LOCALAPPDATA%\PatentMarker\OfficeAddin\Visio\PatentOffice.Visio.dll`；包内、构建暂存与安装 DLL 的 SHA-256 同为 `6D211D3F8F8A3884F0F0823452E8A234749AA05E602BF2E57D5C0BF16DD2830B`，程序集版本 `0.1.1.0`。安装清单记录 Office 位数 64；安装从较早的同 DLL 候选 ZIP 解压目录执行，原 ZIP 已存为 `visio-release-package-install-20260924-213159/installed-source.zip`，SHA-256 `4540D194D057ECBBA49FA3040657B08638AD13272D6CBB5474193FC57BE953D7`。最终 ZIP 只更新了说明文字，DLL 哈希不变。
- 已逐项比较原安装 ZIP 与最终 ZIP 的清单：产品 DLL、Newtonsoft.Json、安装/卸载脚本、共用模块和两份许可证的 SHA-256 全部相同；仅包内 README 更新了验收限制。最终 ZIP 因而与本次实际安装运行的代码及安装逻辑逐字节一致。
- 安装前提：原 0.1.1.0 候选 Visio 进程先关闭，未打开用户图纸；重新从解压目录安装后冷启动新进程，PID `24900`，日志 run ID `2b3aa0e4ffbd44f192816db7392f93b9`。

## 已执行的断言

| 层级 | 动作与业务断言 | 结果 |
|---|---|---|
| L0 | net40 Release 构建，0 warning/0 error；所有发布文件存在、哈希清单精确、ZIP 内无额外文件 | PASS |
| L1 | Visio 代码测试 14/14；跨页残件误配、同路径重开面板身份都具备修复前红灯和修复后绿灯 | PASS |
| 安装器隔离回归 | 首次安装故障回滚、成功安装、升级故障回滚、重复安装、卸载；产品目录外哨兵文件与无关 Visio 注册项不变 | PASS |
| 并列 Office 产品隔离 | Visio 安装后 PowerPoint 已安装 DLL SHA-256 仍为此前记录的 `817505CDCDE97D20DEA8C9A7BB70B790283F0304D15136B2A601E72750472B28`，PowerPoint AddIns `LoadBehavior=3` | PASS |
| 宿主加载 | 新 Visio 进程从 HKCU 产品 CodeBase 加载上述 DLL，`OnConnection`、`OnAddInsUpdate`、`OnStartupComplete` 同 run ID 均为 PASS；面板独立窗口可枚举 | PASS |
| 宿主对象 | 用安装版 Word UI 导出的 2 项、0 警告字典，在真实 Visio COM 对象上绑定、标注、扫描、VSDX 保存关闭重开，见 `visio-host-com-20260924-213353-cc7cbf5d/result.json` | PASS |
| 字典只读 | 输入 `.dict.json` 前后 SHA-256 均为 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479` | PASS |
| 面板找回 | 早期 0.1.1.0 候选冷启动 run `56b61f3b6da944bb804d55c6886e95e4`：`Alt+F4` 后窗口保留，日志记录 `minimized_by_user`；可重新激活并读取面板状态。当前 DLL 仍含同一实现 | PASS |
| 面板文件选择入口 | 当前候选的测试图 `visio-panel-ui-20260924-213455-88e5ab13/panel-test.vsdx` 保存后，面板按钮可用；键盘 `Tab`、`Enter` 打开指定 `.dict.json` 的系统文件选择框 | PASS |
| 完整用户路径 | 系统文件选择框未在 Computer Use 可选窗口列表，截图捕获与控件点击受本机 Windows 19045 helper 问题影响；无法完成字典确认、面板标注按钮、保存后的 UI 漏标检查 | SKIP/BLOCKED |
| 目标旧环境 | Win7 + Office/Visio 2010 x86、本机以外版本和旧 VSD | SKIP/BLOCKED |

Visio 宿主对象测试由脚本反射调用实际安装程序集内部生产方法，测试线由 COM 准备并选中。它证明业务对象和持久化行为，但绕过了面板的绑定/标注按钮；不得把它计为整条用户路径 L3。当前用户级 UI 证据止于打开文件选择框。已请求一次人工完成该选择，若得到结果需补记相同候选包下的面板标注、漏标、保存重开及非产品对象不变断言。

## 失败契约与恢复

跨页面误配、同路径重开和早期顶层直线拒绝的红绿证据分别见 [发布前缺陷契约](visio-release-defects-20260924.md)与[宿主失败契约](visio-host-failure-contract-20260924.md)。桌面面板修复后可发现并可从最小化找回。系统文件对话框限制属于 Computer Use 在本机目标窗口与截图链路的限制，未观察到 Visio 产品弹错或字典解析失败；因此标为 `SKIP/BLOCKED`，不冒充产品 PASS 或 FAIL。

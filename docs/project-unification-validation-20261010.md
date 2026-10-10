# 项目整合与面板指引验证（2026-10-10）

本轮范围为同一主仓库维护、恢复编号比较源码复用，以及 PPT / Visio 面板使用指引。没有修改 Word VBA、dotm、CAD 部署 DLL 或 Office 面板行为。

## 输入与结果

- 时间：2026-10-10，Asia/Shanghai。
- 主仓库基线：`5a12a6c312dc689d6607964af9b33e4faa291b1f`；结果为未提交工作树。
- Office 输入：原独立仓库提交 `bf471b1532768dcd61524773bb33ef8ca4c7be86`。正式跟踪源码及历史证据纳回 `office-com-addin/`；原发布 ZIP 未修改，原仓库仅更新 README 与描述。
- 执行身份：当前登录的 Windows 用户，非 SYSTEM；准确身份与本机路径仅保存在本地 `test-results/project-unification-20261010/environment.json`。
- 初始状态：主仓库没有跟踪 Office 源码；CAD 与 Office 各有一份编号比较器。已存在的本机实验目录和未跟踪材料保留，未纳入本次交付。
- 原指引缺口：原 PPT 发布 README 没有关闭后恢复面板的步骤；Visio 恢复提示位于安装步骤，主 README 没有三端入口表。源码静态检查确认启动时显示面板、关闭时最小化，没有功能区按钮或宏入口。本轮未复现、也未声称修复宿主加载故障。

| 检查 | 断言与结果 | 证据范围 |
|---|---|---|
| 公共源码引用 | PASS：五版 CAD、PPT、Visio 共七个产品项目引用唯一 `NumberIdentity.cs` | L0；没有复制 Office 比较器 |
| 引用门禁红绿 | PASS：隔离副本故意加入 Office 比较器副本时非零失败；去掉副本后通过 | L0；本地 `shared-gate-results.txt` |
| 比较器内容 | PASS：现有 CAD 真源与原 Office 副本代码一致，仅工作树行尾不同；CAD 真源无 Git 差异 | 不构成新的编号算法修复 |
| Structure / Static | PASS：源码结构与现有五版同步、Word 包和安装器静态契约 | 不证明宿主 UI 或安装后加载 |
| Office 构建 | PASS：PowerPoint、Visio 的 net40 产品构建均零错误、零警告 | 两个构建输出 DLL，未加载到 Office |
| 现有代码测试 | PASS：PPT 7/7、Visio 24/24，无跳过 | L1；本地 `code-results/` 的本轮 TRX |
| 隔离安装门禁 | PASS：两端首次/重复安装、故障回滚、卸载及非产品哨兵保护 | 隔离目录与测试注册项，不是实际 Office 安装 |
| PowerShell 语法 | PASS：Windows PowerShell 5.1 与 PowerShell 7 | 包括新增安装完成提示 |
| Office ZIP | PASS：精确文件范围、manifest 哈希与源文件一致；解包读取 README 确认包含重新启动、任务栏、Alt+Tab 和 COM 加载项检查 | 新 ZIP 的说明已经更新 |
| 文档导航与差异 | PASS：当前四份导航文档的本地文件链接存在，`git diff --check` 通过 | 本地可审阅结果 |
| 新 ZIP 的宿主用户路径 | SKIP：本轮没有启动 Word、PPT、Visio 或 CAD，未安装到实际产品目录 | 不给新包新增 L3/L4 结论；旧记录只适用于旧 DLL/ZIP |

## 本轮制品

本地包目录：`test-results/project-unification-20261010/packages/`。这些制品未上传 GitHub。

| 制品 | SHA-256 |
|---|---|
| `PatentMarker-WordPowerPoint-0.1.2.0-20261010-103948.zip` | `81F7493EDFA3E8A91B1A17A4359D3780A35528742BC9023E1450BFB5B042977F` |
| `PatentMarker-WordVisio-0.1.4.0-20261010-103948.zip` | `931153DB368FBFA76BF73BD047B478EEACE7F5288F6003C850D4971B56ED94CD` |
| `office-com-addin/dist/PatentOffice.PowerPoint.dll` | `901FDF48C14B451A8782168477946198608B69E82CF80E60B7CF76C0164ADC54` |
| `office-com-addin/dist/Visio/PatentOffice.Visio.dll` | `CF56AD5228AA1E6CC8EF014AC84B6312CF966BD5026F413A3F16F2CC2FE2294E` |

实际加载路径、宿主版本/位数、宿主 run ID：本轮没有加载新 DLL，均不适用。新 ZIP 保留现有产品版本号；本次改变说明和源码引用，没有新增功能或编号比较逻辑。

## 未覆盖与远程状态

没有重新验收实际宿主启动、面板恢复、视觉布局或旧版本/32 位 Office。历史实机记录保留原范围，不能用本轮构建/测试外推。

本页记录提交前的本地验证，GitHub Actions 结果以[主仓库对应提交的工作流](https://github.com/Wujiamu/PatentCAD-Annotator/actions)为准。用户随后授权提交并推送，同时更新主仓库描述和原 Office 仓库的 README / 描述。既有 GitHub 发布资产不重发。

# PowerPoint 0.1.2.0 / Visio 0.1.4.0 推送前验证

## 基线与证据范围

- 时间：2026-10-01，Asia/Shanghai；执行身份为本机当前用户，完整身份留在本机原始记录。
- 源码：`codex/office-addins-release`，基线 `a1e8886` 加未提交的 Office 变更。原有 CAD 暂存内容另属既有工作。
- 本机：Windows 10 22H2 build 19045 x64，PowerPoint / Visio `16.0.20430.20092` x64，.NET Framework 4.8.1。
- 编译目标仍为 .NET Framework 4.0；Office 对象通过迟绑定 COM 调用，无本机 PIA 依赖。不能据此宣布 Win7 / Office 2010 x86 已支持。
- 输入是 2026-09-24 安装版 Word 面板实际导出的脱敏两项字典：编号 1、2，名称底座、支架。PPT / Visio 主名与字典不同，目录有无关 DWG 哨兵，绑定由面板输入完整路径完成。
- 桌面操作使用 `@oai/sky`，由独立 low 档操作代理执行。截图两次超时，元素点击也返回 `coordinate input geometry is unavailable`；未使用猜测坐标。面板、警告和状态由辅助树及只读公共 COM 对象核对。
- 原始日志、TRX、故障副本和图稿保存在本机忽略目录 `test-evidence/office-release-20261001/`；本页是可随源码提交的脱敏摘要。

## 代码与安装门禁

| 检查 | 结果 | 范围 |
|---|---|---|
| 干净源码副本 `verify-code.ps1` | PASS | 不带 bin / obj / dist / 本机实验；两个 net40 产品构建均无警告和错误 |
| PowerPoint 代码测试 | PASS，7/7 | 字典只读解析、编号比较、COM 接口与源码链接；零跳过 |
| Visio 代码测试 | PASS，24/24 | 同上，加绑定、配对扫描、文档身份及选择拒绝；零跳过 |
| 两个隔离安装器 | PASS | 首装、重复安装、首装/升级故障回滚、卸载；无关文件和 HKCU 哨兵不变 |
| Windows PowerShell 5.1 / PowerShell 7 语法 | PASS | 全部 Office 根目录脚本与模块 |
| CAD 2025 单测 | PASS，122/122 | 共享编号比较器回归；零跳过；NuGet 漏洞源连通警告未影响测试 |
| 五版 CAD 真实编译 | PASS | 2007 / 2010 / 2013 / 2015 / 2025；2025 有既有 WindowsBase 引用警告 |
| 根 Structure / Static | PASS | 当前源码结构、同步及相关静态门禁；不能替代宿主验收 |
| Office CI 作业 | SKIP | 尚未远程执行；已配置使用相同入口，本机干净副本通过 |

### 发现与修复的验证问题

1. **Windows PowerShell 5.1 编码失败：FAIL → PASS。** 原 `prepare-visio-panel-test.ps1` 的 UTF-8 无 BOM 文件被按本机旧编码解释，产生两项解析错误，尚未进入宿主准备。只为六个相关 Visio 脚本增加 UTF-8 BOM，不转换正文或行尾；5.1 语法检查及同一准备入口随后通过。失败原件和结果留在 `pre-bom/`。
2. **Visio 负例覆盖：补齐并验证检错能力。** 旧 20 项代码测试没有覆盖 `MarkSelectedLine` 的空选、普通矩形和已有标注线拒绝，旧文档对此有误。新增四项用例（空选/多选为两个参数），断言拒绝发生在 BeginUndoScope 前。隔离副本故意移除普通矩形校验后，精确过滤的同一测试稳定 FAIL；恢复源码后全套 24 项 PASS。该红绿证据属于 L1，不能当作安装后 UI 证据。
3. **跳过分类：更正历史结论并实际回归。** 2026-09-28 Visio 外部反射回归曾在空选调用停住，重跑跳过三项必要负例，却输出总体 PASS。本次修正为显式选择跳过；实际 Windows PowerShell 5.1 STA 的 `-SkipNegativeCases` 结果为总体 SKIP，三个负例也均为 SKIP。原始结果目录后缀 `f4bf16f2`；[历史记录](office-cold-start-20260928.md) 已更正。
4. **Visio 反射诊断的可用路径：Windows PowerShell 5.1 STA。** 同一个已安装 0.1.4.0 DLL 在此环境完整执行绑定、空选/矩形/已有线拒绝、正常标注、扫描、保存重开和字典字节不变断言，全部 PASS，约 7 秒。最后结果目录后缀 `78b41715`。脚本现在要求此运行时；Core 调用实际返回 SKIP、非零退出且不修改宿主。旧 Core 路径停顿的根因尚未完全定位，这个白盒结果不替代面板 L3。一次辅助 `Start-Process` 调用因继承了不合适的模块路径而找不到 Get-FileHash，未进入文档创建；改为标准直接调用 Windows PowerShell 后成功，不是产品标注失败。

## 本机面板验收

PowerPoint 首次正常启动 run ID：`461f7e802ab34d43a3b37f55fb5480ba`。Visio 首次正常启动 run ID：`b0d1fb022f054b93bd25ce734b04f8e8`。两者均从实际安装目录加载，生命周期日志含版本、64 位与程序集路径。

### PowerPoint

已在真实面板输入路径并绑定；第一页标注 1 后检查只漏 2，通过画布键盘切到第二页并选中预制直线后标注 2，整稿检查漏标 0。无选线时的标注被拒绝。保存后的公共 COM 审计确认两页各有一个产品组、每组两个子形状，编号分别 1 / 2，原图片数量、位置和大小未变，Saved=true。

损坏绑定字典后，面板保留两项旧列表、禁用标注和检查、显示解析错误；恢复字节后自动重新加载，再检查仍漏标 0。字典原始字节与属性恢复。直线由公共 COM 准备；第二页选线及业务面板操作是真实键盘操作，鼠标画线和视觉位置未覆盖。

### Visio

已通过真实面板路径绑定、首线标注 1、检查只漏 2。空选时拒绝，误选自动生成的编号框时也拒绝。进一步原生画布选线未成功，明确保留 SKIP；使用微软[快捷键说明](https://support.microsoft.com/en-us/accessibility/visio/keyboard-shortcuts-for-visio) 的 Tab 焦点 / Enter 选择及有限焦点恢复后，公共 COM 仍无选择。没有将输入已发送记为通过。

随后通过公共 COM 精确预选测试对象，真实面板拒绝已有产品线和普通矩形；另一条线标同号 1 成功，第二页标 2 成功，全稿检查零漏标。保存审计确认三对线/编号框、编号 1 两对和 2 一对、三条线 EndArrow=13、每对 ID 在同一页且编号一致。普通矩形 ID=3、OneD=0、尺寸 1×1 且无产品元数据；Saved=true。

字典损坏和移走时均保留旧两项列表、禁用标注/检查并显示错误；恢复字节后重新启用，检查仍零漏标。公共 COM 创建无绑定的第二份临时图稿 B，面板转为空列表、未绑定及禁用操作；真实主窗 Ctrl+Tab 回 A 后绑定和列表恢复，检查零漏标。这个切换测试不覆盖“定时刷新前立即点击”的竞态分支；该分支历史红绿和现有按钮同步核对仍保留。

### 最终 ZIP 安装后的全新进程重开

**结果：PASS，限定为本机安装后面板与持久化 L3 子链。**

两宿主先完整关闭，再从最终 ZIP 安装，随后分别由 `sky.launch_app` 正常冷启动。没有手动连接加载项、调用初始化器或内部扫描方法；正常启动的生命周期、绑定恢复与面板动作使用同一个宿主 run ID。保存产物由公共 COM 打开，面板检查通过真实键盘按钮执行，因此不把文件打开或形状选择说成原生鼠标操作。

| 宿主 | 冷启动 run ID | 重开后的业务断言 |
|---|---|---|
| PPT 0.1.2.0 | `effa750310204a6fa03edb270a2b9a9f`，21:47 | 自动恢复字典两项及原关联；两页产品组仍在；面板 `missing=0;marked=2`；原图片保留、Saved=true |
| Visio 0.1.4.0 | `4bf15ea99fc243f68b0d7af2828c356d`，21:49 | 自动恢复字典两项及原关联；三对标注和普通矩形哨兵不变；面板 `missing=0;stale=0;malformed=0;marked=2`，范围为前景页；Saved=true |

两进程的日志均包含 OnConnection 和 OnStartupComplete PASS，实际程序集路径与最终安装记录一致。检查完只关闭本轮文稿与实例；POWERPNT / VISIO 进程清零。两份输入字典最终与原始字节完全相同，Hidden / System / Archive 属性保留（值 38）。完整键盘步骤及辅助树留在 `final-cold-reopen-ui.txt`；日志摘录和进程断言留在本机原始记录。

## 制品与推送边界

实际加载目录分别为 `%LOCALAPPDATA%\PatentMarker\OfficeAddin\PowerPoint` 和 `...\Visio`。本轮只保留必要的部署 ZIP 与实际加载 DLL SHA-256；字典是否修改以逐字节比较断言，不给字典或图稿另外计算交付哈希。现有解析器的刷新/日志及安装器所有权校验仍沿用自身哈希机制。

| 最终制品 | SHA-256 |
|---|---|
| `PatentMarker-WordPowerPoint-0.1.2.0-20261001-212952.zip` | `3FF0B0853BD396481B1B3D095CBE5FD5FF3F45729A0BF925781746918A178B50` |
| 实际安装 `PowerPoint/PatentOffice.PowerPoint.dll` | `94BDAFC2015C0C01AEAF8D148FAAC73CC027727CE23561C3149C6B6C48DDD17E` |
| `PatentMarker-WordVisio-0.1.4.0-20261001-212956.zip` | `C02D749BDDB220039FC02992CF68ED54688933F69449B89A1F88724A7E77FE40` |
| 实际安装 `Visio/PatentOffice.Visio.dll` | `F679ED672CA9AE015CD3A5ACEC23E09507A7D3D3E3991250C76C84769398932A` |

两个 ZIP 都经过构建、代码测试、隔离安装回归和精确包内容检查，并从包内 Windows PowerShell 5.1 安装入口实际覆盖安装。最终安装 DLL 与此前面板运行及本机源码构建逐字节一致；版本保持 0.1.2.0 / 0.1.4.0，后续改动只涉及测试、工具和说明。

本次新增共享层是 Office 专用的四份链接源码，两个宿主仍独立 DLL / COM 身份 / 面板 / 绑定 / 标注。跨 CAD / Office 只共享 `NumberIdentity.cs`。不复用 CAD 的完整面板、字典读写或宿主代码，不改变 Word 生产代码。

共享编号比较器的源码改动已做 CAD 单测与五版编译；本轮没有重发 CAD 部署 DLL，也不把源码新比较行为写成已安装 CAD 的结果。原有 CAD 暂存资产应按其独立交付记录处理。

Windows 7 + Office/Visio 2010 x86、其他宿主版本/位数、鼠标拖画、视觉布局、Visio 粘合跟随、复杂组、撤销和旧 VSD 均待验。实验性发布应保持这些范围声明。

## 推送准备结论

**PPT / Visio 源码和上述实验性 ZIP 已达到可推送、可发布准备状态。** README、可行性报告、使用说明、CI 门禁与两端发布说明已同步；必要负例、安装恢复及本机限定 L3 子链有明确证据。本页为提交前验收快照，当时尚未提交、推送或创建 GitHub Release，远程 CI 尚未运行。随后用户确认 CAD、PPT、Visio 与 README 一起提交；合并提交不扩大各自的验收结论，仓库提交及远程 CI 结果以 GitHub 历史为准。

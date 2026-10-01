# Word → Office 只读标图原型：可行性与验收记录

- **记录日期**：2026-10-01；以下历史条目保留原测试日期与对应制品版本

- **产品版本**：当前候选为 PowerPoint 0.1.2.0、Visio 0.1.4.0；0.1.1.0 / 0.1.3.0 的 L3 宿主结论是旧制品历史基线

- **最新结论状态**：2026-10-01 干净源码构建、PPT 7/7 与 Visio 24/24 代码测试、两端隔离安装回滚/卸载检查通过。本机 Office 16 x64 的真实面板绑定、两页标注与整稿漏标检查通过；线条由公共 COM 准备，Visio 的原生画布选线仍待验。最终包、冷重开与限定 L3 子链见[推送前验证](test-evidence/office-release-validation-20261001.md)。2026-09-28 Visio 跳过必要负例的总体结果更正为 SKIP，代码负例覆盖声明也已更正，见[历史记录](test-evidence/office-cold-start-20260928.md)。Office 2010 x86 与 Windows 7 未验。

- **PowerPoint 阶段边界**：只读 Word 导出的 `.dict.json`，写入 PowerPoint 演示文稿；不修改 Word/CAD 生产代码，不回写字典，不含 Visio、图片导入或 CAD 式画布点选。Visio 后续实现独立记录在本报告末尾

- **2026-09-28 代码债校准**：PPT 和 Visio 仍独立编译为各自的 DLL，并没有复用 CAD 字典 IO、面板或宿主代码。二者现在链接同一组 Office 字典 DTO/只读解析器、COM 扩展接口和诊断日志源码；CAD 共享层只链接 `NumberIdentity.cs`。新的共享源码经 PPT/Visio 代码测试和五版 CAD 构建核对。本文历史 L3 结论仍绑定对应验收记录中的旧包哈希，不能外推到本次新构建。

## 结论

PowerPoint 方向已有可构建的 .NET Framework 4.0 C# COM 加载项原型，功能路径包括手选字典并写入演示文稿 Custom XML、定时重读字典、把所选原生直线与编号文字分组并写入 Tags，以及扫描全部幻灯片的产品标注组检查漏标。Office 不复用 CAD 的字典 IO、宿主交互或面板。2026-09-28 起 PPT 与 Visio 共同链接 Office 专用的字典模型/解析器、COM 扩展接口和诊断实现；两端仍各自编译到独立 DLL。跨 CAD/Office 的源码复用限于 `NumberIdentity`。

构建及隔离安装/卸载故障回归已通过。产品 DLL 已安装到当前用户目录；COM 注册修复后的 Office 16 x64 全新 PowerPoint 进程确实加载了产品 DLL。限定场景中，已通过面板绑定相对路径字典、在面板执行标注和整份漏标检查、保存演示文稿、关闭 PowerPoint、再以全新进程打开保存产物，并由面板检查到编号 1、2 均已标注。修复前 `SlideID` 和 `FlipH` 两项运行时错误均有已安装加载项的真实红灯日志；修复后同一标注操作及反向斜线场景转绿。

2026-09-24 的早期 PPT 回归字典来自 Word VBA 的 L2 源码宿主副本；0.1.1.0 的新回归改用安装版 Word 面板实际导出的字典，并在真实 PowerPoint 面板中绑定、标注与检查。后续键盘回归在 PowerPoint 形状库中插入原生直线并经面板成功标注；另一页直线虽由 COM 预备，却通过 PowerPoint 键盘选择后经面板标注。这两组在全新进程重开后均被识别，因此结论是本机 PowerPoint 16 x64 的**限定 L3 面板主链通过**。Computer Use 的 `@oai/sky` 可操作面板和功能区；本机 Windows 19045 的截图捕获受已知接口问题影响，无截图时点按返回 `coordinate input geometry is unavailable`。未用猜测坐标替代鼠标拖拽定向画线及视觉目检。

本机 PowerPoint 结果不外推到 Windows 7 + Office 2010 x86。Visio 是独立原型，不复用 PowerPoint 的对象结构与绑定实现；其当前实测范围单列于本报告末尾。

## 本轮更正：旧报告中不准确的判断

| 主题           | 更正后的结论                                                                                                                                                                                                           |
| ------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| CAD 代码复用范围   | 旧报告称整套 CAD 面板、多个 IO 类可直接链接，范围过大。面板与字典加载路径依赖 AutoCAD 文档、命令、Editor、实体及会话服务。本原型只链接 `cad-plugin/Shared/IO/NumberIdentity.cs`；PowerPoint 面板与字典 DTO/只读解析器独立实现，不链接 CAD 的 `DictLoader`、`DictWriter`、`ConfigLoader` 或 UI。 |
| Word 字典命名    | 不是按 PPT/Word 同名自动配对，也不是“精确匹配 → 包含匹配”。`vba/AutoExport.bas` 的当前行为是：无 DWG 时用 Word 文件主名；一个 DWG 时用该 DWG 主名；多个 DWG 时需明确选择，自动导出只复用当前 Word 文档运行中记住的选择，未选择时失败关闭。                                                          |
| CLR 并行加载     | “Office 进程只能装一个 CLR，先装 CLR 2 就必然阻止 CLR 4”过于绝对。.NET Framework 4 支持进程内 CLR 并行宿主；但具体 Office 主机、COM 激活策略与已有加载项仍需实机组合验证。不能把多 CLR 自动当作冲突，也不能据此承诺所有组合兼容。                                                                |
| Visio 窗口类型常量 | 锚栏窗口示例使用 `visAnchorBarAddon`（值 10）；`visDockedStencilAddon`（值 11）表示具有停靠模具行为的附加窗口。当前 Visio 加载项采用独立 WinForms 浮动面板，没有使用锚栏常量；冷启动后可独立枚举并从最小化恢复，面板路径输入、标注与漏标检查已通过。 |
| 本机 .NET 版本   | 注册表 Release 值 **533325 对应 .NET Framework 4.8.1**，原报告误写为 4.8。本项目以 `net40` 编译。                                                                                                                                     |
| Office PIA   | 本项目不引用 Office/PowerPoint PIA，而是手写 `IDTExtensibility2` 声明并通过 COM 迟绑定访问 PowerPoint 对象；因此构建不要求本机安装 Office 2010 PIA。仍须在 Office 2010 宿主验证所调用成员及 COM 回调签名。                                                             |
| 任务窗格         | 当前实现是独立的无模式 WinForms 面板，不是 `ICustomTaskPaneConsumer`/ActiveX 自定义任务窗格。后者不属于本阶段功能。                                                                                                                                 |

## 当前实现

### 字典绑定与刷新

- 演示文稿必须先保存为 `.pptx`；用户手选 Word 导出的 `.dict.json`，解析成功后才写入专用 Custom XML 部件。

- PPT 与字典在同一卷时存相对路径，不同卷时存绝对路径。文件丢失时要求重新选择，不按同名推断。

- 每两秒计算绑定文件 SHA-256 并检查变化。解析失败会显示错误、暂停新增标注并保留上一份有效列表；相同坏文件不会每两秒重复解析和写日志；文件改变后再尝试读取。

- 字典读取只访问文件，不调用字典写入器，不修改 `.dict.json`。

### 标注与漏标

- 用户从编号文字位置向图中目标绘制一条 PowerPoint 原生直线，选线后在面板选编号并点击“标注所选直线”。起点放置字典原样编号，终点添加箭头；方向反了就应重画该线。

- 只接收当前幻灯片中单独选中的原生直线，并核对幻灯片与演示文稿归属。标注会将直线与文字组成 PowerPoint 组，并在组上写入 `PATMARKER=PATENTMARKER` 和 `PATNUMBER=<编号>`。

- 保存重开后扫描整份演示文稿所有幻灯片。只有带产品 Tags 且仍由一条直线和一个文字框组成的组才计入；普通图形不计入。同号标注一次即可满足漏标检查，重复标注合法。

- 编号比对使用 `NumberIdentity` 的 Trim 与大小写不敏感规则，展示和写入标签文字保留字典原值（PowerPoint Tags 会将其值转成大写，因此扫描按同一规则比较）。

- 用户负责在现有图片上画线。当前代码没有导入或改写图片，也没有图像识别、指向区域命中或自动吸附。

### 安装与诊断

- 安装位置：`%LOCALAPPDATA%\PatentMarker\OfficeAddin\PowerPoint`。仅部署产品 DLL 与 Newtonsoft.Json DLL，并写入产品 GUID、ProgID 及 PowerPoint AddIns 项；`LoadBehavior=3`。

- 安装器要求准确的产品所有权清单。首次安装、升级、注册失败均以暂存目录和注册表快照回滚；卸载只删除带有对应所有权清单的产品目录及专属注册项。

- 每次加载项连接生成 run ID；诊断写到 `%LOCALAPPDATA%\PatentMarker\Logs\office-ppt-YYYYMMDD.tsv`，包括 Office 版本/位数、生命周期、绑定路径、字典哈希、标注与检查阶段和错误，不记录演示文稿正文。

## 本机环境与现有证据

| 项目                          | 证据与结果                                                                                                                                                                                 | 层级/状态                         |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------- |
| PowerPoint                  | Microsoft 365 Click-to-Run，版本 `16.0.20430.20092`，64 位，位于 `C:\Program Files\Microsoft Office`                                                                                          | 注册表/文件只读核对                    |
| Windows                     | Windows 10 Pro build `19045`                                                                                                                                                          | 注册表只读核对                       |
| .NET Framework              | Release `533325`，版本字符串 `4.8.09037`，对应 .NET Framework 4.8.1                                                                                                                            | 注册表只读核对                       |
| Office PIA                  | 本项目不引用 PIA；编译基于系统 .NET Framework 4.0 targeting pack 与 COM 迟绑定                                                                                                                         | 源码/项目文件核对                     |
| 编译                          | `office-com-addin/build.ps1`；net40 Release 成功，0 warning、0 error；发布暂存 DLL 哈希核对通过                                                                                                       | L0 PASS                       |
| 安装回归                        | 唯一临时目录和隔离 HKCU 分支；覆盖首次安装中途失败、重复安装、升级写注册表后故障回滚、卸载，并核对无关加载项哨兵值不变                                                                                                                        | 安装器回归 PASS；不是 Office 宿主证据     |
| 实际部署                        | Office 64 位产品键 `HKCU\Software\Microsoft\Office\PowerPoint\Addins\PatentOffice.PowerPointAddIn` 的 `LoadBehavior=3`；COM CodeBase 指向用户 LocalAppData 产品 DLL；后续冷启动日志确认该 DLL 已加载 | 安装检查 PASS；宿主加载 PASS           |
| 已部署加载项 DLL                  | `%LOCALAPPDATA%\PatentMarker\OfficeAddin\PowerPoint\PatentOffice.PowerPoint.dll`，历史 0.1.1.0 SHA-256 `11991315EF0631F1893B113415D5D4B4E975A33A8CFCAC9C616E5306B10EEC79`，与 2026-09-27 候选 ZIP 包内 DLL 一致；前一 0.1.1.0 候选为 `3135C19BEFAD7A8831347E8876867E9E68A8CA87A21DF884F41C232FDC4A287B` | 当前候选、部署及日志中的运行加载路径/外部哈希 PASS |
| Newtonsoft.Json DLL         | 同目录 `Newtonsoft.Json.dll`，SHA-256 `C69B18993D8236E5DFE3F0580A4392E7BC0B5F525911737318117C91D43B3EA5`                                                                                  | 安装后进程外哈希                      |
| PowerPoint 冷启动与重开            | `ae932fa13c1a47409e968031fe2504ad` 冷重开保存的两页测试稿并通过面板检查；最终诊断版 run `4c5f2db795ef4585bf7f3c39e2a13cc1` 也全新启动、恢复绑定、读 2 项并经面板检查返回“均已标注”，日志 `missing=0;marked=2` | 冷启动、冷重开及宿主业务子链 PASS      |
| PowerPoint 标注与漏标             | run `26634ed58381454d986e620aae86918f`：UI 面板标注 PASS（SlideID 256/257）；覆盖水平、VerticalFlip 及 HorizontalFlip 直线；漏标检查依次得到 `missing=1`、`missing=0`；普通未标记直线不能替代产品标注组 | 安装后宿主限定场景 PASS               |
| Word → PPT 字典来源               | 历史 0.1.0.0 PPT 回归使用 `verify-vba-export.vbs` 的 `L2_SOURCE_IMPORT` 字典副本，SHA-256 `CF262EDAA81D4442FC58186107AD909185D5FD1CAB75AE98435884DE0DD4DC77`。历史 0.1.1.0 回归直接绑定安装版 Word 面板导出的 2 项字典，SHA-256 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479`，测试前后相同 | 历史输入 L2；当前跨宿主字典只读 PASS |
| 测试演示文稿产物                  | `ppt-ui-smoke-20260924.pptx`：两页，保存后 159586 字节，SHA-256 `EAC077925EAB6D1F1C6076AA8A918D1A37BBC38C73F3A0190F290F12779C1BCE`；冷重开后两页各有一个正确 Tags 产品组，普通线无 Tags | 保存及冷重开产物断言 PASS             |
| Computer Use 交互               | `@oai/sky` 枚举并冷启动 PowerPoint，通过键盘执行面板绑定/标注/漏标；后续还由功能区键盘插入原生直线、切页并以 `Tab` 选中另一页已有直线。Windows 19045 截图捕获有已知接口错误，无截图时点按提示 `coordinate input geometry is unavailable`；未用坐标猜测 | 键盘交互 PASS；鼠标拖拽画线及视觉布局未覆盖 |
| Word → PPT 前一候选面板场景             | 安装版 Word UI 导出 2 项字典；run `6ca260ba4806417bb5ecdd82a917a89e` 经面板绑定、两页标注和检查，PowerPoint UI 保存；run `659e3167c3ea4f708356e93662c58346` 冷重开后面板无漏标。第三份 PPT 的字典副本损坏/丢失与恢复、图片误选均通过。run `e3b2e9e083e842cb9e46114bec4eecdb` 将 UI 插入的原生直线标为 1；run `65cb01db81bb476c85d3c748473757e9` 键盘选中第二页已有直线并标为 2；run `a2b5d87696b54d06bbd81e01779a3b8b` 冷重开后面板无漏标。测试字典 SHA-256 不变 | 前一候选限定 L3 PASS；鼠标拖拽画线、鼠标选线和视觉布局待验 |
| Word → PPT 当前候选面板场景             | 2026-09-27 ZIP 解压后覆盖安装；run `2146f608ab744d1aa521624decf9d7d9` 自动恢复真实 Word 字典副本、读取 2 项、界面键盘选线并经面板新增同号编号 1 标注，PowerPoint UI 保存。run `8499ed773a404b818a36d0d3fc7d7479` 冷重开后面板检查 `missing=0;marked=2`。保存产物有 1/1/2 三个产品 Tags 部件，字典 SHA-256 不变；与前一候选的 100 个方法 IL 一致 | 当前 ZIP 限定 L3 PASS；鼠标拖拽画线、鼠标选线和视觉布局待验 |
| 跨卷关联与异目录副本重开           | C: 测试 PPTX 经真实面板绑定 F: 字典副本，保存的产品 Custom XML 为 `kind="absolute"`；复制 PPTX 到 C: 另一目录后，新 run `f6152041a75e45e5903584777ca97e85` 自动读 2 项，面板全稿检查 `missing=0;marked=2`，字典哈希不变。详见 [发布验收记录](ppt-release-validation.md) | 本机安装版跨卷路径与冷重开 PASS |
| 非产品注册项最终快照           | 安装后与前一轮宿主测试后快照相等；更晚的 PowerPoint 冷启动中，第三方 `MSOfficePLUS` 的 `FeaturesGate.LastUpdateTime` 自动变化，其他键值不变。该值自身声明 60 分钟刷新间隔，归因为第三方定时刷新仍属推断，未独立复现实验 | 严格“全程零变化”FAIL；产品安装隔离回归 PASS |
| Windows 7 + Office 2010 x86 | 无目标环境证据                                                                                                                                                                               | 待验，不宣称支持                      |
| Visio                       | 独立 0.1.3.0 候选已封包并从 ZIP 安装；Visio 16 x64 冷启动、真实 Word UI 字典经面板路径输入绑定、标注、全稿扫描和 VSDX 冷启动重开通过；0.1.2.0 候选另覆盖两页、字典故障恢复和多文档隔离；快速切换竞态有红绿证据 | 发布包与限定 L3 面板主链 PASS；画线、粘合及旧环境待验 |

最初的 COM ProgID 注册错误及后续标注代码红灯均在下节保留历史失败契约，并附有修复后实测。Computer Use 的截图和画布输入边界与加载项业务结果分别记录，不把一次坐标输入失败当作 PowerPoint 无法操作。

### 修复前失败契约（2026-09-24 06:51 UTC）

- 执行环境：用户 本机用户；Windows 10 Pro 10.0.19045 x64；Microsoft 365 Click-to-Run PowerPoint 16.0.20430.20092 x64，进程 POWERPNT.EXE PID 3584，路径 C:/Program Files/Microsoft Office/root/Office16/POWERPNT.EXE；.NET Framework 4.8.1。
- 部署对象：产品版本 0.1.0.0；候选与已部署 PatentOffice.PowerPoint.dll SHA-256 均为 4728C23FAF777A1FAB28BD00306B27E1E85D68C9ED60CB4502510F6517B12A6C；Newtonsoft.Json.dll SHA-256 为 C69B18993D8236E5DFE3F0580A4392E7BC0B5F525911737318117C91D43B3EA5。PowerPoint 用户级 AddIns 键 LoadBehavior=3，COM 类 CodeBase 指向已部署 DLL。
- 前置文件状态：验收目录 %TEMP%/PatentMarker-WordPpt-E2E-4c124e0f65e9498bba75775ba2ffec00 只有 demo-part.dwg 与 sample-existing-image.png，没有 .dict.json 或 .pptx。PowerPoint 新建的“演示文稿1”未保存；Word 未运行。未生成加载项 run ID，未改动字典字节。
- 最短失败步骤：使用 @oai/sky 冷启动 PowerPoint；在同一实例读取 Application.COMAddIns；检查进程模块和预期日志；在同一用户环境运行 CreateObject("PatentOffice.PowerPointAddIn") 两次。
- 预期：ProgID 能解析；PowerPoint 的 COMAddIns 中存在产品项并已连接；进程加载产品 DLL；OnConnection 写出新 run ID 和启动日志。
- 实际：COMAddIns 中没有产品项；进程模块中没有产品 DLL；%LOCALAPPDATA%/PatentMarker/Logs/office-ppt-20260924.tsv 不存在；两次 CreateObject 返回 VBScript 错误 429，PowerShell Type.GetTypeFromProgID 返回 0x800401F3 (CO_E_CLASSSTRING)；无 run ID、无业务操作。
- 定位与源码工作树：安装器把 ProgID 的 CLSID、CurVer 写成父键普通值，但 COM 需要 ProgID/CLSID 与 ProgID/CurVer 子键默认值；CLSID 根上的 ProgID、VersionIndependentProgID 也误写为普通值。此结构与 ProgID 解析失败相符。复现时未提交源码；office-com-addin/ 为未跟踪产品目录，根 README.md 已修改；.codex-remote-attachments/ 与 _paneltest/ 保留原样。
- Computer Use 单独限制：@oai/sky 初始化、应用枚举、PowerPoint 启动和开始页 UIA 读取成功。点击空白模板失败为 coordinate input geometry is unavailable；键盘 Ctrl+N 可创建新演示文稿。该输入几何限制与 COM 注册缺陷分别记录，截图仍按本机 Windows 19045 项目约定跳过。

### 修复前失败契约：标注归属检查（2026-09-24 07:48 UTC）

- 执行身份与宿主：`本机用户`；Windows 10 Pro build `19045` x64；Microsoft 365 PowerPoint `16.0.20430.20092` x64；.NET Framework 4.8.1。
- 部署及加载对象：已安装并由本次 PowerPoint 冷启动加载的 `PatentOffice.PowerPoint.dll`，版本 `0.1.0.0`，路径 `%LOCALAPPDATA%\PatentMarker\OfficeAddin\PowerPoint\PatentOffice.PowerPoint.dll`，SHA-256 `4728C23FAF777A1FAB28BD00306B27E1E85D68C9ED60CB4502510F6517B12A6C`；run ID `646d4cdbe5994f4289689d647edcfe94`。日志有 `OnConnection`、`OnAddInsUpdate`、`OnStartupComplete` PASS。
- 前置产物：临时 Word 源码宿主回归 `verify-vba-export.vbs` 为 `PASS|L2_SOURCE_IMPORT`。其 `single.dict.json` 与验收目录 `demo-part.dict.json` 逐字节相同，均为 387 字节、SHA-256 `CF262EDAA81D4442FC58186107AD909185D5FD1CAB75AE98435884DE0DD4DC77`，解析为编号 1/2 两项。测试 PPTX `ppt-ui-smoke-20260924.pptx` 保存后为 156747 字节、SHA-256 `3E8F1B53A9CF3B09B39DC447AB89ECE5CB2603050AE1779E958002D470A94ED4`；含一页、既有测试图片和一条原生直线。演示文稿内有且仅有一条产品关联 Custom XML，路径为相对路径，已指向测试字典。
- 最短操作：冷启动 PowerPoint；打开测试 PPTX；通过产品面板选择 `demo-part.dict.json` 并成功读取 2 项；保存；确认当前活动演示文稿确为该测试文件，`Selection.Type=2`、所选 Shape 数量为 1、`Shape.Type=9`；从面板执行“标注所选直线”。
- 预期：所选直线经归属校验后，与编号文字组成带 `PATMARKER=PATENTMARKER` 及 `PATNUMBER` Tags 的形状组，面板不报错。
- 实际：面板弹出“无法添加标注”，错误正文为 `“System.__ComObject”未包含“SlideID”的定义`。同一 run ID 的日志记录 `annotation.create` / `FAIL` / `RuntimeBinderException`。失败发生在选线归属校验，未生成标注组。
- 限制说明：字典来自既有 Word VBA 的 L2 源码宿主导出，不是安装后 Word UI 导出；PPTX 中图片和测试直线由 COM 准备。字典绑定和保存经已安装加载项面板及 PowerPoint UI 执行。本失败是已安装加载项的真实 COM 业务失败，不能以构建或安装回归覆盖。


### 修复后续失败契约：直线端点属性读取（2026-09-24 08:04 UTC）

- 执行身份与宿主：`本机用户`；Windows 10 Pro build `19045` x64；Microsoft 365 PowerPoint `16.0.20430.20092` x64；当前进程 PID `13932`，实际执行文件 `C:\Program Files\Microsoft Office\root\Office16\POWERPNT.EXE`；.NET Framework 4.8.1。
- 实际加载对象：全新 PowerPoint 进程 run ID `6702a05008c9439b9aa9c6dc72c83438`；产品 `0.1.0.0`，生命周期日志 `OnConnection`、`OnAddInsUpdate`、`OnStartupComplete` 均为 PASS。已加载/部署 DLL 路径 `%LOCALAPPDATA%\PatentMarker\OfficeAddin\PowerPoint\PatentOffice.PowerPoint.dll`，与当时的候选暂存 DLL SHA-256 相同，均为 `0EB76BE7544B6EA51D09DC6AEE98E3EA39A761B694DFD62337709E42D9FFA62A`。
- 前置状态：当前只打开验收目录中的 `ppt-ui-smoke-20260924.pptx`，PowerPoint COM 报告 `Saved=True`；1 张幻灯片，2 个形状：已有图片 `Picture 2`（type 13）和选中原生直线 `Straight Connector 3`（type 9）。直线 Left=70、Top=160、Width=140、Height=0、Rotation=0；对象模型读取 `HorizontalFlip=False`、`VerticalFlip=False`。PPTX 文件 156747 字节、SHA-256 `3E8F1B53A9CF3B09B39DC447AB89ECE5CB2603050AE1779E958002D470A94ED4`。绑定字典 387 字节、SHA-256 `CF262EDAA81D4442FC58186107AD909185D5FD1CAB75AE98435884DE0DD4DC77`，解析为 2 项，绑定仍显示于面板；本次失败前后字典哈希相同。
- 最短失败步骤：在真实 PowerPoint 面板选中编号 1，对当前唯一选中的原生直线执行“标注所选直线”。前一处 `line.Parent.Parent` 归属检查错误已按第一个失败契约修正；这次实际操作已越过归属检查，进入端点读取。
- 预期：端点读取成功并按用户画线方向创建带编号文字的产品标注组。
- 实际：加载项记录 `annotation.create` / `FAIL` / `RuntimeBinderException:“System.__ComObject”未包含“FlipH”的定义`，日志时间 `2026-09-24T16:04:07.781`（本地时间）。面板显示同一错误；演示文稿保持 Saved=True，形状数仍为 2，未生成标注组。
- 根因已确认：PowerPoint `Shape` 暴露只读属性 `HorizontalFlip` 与 `VerticalFlip`，源码却访问不存在的 `FlipH` 与 `FlipV`。在修改 `GetLineEndpoints` 前留下这次同场景红灯；后续修复改用对象模型实际属性，并经同一安装后面板标注操作转绿。运行时读到 `VerticalFlip=-1` 和 `HorizontalFlip=-1` 的斜线均成功通过端点读取。
- 当时的限制（之后的覆盖见“修复后回归证据”）：绑定字典是既有 Word VBA 源码导入 L2 回归的输出副本；测试图片与直线由 PowerPoint COM 设置，未经过用户手工绘线；此红灯样本是一条水平线。之后已测两种翻转斜线及保存冷重开，但 Word 安装后 UI 导出和画布手绘仍未验证。

### 修复后回归证据（2026-09-24）

- 源码修复：选线归属取自 `line.Parent`（其父对象即幻灯片）；端点方向读取 `Shape.HorizontalFlip` / `Shape.VerticalFlip`。`office-com-addin/build.ps1` 的 net40 Release 构建成功，0 warnings、0 errors。
- 业务回归版本：面板标注场景用的候选与实际加载文件 SHA-256 均为 `E6F43BFD4EFC720AD85C56862A45641AB500DDAAEB93B0AA37105EC5AFB8FE05`。run `26634ed58381454d986e620aae86918f` 是修复版冷启动；`ae932fa13c1a47409e968031fe2504ad` 是保存产物后的冷重开。两次 `OnConnection`、`OnAddInsUpdate`、`OnStartupComplete` 均为 PASS。
- 同场景红绿：run `6702a05008c9439b9aa9c6dc72c83438` 在原生直线选择有效、面板按钮已按下的情况下记录 `FlipH` 读取 FAIL；run `26634ed58381454d986e620aae86918f` 用同一已安装面板操作标注原生水平线，记录 `annotation.create PASS number=2;slide_id=256`。这证明错误穿过安装、冷启动、UI 面板和 PowerPoint COM 调用后被实际复现并修复，不是单测结果。
- 翻转方向：同一 run 中，`VerticalFlip=True` 的斜线创建编号 1 组成功；PowerPoint 对象模型随后读到线条 Left=80、Top=230、Width=140、Height=70、VerticalFlip=-1，编号文字框左上角为 48、292.6351，符合起点 (80,300)。反向斜线 `HorizontalFlip=-1` 在第二页的面板标注也记录 PASS，SlideID=257。
- 整份统计：活动页为第 2 页时，编号 2 只在第 1 页，编号 1 曾在两页各出现一次；面板显示全稿无漏标，日志 `marking.check PASS missing=0;marked=2`。临时删除两个编号 1 产品组后，保留第 2 页无 Tags 的普通直线，面板报告“漏标 1 项：1”，日志 `missing=1;marked=1`；恢复第 2 页产品组后再次 `missing=0;marked=2`。同号重复组不导致错误或漏标。
- 输入防护：未选字典编号时面板提示“请先选择一个编号”；日志记录 `annotation.create FAIL InvalidOperationException`，当时测试稿形状数未变、直线仍被单独选中。重新从列表选编号后恢复成功。
- 保存与冷重开：在面板完成保存前检查后，用 PowerPoint `Ctrl+S` 保存。COM 核对两页、编号 1/2 各一个有效组、普通线无 Tags、`Saved=True`；文件 159586 字节、SHA-256 `EAC077925EAB6D1F1C6076AA8A918D1A37BBC38C73F3A0190F290F12779C1BCE`。关闭 PowerPoint 后确认进程/窗口消失，以全新进程 run `ae932fa13c1a47409e968031fe2504ad` 重开该产物，COM 读回两组 Tags，面板恢复相对字典关联、读取 2 项；面板再查返回全稿无漏标，日志 `missing=0;marked=2`。
- 字典保护：测试字典 387 字节，SHA-256 始终为 `CF262EDAA81D4442FC58186107AD909185D5FD1CAB75AE98435884DE0DD4DC77`；标注、保存、冷重开期间未变化。测试 PPT 与字典主名不同，且目录中存在 DWG 占位文件，绑定依靠 PPT 内保存的相对关联而不是按名称猜测。
- 加载路径诊断补强：随后只改连接日志，新增当前程序集的 `Assembly.Location`，标注逻辑未改。最终候选与已安装 DLL SHA-256 均为 `817505CDCDE97D20DEA8C9A7BB70B790283F0304D15136B2A601E72750472B28`；冷启动 run `4c5f2db795ef4585bf7f3c39e2a13cc1` 的 `OnConnection` 同行记有 `assembly=%LOCALAPPDATA%\PatentMarker\OfficeAddin\PowerPoint\PatentOffice.PowerPoint.dll`，随后三个生命周期阶段 PASS。进程外对该日志路径所指文件计算的哈希与候选一致。该最终诊断版重新打开保存产物，记录 `binding.resolve PASS`、`dict.read PASS`，并经真实面板检查 `marking.check PASS missing=0;marked=2`。
- 层级限制：这是已安装产品、真实 PowerPoint 冷进程、真实面板按钮与重开文件上的宿主黑盒子链通过；测试线条由 PowerPoint COM 准备和选中，字典来自 Word 源码导入 L2 回归。未覆盖实际 Word 安装版 UI 导出及画布手绘/鼠标选线，因此完整 L3 用户路径仍待验。

### 延续实机复核：键盘形状库与无效线型拒绝（2026-09-24）

- 用户操作中途可能影响窗口状态；本次每次输入后重新读取目标窗口，没有把单次误触视为环境整体不可用。`@oai/sky` 能重新枚举并读取 PowerPoint 与加载项面板；面板仍能通过键盘执行检查。
- 在活动测试稿中，键盘访问键 `Alt+N`、`S`、`H` 打开 PowerPoint 形状库。键盘确认后画布出现 `直接箭头连接符 5`。加载项面板执行“标注所选直线”后明确拒绝，提示“选中的对象不是 PowerPoint 原生直线”；同一 run `4c5f2db795ef4585bf7f3c39e2a13cc1` 记录 `annotation.create FAIL InvalidOperationException:选中的对象不是 PowerPoint 原生直线。请先绘制直线，再选中它。`。这覆盖了真实面板对错误线型的防护，不算普通直线绘制通过。
- 用 PowerPoint `Ctrl+Z` 撤销本次临时形状后，面板重新检查显示全稿无漏标；保存的测试 PPTX SHA-256 仍为 `EAC077925EAB6D1F1C6076AA8A918D1A37BBC38C73F3A0190F290F12779C1BCE`，字典仍为 `CF262EDAA81D4442FC58186107AD909185D5FD1CAB75AE98435884DE0DD4DC77`。测试产物没有被这次探查改写。
- 画布截图请求仍返回 `SetIsBorderRequired failed: 不支持此接口 (0x80004002)`；对形状库可访问项尝试 UIA 索引点击时也返回 `coordinate input geometry is unavailable`。形状库可用键盘到达，但本次没能从库中可靠地激活普通 `直线` 并在图片上确定起止点。未使用猜测坐标。这个接口问题限制的是画布拖画证据，不能据此断言本机 PowerPoint、加载项或键盘交互整体不可用。
- Word 窗口在此次早期复核前已打开显示附图标记文本的 `文档1`。`Ctrl+N` 实际新建了独立空白 `文档2`（通过 `list_windows` 发现；简化的 `list_apps` 未列出它），本次只在 `文档2` 操作。`Alt+F8` 的宏列表可见 `PatentMarkerAddin.AutoExport.ShowPatentDictPanel`，证明产品入口在该 Word 会话中可发现；但 UIA 对列表项点击及可编辑宏名字段写入均返回 `element ... is not available in cached app state`，刷新窗口状态后重试仍相同，键盘输入也未改变当前宏选择，最后以 `Esc` 关闭对话框。此次早期尝试没有运行宏、输入正文、保存或执行导出；随后关闭了本轮创建的空白 `文档2`，原 `文档1` 仍在窗口列表中。后续安装版 Word UI 导出结果见下一节。这是宏对话框选择路径未完成，不代表 Word 或本机 Computer Use 整体不可用。

### 后续安装版 Word UI 导出与 Visio 联测（2026-09-24）

- 使用原 Word 会话中本轮创建的独立脱敏 `.docm` 测试文件；原 `文档1` 未操作。面板显示状态最初为“导出失败：没有可用输出路径”，但文件已保存。控件索引点击在重新选择原生窗口后仍报“控件不可用”；发送 Enter 后面板显示“已导出”，真实产品日志 run `20260924-151850-9FE7C2` 的 `export.start`、`export.target`、`export.success` 均为 PASS。
- 实际加载模板：`%APPDATA%\Microsoft\Word\STARTUP\PatentMarker.dotm`，SHA-256 `2F3E4C46DAAC61225648EA5905507EAAC6EBF602947F074C93CE2CEF91457BC8`；Word 16.0 x64，VBA 版本 1.0.3。实际输出 `office-com-addin/test-evidence/word-installed-export-20260924-bb620f8f/visio-word-export-test.dict.json`，403 字节、SHA-256 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479`；解析为 2 项、0 警告。
- 将该 Word UI 产物交给已安装 Visio DLL，run `11fcc1fd80624e6fb6ac292764381fb1` 在 Visio 16.0.20430.20092 x64 中完成绑定、标注、扫描及 VSDX 保存关闭重开；字典 SHA-256 前后相同。Visio 对象方法经反射调用，未经过 Visio 加载项面板。
- 此结果证明安装版 Word 面板导出及真实导出文件被 Visio 生产代码读取的宿主对象子链；Word 不是为本轮测试新启动的进程，Visio 部分也绕过了面板，因此不把组合结果定为 L3。完整记录见 [Word UI→Visio 联测证据](test-evidence/word-visio-real-export-20260924.md)。

## 完整 L3 用户路径剩余验收清单

历史 0.1.1.0 已将安装版 Word 面板导出的 2 项字典用于真实 PPT 面板；不同主名、同卷相对路径、真实 C:/F: 跨卷绝对路径及异目录 PPTX 副本冷重开、多文稿切换、字典副本丢失/损坏后的恢复、跨页漏标、误选图片与箭头连接符的拒绝均有实测。后续键盘回归还覆盖界面插入原生直线及键盘选线。完整鼠标用户路径和目标旧环境仍有以下缺口：

1. 在幻灯片画布上从编号文字位置向图片目标拖拽绘制普通直线，再用鼠标选中它；目前界面插线为默认尺寸，没有截图、坐标拖拽或视觉布局证据。
2. 用安装版 Word 面板覆盖“同目录单 DWG”和“多个 DWG 手选”导出规则，再分别交给 PPT 面板读取；当前真实 Word UI 字典只证明已导出产物可被 PPT 只读使用。
3. 在 Windows 7 + Office 2010 x86 目标环境实装、冷启动并重复核心用户场景；本机 PowerPoint 16 x64 结论不外推。

Win7 + Office 2010 x86 仍无目标环境证据；Visio 的本机限定结果见下节。本机 Windows 19045 的截图接口缺陷不阻断键盘驱动的 PowerPoint 面板操作；但不能以坐标猜测代替未取得的画布拖画证据。

## 独立 Word → Visio 加载项

Visio 已作为独立实现开始落地，未把 PowerPoint 的形状组、Tags 或 Custom XML 直接搬入 Visio。产品代码位于 office-com-addin/src/PatentOffice.Visio，构建及独立安装器入口位于 office-com-addin/；具体使用方式、对象结构与限制见 [Visio 原型说明](visio-prototype.md)。

首轮交互采用 Visio 原生一维线：用户从编号位置画到目标，选中线后绑定编号。代码保留所选线条与现有连接关系，只在终点写箭头；编号框和引线保留为两个对象，各自以 ShapeSheet User 单元格保存产品标识、编号、版本及配对 ID。文档绑定信息也存于当前 Visio 文档的 User 单元格。全稿检查遍历前景页，背景页不重复计数；同号多次标注合法，字典外编号和损坏标注单独报告。

采用此交互的依据最初来自官方对象模型文档及代码结构审查；当前顶层直线的标注和 VSDX 保存重开已由本机 Visio COM 宿主回归验证。Visio 的 Page.DrawLine 与原生直线工具等效；一维形状有 Begin/End 端点，Cell.GlueTo 用于在形状间建立连接；ShapeSheet 的 visSectionUser 用来存外部解决方案数据。[Page.DrawLine](https://learn.microsoft.com/en-us/office/vba/api/visio.page.drawline)、[Shape.OneD](https://learn.microsoft.com/en-us/office/vba/api/visio.shape.oned)、[Cell.GlueTo](https://learn.microsoft.com/en-us/office/vba/api/visio.cell.glueto)、[Shape.AddNamedRow](https://learn.microsoft.com/en-us/office/vba/api/visio.shape.addnamedrow)。COM 加载项是 Visio 支持的扩展类型；Visio 的 VSTO 加载项配置路径为 HKCU Software Microsoft Visio Addins。[Visio 扩展功能](https://learn.microsoft.com/en-us/office/vba/visio/concepts/about-extending-the-functionality-of-visio)、[VSTO 注册项](https://learn.microsoft.com/en-us/visualstudio/vsto/registry-entries-for-vsto-add-ins?view=visualstudio)。

### 0.1.1.0 历史候选与缺陷修复

首轮对象回归实测 Visio COM 对顶层一维直线的识别、箭头/编号形状生成、产品 ShapeSheet 标识扫描及 VSDX 保存关闭重开。该实测发现并修复 `ContainingShape` 缺陷：顶层形状会返回 PageSheet，旧判断将其误认作组内形状。安装旧 DLL 的真实宿主调用稳定失败，修复后同一场景转绿。之后又在 Word 16.0 x64 已安装加载项面板中对脱敏测试文档执行手动导出，并把实际生成的 `.dict.json` 提供给 Visio 宿主回归；字典解析出 2 项、0 警告，Visio 绑定/标注/扫描/保存重开 PASS，输入字典 SHA-256 不变。以下早期宿主回归通过 PowerShell 反射调用内部生产方法，尚未覆盖 Visio 面板。加载项不会自动创建或粘合引线；编号框未与引线建立 Visio 粘合，用户单独移动引线起点时编号框不会跟随。

首轮验证记录（2026-09-24）：执行身份 本机用户；Windows 10 build 19045 x64、Word/Visio 16.0 x64。Word 安装模板 `%APPDATA%\Microsoft\Word\STARTUP\PatentMarker.dotm` 的 run ID `20260924-151850-9FE7C2` 记录 `export.start/target/success` 均 PASS。脱敏测试文档为 `office-com-addin/test-evidence/word-installed-export-20260924-bb620f8f/visio-word-export-test.docm`；真实导出字典 SHA-256 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479`。首轮修复版 Visio 安装 DLL SHA-256 `96F782BE5C8A646EB1C5D947C226340F66F451095547344E0122F5201F1092A7`。Visio 红绿回归旧版红灯 run `d370ad5361644b15a41741ea958b43c7`，修复后 run `0cd3b71c498548e5ad5903358fdc1c20`；真实 Word 产物输入的 Visio 回归 run `11fcc1fd80624e6fb6ac292764381fb1`。详细操作和产物见 `test-evidence/visio-host-failure-contract-20260924.md` 及两个 Visio `result.json`。以下表格为 0.1.1.0 的历史状态，首轮哈希只作历史证据。

| 检查 | 结果 | 证据 |
|---|---|---|
| net40 Release 构建 | PASS，0 warning、0 error | office-com-addin/build-visio.ps1；程序集版本 0.1.1.0 |
| 构建候选与暂存副本 | PASS，SHA-256 相同 | 当前候选及从 ZIP 安装的 DLL SHA-256 为 `6D211D3F8F8A3884F0F0823452E8A234749AA05E602BF2E57D5C0BF16DD2830B` |
| 纯函数、字典解析与扫描规则 | PASS，14/14 | dotnet test office-com-addin/tests/PatentOffice.Visio.CodeTests，net48；新增跨页面残件误配与同路径重开面板状态红绿回归 |
| 安装器隔离回归 | PASS | 首装故障回滚、正常安装、升级故障回滚、重复安装、卸载；安装目录外文件哨兵与无关注册项哨兵保持不变 |
| PowerShell 脚本解析 | PASS | Visio 构建、模块、安装、卸载与验证脚本均解析成功 |
| 安装及注册 | PASS | `%LOCALAPPDATA%\PatentMarker\OfficeAddin\Visio`；HKCU 64 位 `Software\Microsoft\Visio\Addins\PatentOffice.VisioAddIn` 的 `LoadBehavior=3`；COM CodeBase 指向已安装产品 DLL |
| 当前安装 DLL 哈希 | PASS | 候选、发布包及安装 DLL SHA-256 均为 `6D211D3F8F8A3884F0F0823452E8A234749AA05E602BF2E57D5C0BF16DD2830B` |
| 当前候选冷启动加载 | PASS | run ID `2b3aa0e4ffbd44f192816db7392f93b9`；`OnConnection`、`OnAddInsUpdate`、`OnStartupComplete` 均 PASS；宿主 16.0 x64 |
| 顶层直线标注红绿回归 | FAIL → PASS | 旧已安装 DLL run `d370ad5361644b15a41741ea958b43c7` 错将 PageSheet 当组；同场景修复版 run `0cd3b71c498548e5ad5903358fdc1c20` 的绑定、标注、扫描和 VSDX 持久化均 PASS |
| 字典只读断言 | PASS | 本地 fixture 回归前后 SHA-256 均为 `E1DA5334883DFE774CBC836A435181DDF0C61DBE13A593EA196325B233EC9856`；真实 Word UI 导出字典回归前后均为 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479` |
| Word UI 导出字典 | PASS | Word run `20260924-151850-9FE7C2`；面板显示“已导出”，运行日志 `export.start/target/success` 均 PASS；2 个编号、0 警告 |
| Word 字典输入 Visio | PASS（面板绕过） | Visio run `11fcc1fd80624e6fb6ac292764381fb1`；绑定、标注、扫描、VSDX 保存关闭重开 PASS |
| 当前候选 Word 字典输入 Visio | PASS（面板绕过） | run `cc7cbf5dc484497f87166077d4203d6b`；绑定、标注、扫描、VSDX 保存关闭重开 PASS，字典 SHA-256 前后相同；重开后面板日志 `binding.resolve PASS`、`dict.read PASS` |
| 发布 ZIP | PASS | `PatentMarker-WordVisio-0.1.1.0-20260924-214136.zip`，SHA-256 `5EAB82B7FB5C5FBC4EBAFB4E63B6723B3C437EA93FD4600FA0B0B0DF9E5CFD7C`；精确文件清单与逐文件哈希通过 |
| Visio 面板找回与选择框入口 | PASS | 面板可独立枚举、关闭后最小化并重新激活；当前候选由面板按钮打开字典文件对话框 |
| Visio 完整面板标注与画布选线 | SKIP/BLOCKED | 文件对话框不在 Computer Use 可选窗口列表，文件选择和标注按钮尚未完成；有效线条由 COM 准备并选中 |

此表只说明 0.1.1.0 的阶段性状态；其“面板未通过、多页/多文档待验”已被后续 0.1.2.0 证据更新。[0.1.1.0 历史验收](test-evidence/visio-release-validation-20260924.md)仍保留故障和红绿追踪。

### 0.1.2.0 历史候选验收

0.1.2.0 为面板增加完整路径输入和“绑定此路径”按钮，与文件选择框共用校验和绑定逻辑。已从 ZIP 解压安装；安装 DLL SHA-256 `C7470AD9E7D5B641816EBA679B30EDC35EB4E0AAD3C9427021E7C996D5AD4542`。本机 Visio 16.0 x64 首次冷启动 run `66ff12cd06ac469cb49c9970939a6912`；在真实面板输入安装版 Word 导出的两项字典，按 Enter 绑定，标注第一页编号 1 并检查到编号 2 漏标。保存退出后 run `676878316b924164811834b8b5d67492` 自动恢复绑定；第二前景页用 Visio 画布键盘选线，再由面板标注编号 2，全稿检查为 `marked=2;missing=0`。再次保存退出、冷启动 run `59660613f3b34a7bbd5999d23414c8da` 重开同一两页 VSDX 后，面板检查结果仍相同。

另用第二文档验证绑定隔离，字典丢失与真实损坏后保留上次有效列表、禁用操作，恢复文件后自动重新读取；原 Word 字典 SHA-256 始终为 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479`。空选择、普通矩形、同一引线重复绑定由已安装程序集的真实 Visio COM 对象回归拒绝。net40 Release 构建 0 warning、0 error，代码测试 14/14，隔离安装器首装、重复安装、故障回滚、卸载和非产品哨兵保护通过。具体测试包、日志 run ID、产物路径及限制见 [0.1.2.0 历史验收](test-evidence/visio-release-validation-20260924-v012.md)。

画线仍由 Visio COM 准备，第一页线由 COM 选中；面板绑定、标注、检查及重开是真实 UI 操作。此历史候选当时达到本机面板与持久化限定 L3 子链，但快速切换文档时仍有下节的竞态。

### 0.1.3.0 历史发布候选

代码复核发现：面板缓存 A 图字典后、两秒定时刷新之前切到 B 图，立即点“检查整份文档”会继续输出 A 图的漏标结果。同一真实 Visio 宿主对象/面板处理器回归下，0.1.2.0 稳定 `FAIL`；0.1.3.0 在标注和检查按钮入口先同步刷新活动文档，识别切换后取消本次操作并要求重新选择，转为 `PASS`。状态读取异常时也暂停缓存字典操作。这个脚本直接调用面板处理器，只记 L2；详见 [同场景红绿记录](test-evidence/visio-release-validation-20260925-v013.md)。

0.1.3.0 net40 Release 构建 0 warning、0 error，14/14 代码测试，隔离安装器回滚、重复安装、卸载和非产品哨兵保护均通过；Windows PowerShell 5.1 的封包和安装入口也通过。唯一当前 发布 ZIP（本地原始产物未公开） SHA-256 `EF21913F5D35EB534B74916EA815D765BA67DA6021B0A1C1CB07CDF2405DF6D5`，从包内脚本覆盖安装，实际 DLL SHA-256 `1A807EA80EC79C8DCF1F78816487B88DC2098F3EBC192F8AB8B2DAF9094F8AEC`。全新 Visio 16 x64 进程 run `4bb253aecae24fa7b036454dcb448ced` 经真实面板路径绑定安装版 Word UI 字典、标注编号 1、检查到编号 2 漏标；保存关闭后再冷启动 run `96121bcb91dc48b083fe4e00b1739dff`，重新打开 VSDX，面板自动恢复字典并得同一漏标结果。字典 SHA-256 前后不变，测试进程已关闭。这两次 UI 运行使用同 DLL 的初版 0.1.3.0 包；最终 ZIP 更新了安装脚本和 README，经 PowerShell 5.1 实际安装、冷启动 run `f46443d3cc4f41539cb466d82e8e2190` 与面板重开检查，结果仍为漏标编号 2。差异与证据见发布记录。

当前可发布范围限定为本机 Visio 16 x64 的面板与持久化 L3 子链；线条由 COM 预先创建。鼠标拖画、目标粘合、撤销、复杂组、旧 `.vsd`、Office 2010 x86、Windows 7 和其他 Visio 位数未覆盖，不写成已支持。

## 外部文档与本地源码依据

| 依据                                                                                                                                                                                                  | 支持的判断                                                            |
| --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------- |
| [Presentation.CustomXMLParts](https://learn.microsoft.com/en-us/office/vba/api/powerpoint.presentation.customxmlparts)                                                                              | 演示文稿对象提供关联的 Custom XML 部件集合                                      |
| [Shape.Tags](https://learn.microsoft.com/en-us/office/vba/api/powerpoint.shape.tags)                                                                                                                | 形状 Tags 可持久标识对象；Tag 值按大写文本保存                                     |
| [ShapeRange.Group](https://learn.microsoft.com/en-us/office/vba/api/powerpoint.shaperange.group)、[Shapes.AddTextbox](https://learn.microsoft.com/en-us/office/vba/api/powerpoint.shapes.addtextbox) | 可把原生形状组合，并以点为单位创建文字框                                             |
| [LineFormat.EndArrowheadStyle](https://learn.microsoft.com/en-us/office/vba/api/powerpoint.lineformat.endarrowheadstyle)                                                                            | PowerPoint 线条末端箭头属性可读写                                           |
| [Shape.HorizontalFlip](https://learn.microsoft.com/en-us/office/vba/api/powerpoint.shape.horizontalflip)、[Shape.VerticalFlip](https://learn.microsoft.com/en-us/office/vba/api/powerpoint.shape.verticalflip) | 直线方向端点由只读翻转属性判定；运行时对象也已确认返回值 |
| [Visio Windows.Add](https://learn.microsoft.com/en-us/office/vba/api/visio.windows.add)、[VisWinTypes](https://learn.microsoft.com/en-us/office/vba/api/visio.viswintypes)                           | Visio `Window.Windows.Add` 锚栏示例及窗口类型常量                           |
| [.NET Framework 版本与依赖](https://learn.microsoft.com/en-us/dotnet/framework/install/versions-and-dependencies)                                                                                        | CLR 2.0 与 CLR 4 的版本关系及 .NET 4 进程内并行宿主说明                          |
| [.NET Framework Release 值检查](https://learn.microsoft.com/en-us/dotnet/framework/install/how-to-determine-which-versions-are-installed)                                                              | Release `533325` 在适用系统上代表 .NET Framework 4.8.1                   |
| [Office COM Add-in 注册](https://learn.microsoft.com/en-us/previous-versions/office/troubleshoot/office-developer/office-com-add-in-using-visual-c)                                                   | `IDTExtensibility2`、HKCU Office Addins 与典型 `LoadBehavior=3` 注册模式 |
| `vba/AutoExport.bas`                                                                                                                                                                                | 当前 Word 导出目录、DWG 目标选择、记忆选择及 `.dict.json` 文件名规则                   |
| `cad-plugin/Shared/IO/NumberIdentity.cs`                                                                                                                                                            | 编号 Trim 与大小写不敏感比较规则；也是本原型唯一链接的 CAD 源文件                           |

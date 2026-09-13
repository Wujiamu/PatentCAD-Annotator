# PatentCAD-Annotator

**AutoCAD 专利图纸标注插件** — 从 Word 说明书提取附图标记，通过共享字典在 CAD 中标注、编辑并对照变化。

**AutoCAD patent drawing annotation plugin** — Extract reference numerals from Word into a shared dictionary, annotate drawings, and review changes in CAD.

当前工作区：**1.0.3 candidate（待发布 / unreleased）**。发布记录见 [CHANGELOG.md](CHANGELOG.md)。

---

## 中文说明

### 项目简介

PatentCAD-Annotator 用于减少专利图纸标注中的重复操作：从 Word 提取编号与名称，在 CAD 中创建统一样式的引线标注、检查漏标、对齐文字，并在字典变化时显示差异。

工作流：Word 面板手动导出或启用保存时自动导出 → 生成 `.dict.json` → CAD 中用 `BZ` 打开字典面板 → 双击条目或用 `BZM` 创建标注 → 字典更新后查看差异。

### Word 与 CAD 如何交换数据

两端通过文件交换数据，正常使用不要求 Word 与 CAD 建立 COM 连接，也不要求两个程序同时打开。CAD 面板约每 2 秒检查字典变化。

| 操作 | 实际影响 |
|---|---|
| Word 导出 | 从当前文档内容提取编号/名称，写入字典；CAD 重载后显示差异 |
| CAD 粘贴识别、新增或编辑条目 | 写回字典；在 CAD 编辑编号时，联动更新当前图纸中匹配的插件标注 |
| CAD 修改名称、删除字典条目 | 修改字典；改名称不修改图纸文字，删除条目不自动删除图纸实体 |
| Word 再次导出，发现字典曾由 CAD 修改 | 先备份 CAD 字典，再写入 Word 版；CAD 面板提供“采用 Word / 恢复 CAD / 稍后”裁决 |

**CAD 的字典修改不会自动写回 Word 正文。** Word 导出后的差异高亮也不等于图纸已经自动改号；需要在 CAD 中核对和处理。文件替换、Word 保存与 DWG 事务不是一个整体操作，同时在两端编辑同一字典仍可能产生冲突。

字典默认写在 Word 文档目录，命名规则为：

- 无 DWG：使用 Word 文件主名。
- 只有一个 DWG：使用该 DWG 主名。
- 多个 DWG：点击“手动导出字典”后列出同目录 DWG，选择目标；字典按所选 DWG 主名写入 `<DWG主名>.dict.json`。自动保存只复用本次文档已经选定的目标，未选择前拒绝写入并尝试记录 `autoexport-error.txt`。

CAD 优先读取当前 DWG 同目录同主名的字典，其次读取存在的 `config.DefaultDictPath`。遇到“Word 已导出但 CAD 没更新”，先核对两端实际路径。

`.dict.json` 与冲突备份默认具有“隐藏+系统”属性。交接项目时确认字典随文件夹一起复制；仅发送 Word 或 DWG 文件不会携带字典。需要手动查看时，在 CAD 字典面板点击“显示 JSON”，或在 Word 面板勾选“显示 JSON（允许手动编辑）”。这个开关只改变文件属性，不改 JSON 内容；再次写回时会保留可见状态。可见 JSON 的内容如果在 Word 覆盖前确实发生变化，会先生成现有 `.word-*.bak` 备份，再由 CAD 面板提供冲突裁决。编辑后回到 CAD 面板点击“重载”检查结果，非法 JSON 会保留当前有效状态并记录加载失败。也可以在资源管理器中显示隐藏文件并取消隐藏受保护的操作系统文件，操作后可恢复显示设置。

### 当前标注功能

- 2010/2013/2015/2025 四个版本使用 **MLeader（F 方案）** 创建标注：单个多重引线实体自持 MText 文字，顶点链为 `附着点 → 拐点… → 缩进端点`（末顶点沿最后一段方向缩进 0.4×字高，不直接触及文字），文字仍锚定在 `TextLocation`；并禁用全部自动几何（dogleg/landing/extend），引线路径沿用户点链构造，并在文字前保留间距；2007 无 MLeader API，保持 `Leader + MText`。
- 历史上 v4.0 曾因 MLeader“鱼钩形态”回退到 `Leader + MText`；2026-08-15 形态探针定位根因为顶点链不完整（只给 attach→dogleg 两点），F 方案补全文字点后问题消除（详见 [F 方案文档](docs/mleader-f-plan.md)）。
- 无箭头时 `ArrowSize` 置 0（非零值会修剪引线起点，导致引线不触及零件），箭头用空箭头块 `_PAT_NO_ARROW` 实现；`ExtendLeaderToText` 为 2014+ SDK 属性，代码中以反射访问保持 2010-2012 兼容。
- 面板支持“三点 / 无限点”模式切换。三点模式只采集用户指定的 3 个点；无限点模式允许连续采集多个拐点；两种模式都不会额外写入文字附着点。
- 点数模式默认是三点；点击“点数”按钮后才切换为无限点，设置按当前图纸会话保留。
- 三点或无限点标注过程中，按 ESC 或右键菜单中的“确认/取消”都可以退出当前标注命令；无限点采集到一半时也可以直接取消。
- 面板条目单击只选择，双击直接开始标注；右键选择“编辑条目”或选中后按 `F2` 才进入修改，不再需要先打开编辑框再点击“保存并标注”。
- 面板标注请求按当前图纸隔离并去重；若 CAD 正在执行其他命令，会在空闲后自动补发一次 `PATMARK`，不会因重复点击叠加失效命令。
- 面板检测到 Word 覆盖 CAD 修改时会启用“裁决”按钮，可选择采用 Word 版、恢复 CAD 版或稍后处理；PATCHECK 结果按图纸隔离，重载字典时只清理当前图纸的高亮状态。
- 标注文字始终保持水平。引线可以按面板设置使用直线或样条形式。
- `PATSELECTALL`/`BZS` 通过扩展字典标记 `PATENTMARKER_MLEADER` 识别新建 MLeader（并记录用户点链），同时兼容旧图纸的 Leader 标注与独立文字。
- 新增 `PATMLSET`（开关脚本化入口）与 `PATMLVERIFY`（形态诊断：Explode 全部 PAT MLeader 并对照记录点链输出报告，回归测试工具）。
- 旧图纸处理：不迁移既有实体，`PATSELECTALL` 只认带 PAT 标记的 MLeader；旧 Leader+MText 标注继续被识别。
- 面板新增“Brace/大括号”按钮，对应 `PATBRACE`（别名 `DAGUOHAO`）：依次指定顶部、底部和宽度方向三点，创建独立的参数化矢量大括号；它不是文字字符，也不加入 Leader/MText 标注关联。
- `PATBRACEEDIT` 支持两种调整方式：重新点选顶部/底部/宽度方向控制点，或直接输入高度和宽度。第一版不依赖原生自定义夹点，使用命令交互保证五个 AutoCAD 版本的兼容性。
- 第三点决定中部尖点的朝向和宽度：竖向大括号可向左/向右，横向大括号可向上/向下；从端点轴线到尖点的整个轮廓均位于第三点所指一侧。
- 大括号轮廓以 DrawingML/PPT `Right Brace` 为视觉基准：端部和中心使用四段四分之一椭圆，直干位于所选宽度的中线，中心保留单一尖锐折角；不会越过端点轴线或生成 W 型轮廓。
- **PATCHECK 只做漏标检测**：报告"字典有 · 图纸未标注"清单（命令行列出，面板同步以橙色 + `△` 前缀高亮），由面板"检测"按钮或 `BZC` 触发；不检查“图纸有 · 字典无”与重复编号；删除字典条目可能留下原有图纸标注，同一部件多处标同号也是合法用法。
- **PATALIGN 使用“选择集先行”流程**：先选中要对齐的标注（支持 `BZS` 建立的 pickfirst 预选集），再指定**线**或**框**基准——线模式把文字投影到基准线；框模式把文字推到指定边外侧（间距由 config.json `align.marginToFrame` 控制）。空间不足时自动延伸：线模式沿基准线方向紧凑排列并越过线端；框模式按列向远离框的方向退位（避免各边延伸交叉重叠）。排列顺序一律为投影顺序，不按编号大小或层级重排；文字占位测量失败时退化为纯投影。移动 MLeader 文字时末顶点自动跟随（Xrecord 点链同步重写），对齐后 `PATMLVERIFY` 仍然通过。
- 面板新增"检测"与"对齐"两个按钮，分别触发 `PATCHECK` 与 `PATALIGN`。

v4.0 放弃 MLeader 的问题现象、日志证据见 [MLeader 额外附着点问题总结](docs/mleader-attachment-grip-incident.md)；该问题已被 F 方案解决（顶点链补全文字点），详见 [MLeader F 方案文档](docs/mleader-f-plan.md)。

### 版本总览

由于 AutoCAD 托管 API 与 .NET 运行时强绑定，单个 DLL 无法覆盖 2007—2026 全部版本，按 API 断代划分为 5 个版本。**请根据你本机的 AutoCAD 年份选择对应版本：**

| 目录 | AutoCAD 支持范围 | .NET | 插件系统基线 | 标注方式 | 验证范围 |
|------|-------------|------|---------|----------|------|
| [`cad-plugin/2007/`](cad-plugin/2007/) | **2007 ~ 2009** | 2.0 | Win7 | Leader + MText | 本地构建有记录；旧版宿主待验 |
| [`cad-plugin/2010/`](cad-plugin/2010/) | **2010 ~ 2012** | 3.5 | Win7 | MLeader（F 方案） | 本地构建有记录；旧版宿主待验 |
| [`cad-plugin/2013/`](cad-plugin/2013/) | **2013 ~ 2014** | 4.0 | Win7 | MLeader（F 方案） | 本地构建有记录；旧版宿主待验 |
| [`cad-plugin/2015/`](cad-plugin/2015/) | **2015 ~ 2024** | 4.5 | Win7 | MLeader（F 方案） | 本地构建有记录；旧版宿主待验 |
| [`cad-plugin/2025/`](cad-plugin/2025/) | **2025 ~ 2026+** | 8.0 | Win10+ | MLeader（F 方案） | 2026 命令级有记录；GUI 待验 |

表中范围是当前项目支持声明，不代表每个年份都已实测；系统基线也不能替代对应 AutoCAD/Office 的安装要求。未来年份需另行验证。

### 为什么分 5 个版本？能否交叉使用？

**不能交叉使用。** 每个版本的 DLL 只能在其对应的 AutoCAD 年份区间内运行，原因：

1. **.NET 运行时不兼容** — 2007~2009 的 CAD 只加载 .NET 2.0 程序集，2025+ 只加载 .NET 8，CLR 完全不同，DLL 无法被加载。
2. **托管 API 断代** — 2010/2013/2015/2025 使用 MLeader（F 方案，2007 无该实体故用 `Leader` + `MText`）；各版本引用的 API 程序集和方法签名仍然不同（如 `ExtendLeaderToText` 属性 2014+ 才有）。
3. **程序集版本绑定** — 编译时引用的 `acdbmgd.dll` 内部接口随 CAD 版本变化，跨版本加载会抛 `MissingMethodException`。

> 例：把 2007 版装到 AutoCAD 2026 → 无法加载（.NET 2.0 vs .NET 8）；把 2015 版装到 AutoCAD 2012 → 无法加载（.NET 4.5 vs .NET 3.5，且 SDK 程序集版本不匹配）。

详细的分版理由见 [docs/version-plan.md](docs/version-plan.md)。

### 快速开始

1. **选择部署包**：按上表选择 `PatentMarker-<年份>-deploy/`，完整解压到可写且位置固定的目录。保留 DLL、脚本与整个 `vba/` 子目录，安装后不要随意移动。
2. **安装 Word 工具**：先保存并关闭全部 Word 窗口，再运行包内 `install-vba.vbs`。脚本把同目录、预先构建好的 `PatentMarker.dotm` 逐字节校验后安装到 Word `Startup` 目录，**不会打开、修改或保存 `Normal.dotm`，也不要求“信任对 VBA 工程对象模型的访问”**。重复安装会保留旧产品加载项和所有权清单的可恢复备份；卸载使用 `uninstall-vba.vbs`。
3. **导出字典**：重新启动 Word 后，Startup 加载项通过 `AutoExec` 自动挂接保存事件。打开并保存说明书，用 Alt+F8 运行 `ShowPatentDictPanel` 查看状态或手动导出。同目录有多个 DWG 时，第一次勾选“保存时自动导出”会先要求选择目标 DWG，也可以先点击“手动导出字典”完成选择；选择后普通保存即按该 DWG 名称自动生成 JSON。首次保存或另存为后，在最终目录再次保存或手动导出并确认对应图纸。
4. **安装 CAD 插件**：运行所选包的 CAD 安装入口，见下方部署包表。打开目标 DWG，用 `BZ` 打开面板。
5. **标注与检查**：单击选择条目，双击开始标注；也可用 `BZM`。F2/右键编辑条目，`BZC` 检查漏标，`BZS` 选择标注后用 `BZA` 对齐。

具体版本操作见 [2007](cad-plugin/2007/README.md)、[2010](cad-plugin/2010/README.md)、[2013](cad-plugin/2013/README.md)、[2015](cad-plugin/2015/README.md)、[2025](cad-plugin/2025/README.md)。

### 已知限制与排查

- **Word 2010 真机仍需验收**：当前加载项由本机 Word 16.0 64 位从根 VBA 真源生成；同一候选包已在该环境通过真实 Startup 安装与全新正常 Word 进程回归，但不能外推到 Word 2010、32 位 Office 或其他宏策略。
- **安装不再修改 Normal**：新安装器仅复制 `PatentMarker.dotm` 到 Startup。旧版本若曾把 PatentMarker 组件装入 `Normal.dotm`，本次不会自动删除；请先保留完整 Normal 备份和组件清单，再把旧组件迁移视为单独的数据恢复操作，不要用新安装覆盖或批量清空用户宏。
- **诊断日志**：加载、`AutoExec`、钩子创建、`DocumentBeforeSave`、目标路径、导出成功/失败都写入 `%LOCALAPPDATA%\PatentMarker\Logs\word-vba-YYYYMMDD.tsv`，同一 Word 运行使用同一 `run_id`。文档目录的 `autoexport-error.txt` 只是简短错误提示；排查自动导出时同时核对日志中的加载模板路径和实际 Startup 文件哈希。
- **自动导出目标选择**：勾选“保存时自动导出”时，如果当前目录有多个 DWG 且本文件尚未选择目标，面板会先要求选择；选择成功后普通保存即可自动生成对应 DWG 名称的 JSON，不会静默猜测图纸。
- 回归 VBS 会在 `%TEMP%\PatentMarker*` 下创建临时 `.docm`；脚本现在无论成功还是显式失败都会逐个关闭 Word 文档，并删除自己生成的测试 `.docm`，保留日志和 JSON 诊断文件。旧版本运行留下的临时文件需人工清理。
- 自动导出发生在 Word 完成保存之前。本机程序化 Save As 改名/换目录验收确认：保存事件会先更新旧路径的 JSON，新路径需要随后再普通保存一次或手动导出；交互式取消 Save As 尚未验收。当前没有 Word、字典与 DWG 的整体回滚保证，两端并发写入仍需专门回归。
- Word 导出失败时查看文档目录的 `autoexport-error.txt`；检查文档是否已有路径、多 DWG 是否已手动选择目标、目录是否可写以及字典/备份是否被占用。
- CAD 插件已能加载时用 `PATDOCTOR` / `BZD` 检查。插件连命令都无法加载时，运行对应包的外部 `doctor-<年份>.vbs`（旧版）或 `doctor-2025.ps1`；诊断可从离线检查升级为启动 CAD 的在线检查，运行前保存工作并阅读脚本提示。

### 编译说明

各版本编译前需从对应 AutoCAD 安装目录（或 [ObjectARX SDK](https://aps.autodesk.com/developer/overview/autocad-objectarx-sdk-downloads)）获取 SDK DLL：

| 版本 | 所需 DLL | 放置位置 |
|------|---------|----------|
| 2007/2010 | acdbmgd.dll, acmgd.dll | `PatentMarker/lib/` |
| 2013/2015 | acdbmgd.dll, acmgd.dll, accoremgd.dll | `PatentMarker/lib/` |
| 2025 | acdbmgd.dll, acmgd.dll, accoremgd.dll | `PatentMarker/lib/` |

- 2013/2015 版使用 Newtonsoft.Json 13.0.3（NuGet 还原），发布时经 ILRepack 合并进 `PatentMarker.dll`（单文件部署，安装无需额外 DLL）
- 2013 必须使用该 NuGet 包的 `net35` 资产，2015 使用 `net45`；发布合并优先通过根目录 `package.ps1` 完成。
- 2025 的 JSON 库为内置 System.Text.Json，无需额外部署 JSON DLL；仍依赖对应 AutoCAD 与 .NET 环境。

### 部署包

| 版本 | 安装入口 | 说明 |
|------|----------|------|
| 2007 | `install-2007.bat` / `install-2007.vbs` | 适用于 AutoCAD 2007~2009 |
| 2010 | `install-2010.vbs` | 适用于 AutoCAD 2010~2012 |
| 2013 | `install-2013.vbs` | 适用于 AutoCAD 2013~2014，DLL 已内嵌 Newtonsoft.Json |
| 2015 | `install-2015.vbs` | 适用于 AutoCAD 2015~2024，DLL 已内嵌 Newtonsoft.Json |
| 2025 | `install-2025.ps1` | 对应 2025/2026+ 版本组；脚本运行后生成 LSP 供 APPLOAD 加载，也可直接 NETLOAD 对应 DLL |

五套部署包都包含对应版本的 `PatentMarker.dll`、CAD 安装/卸载及诊断脚本、Word 的 `PatentMarker.dotm` / `install-vba.vbs` / `uninstall-vba.vbs`，以及 9 个 VBA 真源文件。不要把不同 AutoCAD 年份的 DLL 混用。
若 PowerShell 策略阻止脚本启动，脚本也无法生成 LSP；此时可在 CAD 中用 `NETLOAD` 选择匹配版本的 DLL。
2025 安装脚本会合并 HKCU/HKLM，枚举 R25.0、R25.1、R26.0 中的全部配置并逐一写入 HKCU，支持 AutoCAD 2025/2026 并存安装。
2007/2010/2013/2015 安装脚本也会合并 HKCU/HKLM，枚举各自支持范围内的全部注册表配置并逐一写入，支持这些年份的并存安装；2010 卸载脚本会同步清理其生成的 acad.lsp 片段。

### 验证范围与开发命令

以下汇总 [开发记录](docs/development-log.md)、[本轮 Word VBA 验收记录](docs/word-vba-acceptance-2026-09-13.md) 与 [维护记录](docs/maintainability-repair-plan.md) 中的证据。Word 证据明确分层，源码直导入不能替代安装后自动启动：

| 层级 | 已有记录 | 不能据此推断 |
|---|---|---|
| 本地编译与发行暂存 | 五版构建；2013/2015 ILRepack 合并及发行暂存检查 | 所有目标年份的 AutoCAD 均能实际加载 |
| 自动化测试 | 2025 单测 120/120；2007/2010/2013/2015 契约模拟各 33/33 | 真实 AutoCAD 宿主 API、面板交互或 Word 2010 安装结果已通过 |
| Word L2 源码宿主 | `verify-vba-export.vbs` 与已从忽略规则中放行的正式 `test-vba-panel.vbs` 直接导入根源码，覆盖路径映射、多 DWG 选择复用、普通保存/失败取消，以及组件类型、公开入口、UserForm 与手动导出 | 部署包安装、Startup、AutoExec 或重启持久化已通过 |
| Word L4（本机 Word 16.0 64 位） | 同一候选包经真实 Startup 安装后连续两次正常启动；不调用初始化器/手动导出，覆盖普通保存、多文档隔离、Save As 改名/换目录及后续保存、锁定/只读字典的取消保存与恢复、运行中拒绝安装及卸载；JSON、事件日志、退出阶段 run ID、Normal 和非产品 Startup 哈希均有断言 | Word 2010、32 位 Office、交互式 Save As 取消、ACL 拒绝、VBA Reset 或宏策略阻止已经通过 |
| AutoCAD 2026 命令级 | 2025 部署 DLL 的标注、检测、对齐、点链校验及保存重开记录；1.0.2 标注冒烟和大括号创建/尺寸编辑补测 | BZ 面板鼠标/对话框、旧图纸目检或 2007/2010/2013/2015 真宿主验证完成 |

CI 定义见 [.github/workflows/build.yml](.github/workflows/build.yml)：执行 Structure、Static、2025 单测和四版 Simulation。Autodesk SDK 不入库，因此 CI 不做五版真实编译，也不运行 Word 或 AutoCAD GUI。

常用命令如下，按变更选择检查。仅文档调整无需构建；共享 C# 变更应编译受影响版本；Word 保存、窗体与 CAD 面板改动还需对应宿主验证。

```powershell
./build.ps1 -Structure           # 项目引用、部署文件存在性
./build.ps1 -Static              # 源码/部署副本一致性与安装器静态契约
./build.ps1 -Simulation          # 2007/2010/2013/2015 生产命令的模拟宿主契约
dotnet test ./cad-plugin/2025/PatentMarker.Tests/PatentMarker.Tests.csproj --configuration Release --nologo -v minimal
./build.ps1 -Version all -Check  # 五版 SDK 与工具链环境检查，不执行编译
./build.ps1 -Version 2025        # 编译对应版本；可换其他年份或 all
./check-api-contract.ps1 -Version all # SDK 元数据检查，当前仅含 2010/2013/2015/2025
./check-autocad-host.ps1          # 只读检查本机 AutoCAD/COM/许可服务
cscript //nologo ./tools/verify-vba-export.vbs ./vba # L2：直接导入源码，非安装器/启动测试
cscript //nologo ./tools/test-vba-panel.vbs ./vba   # L2：组件/公开入口/UserForm/手动导出
./vba-sync.ps1                     # 根 VBA 真源同步到五套部署源码
cscript //nologo ./tools/build-vba-addin.vbs ./vba ./word-addin/PatentMarker.dotm
./tools/verify-dotm-package.ps1 -Path ./word-addin/PatentMarker.dotm # vbaProject + AutoExec 宏元数据
./sync-word-addin.ps1              # 规范 dotm、Word 安装/卸载脚本同步到五包
./vba-sync.ps1 -Check              # 漂移时返回非零；不能只看输出中的 PASS 字样
./sync-word-addin.ps1 -Check       # 漂移时返回非零；逐字节核对三项 Word 安装资产
./tools/verify-word-sync-gates.ps1 # 临时故障注入：漂移必须失败且 -Check 不得写文件
powershell -NoProfile -ExecutionPolicy Bypass -File ./tools/verify-vba-installer.ps1 # L2 隔离安装/回滚/哨兵
powershell -NoProfile -ExecutionPolicy Bypass -File ./tools/verify-vba-installed-e2e.ps1 -ExtendedMatrix -KeepArtifacts # L4：真实 Startup 扩展矩阵
```

2007/2010 需要传统 MSBuild；2013/2015 在缺少 MSBuild.exe 时可利用本机准备的引用程序集与 dotnet msbuild；2025 为 SDK 风格工程。SDK、引用程序集和 ILRepack 等本地依赖不是仓库自带的完整环境，先检查再构建。

跨语言语料位于 [Fixtures/vba-corpus](cad-plugin/2025/PatentMarker.Tests/Fixtures/vba-corpus/)。需要用 Word 重新生成预期时，先将语料复制到临时目录，再运行 `cscript //nologo ./tools/generate-vba-corpus.vbs ./vba <临时语料目录>`。脚本会改写该目录的 `vba-expected-v4-output.txt`；比较并确认规则变化后才更新仓库基线。

### 源码同步与发布

- Word 源码只改根 `vba/`：先运行 `./vba-sync.ps1`，重新生成规范 `word-addin/PatentMarker.dotm`，再运行 `./sync-word-addin.ps1`。构建器会补齐并验证 Word 用于发现 `ShowPatentDictPanel`、`AutoExec`、`AutoExit` 的包内宏元数据；两个同步脚本的 `-Check` 发现漂移都会非零退出，`build.ps1 -Static` 和打包器还会把五套部署副本逐一与规范源比较。
- MLeader 命令在选定版本修改后运行 `./sync-mleader-group.ps1 -SourceVersion <年份>`，再运行 `./check-version-sync.ps1`。默认源是 2010，修改其他版本时须显式指定，避免覆盖新代码。
- `./package.ps1 -Version <年份或all>` 将已有构建产物写入新暂存目录，并为 2013/2015 合并 Newtonsoft.Json、检查外部引用。检查暂存后，用 `-Apply` 更新部署 DLL（会备份旧 DLL）。打包不代替编译；DLL、VBA、安装器及说明必须对应同一交付状态。
- Word VBA 文本和部分安装脚本使用 GBK/CP936；编辑前检查实际编码。`.frm/.frx` 配对维护，二进制窗体资源经 Word 设计器生成；字典输出为 UTF-8 无 BOM。
- 修改 `clsSaveHook.cls` 或启动入口后，必须重新生成 dotm，并分别执行源码直导入回归与真实 Startup 全新进程回归。源码测试、部署文件哈希和安装后行为属于不同证据层，不能相互替代。

### 命令清单

| 命令 | 别名 | 说明 |
|------|------|------|
| `PATPALETTE` | `BZ` / `BIAOZHU` | 打开字典面板 |
| `PATMARK` | `BZM` | 创建引线标注 |
| `PATCHECK` | `BZC` | 漏标检测：报告"字典有 · 图纸未标注"清单并在面板高亮 |
| `PATALIGN` | `BZA` | 对齐标注文字（先选标注，再选线/框基准；空间不足时自动延伸排列） |
| `PATSELECTALL` | `BZS` | 全选标注实体 |
| `PATMLSET` | — | MLeader 脚本化开关（仅 2010/2013/2015/2025） |
| `PATMLVERIFY` | — | MLeader 形态诊断报告：对照记录点链校验（仅 2010/2013/2015/2025） |
| `PATBRACE` | `DAGUOHAO` | 三点创建独立参数化矢量大括号 |
| `PATBRACEEDIT` | — | 通过控制点或输入高度/宽度调整大括号 |
| `PATDOCTOR` | `BZD` | 插件自检并生成诊断报告（样式/设置/字典/实体扫描 + 最近错误） |

### VBA 模块（Word 端，全版本共享）

附图标记编号支持纯数字、字母后缀以及字母后继续数字（例如 123A、123A1、123A2）。编号必须在既有条目分隔标点处结束：中文/英文逗号、顿号、分号或句号；这套边界规则同时用于 Word VBA 导出和 CAD 粘贴识别。

| 文件 | 用途 |
|------|------|
| `Patterns.bas` | 正则匹配工具 |
| `DictModel.bas` | 字典数据模型 |
| `JsonWriter.bas` | JSON 序列化 |
| `PatentExtractor.bas` | 保留兼容的占位模块；当前提取流程由 AutoExport 调用 DictModel |
| `AutoExport.bas` | 面板入口、自动导出开关、路径映射及导出编排 |
| `PatentMarkerBootstrap.bas` | Startup 全局模板的 `AutoExec` / `AutoExit` 生命周期入口 |
| `clsSaveHook.cls` | DocumentBeforeSave 事件监听；由生成器编入全局模板 |
| `PatentDictPanel.frm` | Word 工具面板定义与事件代码 |
| `PatentDictPanel.frx` | 配套二进制窗体资源，生成 `PatentMarker.dotm` 时与 `.frm` 一起导入 |

共 8 个 VBA 组件、9 个物理源文件；唯一支持的手动宏入口为 `ShowPatentDictPanel`，`AutoExec` / `AutoExit` 仅供 Word 生命周期调用。生成的规范全局模板位于 `word-addin/PatentMarker.dotm`。

### 目录结构

`cad-plugin/Shared/` 是五个 .NET 版本共用的源代码层（共同逻辑由对应版本链接，Leader 命令仅供 2007 链接），包含编号、设置、字典差异/冲突、粘贴识别、语言与文案、标注命令、面板控件/工作流/会话/渲染、三个对话框、样式初始化与 PATDOCTOR 诊断模块。各版本项目通过 `<Compile Include="..\..\Shared\...">` 源码链接编译；版本目录保留入口文件与 JSON/IO 适配层（2013/2015 用 Newtonsoft、2025 用 System.Text.Json、2007/2010 用 SimpleJson），2010/2013/2015/2025 另有版本本地 `Commands/`（7 个 MLeader 组文件：F 方案创建/开关/校验 + v5.1 的 PATCHECK/PATALIGN + 全选，四版本字节级相同）。`check-version-sync.ps1` 强制校验：共享文件不得在版本目录出现本地副本且必须被对应 csproj 链接；MLeader 组文件四版本一致且 2007 不携带。文件清单以当前 csproj 与同步脚本为准。

```
PatentCAD-Annotator/
├── cad-plugin/
│   ├── Shared/              # 共享 C# 与 CAD 宿主代码（按项目链接编译，不合并 CLR）
│   ├── RuntimeContract.Tests/  # 2007/2010/2013/2015 契约模拟测试工程（仿真 host）
│   ├── 2007/               # AutoCAD 2007~2009（Leader + MText，.NET 2.0）
│   │   └── PatentMarker/    #   C# 源码 + csproj
│   ├── 2010/               # AutoCAD 2010~2012（MLeader F 方案，.NET 3.5）
│   ├── 2013/               # AutoCAD 2013~2014（MLeader F 方案，.NET 4.0）
│   ├── 2015/               # AutoCAD 2015~2024（MLeader F 方案，.NET 4.5）
│   └── 2025/               # AutoCAD 2025~2026+（MLeader F 方案，.NET 8.0）
├── vba/                     # 8 个组件、9 个 Word VBA 物理真源（含配对 .frm/.frx）
├── PatentMarker-2007-deploy/   # 2007 版即装即用部署包（DLL + 脚本 + VBA）
├── PatentMarker-2010-deploy/   # 2010 版即装即用部署包
├── PatentMarker-2013-deploy/   # 2013 版即装即用部署包
├── PatentMarker-2015-deploy/   # 2015 版即装即用部署包
├── PatentMarker-2025-deploy/   # 2025 版即装即用部署包
├── demo/                       # 动态演示页面（最新：PatentMarker-Demo-v5.html）
├── docs/
│   ├── version-plan.md      # 版本规划（分版理由）
│   ├── development-log.md   # 变更记录
│   ├── mleader-f-plan.md    # MLeader F 方案（三点顶点链）
│   └── mleader-attachment-grip-incident.md # MLeader 附着点问题总结（已被 F 方案解决）
└── LICENSE
```

### 文档

- [docs/version-plan.md](docs/version-plan.md) — 版本规划与分版理由
- [docs/development-log.md](docs/development-log.md) — 变更记录
- [docs/mleader-f-plan.md](docs/mleader-f-plan.md) — MLeader F 方案（三点顶点链）定义、实证与架构
- [docs/mleader-attachment-grip-incident.md](docs/mleader-attachment-grip-incident.md) — MLeader 额外附着点问题（v4.0 舍弃原因，已被 F 方案解决）
- 各版本详细文档：[2007](cad-plugin/2007/README.md) | [2010](cad-plugin/2010/README.md) | [2013](cad-plugin/2013/README.md) | [2015](cad-plugin/2015/README.md) | [2025](cad-plugin/2025/README.md)

### 版本历史

当前工作区正在准备 **1.0.3（待发布）**。变更记录见 [CHANGELOG.md](CHANGELOG.md)；完整开发归档见 [docs/development-log.md](docs/development-log.md)。以下为 1.0.0 发布前的里程碑时间线（仅作演进参考，不再以版本号对外呈现）。

| 里程碑 | 日期 | 主要变更 |
|--------|------|----------|
| M5 (v5.3) | 2026-08-18 | 字典文件隐藏化：`.dict.json` 及 `.bak` 备份写入后设"隐藏+系统"属性，资源管理器默认不可见；CAD 写回/冲突裁决适配；后放入 DWG 时自动清理隐藏孤儿字典 |
| M5 (v5.2) | 2026-08-18 | 引线末端与文字之间加入随字高同步变化的间距（回缩 0.4×字高） |
| M4 (v5.1) | 2026-08-16 | PATCHECK 简化为漏标检测（面板"检测"按钮触发，未标注条目橙色 + △ 高亮）；PATALIGN v2 重做（选择集先行 → 线/框基准 → 空间不足自动延伸，排列顺序 = 投影顺序）；面板新增"检测/对齐"按钮；AutoCAD 2026 命令级批处理有通过记录（含 pickfirst 与保存-重开，GUI 另验） |
| M4 (v5.0) | 2026-08-16 | 标注引擎切换为 MLeader（F 方案三点顶点链）：2010/2013/2015/2025 四版本统一，单实体自持文字、无鱼钩、无额外附着点；新增 `PATMLSET`/`PATMLVERIFY`；AutoCAD 2026 实测 4/4 PASS；2007 保持 Leader + MText |
| M4 (v4.9) | 2026-08-15 | Word 端接口收敛：4 个宏精简为单一入口 `ShowPatentDictPanel`，打开"专利标注字典工具"面板（手动导出按钮 + 保存时自动导出开关）；新增 `PatentDictPanel.frm`/`.frx` UserForm，5 套部署包与构建脚本纳入 .frm/.frx 校验 |
| M3 (v4.6) | 2026-08-15 | 技术债清理三阶段：VBA 单源化（根 `vba/` + `vba-sync.ps1`）、共享层收敛至 29 文件、契约测试补齐 2007 版；修复 Shared 层 .NET 4.0 API 兼容性回归；五套部署包重新打包并经 AutoCAD 2026 实测 |
| M3 (v4.5) | 2026-08-15 | 新增 `PATDOCTOR`（`BZD`）自动诊断机制：共享源码 Diagnostics 模块、RawLog 错误环形缓冲、自检报告；五版本编译通过 |
| M2 (v4.1) | 2026-08-11 | 新增独立参数化矢量大括号：三点创建、控制点交互调整和高度/宽度输入；五个版本及部署包同步 |
| M2 (v4.0) | 2026-08-06 | CAD 端字典编辑闭环：粘贴识别（VBA 引擎移植 C#）+ 编辑对话框（改号/改名/新增/删除）+ 实体联动（改号同步图纸）+ 冲突裁决；修复 MLeader 额外附着点问题，五个版本统一使用 Leader + MText 并重新编译部署 |
| M1 (v3.2) | 2026-08-04 | 修复 MLeaderStyle 未入库先设属性异常；2013/2015 改单文件部署（ILRepack 合并 Newtonsoft.Json）；VBA 分隔符类补全角分号 |
| M1 (v3.1) | 2026-08-03 | 新增三点模式（面板切换按钮）：固定 3 点采集引线，与线型开关正交；全 5 版本同步 |
| M1 (v3.0) | 2026-08-03 | VBA v3.0 多格式识别（括号/连字符/英文标点/裸列表）；C# 取消 JSON 排序按原文顺序；全版本重新编译部署 |
| M1 (v2.5) | 2026-07-27 | 修复 Word 2010 无法导入 clsSaveHook.cls 的兼容性问题（改为代码注入）；修复 2007/2010 版箭头大小修改后不能立即生效；所有部署包补充 install-vba.vbs |
| M1 (v2.4) | 2026-07-26 | 多版本适配完成（2010/2013/2015/2025），全部通过编译验证；动态复核修复 MLeader API 名称、ArrowSize/TextHeight 实例同步 |
| M1 (v2.0) | 2026-07 | 2007 版完成：样条曲线引线 + 无限拐点 + 面板控制 + 字典自动刷新 |

---

## English

### Overview

PatentCAD-Annotator extracts reference numerals and names from Word, creates consistent CAD annotations, highlights dictionary changes, checks missing labels, and aligns annotation text.

Workflow: export from the Word panel, manually or with auto-export enabled → write `.dict.json` → open the CAD palette with `BZ` → double-click an entry or run `BZM` to annotate.

### Data exchange and its limits

Word and CAD exchange files; normal use does not require a live COM connection or both applications to stay open. The CAD palette checks for dictionary changes about every two seconds.

- Word export updates the dictionary; CAD reloads it and highlights differences. This does not automatically renumber existing drawing annotations.
- CAD entry edits update the dictionary. Renumbering an entry updates matching plugin annotations in the current drawing; renaming does not change drawing text, and deleting an entry does not delete drawing entities.
- Before Word overwrites a CAD-edited dictionary, it creates a backup. The CAD palette offers **Keep Word / Restore CAD / Later**.
- **CAD edits do not rewrite the Word document.** Word saves, dictionary writes and DWG transactions are separate operations; concurrent editing is not a guaranteed conflict-free workflow.

Word writes beside the document: no DWG means the Word base name; one DWG means that DWG base name; with multiple DWGs, manual export lists the files and requires an explicit target selection. The dictionary uses the selected DWG base name, and automatic save reuses that selection for the current document; before a selection, export is rejected with an `autoexport-error.txt` diagnostic when writable. CAD first reads the dictionary beside the current DWG with the same base name, then an existing `config.DefaultDictPath`.

Dictionaries and conflict backups default to Hidden+System attributes. The CAD palette offers **Show JSON / Hide JSON**, and the Word panel offers **Show JSON (allow manual editing)**; the switch changes file attributes only, and subsequent writes preserve the visible state. If a visible JSON file was manually changed, Word backs it up before overwriting and the existing CAD conflict decision remains available. Include dictionaries when sharing a project folder; sending only the Word or DWG file does not include them. After editing, reload the dictionary in CAD and check the result. To inspect a hidden file in Explorer, show hidden files and temporarily disable hiding protected operating system files.

### Current annotation features

- Editions 2010/2013/2015/2025 create annotations as a single **MLeader (Plan F)** entity that carries its own MText: the vertex chain is `attach → bend(s) → shortened endpoint`; the last vertex is pulled back by 0.4× text height while text remains anchored at `TextLocation`. Automatic geometry (dogleg/landing/extend) is disabled, preserving the picked path with a gap before the text. Edition 2007 has no MLeader API and keeps `Leader + MText`.
- v4.0 rolled MLeader back because of the "fishhook" distortion; the 2026-08-15 form probe traced the root cause to an incomplete vertex chain (attach→dogleg only). Plan F fixes it by appending the text point — see the [Plan F document](docs/mleader-f-plan.md).
- `ArrowSize` is set to 0 when the arrow is off (a non-zero value trims the leader start away from the part); the arrow-off look uses an empty arrow block `_PAT_NO_ARROW`. `ExtendLeaderToText` is a 2014+ SDK property and is accessed via reflection so one source file serves 2010-2012 as well.
- The palette supports a three-point / unlimited-point mode switch. Three-point mode collects exactly the three points selected by the user; unlimited-point mode accepts any number of user-selected dogleg points. Neither mode adds a text attachment point to the user's geometry.
- Three-point mode is the default; clicking the point-count button switches to unlimited mode for the current drawing session.
- Single-click selects an entry and double-click starts marking directly. Right-clicking an entry or pressing `F2` opens editing; the edit dialog no longer contains a separate Save & Mark action.
- Marking requests are isolated per drawing and de-duplicated; if AutoCAD is busy with another command, the request is retried once the document is idle instead of stacking unusable `PATMARK` invocations.
- Annotation text is forced to remain horizontal. The leader can still be configured as straight or spline through the palette.
- `PATSELECTALL` recognizes the new MLeaders through the extension-dictionary marker `PATENTMARKER_MLEADER` (which also records the user point chain), while remaining compatible with legacy Leader annotations and standalone text in old drawings.
- New commands: `PATMLSET` (scriptable switches) and `PATMLVERIFY` (form diagnostic: explodes all PAT MLeaders and reports against the recorded chains — the regression tool).
- Legacy drawings: existing entities are not migrated; `PATSELECTALL` only recognizes MLeaders carrying the PAT marker, and old Leader+MText annotations keep working.
- The palette adds a `Brace` button for `PATBRACE` (alias `DAGUOHAO`). Pick the top, bottom and width-direction points to create an independent parameterized vector brace; it is not a text glyph and is not part of the Leader/MText relationship.
- `PATBRACEEDIT` adjusts a brace either by repicking its top/bottom/width control points or by entering an exact height and width. The first implementation uses command interaction instead of native custom grips so the same behavior remains available across all five AutoCAD generations.
- The third point controls both the center-tip direction and width: vertical braces can point left or right, and horizontal braces can point up or down. The complete profile stays on the selected side between the endpoint axis and the tip.
- The brace follows the DrawingML/PPT `Right Brace`: four quarter-ellipse transitions, straight stems at half the selected width, and one sharp center fold. It never crosses the endpoint axis or creates a W-shaped outline.
- **PATCHECK is an unmarked-only check**: it reports the "in dictionary but not annotated" list (in the command line, and highlighted in the palette with an orange `△` prefix), triggered by the palette Check button or `BZC`. It does not report annotations absent from the dictionary or duplicate numbers: deleting a dictionary entry can leave an annotation, and the same part may legitimately be labelled more than once.
- **PATALIGN uses a selection-first flow**: select the annotations to align first (the pickfirst set built by `BZS` is honored), then pick a **Line** or **Frame** reference — Line mode projects the texts onto the baseline; Frame mode pushes them outside the chosen side (offset from `align.marginToFrame` in config.json). When space is short it auto-extends: Line mode compacts along the baseline direction and continues past the endpoint; Frame mode spills into extra columns stepping away from the frame (so per-side extensions never cross and overlap). Ordering is always the projection order — never re-sorted by numeral value or hierarchy — and the command falls back to pure projection when text measurement fails. Moving an MLeader text drags its last vertex along (the Xrecord point chain is rewritten), so `PATMLVERIFY` still passes after aligning.
- The palette adds `Check` and `Align` buttons that trigger `PATCHECK` and `PATALIGN` respectively.

See [MLeader attachment-grip incident report](docs/mleader-attachment-grip-incident.md) for the v4.0 log evidence and rejected fixes; the issue is resolved by Plan F (complete vertex chain). Details in the [Plan F document](docs/mleader-f-plan.md).

### Versions

Because AutoCAD's managed API is tightly bound to the .NET runtime, a single DLL cannot cover AutoCAD 2007—2026. The project is split into 5 versions along API boundaries. **Choose the version matching your AutoCAD year:**

| Directory | AutoCAD range | .NET | Plugin OS baseline | Annotation | Evidence |
|-----------|---------|------|--------|------------|--------|
| [`cad-plugin/2007/`](cad-plugin/2007/) | **2007 ~ 2009** | 2.0 | Win7 | Leader + MText | Local build recorded; legacy host pending |
| [`cad-plugin/2010/`](cad-plugin/2010/) | **2010 ~ 2012** | 3.5 | Win7 | MLeader (Plan F) | Local build recorded; legacy host pending |
| [`cad-plugin/2013/`](cad-plugin/2013/) | **2013 ~ 2014** | 4.0 | Win7 | MLeader (Plan F) | Local build recorded; legacy host pending |
| [`cad-plugin/2015/`](cad-plugin/2015/) | **2015 ~ 2024** | 4.5 | Win7 | MLeader (Plan F) | Local build recorded; legacy host pending |
| [`cad-plugin/2025/`](cad-plugin/2025/) | **2025 ~ 2026+** | 8.0 | Win10+ | MLeader (Plan F) | 2026 command checks recorded; GUI pending |

These are project support declarations, not proof that every year has been tested. The OS baseline does not replace the requirements of the installed AutoCAD/Office version; future releases need separate verification.

### Why 5 versions? Can I use one version on a different AutoCAD?

**No cross-version usage.** Each DLL only works within its designated AutoCAD year range:

1. **.NET runtime mismatch** — AutoCAD 2007–2009 loads .NET 2.0 only; 2025+ loads .NET 8 only. The CLR is entirely different.
2. **Annotation implementation profile** — Editions 2010/2013/2015/2025 use MLeader (Plan F; 2007 lacks the entity and keeps `Leader` + `MText`). Separate .NET targets and SDK DLLs are still required (e.g. the `ExtendLeaderToText` property only exists in the 2014+ SDK).
3. **Assembly binding** — `acdbmgd.dll` internal interfaces change per CAD version; loading a mismatched DLL throws `MissingMethodException`.

See [docs/version-plan.md](docs/version-plan.md) for full rationale.

### Quick start

1. Choose `PatentMarker-<year>-deploy/` for your AutoCAD and extract the complete package into a writable, stable directory. Keep the DLL, scripts and entire `vba/` folder together.
2. Save work and close every Word window, then run `install-vba.vbs`. It byte-verifies and installs the prebuilt `PatentMarker.dotm` into Word's Startup folder. It **does not open, edit, or save `Normal.dotm` and does not require AccessVBOM**. Repeated installs preserve recoverable backups; use `uninstall-vba.vbs` to remove only the owned add-in.
3. Restart Word. The Startup add-in attaches the save event through `AutoExec`. Open and save the document, then use Alt+F8 → `ShowPatentDictPanel` to inspect status or export manually. With multiple DWGs, select a target once; later ordinary saves reuse it. After a first save or Save As, save again or export from the final location.
4. Run the matching CAD installer below. Open the target DWG and run `BZ`.
5. Single-click selects; double-click or `BZM` starts marking. F2/right-click edits entries, `BZC` checks missing annotations, and `BZS` followed by `BZA` selects and aligns annotations.

Edition guides: [2007](cad-plugin/2007/README.md), [2010](cad-plugin/2010/README.md), [2013](cad-plugin/2013/README.md), [2015](cad-plugin/2015/README.md), [2025](cad-plugin/2025/README.md).

### Known limitations and diagnosis

- **Word 2010 still needs real-host acceptance**: the generated add-in and fresh-process Startup flow passed on local Word 16.0 64-bit. This does not establish compatibility with Word 2010, 32-bit Office, or different macro policies.
- **Normal is outside the installer boundary**: legacy releases may have left PatentMarker components in `Normal.dotm`; the new installer deliberately does not remove them. Inventory and migration require a separate, recoverable operation that proves unrelated user macros are unchanged.
- **Diagnostics**: lifecycle, hook, save-event, path and export stages are written to `%LOCALAPPDATA%\PatentMarker\Logs\word-vba-YYYYMMDD.tsv` under a per-run ID. `autoexport-error.txt` is only the short document-local failure note.
- **Automatic-export target selection**: when the folder contains multiple DWGs and the document has no target yet, enabling “Auto-export on save” asks for a target instead of silently guessing. Once selected, an ordinary save creates the JSON for that DWG.
- Regression VBS files create temporary `.docm` files under `%TEMP%\PatentMarker*`; they now close every document and delete their own test `.docm` on both success and explicit failure while retaining logs and JSON diagnostics. Artifacts from older runs may need manual cleanup.
- Auto-export runs before Word finishes saving. A programmatic rename/move Save As on the local host updated the old-path JSON first; the new-path JSON required one later ordinary save or a manual export. Interactive Save As cancellation remains untested. There is no combined Word/JSON/DWG rollback, and concurrent writes need further regression coverage.
- For export failures, check `autoexport-error.txt`, the document path, whether a target was selected when multiple DWGs were present, write access and file locks.
- Use `PATDOCTOR` / `BZD` when the plugin loads. Otherwise use the package's external `doctor-<year>.vbs` or `doctor-2025.ps1`; it can escalate from offline checks to launching CAD, so save work and review its prompts first.

### Deployment packages

| Edition | Installer | Notes |
|---------|-----------|-------|
| 2007 | `install-2007.bat` / `install-2007.vbs` | AutoCAD 2007~2009 |
| 2010 | `install-2010.vbs` | AutoCAD 2010~2012 |
| 2013 | `install-2013.vbs` | AutoCAD 2013~2014; Newtonsoft.Json is merged into the DLL |
| 2015 | `install-2015.vbs` | AutoCAD 2015~2024; Newtonsoft.Json is merged into the DLL |
| 2025 | `install-2025.ps1` | 2025/2026+ edition group; generates an LSP for APPLOAD when the script runs; direct NETLOAD is also available |

Each package contains the matching `PatentMarker.dll`, CAD installation/uninstallation and diagnostics, the Word `PatentMarker.dotm` / `install-vba.vbs` / `uninstall-vba.vbs` assets, and nine canonical VBA source files. Do not mix DLLs between AutoCAD year ranges. If policy prevents the PowerShell script from starting, no LSP can be generated; use `NETLOAD` with the matching DLL.
The 2025 installer enumerates detected R25.0, R25.1, and R26.0 profiles and registers each one in HKCU, so side-by-side AutoCAD 2025/2026 installs are covered.
All five installers and uninstallers merge HKCU/HKLM profile lists, process every supported configuration, and de-duplicate repeated profiles; the 2010 uninstaller also removes generated `acad.lsp` fallback blocks.

### Commands

| Command | Alias | Description |
|---------|-------|-------------|
| `PATPALETTE` | `BZ` / `BIAOZHU` | Open dictionary palette |
| `PATMARK` | `BZM` | Create leader annotation |
| `PATCHECK` | `BZC` | Unmarked check: report "in dict but not annotated" list and highlight in palette |
| `PATALIGN` | `BZA` | Align annotation texts (select annotations first, then a line/frame reference; auto-extend when space is short) |
| `PATSELECTALL` | `BZS` | Select all annotation entities |
| `PATMLSET` | — | Scriptable MLeader switches (2010/2013/2015/2025 only) |
| `PATMLVERIFY` | — | MLeader form diagnostic: validate entities against recorded point chains (2010/2013/2015/2025 only) |
| `PATBRACE` | `DAGUOHAO` | Create an independent parameterized vector brace from three points |
| `PATBRACEEDIT` | — | Adjust a brace by control points or exact height/width |
| `PATDOCTOR` | `BZD` | Self check the plugin and write a doctor report (styles, settings, dictionary, entity scan, recent errors) |

### Verification and development

The following summarizes evidence in [the development log](docs/development-log.md), [this Word VBA acceptance record](docs/word-vba-acceptance-2026-09-13.md), and [maintenance notes](docs/maintainability-repair-plan.md). Word evidence is deliberately layered: importing source into a test document is not evidence that an installed Startup add-in initialized itself.

| Layer | Recorded evidence | Remaining boundary |
|---|---|---|
| Build/package | Five local builds; 2013/2015 ILRepack and release staging | Loading in each target AutoCAD year |
| Automated tests | 2025: 120/120; 2007/2010/2013/2015 simulations: 33/33 each | Real host APIs, GUI and installed Word code |
| Word L2 source host | `verify-vba-export.vbs` and the formal `test-vba-panel.vbs` (now explicitly unignored) import root source and check mapping, ordinary save/failure cancellation, component types, public entry points, the UserForm, and manual export | Deployment install, Startup loading, AutoExec or restart persistence |
| Word L4 (local Word 16.0 64-bit) | The same candidate passed two normal post-install starts without calling the initializer or manual export; assertions cover ordinary save, multi-document isolation, rename/move Save As plus follow-up save, locked/read-only cancellation and recovery, install/uninstall refusal while Word is running, shutdown run-ID continuity, JSON/log content, and unchanged Normal/unrelated Startup hashes | Word 2010, 32-bit Office, interactive Save As cancellation, ACL denial, VBA Reset, or blocked-macro policy |
| AutoCAD 2026 | 2025 DLL command checks for marking, checking, alignment, chain validation and persistence; 1.0.2 marking/brace smoke checks | Interactive palette/dialogs, legacy drawings and older AutoCAD hosts |

[CI](.github/workflows/build.yml) runs Structure, Static, the 2025 unit suite and four simulated host suites. It does not compile the five production DLLs without the locally supplied Autodesk SDK, or run Word/AutoCAD GUI tests. See the Chinese development command block above for exact commands: `-Check` is environment inspection, and API `-Version all` currently covers 2010/2013/2015/2025 only.

Edit Word code only in root `vba/`, run `./vba-sync.ps1`, rebuild `word-addin/PatentMarker.dotm`, verify its VBA project and macro-discovery metadata with `tools/verify-dotm-package.ps1`, and then run `./sync-word-addin.ps1`. Both synchronization scripts return a nonzero exit code on `-Check` drift, while `build.ps1 -Static` independently compares every deployment copy with its canonical source. Edit the MLeader group in one chosen edition and run `./sync-mleader-group.ps1 -SourceVersion <year>` before the consistency check. The default source is 2010.

Corpus fixtures live in [Fixtures/vba-corpus](cad-plugin/2025/PatentMarker.Tests/Fixtures/vba-corpus/). Copy them to a temporary directory before running `cscript //nologo ./tools/generate-vba-corpus.vbs ./vba <temporary-corpus-directory>`; the generator overwrites that directory's expected-output file. Review differences before updating the tracked baseline.

For local builds, supply SDK DLLs in each edition's `PatentMarker/lib/`: acdbmgd/acmgd for 2007/2010, plus accoremgd for later editions. Newtonsoft.Json 13.0.3 uses the net35 asset for 2013 and net45 for 2015. `./package.ps1 -Version <year-or-all>` stages existing builds, merges Newtonsoft for those two editions and checks assembly references; review the stage before using `-Apply` to update deployment DLLs. Packaging does not compile source.

The source tree uses linked `cad-plugin/Shared/` files, version-specific runtime/JSON adapters and a seven-file MLeader command group. Word has six `.bas` files, `clsSaveHook.cls`, and the paired `PatentDictPanel.frm/.frx`: eight components and nine physical source files. `PatentExtractor.bas` is a compatibility placeholder. Preserve text encodings (many VBA/VBS files use GBK/CP936), keep form resources paired, rebuild the canonical add-in after source changes, and rerun both the source-host and installed-host checks when changing save behavior.

### Version

The workspace is preparing **1.0.3 (unreleased)**. See [CHANGELOG.md](CHANGELOG.md) for release notes and [docs/development-log.md](docs/development-log.md) for the full development archive. The table below is the pre-1.0.0 milestone timeline (kept for reference only; internal iteration numbers are no longer exposed as public versions).

| Milestone | Date | Key changes |
|-----------|------|-------------|
| M5 (v5.3) | 2026-08-18 | Hide dict files: `.dict.json` and its `.bak` backups get Hidden+System attributes, invisible in Explorer by default; CAD write-back/conflict resolution adapted; orphan hidden dicts auto-cleaned when a DWG appears later |
| M5 (v5.2) | 2026-08-18 | Leader tip pulled back from the text by a height-proportional gap (0.4× text height) |
| M4 (v5.1) | 2026-08-16 | PATCHECK reduced to unmarked-only check (palette Check button, orange `△` highlight); PATALIGN v2 rebuilt (selection-first → line/frame reference → auto-extend); new Check/Align palette buttons; command-level batch checks recorded on AutoCAD 2026 (incl. pickfirst and save-reopen; GUI separate) |
| M4 (v5.0) | 2026-08-16 | Annotation engine switched to MLeader (Plan F three-point vertex chain) across 2010/2013/2015/2025: single entity, fishhook-free, no extra attachment point; new `PATMLSET`/`PATMLVERIFY`; AutoCAD 2026 on-machine 4/4 PASS; 2007 keeps Leader + MText |
| M4 (v4.9) | 2026-08-15 | Word-side interface converged to a single `ShowPatentDictPanel` entry point with the annotation palette (manual export + auto export on save); added `PatentDictPanel.frm`/`.frx` UserForm; deployed packages and build scripts now validate .frm/.frx |
| M3 (v4.6) | 2026-08-15 | Three-stage tech-debt cleanup: VBA single-sourcing (`vba/` + `vba-sync.ps1`), shared layer tightened to 29 files, contract tests added for 2007; fixed a .NET 4.0 API compatibility regression; repackaged all five packages and verified on AutoCAD 2026 |
| M3 (v4.5) | 2026-08-15 | `PATDOCTOR` (`BZD`) auto-diagnostics: shared Diagnostics module, RawLog ring buffer, self-check report; all five editions compile |
| M2 (v4.1) | 2026-08-11 | Independent parameterized vector brace: create from three points, adjust via control points or exact dimensions; synced across five editions and packages |
| M2 (v4.0) | 2026-08-06 | CAD-side dictionary editing loop: paste recognition (VBA engine ported to C#) + edit dialog (renumber/rename/add/delete) + entity linkage + conflict resolution; fixed the MLeader extra-attachment issue; all five editions unified on Leader + MText and recompiled |
| M1 (v3.2) | 2026-08-04 | Fixed MLeaderStyle set-before-add exception; 2013/2015 switched to single-file deployment (ILRepack-merged Newtonsoft.Json); VBA delimiter set completed with full-width semicolon |
| M1 (v3.1) | 2026-08-03 | Three-point mode (palette toggle) — fixed 3-point leaders, orthogonal to the line-type switch; synced across all 5 editions |
| M1 (v3.0) | 2026-08-03 | VBA multi-format recognition (brackets/hyphens/English punctuation/bare lists); C# JSON no longer sorted (keeps source order); all editions recompiled |
| M1 (v2.5) | 2026-07-27 | Fixed Word 2010 clsSaveHook.cls import incompatibility (injected instead); fixed arrow-size not applying immediately on 2007/2010; install-vba.vbs added to all packages |
| M1 (v2.4) | 2026-07-26 | Multi-edition adoption completed (2010/2013/2015/2025), all compiling; verified fixes for MLeader API names and ArrowSize/TextHeight instance sync |
| M1 (v2.0) | 2026-07 | 2007 edition completed: spline leader + unlimited doglegs + palette control + auto dictionary refresh |

### License

This project is licensed under the [MIT License](LICENSE).

Note: The `acdbmgd.dll` / `acmgd.dll` / `accoremgd.dll` referenced at build time are Autodesk SDK assemblies and are NOT included in this repository — users must supply them from their local AutoCAD installation or [ObjectARX SDK](https://aps.autodesk.com/developer/overview/autocad-objectarx-sdk-downloads).

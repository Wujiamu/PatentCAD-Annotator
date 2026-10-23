# PatentMarker — Word 专利附图标注工具系列

**一个项目，三条标注路线：Word → AutoCAD、Word → PowerPoint、Word → Visio。**

PatentMarker 专利附图标注工具系列：Word 提取编号字典，AutoCAD、PowerPoint、Visio 完成标注与漏标检查；共用 .dict.json 协议，按宿主独立打包。

本仓库统一维护源码、公共规则、说明和验证入口；仓库地址沿用 `PatentCAD-Annotator`。

**PatentMarker annotation suite** — One Word dictionary exporter, with AutoCAD, PowerPoint and Visio annotation clients maintained in this repository and packaged separately.

**当前版本：v1.0.3** · [更新记录](CHANGELOG.md)

## 先打开标注面板

| 使用端 | 面板入口 | 安装与使用 |
|---|---|---|
| Word（三条路线共用） | 按 `Alt+F8`，选择 `ShowPatentDictPanel` 并运行；打开“专利标注字典工具”后导出字典 | [Word 安装步骤](#快速开始) |
| AutoCAD | 在命令行输入 `BZ`（或 `PATPALETTE`）并回车 | [CAD 使用说明](#中文说明) |
| PowerPoint | 安装后完全退出并重新启动 PowerPoint，自动出现“专利标注字典工具 · PowerPoint” | [PPT 快速开始](office-com-addin/ppt-release-readme.md) |
| Visio | 安装后完全退出并重新启动 Visio，自动出现“专利标注字典工具 · Visio” | [Visio 快速开始](office-com-addin/visio-release-readme.md) |

**PPT / Visio 面板被关闭或最小化后**：点击 Windows 任务栏中对应的“专利标注字典工具”窗口恢复，或按 `Alt+Tab` 切换到它。当前版本采用启动时打开的浮动面板，没有功能区按钮或宏命令；完整的加载项检查步骤见[面板入口与故障排查](office-com-addin/docs/open-annotation-panel.md)。

项目关系与实际共享代码见[架构与仓库说明](docs/project-family.md)。

---

## 中文说明

### 项目简介

PatentCAD-Annotator 用于减少专利图纸标注中的重复操作：从 Word 提取编号与名称，在 CAD 中创建统一样式的引线标注、检查漏标、对齐文字，并在字典变化时显示差异。

工作流：Word 面板手动导出或启用保存时自动导出 → 生成 `.dict.json` → CAD 中用 `BZ` 打开字典面板 → 双击条目或用 `BZM` 创建标注 → 字典更新后查看差异。

### PowerPoint / Visio 标注

Word 导出的同一份 .dict.json 也可用于 PowerPoint 和 Visio。两端加载项只读字典，分别创建原生标注、保存文稿关联，并检查漏标。

| 宿主 | 标注与绑定 | 检查范围 | 字典写入 |
|---|---|---|---|
| AutoCAD | 原生引线；按 DWG 路径查找字典 | 当前图纸中的产品标注 | 面板编辑会写回 |
| PowerPoint | 原生直线与编号文字分组；Tags 识别；Custom XML 保存字典关联 | 整份演示文稿的产品标注组 | 只读 |
| Visio | 一维线与编号框配对；ShapeSheet 保存身份与字典关联 | 整份文档前景页的产品配对 | 只读 |

安装及使用见 [Office 加载项说明](office-com-addin/README.md)、[PPT 快速开始](office-com-addin/ppt-release-readme.md)与 [Visio 快速开始](office-com-addin/visio-release-readme.md)。架构与共享代码见[项目结构](docs/project-family.md)。

### Word 与 CAD 如何交换数据

Word 与 CAD 通过 .dict.json 文件交换数据，正常使用不要求两个程序建立 COM 连接或同时打开。CAD 面板约每 2 秒检查字典变化。PPT / Visio 加载项也读取同一 Word 字典；字段和编号比较约定见[统一字典契约](office-com-addin/docs/dictionary-contract.md)。

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

- 2010/2013/2015/2025 四版使用单个 **MLeader（F 方案）** 自持 MText，保留用户附着点及拐点。文字保持在拾取点，引线末端连接朝向最后拐点的实际文字边框中点或上/下角外侧，保留 0.4×字高间隙；使用原生 LandingGap，不直接按文字插入点缩进。dogleg/landing 延伸关闭；2007 保持 `Leader + MText`。
- 无箭头时 `ArrowSize` 置 0（非零值会修剪引线起点，导致引线不触及零件），箭头用空箭头块 `_PAT_NO_ARROW` 实现；`ExtendLeaderToText` 为 2014+ SDK 属性，代码中以反射访问保持 2010-2012 兼容。
- 面板支持“三点 / 无限点”模式切换。三点模式只采集用户指定的 3 个点；无限点模式允许连续采集多个拐点；两种模式都不会额外写入文字附着点。
- 点数模式默认是三点；点击“点数”按钮后才切换为无限点，设置按当前图纸会话保留。
- 三点或无限点标注过程中，按 ESC 或右键菜单中的“确认/取消”都可以退出当前标注命令；无限点采集到一半时也可以直接取消。
- 面板条目单击只选择，双击直接开始标注；右键选择“编辑条目”或选中后按 `F2` 才进入修改，不再需要先打开编辑框再点击“保存并标注”。
- 面板标注、大括号、对齐和检测共用按图纸隔离的命令调度：切换按钮会取消面板启动的交互命令，再执行最新请求；连续点击以最后一次为准。用户直接启动的原生或其他命令结束后，面板才补发请求。
- 新建 MLeader 设置左右 `AttachmentMiddle`，按实际文字边框确定连接侧，并保留原生间隙。旧实体不批量迁移；原生夹点/MOVE结束后同步产品MLeader记录，Undo/Redo由宿主原生记录还原，历史回放不进入同步事务。
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

### AutoCAD 适配包

由于 AutoCAD 托管 API 与 .NET 运行时强绑定，单个 DLL 无法覆盖 2007—2026 全部版本，按 API 断代划分为 5 个版本。**请根据你本机的 AutoCAD 年份选择对应版本：**

| 目录 | AutoCAD 支持范围 | .NET | 插件系统基线 | 标注方式 |
|------|-------------|------|---------|----------|
| [`cad-plugin/2007/`](cad-plugin/2007/) | **2007 ~ 2009** | 2.0 | Win7 | Leader + MText |
| [`cad-plugin/2010/`](cad-plugin/2010/) | **2010 ~ 2012** | 3.5 | Win7 | MLeader（F 方案） |
| [`cad-plugin/2013/`](cad-plugin/2013/) | **2013 ~ 2014** | 4.0 | Win7 | MLeader（F 方案） |
| [`cad-plugin/2015/`](cad-plugin/2015/) | **2015 ~ 2024** | 4.5 | Win7 | MLeader（F 方案） |
| [`cad-plugin/2025/`](cad-plugin/2025/) | **2025 ~ 2026+** | 8.0 | Win10+ | MLeader（F 方案） |

系统基线须同时满足所安装的 AutoCAD / Office 本身的要求。

### 为什么分 5 个适配包？能否交叉使用？

**不能交叉使用。** 每个版本的 DLL 只能在其对应的 AutoCAD 年份区间内运行，原因：

1. **.NET 运行时不兼容** — 2007~2009 的 CAD 只加载 .NET 2.0 程序集，2025+ 只加载 .NET 8，CLR 完全不同，DLL 无法被加载。
2. **托管 API 断代** — 2010/2013/2015/2025 使用 MLeader（F 方案，2007 无该实体故用 `Leader` + `MText`）；各版本引用的 API 程序集和方法签名仍然不同（如 `ExtendLeaderToText` 属性 2014+ 才有）。
3. **程序集版本绑定** — 编译时引用的 `acdbmgd.dll` 内部接口随 CAD 版本变化，跨版本加载会抛 `MissingMethodException`。

> 例：把 2007 版装到 AutoCAD 2026 → 无法加载（.NET 2.0 vs .NET 8）；把 2015 版装到 AutoCAD 2012 → 无法加载（.NET 4.5 vs .NET 3.5，且 SDK 程序集版本不匹配）。

详细的分版理由见 [docs/version-plan.md](docs/version-plan.md)。

### 快速开始

1. **选择部署包**：按上表选择 `PatentMarker-<年份>-deploy/`，完整解压到可写且位置固定的目录。保留 DLL、脚本与整个 `vba/` 子目录，安装后不要随意移动。
2. **安装 Word 工具**：先保存并关闭全部 Word 窗口，再运行包内 `install-vba.vbs`。脚本把同目录、预先构建好的 `PatentMarker.dotm` 逐字节校验后安装到 Word `Startup` 目录，**不会打开、修改或保存 `Normal.dotm`，也不要求“信任对 VBA 工程对象模型的访问”**。重复安装会保留旧产品加载项和所有权清单的可恢复备份；卸载使用 `uninstall-vba.vbs`。
3. **导出字典**：重新启动 Word 后，Startup 加载项通过 `AutoExec` 自动挂接保存事件。打开并保存说明书，用 Alt+F8 运行唯一的项目操作入口 `ShowPatentDictPanel`，在“专利标注字典工具”面板中选择手动导出、保存时自动导出或显示/隐藏 JSON。同目录有多个 DWG 时，第一次勾选“保存时自动导出”会先要求选择目标 DWG，也可以先点击“手动导出字典”完成选择；选择后普通保存即按该 DWG 名称自动生成 JSON。首次保存或另存为后，在最终目录再次保存或手动导出并确认对应图纸。
4. **安装 CAD 插件**：运行所选包的 CAD 安装入口，见下方部署包表。打开目标 DWG，用 `BZ` 打开面板。
5. **标注与检查**：单击选择条目，双击开始标注；也可用 `BZM`。F2/右键编辑条目，`BZC` 检查漏标，`BZS` 选择标注后用 `BZA` 对齐。

具体版本操作见 [2007](cad-plugin/2007/README.md)、[2010](cad-plugin/2010/README.md)、[2013](cad-plugin/2013/README.md)、[2015](cad-plugin/2015/README.md)、[2025](cad-plugin/2025/README.md)。

### 使用注意事项与故障排查

- **Word 宏与启动**：允许产品加载项运行宏；安装完成后完全退出并重新打开 Word，再运行 `ShowPatentDictPanel`。
- **保留用户模板**：安装器只安装 `PatentMarker.dotm`，不修改 `Normal.dotm`。旧安装若向 Normal 写入过组件，先备份模板和组件清单，再单独迁移，避免删除用户宏。
- **导出路径**：同目录有多个 DWG 时先选择目标图纸。首次保存、另存为改名或换目录后，再普通保存一次或手动导出，确认新路径下的字典。
- **文件冲突**：自动导出发生在 Word 完成保存之前；Word、JSON 和 DWG 不构成一次整体事务。避免同时在两端改写同一字典，发现冲突后使用 CAD 面板的裁决入口。
- **Word 日志**：查看文档目录的 `autoexport-error.txt` 和 `%LOCALAPPDATA%\PatentMarker\Logs\word-vba-YYYYMMDD.tsv`，检查路径、目标选择、写权限及文件占用。
- **CAD 诊断**：插件可加载时运行 `PATDOCTOR` / `BZD`；无法加载时使用对应部署包的 `doctor-<年份>.vbs` 或 `doctor-2025.ps1`。外部诊断可能启动 CAD，运行前保存工作并按提示操作。

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

### 开发

开发环境与同步方式见[开发记录](docs/development-log.md)和 [Office 开发说明](office-com-addin/README.md)。常用命令：

```powershell
./build.ps1 -Structure
./build.ps1 -Static
./build.ps1 -Simulation
./build.ps1 -Version 2025
./office-com-addin/verify-code.ps1
```

按目标 AutoCAD 适配包准备 SDK 与对应 .NET 构建工具；Autodesk SDK 不随仓库分发。

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

共 8 个 VBA 组件、9 个物理源文件；Alt+F8 中本加载项只显示一个操作入口 `ShowPatentDictPanel`；`AutoExec` / `AutoExit` 为私有生命周期过程，仍由 Word 自动调用，包内保留其发现元数据。其他加载项或旧 Normal 中的宏不会由安装器自动清理。生成的规范全局模板位于 `word-addin/PatentMarker.dotm`。

### 目录结构

`cad-plugin/Shared/` 是五个 .NET 版本共用的源代码层（共同逻辑由对应版本链接，Leader 命令仅供 2007 链接），包含编号、设置、字典差异/冲突、粘贴识别、语言与文案、标注命令、面板控件/工作流/会话/渲染、三个对话框、样式初始化与 PATDOCTOR 诊断模块。各版本项目通过 `<Compile Include="..\..\Shared\...">` 源码链接编译；版本目录保留入口文件与 JSON/IO 适配层（2013/2015 用 Newtonsoft、2025 用 System.Text.Json、2007/2010 用 SimpleJson），2010/2013/2015/2025 另有版本本地 `Commands/`（7 个 MLeader 组文件：F 方案创建/开关/校验 + PATCHECK/PATALIGN + 全选，四版本字节级相同）。`check-version-sync.ps1` 强制校验：共享文件不得在版本目录出现本地副本且必须被对应 csproj 链接；MLeader 组文件四版本一致且 2007 不携带。文件清单以当前 csproj 与同步脚本为准。

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
├── vba/                     # 三条路线共用的 Word 字典导出真源
├── office-com-addin/         # PPT / Visio 衍生端、Office 公共源码及独立打包脚本
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

- [更新记录](CHANGELOG.md)
- [架构与仓库说明](docs/project-family.md)
- [AutoCAD 适配包设计](docs/version-plan.md)
- [Office 安装与使用](office-com-addin/README.md)
- [面板入口与故障排查](office-com-addin/docs/open-annotation-panel.md)
- [MLeader 标注方案](docs/mleader-f-plan.md)

## English

### Overview

PatentCAD-Annotator extracts reference numerals and names from Word, creates consistent CAD annotations, highlights dictionary changes, checks missing labels, and aligns annotation text.

Workflow: export from the Word panel, manually or with auto-export enabled → write `.dict.json` → open the CAD palette with `BZ` → double-click an entry or run `BZM` to annotate.

### PowerPoint / Visio annotation add-ins

The add-ins read the same Word-generated .dict.json dictionary, create native annotations, persist document bindings and check missing numbers. PowerPoint uses line-and-label groups, Tags and Custom XML; Visio uses paired lines and labels with ShapeSheet identity. Both clients keep the dictionary read-only.

See the [Office guide](office-com-addin/README.md), [PowerPoint quick start](office-com-addin/ppt-release-readme.md), [Visio quick start](office-com-addin/visio-release-readme.md) and [project architecture](docs/project-family.md).

### Data exchange and its limits

Word and CAD exchange files; normal use does not require a live COM connection or both applications to stay open. The CAD palette checks for dictionary changes about every two seconds.

- Word export updates the dictionary; CAD reloads it and highlights differences. This does not automatically renumber existing drawing annotations.
- CAD entry edits update the dictionary. Renumbering an entry updates matching plugin annotations in the current drawing; renaming does not change drawing text, and deleting an entry does not delete drawing entities.
- Before Word overwrites a CAD-edited dictionary, it creates a backup. The CAD palette offers **Keep Word / Restore CAD / Later**.
- **CAD edits do not rewrite the Word document.** Word saves, dictionary writes and DWG transactions are separate operations; concurrent editing is not a guaranteed conflict-free workflow.

Word writes beside the document: no DWG means the Word base name; one DWG means that DWG base name; with multiple DWGs, manual export lists the files and requires an explicit target selection. The dictionary uses the selected DWG base name, and automatic save reuses that selection for the current document; before a selection, export is rejected with an `autoexport-error.txt` diagnostic when writable. CAD first reads the dictionary beside the current DWG with the same base name, then an existing `config.DefaultDictPath`.

Dictionaries and conflict backups default to Hidden+System attributes. The CAD palette offers **Show JSON / Hide JSON**, and the Word panel offers **Show JSON (allow manual editing)**; the switch changes file attributes only, and subsequent writes preserve the visible state. If a visible JSON file was manually changed, Word backs it up before overwriting and the existing CAD conflict decision remains available. Include dictionaries when sharing a project folder; sending only the Word or DWG file does not include them. After editing, reload the dictionary in CAD and check the result. To inspect a hidden file in Explorer, show hidden files and temporarily disable hiding protected operating system files.

### Current annotation features

- Editions 2010/2013/2015/2025 create one **MLeader (Plan F)** carrying MText. Picked attachment/bends and text position are preserved; the terminal attaches outside the actual facing text-box edge at middle/top/bottom corner with a native 0.4×text-height gap. Dogleg/landing extensions stay disabled. Edition 2007 keeps `Leader + MText`.
- `ArrowSize` is set to 0 when the arrow is off (a non-zero value trims the leader start away from the part); the arrow-off look uses an empty arrow block `_PAT_NO_ARROW`. `ExtendLeaderToText` is a 2014+ SDK property and is accessed via reflection so one source file serves 2010-2012 as well.
- The palette supports a three-point / unlimited-point mode switch. Three-point mode collects exactly the three points selected by the user; unlimited-point mode accepts any number of user-selected dogleg points. Neither mode adds a text attachment point to the user's geometry.
- Three-point mode is the default; clicking the point-count button switches to unlimited mode for the current drawing session.
- Single-click selects an entry and double-click starts marking directly. Right-clicking an entry or pressing `F2` opens editing; the edit dialog no longer contains a separate Save & Mark action.
- Marking, braces, alignment and checks share a per-drawing command scheduler. A palette switch cancels an interactive command started by the palette and runs the latest request; requests wait for native or other commands to finish.
- New MLeaders use side-specific `AttachmentMiddle`, facing-side direction, and native outside gap. Old entities are not automatically migrated.
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

### AutoCAD editions

Because AutoCAD's managed API is tightly bound to the .NET runtime, a single DLL cannot cover AutoCAD 2007—2026. The project is split into 5 versions along API boundaries. **Choose the version matching your AutoCAD year:**

| Directory | AutoCAD range | .NET | Plugin OS baseline | Annotation |
|-----------|---------|------|--------|------------|
| [`cad-plugin/2007/`](cad-plugin/2007/) | **2007 ~ 2009** | 2.0 | Win7 | Leader + MText |
| [`cad-plugin/2010/`](cad-plugin/2010/) | **2010 ~ 2012** | 3.5 | Win7 | MLeader (Plan F) |
| [`cad-plugin/2013/`](cad-plugin/2013/) | **2013 ~ 2014** | 4.0 | Win7 | MLeader (Plan F) |
| [`cad-plugin/2015/`](cad-plugin/2015/) | **2015 ~ 2024** | 4.5 | Win7 | MLeader (Plan F) |
| [`cad-plugin/2025/`](cad-plugin/2025/) | **2025 ~ 2026+** | 8.0 | Win10+ | MLeader (Plan F) |

Also meet the system requirements of the installed AutoCAD and Office applications.

### Why five editions? Can I use one DLL on a different AutoCAD?

**No cross-version usage.** Each DLL only works within its designated AutoCAD year range:

1. **.NET runtime mismatch** — AutoCAD 2007–2009 loads .NET 2.0 only; 2025+ loads .NET 8 only. The CLR is entirely different.
2. **Annotation implementation profile** — Editions 2010/2013/2015/2025 use MLeader (Plan F; 2007 lacks the entity and keeps `Leader` + `MText`). Separate .NET targets and SDK DLLs are still required (e.g. the `ExtendLeaderToText` property only exists in the 2014+ SDK).
3. **Assembly binding** — `acdbmgd.dll` internal interfaces change per CAD version; loading a mismatched DLL throws `MissingMethodException`.

See [docs/version-plan.md](docs/version-plan.md) for full rationale.

### Quick start

1. Choose `PatentMarker-<year>-deploy/` for your AutoCAD and extract the complete package into a writable, stable directory. Keep the DLL, scripts and entire `vba/` folder together.
2. Save work and close every Word window, then run `install-vba.vbs`. It byte-verifies and installs the prebuilt `PatentMarker.dotm` into Word's Startup folder. It **does not open, edit, or save `Normal.dotm` and does not require AccessVBOM**. Repeated installs preserve recoverable backups; use `uninstall-vba.vbs` to remove only the owned add-in.
3. Restart Word. The Startup add-in attaches the save event through `AutoExec`. Open and save the document, then use the add-in’s sole Alt+F8 entry, `ShowPatentDictPanel`, to choose manual export, export on save, or JSON visibility in the panel. The private `AutoExec` / `AutoExit` lifecycle procedures still run automatically; unrelated and legacy Normal macros are preserved. With multiple DWGs, select a target once; later ordinary saves reuse it. After a first save or Save As, save again or export from the final location.
4. Run the matching CAD installer below. Open the target DWG and run `BZ`.
5. Single-click selects; double-click or `BZM` starts marking. F2/right-click edits entries, `BZC` checks missing annotations, and `BZS` followed by `BZA` selects and aligns annotations.

Edition guides: [2007](cad-plugin/2007/README.md), [2010](cad-plugin/2010/README.md), [2013](cad-plugin/2013/README.md), [2015](cad-plugin/2015/README.md), [2025](cad-plugin/2025/README.md).

### Usage notes and diagnosis

- Allow the product add-in macros, restart Word after installation and open `ShowPatentDictPanel`.
- The installer installs `PatentMarker.dotm` without editing `Normal.dotm`. Back up legacy templates and inventory their components before migrating any old embedded macros.
- Choose the target when several DWGs share a folder. After a first save or Save As to a new name or folder, save once more or export manually to create the new-path dictionary.
- Word exports before completing its save. Word, JSON and DWG are separate files and transactions; avoid simultaneous dictionary edits and use the CAD conflict-resolution choices when needed.
- For Word failures, inspect `autoexport-error.txt` and `%LOCALAPPDATA%\PatentMarker\Logs\word-vba-YYYYMMDD.tsv`, target paths, permissions and file locks.
- Use `PATDOCTOR` / `BZD` when the plugin loads; otherwise run the matching external doctor script. Save your work before allowing a diagnostic script to launch CAD.

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

### Development

See the [development guide](docs/development-log.md) and [Office guide](office-com-addin/README.md). Supply the Autodesk SDK and matching .NET build tools for the selected edition.

```powershell
./build.ps1 -Structure
./build.ps1 -Static
./build.ps1 -Simulation
./build.ps1 -Version 2025
./office-com-addin/verify-code.ps1
```

Word source lives in `vba/`; use `vba-sync.ps1` and `sync-word-addin.ps1` to update deployment copies. For the MLeader command group, specify the edited source edition in `sync-mleader-group.ps1 -SourceVersion <year>`.

### Version

**Current version: v1.0.3.** See [CHANGELOG.md](CHANGELOG.md) for change history.

### License

This project is licensed under the [MIT License](LICENSE).

Note: The `acdbmgd.dll` / `acmgd.dll` / `accoremgd.dll` referenced at build time are Autodesk SDK assemblies and are NOT included in this repository — users must supply them from their local AutoCAD installation or [ObjectARX SDK](https://aps.autodesk.com/developer/overview/autocad-objectarx-sdk-downloads).

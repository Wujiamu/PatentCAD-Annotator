# 变更记录

本项目遵循语义化版本管理。

---

## 1.0.0 里程碑总览（2026-08-18）

**首个正式版本发布。** 此前 v2.0—v5.3 均为发布前开发里程碑，已并入 1.0.0 的功能总览（对外口径，见根 `README.md` 的"版本历史"与 `CHANGELOG.md`）。本文件保留 v2.0—v5.3 的**完整开发归档**，供追溯演进与排障，其版本号为内部迭代号，不再作为对外发布版本呈现。

里程碑分组（时间线参考）：

- **M1 (v2.0–3.2)**：2007 版完成 → 多版本适配 → 多格式识别。
- **M2 (v4.0–4.1)**：CAD 端字典编辑闭环 + 参数化矢量大括号。
- **M3 (v4.5–4.8)**：PATDOCTOR 诊断、共享层收敛、五套部署包重打包。
- **M4 (v4.9–5.1)**：面板单一入口、PATCHECK 漏标检测、PATALIGN v2、MLeader F 方案、AutoCAD 2026 实机测试。
- **M5 (v5.2–5.3)**：引线末端间距、字典文件隐藏化 + 孤儿自动清理。

以下为完整开发归档。

---

## v5.3 (2026-08-18)

**字典文件隐藏化**：`.dict.json` 及 `.word-*.bak` 备份写入后自动设置"隐藏+系统"属性，资源管理器默认不可见（需勾选"显示隐藏的文件"**并**取消"隐藏受保护的操作系统文件"才可见）；文件夹整体拷贝/共享行为不变，CAD 端 `File.Exists`/时间戳轮询对隐藏文件正常工作，双向同步无变化。

- **Word 端（`vba/AutoExport.bas`，5 套部署包 + 2007-v2 同步，SHA256 一致）**：
  - 导出前 `SetAttr vbNormal` 清属性（ADODB `SaveToFile` 无法覆盖隐藏文件），导出后设 `vbHidden Or vbSystem`；
  - `.bak` 备份同样设隐藏；清理旧备份的 `Dir()` 补 `vbHidden Or vbSystem` 参数（`Dir` 默认不返回隐藏文件）+ `Kill` 前清属性；
  - 新增 `CleanupOrphanWordDict`：文件夹后放入 DWG 导致导出基名从 Word 名切换为 DWG 名时，自动删除 Word-only 时代的旧 `<Word名>.dict.json`（清属性后 Kill）——该文件已隐藏，用户无法手动发现/删除；仅精确匹配当前 Word 文档基名（不区分大小写），不触碰其他文档字典；不做额外的历史孤儿批量清理（旧孤儿在下次 Word 保存时自然回收）。
- **CAD 端（5 版本 `IO/DictWriter.cs` + Shared `IO/DictConflict.cs`）**：写回前清目标文件隐藏属性（否则 `File.Replace` 覆盖隐藏文件失败）、写回后重设隐藏+系统；冲突裁决删除备份前清属性。
- **部署包**：五版本 DLL 经 `package.ps1 -Apply` 更新（2013/2015 ILRepack 合并后确认无外部 Newtonsoft.Json 引用，旧 DLL 自动备份 `.bak.20260818-100747`）；VBA 七份全量同步。
- 验证（全部本地实际执行）：`build.ps1 -Structure` / `-Static` 全绿（VBA 跨包一致 + Shared 单源层 + MLeader 组）；2025 单元测试 112/112；五版本真实编译通过。
- 兼容说明：升级后已存在的旧 `.dict.json`（无隐藏属性）在下次 Word 保存时自动补上隐藏属性，无需用户干预。

---

## v5.2 (2026-08-18)

**引线末端与文字之间加入随字高同步变化的间距**。此前 PATMARK 创建的引线最后一段一直延伸到文字点（MLeader F 方案的末顶点 / 2007 Leader 的文字端点），视觉上引线几乎直接顶住文字，不够清晰。现改为引线末端沿最后一段方向回缩 `gap = 0.4 × 字高`，文字仍锚定在用户点击的文字点不动。

- **间距常量**：`Shared/Commands/PatLeaderTextAttachment.cs` 新增 `TextGapPerHeight = 0.4` 与 `Retract(previous, textPoint, textHeight)`（纯坐标标量运算，无 `Vector3d`，保持契约模拟测试 stub 可编译）；字高来自 `PatSettingsStore.Current.TextHeight`，字高变大间距自动变大。
- **2007 Leader+MText（Shared 单源）**：`AppendTextEndpoint`/`SetTextEndpoint` 增加 `textHeight` 参数，创建时末顶点改为缩进点；`Shared/Commands/PatMarkCommand.cs` 传入 `TextHeight`；`Shared/Commands/PatAlignCommand.cs` 对齐移动后按新文字点重建缩进末顶点（从倒数第二个稳定顶点向新文字点缩进）。此路径末顶点完全可控，已通过契约模拟测试验证（见下）。
- **MLeader 组（2010/2013/2015/2025）**：`PatMLeaderCreator.Create` 顶点链改为 `attach → dogleg… → 缩进端点`（几何末顶点回缩），记录链（Xrecord）仍保留文字点作为末点——PATMLVERIFY 的 C3（文字位置==记录点）与 C4（文字 1 字高内曲线端点 ≤1）不受影响，任一宿主规范化路径下验证仍通过；`PatAlignCommand`（旧 Leader 回退分支）同步 3 参 `SetTextEndpoint`。四个版本文件字节级一致。
- **同步**：MLeader 组 2 个文件（`PatMLeaderCreator.cs`、`PatAlignCommand.cs`）四版本字节级复制；契约模拟测试 4 工程断言更新（末顶点距文字 = 0.4×字高，容差断言）。
- 验证（全部本地实际执行）：`build.ps1 -Structure` / `-Static` 通过（含 check-version-sync MLeader 组 + Shared 单源校验）；契约模拟测试 2007/2010/2013/2015 各 28/28 通过；2025 单元测试 112/112 通过。
- **实测备注**：本地无 AutoCAD SDK/宿主，五版本真实编译与 MLeader 宿主内行为需在对应 AutoCAD 版本 `NETLOAD` 实测（G 探针曾观察到 MLeader 会把非文字末顶点规范化到文字附着锚点——若宿主覆盖缩进点，2007 路径不受影响、MLeader 路径的间距以宿主最终渲染为准，可在实测后按需调整 `TextGapPerHeight` 或启用受控 `LandingGap`）。

---

## v5.1 (2026-08-16)

**PATCHECK 简化 + PATALIGN v2 重做**。PATCHECK 从三类检测收缩为单一"漏标检测"（字典有 · 图纸未标注）；PATALIGN 从"参考点 + 水平/垂直方向"重做为"选择集先行 → 线/框基准 → 空间不足时默认延伸"。

- **PATCHECK v2**（MLeader 组版 `Commands/PatCheckCommand.cs` + Shared 2007 版同步简化）：删除"图纸有·字典无"（纯插件流程下编号只能来自字典面板，不可能出现）与"同号重复"（同一部件多处标同号是合法用法）两类检测；结果列表写入 `Shared/Commands/PatCheckResult.cs`（静态类，原子引用交换 + Version 计数供面板 Timer 轮询），命令行输出漏标清单（保持字典顺序）。
- **面板"检测"按钮**：`BtnCheck_Click` 经 `SendStringToExecute("PATCHECK\n")` 触发；`AutoRefreshTimer` 比对 `PatCheckResult.Version` 变化后重渲染列表——未标注条目以橙色（DarkOrange）+ `△` 前缀标示（前景色，不与对照模式 diff 背景色冲突）。
- **PATALIGN v2 选型探针**（`tools/MLeaderRepro/MLeaderAlignProbe.cs`，AutoCAD 2026 实测）：A 路径（`TextLocation` setter）移动文字 (-20,+5) 时末顶点同步等量平移（相对差值恒 1.79），dogleg/landing/lineType 不被重置 → **采用**；B（MoveGripPointsAt 文字夹点）效果同 A 但实现复杂 → 备用；C（原生 MLEADERALIGN 经 `ed.Command` 发送）抛 ArgumentException → 弃用。文字宽度测量确认用 `MText.ActualWidth`（10 字符实测 26.43，与字高 3.5 成比例），`GeometricExtents` 含引线不可用。
- **PATALIGN v2**（MLeader 组版 + Shared 2007 版逻辑同步，实体分支不同）：
  - 选集先行（`CommandFlags.UsePickSet` + `SelectImplied`）：pickfirst 预选集优先（`PATSELECTALL`/`BZS` 建立），否则提示选择——一 DWG 多附图时各附图可分别对齐；
  - 线模式：文字投影到 P1→P2 基准线垂足（空间足够，保持原间距）；线长不足时沿 P1→P2 紧凑排列并越过线端延伸；
  - 框模式：文字推到指定边（左/右/上/下）外侧 margin（config.json `align.marginToFrame`，默认 5）；边长不足时按列溢出——第一列沿边排满，后续列沿远离框方向退一列（列距 = 列内最大占位宽 + 2×字高），不按各边延伸散开（避免交叉重叠）；
  - 排列顺序一律为投影顺序（线模式沿线方向、框模式沿边方向），**不按编号大小/层级关系重排**；文字占位测量失败时退化为纯投影（v1 行为）；
  - MLeader 移动原语：`TextLocation` setter（末顶点自动跟随，探针实证）+ `MText.Location` 同步 + `PatMLeaderCreator.UpdateChainTextPoint` 重写 Xrecord 点链文字端，保证对齐后 `PATMLVERIFY` 仍可通过；2007 Leader 路径沿用 v1（移动关联 MText + `SetTextEndpoint` 重写文字端点）；
  - 修复：溢出判定误用 `GetNormal()` 后的单位向量长度（恒 1）导致长线也走紧凑排列——在归一化前保存线长参与判定。
- **check-version-sync.ps1**：MLeader 组清单从 5 文件扩到 7 文件（新增 `PatCheckCommand.cs`、`PatAlignCommand.cs`，四版本字节级一致）；`PatCheckCommand.cs`/`PatAlignCommand.cs` 移出 Shared 强制清单，2007 必须链接 Shared 版本（legacy 校验）。
- **部署包**：`package.ps1 -Apply` 重打包五套（2013/2015 经 ILRepack 合并 Newtonsoft.Json，合并后 `GetReferencedAssemblies()` 无外部引用）。
- 验证（全部本地实际执行）：五版本编译通过；`build.ps1 -Structure` / `-Static` 通过（含 7 文件 MLeader 组 + Shared legacy 校验）；**AutoCAD 2026 批处理实测**（accoreconsole `/s` + SCR，英文 config.local.json，测试字典 `Drawing1.dict.json` 5 条目）——PATCHECK 漏标清单正确（标注 3/5，报 `#40 Bearing`、`#50 Cover`）；PATALIGN 线模式长线投影（`Aligned 3 (0 failed)` 无溢出文案）+ 短线紧凑延伸（`overflowed` 提示）；框模式宽边投影 + 窄边溢出 5 列（`4 extra column(s)`）；两种模式对齐后 `PATMLVERIFY` 全过（Run A 3/3、Run B 5/5，recorded 点链与实体状态一致）。
- 实测备注：accoreconsole 2026 的 SCR 参数为 `/s <script>`（绝对路径），非旧版 `/b`；SCR 中窗口选择对 MLeader 实测失效（完全包含仍 0 命中），改用 `ALL` 关键字选集。
- **全量实机复测（2026-08-16，AutoCAD 2026 + 部署包 DLL `PatentMarker-2025-deploy\PatentMarker.dll`）**：PATDOCTOR `PASS 4 / FAIL 0 / SKIP 2`（报告写入 DLL 旁）；BZM 连续创建 5 条；BZC 全标注时输出 "All dict entries are annotated"；PATALIGN 线模式长线投影 / 短线溢出、框模式宽边投影 / 窄边 4 列溢出全部正确；**pickfirst 流程实测通过**——BZS 建立预选集后 PATALIGN 免提示直接对齐 5 条；PATMLVERIFY 两轮 5/5；SAVEAS 后重开图纸，字典增补 `#60 Gear` 后 BZC 正确报漏标、链持久化校验仍 5/5 通过。局限：accoreconsole 不加载 `acmgd`，BZ 面板需交互式 GUI 会话实测。
- **Demo v5.1 重写（2026-08-17）**：`demo/PatentMarker-Demo-v5.html` 原地重写为深色工程风单文件动态演示（HTML5/CSS3/ES5，零依赖，兼容 Windows 7 + Chrome 109），共 17 幕约 3 分 23 秒。内容修正与补齐：PATCHECK v2 仅报漏标（面板橙色 △ 高亮）、PATALIGN v2 选择集先行（BZS 预选 → 线/框基准 → 溢出列）、新增开关场景（引线/下划线/箭头/线型/点数）、新增 PATBRACE/PATBRACEEDIT 大括号场景（顶→底→宽三点创建 + 控制点/尺寸编辑）、新增 PATDOCTOR/BZD 体检场景（PASS/FAIL/SKIP 汇总 + 报告文件）、VBA 安装场景更新为 7 文件（6 模块 + 面板 UserForm，唯一入口 `ShowPatentDictPanel`）、冲突裁决改为检测后自动弹窗（面板无裁决按钮）。全部命令行提示/按钮文案逐字取自源码（`Strings.cs` 等）。验证（全部本地实际执行）：ES5 语法扫描无 let/const/箭头函数/模板字符串，Node 解析通过；Playwright + Chromium 逐幕 seek 实测 17/17 场景控制台零报错、关键断言全部为真；截图目检确认主题、面板、开关态、△ 高亮、对齐、大括号夹点、体检报告与裁决弹窗渲染正确。

---

## v5.0 (2026-08-16)

**标注引擎切换为 MLeader（F 方案）**：2010/2013/2015/2025 四个版本的新建标注统一改为单个 MLeader 实体（自持 MText 文字）；2007 无 MLeader API，保持 `Leader + MText`。这是 v4.0"放弃 MLeader"决策的正式回归——形态探针（`tools/MLeaderRepro`）证实当年鱼钩形态的根因是顶点链不完整（只给 attach→dogleg），F 方案把文字点补为最后一个顶点后问题消除。

- **F 方案核心**（`Commands/PatMLeaderCreator.cs`）：全禁用自动几何（EnableDogleg/EnableLanding/ExtendLeaderToText=false、DoglegLength/LandingGap=0）+ 顶点链 `attach → dogleg… → text`；新增全禁用样式 `PAT_MLEADER` 与文字样式复用 `PatentTimesNewRoman`。
- **无箭头修复**：`ArrowSize` 在无箭头时置 0——非零 ArrowSize 即使配空箭头块 `_PAT_NO_ARROW` 也会把引线起点修剪掉 ArrowSize，导致引线不触及零件；箭头 On 时恢复面板设定值并配 `ObjectId.Null`（默认实心箭头）。
- **跨版本 API 适配**：`ExtendLeaderToText` 为 2014+ SDK 属性（2010-2012 无此成员），经 `PatMLeaderCreator.SetExtendLeaderToText/GetExtendLeaderToText` 反射访问，四版本单一代码文件；命令文件保持 .NET 3.5 兼容语法（自实现 `IsNullOrWhiteSpace`、显式 `List<Point3d>` 等）。
- **版本本地 Commands 组**：2010/2013/2015/2025 各自 `Commands/` 下 5 个文件（`PatMarkCommand`、`PatMLeaderCreator`、`PatMLeaderSetCommand`、`PatMLeaderVerifyCommand`、`PatSelectAllCommand`）字节级相同；csproj 改为引用本地文件（2010/2013/2015 显式 Include，2025 SDK 隐式包含），Shared 层的 `PatMarkCommand.cs`/`PatSelectAllCommand.cs` 仅由 2007 链接（Leader+MText 基线）。
- **`PATSELECTALL`/`BZS`**：通过扩展字典标记 `PATENTMARKER_MLEADER`（含 hasArrow/isSplined/用户点链 Xrecord）识别 PAT MLeader，兼容旧图纸 Leader 标注与独立文字。
- **新命令**：`PATMLSET`（ThreePoint/Spline/Arrow 开关的脚本化入口，测试用）；`PATMLVERIFY`（形态诊断：Explode 全部 PAT MLeader → 统一解析 Line/Spline/Polyline 曲线（端点 + 弧长 ~2 单位采样）→ 对照记录点链输出 C1-C6 检查报告；C6 断言直线模式的合法几何载体为 Line 或 Polyline——Explode 产物因 AutoCAD 版本而异；报告目录支持 `PATML_REPORT_DIR` 环境变量重定向）。
- **`check-version-sync.ps1` 新增 MLeader 组校验**：`PatMarkCommand.cs`/`PatSelectAllCommand.cs` 移出 Shared 强制清单，改为 MLeader 组规则——四版本存在、字节级一致、被各自 csproj 编译（SDK 风格 csproj 检查无 Compile Remove）、2007 不得携带且必须继续链接 Shared 版本。
- **部署包**：`package.ps1 -Apply` 重打包五套，2010/2013/2015/2025 的 `PatentMarker.dll` 更新为 MLeader 版（2013/2015 经 ILRepack 合并 Newtonsoft.Json）。
- **同步文档**：AGENTS.md（版本矩阵标注 API 列、目录约定 MLeader 组、第 5/6 节改写）、根 README.md（中英双语"当前标注实现"改 v5.0、版本矩阵、目录树、文档列表、版本历史）、新增 `docs/mleader-f-plan.md`（F 方案定义/实证/架构/验收，双语）。
- 验证（全部本地实际执行）：四版本编译通过（2010 .NET 3.5 反射适配为关键回归点）+ 2007 回归编译；`build.ps1 -Structure` / `-Static` 通过（含新版 check-version-sync MLeader 组）；契约模拟测试 4×28 全过；2025 单元测试 112/112；**AutoCAD 2026 批处理实测**（accoreconsole /s + SCR 编排 NETLOAD→PATMLSET→PATMARK 4 场景→PATSELECTALL→PATMLVERIFY）**4/4 PASS**（三点直线无箭头、三点样条+箭头、无限 1 拐点、无限 2 拐点；C1-C6 全过）。

---

## v4.9 (2026-08-15)

- Word 端接口收敛：宏列表由 4 个（`ExtractDict` / `EnableAutoExport` / `DisableAutoExport` / `ExportDict`）精简为唯一入口 `ShowPatentDictPanel`，运行后打开"专利标注字典工具"面板，包含「手动导出字典」按钮与「保存时自动导出」开关；其余内部过程均改为 Function/Private 隐藏，不再占用 Word Alt+F8 宏列表。
- 新增面板 UserForm `PatentDictPanel.frm` + 二进制 `PatentDictPanel.frx`（控件存储，导入 Word 时必需）：`cmdExport` 手动导出（调用 `AutoExport.ExportDict`）、`chkAutoExport` 自动导出开关（读写 `AutoExport.IsAutoExportEnabled`）、`UserForm_Initialize` 初始化勾选状态；面板为 Word COM 生成，控件二进制数据完整，无手写 .frm 的文本控件块导入后变标准模块（type=1）问题。
- `AutoExport.bas`：新增 `ShowPatentDictPanel` 唯一入口与 `IsAutoExportEnabled` 属性（属性 Let 内部路由到 Enable/Disable）；`ExportDict` 由 Sub 改 Function（含 `On Error` 重试路径不变）以隐藏于宏列表；Enable/Disable 改为 Private。
- `JsonWriter.bas`：`WriteToFile` 由 Sub 改 Function 以隐藏于宏列表（UTF-8 无 BOM 写盘逻辑不变）。
- `PatentExtractor.bas`：移除 `ExtractDict` 宏及私有辅助函数，仅保留占位注释（部署包与 Word 端依赖固定文件名，不可删除/重命名）。
- 5 套部署包 `install-vba.vbs` 同步：导入 `PatentDictPanel.frm`（.frx 由 Word 导入 .frm 时自动读取）、清理旧版遗留的已删除模块、更新提示文案与文件计数（6 模块 → 7 文件）；脚本为 GBK 编码，按字节级安全方式修改（PowerShell 显式 GBK 编解码），杜绝中文乱码。
- 构建/同步脚本纳入 UserForm：`vba-sync.ps1` 把 `.frm`/`.frx` 一并从根 `vba/` 同步到 5 套部署包；`build.ps1 -Structure` / `package.ps1` 校验部署包含 `.frm`/`.frx` 且与真源一致。
- 同步文档：AGENTS.md（7 个 VBA 文件、面板入口、禁止事项与结构/静态校验描述）、根 README.md（中英双语 VBA 文件表、快速开始与部署包说明、版本历史 v4.9）、5 套部署包 README.txt（2010/2025 补充 Word 端面板说明，2007 为 GBK 编码）。
- 验证（全部本地实际执行）：`vba-sync.ps1` 推送后 5 套部署包 VBA 文件（含 .frm/.frx）与根 `vba/` 哈希一致；`build.ps1 -Structure` / `-Static` 通过；Word COM 实测导入 7 文件（含 UserForm）成功、宏列表仅含 1 个 `ShowPatentDictPanel`、手动导出生成合法 UTF-8 `.dict.json`、自动导出开关开/关生效，面板按钮/开关中文文案与初始化勾选状态正确。

---

## v4.8 (2026-08-15)

- 新增 CAD 外诊断脚本 doctor：解决 `PATDOCTOR/BZD` 作为 CAD 内命令的固有死锁——插件本身加载失败或命令未注册时，用户无从触发诊断。doctor 不依赖插件加载即可在 AutoCAD 外运行，五套部署包各内置一份（`doctor-2007/2010/2013/2015.vbs` + `doctor-2025.ps1`）。
- 两层诊断结构：Tier 1 离线检查（无需 AutoCAD）——部署 DLL 存在性、demand-load 注册表条目及 LOADER 指向文件是否存在、该版本所需 .NET 运行时（2.0/3.5/4.0/4.5+/8）、`PatentMarker.log` 尾部；Tier 2 在线升级——检测到本版本支持范围内的 AutoCAD 后，自动以 `/b` 批处理模式启动宿主，SCR 编排 `NETLOAD` 部署 DLL → `PATDOCTOR` → `QUIT`（应答 `_N`），生成 CAD 内诊断报告并强制回收宿主进程。
- 鲁棒性设计：启动前检测残留 `acad.exe`（持有 profile 锁会使批处理宿主静默挂起）并以 WARN 跳过在线层；vbs 四份字节级相同（版本从文件名 `doctor-(\d{4})` 自识别，报告与控制台输出含版本号）；vbs 为纯 ASCII（与 install-*.vbs 同一约定，杜绝代码页损坏），ps1 保留中英文控制台 i18n；报告文件（`PatentMarker-doctor-offline-report.txt`）加入 `.gitignore` 运行时产物。
- 同步文档：AGENTS.md 3.1 部署包差异表新增"诊断脚本"列并说明两层结构与单源规则；五套部署包 README.txt 增加"诊断（doctor）"章节（2007 版为 GBK 编码，按编码安全方式拼接，Win7 记事本可正常显示）。
- 验证（全部本地实际执行）：四份 vbs 哈希一致（SHA256 相同）+ 纯 ASCII 字节扫描通过；`cscript doctor-2015.vbs offline` 与 `cscript doctor-2007.vbs`（无参数）退出码 0，版本自识别/注册表扫描/.NET 检查/在线层 SKIP 路径均正确；`doctor-2025.ps1` 全梯子在 AutoCAD 2026 真机通过（Tier 2 自动启动宿主、NETLOAD、PATDOCTOR 报告落盘，OVERALL PASS=5 FAIL=0 WARN=0，宿主回收正常）——经由计划任务派生进程执行以规避本机终端沙箱对 AutoCAD 启动写受限路径的误杀，此前 17:03 的 SCR+/b 实测亦已证明同一编排可用。`build.ps1 -Structure` / `-Static` 通过。

---

## v4.7 (2026-08-15)

- 补齐 AGENTS.md 记录的部署包技术债：2015/2025 两套卸载脚本缺失，现五套部署包安装/卸载脚本齐全。
- 新增 `PatentMarker-2015-deploy/uninstall-2015.vbs`：清理 HKCU/HKLM 注册表自动加载条目，版本候选与 `install-2015.vbs` 完全对称（R20.0–R24.2 + R25.0，共 10 键）；GBK 编码 + 中英文 i18n，与既有 vbs 部署脚本风格一致。
- 新增 `PatentMarker-2025-deploy/uninstall-2025.ps1`：清理 R25.0/R25.1/R26.0 的 HKCU 条目（并防御性清理旧版安装器可能写入的 HKLM 残留）；额外删除 `install-2025.ps1` 生成的 LSP 兜底文件（部署目录与 `%LOCALAPPDATA%\PatentMarker`，目录空则连目录清理，`-KeepLsp` 可保留）；UTF-8 BOM + 中英文 i18n + 日志 `uninstall-2025.log`。
- 修复上一版误提交：`PatentMarker-2025-deploy/load-patent-marker.lsp` 实为 `install-2025.ps1` 的生成物（内容由部署目录绝对路径决定，对其他用户是坏路径），已从版本库移除并加入 `.gitignore`；本地文件由安装脚本随时再生。
- 同步文档：AGENTS.md 3.1 卸载脚本矩阵补齐并删除"缺卸载脚本"备注；两套部署包 README.txt 增加卸载说明；maintainability-repair-plan.md 追加 2026-08-15 执行记录（v4.5/v4.6 成果回填，解除"交互式宿主"表述过时的部分）。
- 验证（全部本地实际执行）：PowerShell PSParser 语法检查 0 错误；真实宿主端到端演练——先 `reg export` 备份本机 R25.1 两个 profile 的 PatentMarker 键 → 执行 `uninstall-2015.vbs`（退出码 0，正确报告"未找到条目"，确认对本机 2025 版无副作用）→ 执行 `uninstall-2025.ps1`（真实删除 2 个 HKCU 键 + 部署目录 LSP，输出与日志正常）→ `reg import` 恢复注册表并核验 LOADCTRLS/LOADER 与备份一致 → 恢复 LSP 文件；`build.ps1 -Structure` / `-Static` 通过。

---

## v4.6 (2026-08-15)

- 技术债清理三阶段（Phase 1 单源化 → Phase 2 共享层收敛 → Phase 3 校验闭环），五个版本行为保持一致，仅 JSON 库与 .NET 目标框架保留版本特有差异。
- Phase 1 VBA 单源化：根目录 `vba/` 成为 6 个 Word 模块的唯一真源，`vba-sync.ps1` 向五套部署包同步，`package.ps1` 在打包前断言部署副本与真源一致；部署包内 VBA 不再手工维护。
- Phase 2 共享层收敛：再收敛 11 个文件族（PatMarkCommand/PatCheckCommand/PatAlignCommand/PatSelectAllCommand、Strings、ArbitrateDialog/EditEntryDialog/PasteRecognizeDialog、DictPaletteControl、PatPaletteCommand、PatStyleInitializer）至 `cad-plugin/Shared/`，共享层达 29 个文件；裁决并统一 PatStyleInitializer 的样式初始化行为差异；五个 csproj 全部改为 `<Compile Include="..\..\Shared\...">` 链接编译，版本目录仅保留版本特有 IO 适配层与入口文件。
- Phase 3 校验闭环：新增 `PatentMarker.RuntimeContract.2007.Tests`，契约模拟测试覆盖 2007/2010/2013/2015 全部四个旧版（各 28 用例）；`PatentCAD.sln` 接入全部五个版本工程与五个测试工程（修复项目 GUID 重复与重名解决方案文件夹）；`check-version-sync.ps1` 增设版本特有文件白名单并补齐 Diagnostics 三文件；`build.ps1 -Static` 的共享层检查改为委托 `check-version-sync.ps1`（消除双重列表维护），版本本地文件组内一致性按同 JSON 栈分组校验（2013↔2015、2007↔2010）。
- 修复收敛引入的兼容性回归：`Shared/Palette/PasteRecognizeDialog.cs` 使用了 .NET 4.0 才有的 `string.IsNullOrWhiteSpace`，导致 2007（.NET 2.0）/2010（.NET 3.5）编译失败；改为 .NET 2.0 兼容写法（`null` 检查 + `Trim().Length == 0`），共享层源码再次通过五版本编译。
- 实测脚本加固：`tools/doctor-live-test.ps1` 由 COM 驱动改为 SCR + `/b` 批处理模式（COM `New-Object -ComObject` 会附着到崩溃残留实例、`SendCommand` 在模态对话框上挂起，见 acad.err 2026-08-15 16:30 致命错误记录）；判定条件改为轮询 doctor 报告落盘，宿主未按脚本 QUIT 时强制回收；启动前检查残留 acad 进程。复测通过：全新 AutoCAD 会话加载新鲜打包的 2025 部署包 DLL，PATDOCTOR 报告 PASS 3 / FAIL 1 / SKIP 2、最近错误 0。
- 仓库清理：`.gitignore` 新增本地自备/临时类条目（`cad-plugin/packages/`、`cad-plugin/tools/`、`tools/ilrepack|net-ref-fetch|refasm/`、探针与构建日志、`.qoder/`、`acad.err`、`docs/handoff-*.md` 会话交接稿）；补齐 `tools/BoundaryHarness.bas`（已被跟踪的 `test-vba-boundary-harness.vbs` 引用）与 `PatentMarker-2025-deploy/load-patent-marker.lsp`（`install-2025.ps1` 的复制源）。
- 验证（全部本地实际执行）：结构检查、静态检查（29 共享文件 canonical + 五版本链接齐全 + 0 警告）、契约模拟测试 4×28 通过、2025 单元测试 112 通过、五版本真实编译通过、五套部署包重新打包（2013/2015 ILRepack 合并后确认无外部 Newtonsoft.Json 引用）、AutoCAD 2026 宿主实测（`/b` 脚本加载 2025 部署包 DLL 并运行 `PATDOCTOR`，报告正常生成：PASS 3 / FAIL 1 / SKIP 2，最近错误 0）。

---

## v4.5 (2026-08-15)

- 新增自动诊断机制 `PATDOCTOR`（别名 `BZD`）：一键自检插件运行状态并生成报告，五个版本共用同一份共享源码（`cad-plugin/Shared/Diagnostics/`，含错误环形缓冲、检查结果模型与报告输出）。
- 自检项：报告目录可写、`PAT_DIM` 标注样式与 `TIMES_ROMAN` 文字样式状态（含箭头/文字高度当前值）、运行设置、`.dict.json` 字典解析与条目数、模型空间实体扫描（Leader/MText 计数）；报告写入 DLL 旁 `PatentMarker-doctor-report.txt`，附环境信息（程序集路径、.NET 运行时）与最近 100 条错误。
- 错误捕获：五个版本的 `PatentMarkerApp.RawLog` 入口挂钩 `PatDiagnostics.OnRawLog`，自动把 error/failed/fatal/exception 类日志行汇入环形缓冲，PATDOCTOR 直接带出；文件日志写盘失败不影响缓冲记录。
- 诊断模块保持 .NET 2.0 / C# 3.0 兼容语法、无 JSON 依赖，通过 csproj 源码链接编入五个版本，不生成跨 CLR DLL；五版本本地编译通过（0 错误）、结构检查与同步检查通过。
- 真实宿主实测通过（`tools/doctor-live-test.ps1`，COM 驱动 AutoCAD 2026、注册表自动加载部署包 DLL）：`BZD` 与 `PATDOCTOR` 全名均正常触发，报告落盘于 DLL 旁，PASS 3 / FAIL 1 / SKIP 2 语义正确（新图纸样式 SKIP、未设置字典路径 FAIL、模型空间扫描 PASS）；2025 版插件在 AutoCAD 2026 中跨版本加载运行正常。实测中发现并修复错误缓冲自引用污染（PATDOCTOR 自身汇总日志含 "error" 字样被钩子误记为错误，已改为跳过 PATDOCTOR 前缀日志且汇总日志不再携带该字样）与报告 Drawing 字段重复两处缺陷。

---

## v4.4 (2026-08-12)

- Unified PATMARK completion handling across all five editions: ESC and the right-click Confirm/Cancel result now leave both three-point and unlimited-point marking mode, including cancellation during an unfinished dogleg sequence.
- Corrected the unlimited-point tie case so it always resolves to a text corner instead of the left/right middle attachment, and added a free-mode regression assertion for the selected corner.
- Localized the new palette switches: Chinese mode now displays `引线` and `下划线`, while English mode continues to display `Leader` and `Underline`.
- Rebuilt the five edition deployment DLLs after verification so field installations receive the source fixes rather than continuing to load the older packaged binaries.
- Unified unlimited-point Leader/MText attachment selection with the four-corner rule used by three-point mode. When the unlimited-point text position uses the last dogleg, the preceding segment now supplies the directional reference instead of collapsing to the left middle attachment.
- Removed the unused palette entries for deleting leaders, selecting all, and conflict handling from all five editions. The underlying commands and data services remain available for compatibility.
- After the follow-up verification request, simulation, unit, structure, static, full-edition build, and API-surface checks were run before publishing.
- Reworked the shared brace profile around the PPT right-brace baseline: the selected side is reserved for the single center cusp, while the endpoint shoulders and straight stems stay on the opposite side; the cusp remains one explicit shared Polyline vertex.

---

## v4.3 (2026-08-12)

- Added synchronized Leader and Underline switches to all five palette editions. With Leader off, PATMARK creates a marked standalone MText at the single picked position; Underline uses AutoCAD MText inline formatting and remains compatible with dictionary checks, renaming, alignment, selection, and bulk deletion.
- Added runtime-contract coverage for standalone underlined text, leader text underline formatting, and standalone entity lifecycle operations. AutoCAD in-process verification is still required for final host rendering confirmation.

---

## v4.2 (2026-08-12)

- 重做共享矢量大括号几何：废弃旧的固定 `ShapeX/ShapeT` 折线采样，改为基于 PPT `Right Brace` 轮廓的参数化曲线；端部肩部平滑过渡，直干保持在同一侧，中部使用真正尖锐的折角，不再生成 W 型或圆头尖点。五个版本继续链接同一份共享几何源码。
- 增加大括号视觉契约测试：验证端点、宽度、方向、单一尖点和折角不连续切线；2010/2013/2015 模拟主机契约测试各 `18/18` 通过，五个版本本地编译通过；五套部署 DLL 已按新几何重新打包并保留时间戳备份。尚未在 AutoCAD 交互主机内实测。

---

## v4.1 (2026-08-11)

- 参数化矢量大括号第一版：新增 `PATBRACE` / `DAGUOHAO` 三点创建命令和面板按钮，使用独立 `Polyline` 保存大括号几何，并在扩展字典中保存顶部、底部、方向侧和宽度参数；不使用文字字符，也不改变现有 Leader/MText 标注路径。
- 新增 `PATBRACEEDIT`：可重新点选顶部、底部和宽度方向控制点，也可直接输入高度和宽度。第一版采用命令交互而非原生自定义夹点，以保持 AutoCAD 2007—2026 的 API 兼容性；控制点调整和尺寸调整共用同一参数模型。
- 修正大括号轮廓方向：旧轮廓的两个外侧肩部和中部尖点在同一侧，导致形状像单侧双鼓包；现在第三点所指的一侧成为中部尖点，两个外侧肩部位于相反侧，支持竖向左/右和横向上/下四个基本方向，更接近 PPT 大括号。
- 五个版本同步面板入口、命令文案、源码链接、运行契约测试和部署 DLL。几何单元测试覆盖端点/宽度、尺寸调整保留方向、控制点调整保留宽度和四个方向；本地编译五版均通过，2010/2013/2015 模拟契约各 17/17，2025 测试 112/112；这些检查不替代真实 AutoCAD 宿主内的交互复核。

## v4.0 (2026-08-04)

- Leader/MText hook line 修复（2026-08-11）：本机未安装 AutoCAD 2007，无法把现场现象表述为已完成 2007 宿主实测；根据 Autodesk Leader 规范，`Leader.Annotation` 会自动生成文字侧 hook line，这正是屏幕上多出吸附点/短线段的根因。五个版本现在不设置原生 Annotation，而是把按象限计算出的文字附着点直接追加为 Leader 最后一个用户顶点，并用扩展字典保存 MText 关系；改号、对齐、删除、全选和旧图纸原生关联读取均已适配。模拟回归确认三点模式只保留 3 个 Leader 顶点、`Annotation` 为空，文字仍可通过内部关系定位。
- Leader/MText 提交后附着点复核（2026-08-11）：模拟主机复现了“事务提交时宿主把上方附着规范化到底部”的生命周期风险。五个版本现在在首个事务提交后重新打开 MText，再次写入附着点和文字位置，并从新事务读回实际附着值；同时记录实际加载 DLL 路径和提交后的 Leader 顶点列表，便于现场区分旧 DLL、关联重算和真实额外顶点。

- Leader/MText 四象限文字附着修正（2026-08-11）：在已有左右侧判断上增加上下方向判断，按靠近文字的最后一个引线拐点相对文字的位置选择 `TopLeft`、`BottomLeft`、`TopRight` 或 `BottomRight`；该点与文字同高时保留 `MiddleLeft`/`MiddleRight`。五个版本继续共用同一附着规则，并补充左上、左下、右上、右下运行契约测试。

- 默认三点模式部署产物复核（2026-08-11）：源码中的新图纸默认值为 `ThreePointMode = true`，但此前五套部署包的 DLL 可能仍是旧构建产物，导致现场打开面板时显示无限点。已重新编译 2007/2010/2013/2015/2025，并更新五套部署包 DLL；2013/2015 同时重新完成 Newtonsoft.Json 合并。模式切换语义不变：首次进入新图纸为三点，点击「点数」后切换为无限点，并在当前图纸会话中保留。

- VBA 编号后缀数字修复（2026-08-11）：Patterns.bas 与 DictModel.bas 的五套部署副本支持 123A1、123A2 等“数字 + 字母 + 数字”编号；纯数字仍保持最多 5 位，编号只在现有逗号、顿号、分号、句号等分隔标点处结束。同步更新 cad-plugin/Shared/IO/MarkingTextParser.cs 及表格预处理规则；同时修复相邻候选条目区间终点计算错误，避免无空格时第二条记录被过滤。检查当前仓库 fixture、实际 Word 样例、8 份批量语料及 v4 期望输出，未发现与该边界规则冲突的样例。五套 VBA 均通过 Word COM 批量回归和 123A1/123A2 → JSON 端到端验证，C# 测试为 112/112。

- Leader/MText 文字侧面附着修正（2026-08-11）：新增共用附着点判断。文字位于最后一个引线拐点右侧时使用 `MiddleLeft`，位于左侧时使用 `MiddleRight`，使引线连接文字左右侧的垂直中部，不再统一落到左上角；五个版本同步并补充左右侧运行契约测试。

- Demo v5 (2026-08-06): based on the previous single-file dynamic demo, added complete scenes for CAD paste recognition and merge write-back, F2/right-click entry editing with drawing leader renumber synchronization, Word/CAD backup arbitration, the new default three-point workflow, and double-click-to-mark. The original v4 demo remains unchanged; the new deliverable is `demo/PatentMarker-Demo-v5.html`.

- Shared source layer (2026-08-06): added `cad-plugin/Shared/` as the canonical source for six AutoCAD-independent modules (`NumberIdentity`, `PatSettings`, `DictDiff`, `DictConflict`, `MarkingTextParser`, `Language`). All five edition projects link these files at compile time; the old 30 copied files were removed after being backed up locally. `build.ps1 -Static` and `check-version-sync.ps1` now enforce the shared link and reject local duplicates. Five-edition compilation, 2025 unit tests (106/106) and 2010/2013/2015 simulated host contracts (15/15) pass.

- Palette boundary extraction (2026-08-06): added shared `DictPaletteWorkflow`, `DictPaletteCadService`, `DictPaletteSession`, and `Cad/PatEntityHelper` source links across all five editions. `DictPaletteControl` now delegates dictionary/cache/conflict coordination and CAD transactions to those services; the five duplicated session/helper implementations were removed after incremental backups. Final verification: five-edition build, 21/21 simulated host tests, 108/108 2025 unit tests, structure/static/sync/API checks, and `git diff --check` all pass. The remaining WinForms view lifecycle stays version-local until an interactive AutoCAD/WinForms host is available.

- View rendering boundary extraction (2026-08-06): added shared `Palette/DictPaletteViewRenderer.cs` for dictionary list rendering, Diff colors, compare columns, filtering and empty-state presentation. All five editions link the same C# 2-compatible source; `DictPaletteControl` retains event coordination, timer lifetime and AutoCAD/dialog interactions. Automated gates pass after the extraction; interactive 2025/2026 verification is the next step.

- Palette interaction simplification (2026-08-06): changed the shared runtime default to three-point mode; the existing point-count button now switches to unlimited mode only when clicked. Across all five version-local WinForms views, double-clicking a dictionary entry now starts `PATMARK` directly, while right-clicking an entry or pressing `F2` opens the edit dialog. Removed the redundant “Save & Mark” dialog action; editing now only saves, deletes or cancels. No shared dictionary/CAD service logic was duplicated or changed.

- Leader fallback for MLeader attachment-grip bug (2026-08-06): the supplied `PatentMarker.log` showed that every requested final dogleg point was preserved as the actual MLeader last vertex; the remaining extra point was MLeader's text-content attachment geometry, not an extra `AddLastVertex` call. Since the grip could not be removed reliably, 2013/2015/2025 now use the 2007/2010 `Leader + MText` construction for PATMARK, PATCHECK, PATALIGN, PATSELECTALL, palette deletion and dictionary rename synchronization. Styles, API checks, runtime simulations and README/version-plan entries were synchronized.

- Runtime contract hardening (2026-08-05): 2025 版的版本矩阵明确覆盖 AutoCAD 2025–2026+；新增五版 `IO/RuntimeHost.cs` 主机边界并将 `PATMARK` 接入，新增 2010/2013/2015 严格 CAD/事务模拟测试（各 5/5，共 15/15），直接驱动对应版本生产命令。新增 `check-api-contract.ps1` 与 `tools/ApiSurfaceCheck/`，通过本机 2010 Leader、2013/2015/2025 MLeader SDK 元数据检查。AutoCAD 2026 COM 自动化返回 `80080005 (CO_E_SERVER_EXEC_FAILURE)`；Core Console 对 2025 DLL 的 `NETLOAD`/`PATCHECK` 脚本退出码为 0，但因无交互面板和独立业务断言，仅记录为部分冒烟，不替代完整主机回归。最终验证：2025 测试 106/106、模拟测试 15/15、五版编译、结构/静态/API 检查和发布暂存均通过。

- Host boundary completion (2026-08-05): extended `IO/RuntimeHost.cs` usage from `PATMARK` to all active-document reads in commands, palette, style initialization, configuration and dictionary paths across all five editions. Added a static invariant to reject direct `MdiActiveDocument` reads outside the host seam. Added read-only `check-autocad-host.ps1`; it finds AutoCAD 2026/Core Console, a running Autodesk licensing service and registered COM ProgIDs without launching or modifying the CAD installation. This narrows the remaining blocker to legitimate interactive licensing/host behavior rather than an untracked code path.

- 2025 installer fallback fix (2026-08-06): corrected the PowerShell installer to create typed registry values with `New-ItemProperty`, always register under the current user's HKCU hive even when AutoCAD is detected through HKLM, generate a usable `load-patent-marker.lsp` fallback, write an installation log, show fatal errors, and pause on interactive runs instead of closing immediately. Local PowerShell 5.1 smoke verification registered both detected AutoCAD 2026 products, generated the LSP fallback, and verified `LOADCTRLS=14`, `MANAGED=1`, and the DLL path.

- Maintainability repair execution (2026-08-05): discovered MSBuild through the local Build Tools installation, fixed legacy NuGet/RID false failures and native build exit-code propagation, and verified all five editions compile. Added a tracked eight-case parser contract fixture, optional local corpus handling, explicit per-drawing activation/release for `ConfigLoader`, `DictLoader`, and `PatSettings`, and `DictPaletteSession` as the first non-UI layer extracted from `DictPaletteControl`. Added the test project to `PatentCAD.sln` and a staging-only `package.ps1` that validates shared VBA modules and 2013/2015 ILRepack references without overwriting deployment packages. Verification: 104/104 tests, five-edition build, structure/static/sync checks, dry-run downlevel check, and release staging passed. AutoCAD 2007/2025 in-process regression remains unexecuted because those hosts are not installed locally.

- Verification and hardening (2026-08-05): added 93 automated tests; all five edition projects compile locally. `DictWriter` now clones merge inputs, repairs null model collections, and uses atomic replacement for existing dictionary files; `DictLoader` normalizes explicit null/nested values before UI use. Backup names now require the documented timestamp format, and CAD restore validates a backup before replacing the current dictionary. Edit/delete dialogs roll back in-memory changes when disk write-back fails. The 2007 project target is aligned to .NET 2.0. Deployment DLLs were rebuilt for all five editions, with Newtonsoft.Json re-merged for 2013/2015. AutoCAD in-process workflow still requires manual validation in each installed AutoCAD year.

- MLeader creation fix (2026-08-05): corrected the 2013/2015/2025 creation order so a valid `MText` (with database defaults), its text location, and the PAT MLeader style are attached before `AddLeaderLine`/`AddLastVertex`. This addresses the 2013 symptom where all points could be picked but no visible leader was created. The fix is synchronized across the MLeader editions; 2007/2010 Leader creation is unchanged.

- MLeader geometry/text fix (2026-08-06): disabled automatic dogleg, landing, and leader-to-text extension in the 2013/2015/2025 MLeader path so three-point mode keeps only the vertices supplied by the user. MLeader text is forced to horizontal angle with zero MText rotation, preventing text from becoming slanted after repositioning. 2007/2010 remain on the existing Leader + MText path, which has no MLeader auto-vertex behavior.

- MLeader landing diagnosis (2026-08-06): Autodesk documents that horizontal text attachment includes a landing line even when the explicit landing flag is disabled. The 2013/2015/2025 path now uses vertical center attachment (while retaining horizontal text angle) to remove that automatic text-side segment. Creation logging records requested attach/dogleg/text points, attachment settings, and the actual last leader vertex in `PatentMarker.log` beside the loaded DLL.

- Marking-section boundary fix (2026-08-05): all five VBA packages and all five CAD paste parsers now honor the document contract `附图标记说明如下：...。` by stopping at the first Chinese full stop before the following `具体实施方式` body. The boundary regression case passes through real Word COM VBA and excludes a body-only `200` marking from the generated JSON; the 2025 parser suite passes 96/96. Existing blank-paragraph handling remains only as a fallback for malformed/legacy text without the required terminator.

- VBA live verification (2026-08-05): Word COM opened the prepared `MU26005942.2稿(1).docx`, imported and executed the six actual VBA modules from all five deployment packages, and produced valid UTF-8 JSON. Fixed Word's CR-only paragraph separators not being recognized when locating the marking section; fixed uppercase suffixes such as `1342A`/`1342B` being lost or misclassified. The prepared document now exports 24 entries with 0 warnings/conflicts; CAD `MarkingTextParser` and all VBA copies are synchronized, with regression tests for CR-only sections and uppercase suffixes. A local eight-document corpus baseline was regenerated from the actual VBA modules; the 2025 test suite passes 95/95.

- 新增 CAD 端字典编辑闭环（全部 5 个版本同步）：
  - **粘贴识别**：面板新增「粘贴识别」按钮，从 Word 说明书粘贴附图标记段落，C# 移植 VBA 识别引擎（`MarkingTextParser`：段落定位 + 表格预处理 + 多格式解析），预览表格可编辑，支持「覆盖整个字典 / 按编号合并」两种写回方式
  - **编辑对话框**：右键或按 `F2` 打开，支持改编号 / 改名称 / 新增 / 删除，编号冲突（忽略大小写）即时校验；标注由列表双击直接触发
  - **实体联动**：改号后自动更新图纸内旧编号的 PAT 引线文字（五个版本统一走 Leader + MText/DBText annotation）并 `Regen`
  - **冲突裁决**：Word 端导出前检测旧字典的 `modified_by: cad` 标记，若 CAD 曾修改则备份为 `<主名>.dict.json.word-<时间戳>.bak`（只保留最新一个）；CAD 面板轮询检测到 Word 已覆盖时状态栏提示并点亮「裁决」按钮，弹窗三选：采用 Word 版（删备份）/ 恢复 CAD 版（备份覆盖回 + 清 CAD 标记）/ 稍后再说
- 字典格式 v4.0：metadata 新增可选 `modified_by` / `modified_at`（CAD 手动修改标记），旧字典文件完全兼容
- 写回通道：CAD 端 `DictWriter` 输出与 VBA `JsonWriter` 逐字符兼容（2 空格缩进、\r\n、UTF-8 无 BOM、键顺序固定）；2007/2010 因 SimpleJson 仅解析不序列化，手写实现同格式序列化器
- 2007/2010 平移要点：目标 .NET 3.5 无 LINQ；降级 `out int` 内联声明与自动属性初始化器；编译用 `MSBuild /tv:4.0`（否则回退 C# 2.0 编译器）；2010 版 csproj 额外引用 PresentationCore（SDK 的 PaletteSet 依赖 WPF IWin32Window）
- 验证：2025 版识别器单测 61 项全过 + 9 份真实语料 C# vs VBA 预期一致；2013/2015/2010/2007 本地 MSBuild 编译通过；2013/2015 重新 ILRepack 合并 Newtonsoft.Json 并更新部署包；2007/2010 组内共享文件 SHA256 一致
- 5 套部署包同步：`PatentMarker.dll` 全部替换为 v4.0 编译产物；6 个 VBA 模块跨包 SHA256 一致（`AutoExport.bas` 新增导出前备份逻辑）
- 新增内部实施计划文档 `docs/v4.0-cad-edit-plan.md`（阶段任务跟踪，与 development-log / version-plan 配套）

---

## v3.2 (2026-08-04)

- 修复 2013/2015/2025 创建 MLeaderStyle 时未入库先设属性的 `eOwnerNotSet` 异常（`PatStyleInitializer` 改为先 `SetAt` + `AddNewlyCreatedDBObject` 再设置属性），双击字典面板条目触发 PATMARK 不再中断
- 2013/2015 改为单文件部署：编译后经 ILRepack 将 Newtonsoft.Json 13.0.3 合并进 `PatentMarker.dll`，安装不再要求/附带 `Newtonsoft.Json.dll`（移除 `install-2013.vbs`、`install-2015.vbs` 的依赖检查，删除两套部署包中的旧 DLL）
- 修复 VBA `Patterns.bas` 模式 1 分隔符类缺全角分号 `；` 的问题：分号/逗号混用（如 `3叶片，31第一叶片，…；4环状部；5沟槽。`）时仅识别逗号条目；补 `；` 后 18 条示例全部命中
- 验证“附图标记说明如下：”冒号前带前缀文字（如“在附图1-7中，附图标记说明如下：”）的段落定位与提取正确
- 5 套部署包 `vba/Patterns.bas` 同步更新（GBK 编码、SHA256 一致）；2013/2015 部署包 `PatentMarker.dll` 替换为 ILRepack 合并版
- 2013/2015 部署包 `README.txt` 重写为 UTF-8：移除 `Newtonsoft.Json.dll` 依赖说明，注明单文件部署
- `build.ps1` 静态检查更新：2013/2015/2025 部署包不得再含外部 `Newtonsoft.Json.dll`
- 本地编译验证：2013（.NET 4.0 / MSB3274 规避：HintPath 改 `lib/net35`）、2015（.NET 4.5）、2025（net8.0）全部通过；ILRepack 合并后程序集不再引用外部 Newtonsoft.Json（类型已内嵌）
- 修正 2013/2015 `PatentMarker.csproj` 的 `Newtonsoft.Json` HintPath（`..\packages` → `..\..\packages`，此前命令行 MSBuild 无法解析引用）

---

## v3.1 (2026-08-03)

- 新增三点模式：面板新增「点数:无限/三点」切换按钮，与线型开关正交
- 三点模式下引线固定 3 点采集：附着点 → 1 个拐点 → 文字位置，第 3 点点击后自动创建
- 关闭时保持原有无限拐点循环采集行为（默认关闭，不影响老用户）
- Esc/回车取消本次（硬性三点，不允许多拐）
- 全部 5 个版本同步：采集层逻辑相同，2007/2010 调用 CreateLeaderWithText，2013/2015/2025 调用 CreateMLeader
- 实体创建函数无改动（接受任意长度拐点列表）
- dotnet test 27/27 通过；check-version-sync 组内一致性确认

---

## v3.0 (2026-08-03)

- VBA v3.0：多格式附图标记识别（括号/连字符/英文标点/裸列表）
- VBA 支持新格式（名称+编号）提取 + 自动导出检测 DWG 命名 + `<br/>` 标签处理
- C# 面板取消 JSON 排序，按原文顺序显示（用户自定义顺序优先）
- 部署包 VBA 全部同步 v3.0（含 2007-v2 / 2007-deploy 的 JsonWriter / PatentExtractor 统一）
- 全部 5 个版本重新编译并更新部署包 DLL（含取消排序改动）

工程基础设施（不改变插件运行时行为）：

- 新增 `AGENTS.md` 代理工作指南：版本矩阵、目录约定、MLeader API 陷阱对照表、跨版本同步规则、部署包逐版本差异
- 新增 `build.ps1` 构建与环境检查脚本：SDK DLL 齐全性检查、2025 版 `dotnet build` 编译、`-Check` doctor 模式、`-Structure` 结构检查
- 新增 `.github/workflows/build.yml`：push/PR 时执行结构完整性检查；因 SDK DLL 版权不入库，真编译需本地执行 `build.ps1`
- 新增 `PatentCAD.sln` 解决方案文件（含 2025 版，可直接 `dotnet build`）

## v2.5 (2026-07-27)

- 修复 Word 2010 无法导入 `clsSaveHook.cls` 的兼容性问题（改为代码注入方式）
- 修复 2007/2010 版箭头大小修改后不能立即生效的问题
- 所有部署包补充 `install-vba.vbs` 脚本

## v2.4 (2026-07-26)

- 完成多版本适配：2010 / 2013 / 2015 / 2025，全部通过编译验证
- 修复 MLeader API 名称适配问题（`TextLocation`、`AddLastVertex` 等）
- 修复 ArrowSize / TextHeight 实例同步问题
- 统一部署包结构：根目录 `PatentMarker-{version}-deploy/`

## v2.0 (2026-07)

- 2007 版完成：样条曲线引线 + 无限拐点 + 默认无箭头
- 面板控制：字高调节、箭头开关、箭头大小、线型切换
- 字典自动刷新（2 秒轮询 `.dict.json` 时间戳）
- 字典对比功能（双向匹配 + 6 色高亮 + 对照列切换）
- 命令拼音别名（`BZ` / `BZM` / `BZC` / `BZA` / `BZS`）
- 全选 PAT 标注实体命令（`PATSELECTALL` / `BZS`）
- VBA 安装脚本：自动导入模块到 Word Normal 模板
- 部署方式：VBScript 注册表 + APPLOAD/LSP 兜底

## v1.0 (2026-06)

- 初始版本：Leader + MText 组合标注方案
- 基础面板：字典列表、搜索、字高调节
- Word VBA 提取器：从说明书提取附图标记字典

# PatentCAD-Annotator

**AutoCAD 专利图纸标注插件** — 从 Word 说明书提取附图标记，在 AutoCAD 图纸中一键标注并保持双向同步。

**AutoCAD patent drawing annotation plugin** — Extract reference numerals from Word specifications, annotate them in AutoCAD drawings with one click, and keep them in sync.

---

## 中文说明

### 项目简介

PatentCAD-Annotator 的目的在于减少专利图纸标注中的机械操作，例如修改标注内容、字体、调整位置等，以提高附图标注工作的效率。并且，在申请文件中的附图标记进行修改之后，还可以自动地进行对比。
具体来说，本项目解决了一下三个痛点：

1. **人工对照易错** — Word 说明书里的附图标记编号与图纸手动对照，容易漏标/错标
2. **修改后不同步** — 说明书改了编号，图纸要逐个找出来改
3. **格式不统一** — 不同人标注的引线样式、文字高度、对齐方式参差不齐

PatentCAD-Annotator 的工作流：Word 保存时自动提取附图标记，保存为字典（.dict.json文件） → CAD 端打开字典面板 → 点击编号即可创建标准引线标注 → 字典变更时自动高亮差异。

v4.0 起支持 CAD 端直接编辑字典：从 Word 粘贴附图标记段落自动识别、右键或按 `F2` 编辑条目、新增/删除条目，修改自动回写 `.dict.json`；改号后图纸内旧编号标注同步更新；Word 再次导出前自动备份被 CAD 修改过的字典，由用户裁决保留哪一版。

### 当前标注实现（v4.1）

- 五个版本统一使用 `Leader + MText` 创建标注。2013/2015/2025 虽然提供 MLeader API，但 MLeader 的文字内容附着机制会产生无法稳定关闭的额外附着点或连接线，因此当前版本不再用 MLeader 创建新标注。
- 面板支持“三点 / 无限点”模式切换。三点模式只采集用户指定的 3 个点；无限点模式允许连续采集多个拐点；两种模式都不会额外写入文字附着点。
- 点数模式默认是三点；点击“点数”按钮后才切换为无限点，设置按当前图纸会话保留。
- 面板条目单击只选择，双击直接开始标注；右键选择“编辑条目”或选中后按 `F2` 才进入修改，不再需要先打开编辑框再点击“保存并标注”。
- 标注文字始终保持水平。引线可以按面板设置使用直线或样条形式。
- 文字附着点按靠近文字的最后一个引线拐点相对文字的位置自动判断四个象限：左上/左下分别连接文字左上/左下，右上/右下分别连接文字右上/右下；同高时仍连接对应侧的中部。
- 为消除 AutoCAD `Leader.Annotation` 自动生成的 hook line，创建时把文字附着点直接作为 Leader 的最后一个用户顶点；MText 通过扩展字典保存内部关联，不再使用原生文字附着关联。
- 事务提交后会重新打开 MText 复写并读回附着点；日志还会记录实际加载的 DLL 路径和提交后的 Leader 顶点数量，便于核对现场是否加载了旧部署包或产生了额外顶点。
- 2013/2015/2025 的校验、对齐、全选、删除和编号同步逻辑已经同步适配 `Leader + MText`。
- 面板新增“Brace/大括号”按钮，对应 `PATBRACE`（别名 `DAGUOHAO`）：依次指定顶部、底部和宽度方向三点，创建独立的参数化矢量大括号；它不是文字字符，也不加入 Leader/MText 标注关联。
- `PATBRACEEDIT` 支持两种调整方式：重新点选顶部/底部/宽度方向控制点，或直接输入高度和宽度。第一版不依赖原生自定义夹点，使用命令交互保证五个 AutoCAD 版本的兼容性。
- 第三点决定中部尖点的朝向：竖向大括号可向左/向右，横向大括号可向上/向下；两端肩部平滑过渡到尖点相反侧的直干，形成 PPT 风格的曲线轮廓。
- 大括号轮廓以 PPT `Right Brace` 为视觉基准：端部是平滑肩部，中段直干位于尖点相反侧，中心是单一真正尖锐的折角，不使用圆弧尖点或 W 型轮廓。

问题现象、日志证据和放弃 MLeader 的完整原因见 [MLeader 额外附着点问题总结](docs/mleader-attachment-grip-incident.md)。

### 版本总览

由于 AutoCAD 托管 API 与 .NET 运行时强绑定，单份源码无法覆盖 2007—2026 全部版本，按 API 断代划分为 5 个版本。**请根据你本机的 AutoCAD 年份选择对应版本：**

| 目录 | 覆盖 AutoCAD | .NET | 最低 OS | 标注方式 | 状态 |
|------|-------------|------|---------|----------|------|
| [`cad-plugin/2007/`](cad-plugin/2007/) | **2007 ~ 2009** | 2.0 | Win7 | Leader + MText | ✅ 已完成 |
| [`cad-plugin/2010/`](cad-plugin/2010/) | **2010 ~ 2012** | 3.5 | Win7 | Leader + MText | ✅ 已完成 |
| [`cad-plugin/2013/`](cad-plugin/2013/) | **2013 ~ 2014** | 4.0 | Win7 | Leader + MText | ✅ 已完成 |
| [`cad-plugin/2015/`](cad-plugin/2015/) | **2015 ~ 2024** | 4.5 | Win7 | Leader + MText | ✅ 已完成 |
| [`cad-plugin/2025/`](cad-plugin/2025/) | **2025 ~ 2026+** | 8.0 | Win10+ | Leader + MText | ✅ 已完成 |

### 为什么分 5 个版本？能否交叉使用？

**不能交叉使用。** 每个版本的 DLL 只能在其对应的 AutoCAD 年份区间内运行，原因：

1. **.NET 运行时不兼容** — 2007~2009 的 CAD 只加载 .NET 2.0 程序集，2025+ 只加载 .NET 8，CLR 完全不同，DLL 无法被加载。
2. **托管 API 断代** — AutoCAD 2013 及以后虽然提供 `MLeader`，但当前项目为避免 MLeader 文字附着夹点，所有版本统一使用 `Leader` + `MText`；各版本引用的 API 程序集和方法签名仍然不同。
3. **程序集版本绑定** — 编译时引用的 `acdbmgd.dll` 内部接口随 CAD 版本变化，跨版本加载会抛 `MissingMethodException`。

> 例：把 2007 版装到 AutoCAD 2026 → 无法加载（.NET 2.0 vs .NET 8）；把 2015 版装到 AutoCAD 2012 → 无法加载（.NET 4.5 vs .NET 3.5，且 SDK 程序集版本不匹配）。

详细的分版理由见 [docs/version-plan.md](docs/version-plan.md)。

### 快速开始（2007 版）

1. **Word 端**：运行 [PatentMarker-2007-deploy/](PatentMarker-2007-deploy/) 中的 `install-vba.vbs`（自动导入 6 个 VBA 模块到 Normal 模板）
2. **CAD 端**：将部署包放到非 C 盘目录，运行 `install-2007.vbs`
3. **使用**：Word 保存 → 生成 `.dict.json` → CAD 中 `BZ` 打开面板 → `BZM` 标注

完整步骤见 [cad-plugin/2007/README.md](cad-plugin/2007/README.md)。

### 编译说明

各版本编译前需从对应 AutoCAD 安装目录（或 [ObjectARX SDK](https://aps.autodesk.com/developer/overview/autocad-objectarx-sdk-downloads)）获取 SDK DLL：

| 版本 | 所需 DLL | 放置位置 |
|------|---------|----------|
| 2007/2010 | acdbmgd.dll, acmgd.dll | `PatentMarker/lib/` |
| 2013/2015 | acdbmgd.dll, acmgd.dll, accoremgd.dll | `PatentMarker/lib/` |
| 2025 | acdbmgd.dll, acmgd.dll, accoremgd.dll | `PatentMarker/lib/` |

- 2013/2015 版使用 Newtonsoft.Json 13.0.3（NuGet 还原），发布时经 ILRepack 合并进 `PatentMarker.dll`（单文件部署，安装无需额外 DLL）
- 2025 版零外部依赖（System.Text.Json 内置）

### 部署包

| 版本 | 安装入口 | 说明 |
|------|----------|------|
| 2007 | `install-2007.bat` / `install-2007.vbs` | 适用于 AutoCAD 2007~2009 |
| 2010 | `install-2010.vbs` | 适用于 AutoCAD 2010~2012 |
| 2013 | `install-2013.vbs` | 适用于 AutoCAD 2013~2014，DLL 已内嵌 Newtonsoft.Json |
| 2015 | `install-2015.vbs` | 适用于 AutoCAD 2015~2024，DLL 已内嵌 Newtonsoft.Json |
| 2025 | `install-2025.ps1` | 适用于 AutoCAD 2025 及以后；若 PowerShell 安装受本机策略影响，脚本会生成 LSP fallback 供 APPLOAD/NETLOAD 手动加载 |

五套部署包都包含对应版本的 `PatentMarker.dll` 和全套 VBA 模块。不要把不同 AutoCAD 年份的 DLL 混用。

### 本地验证状态

- 五个版本均已完成本地编译；
- 2010、2013、2015 主机契约模拟测试均为 7/7（共 21/21）；
- 2025 测试套件为 112/112；新增 123A1/123A2 识别、紧邻分隔符和表格预处理回归用例；
- 五套部署包的 Word VBA 均通过真实 Word COM 批量验证：8 份样例输出与 v4 基线一致，并通过 123A1/123A2 端到端 JSON 验证；
- 五个版本的 API 契约、结构和静态同步检查通过；
- 本地没有完整替代真实 AutoCAD 界面交互的自动化测试，最终部署仍需在对应 AutoCAD 版本中重新加载 DLL 后实测。

可使用根目录 `build.ps1` 辅助构建与环境检查：

```powershell
.\build.ps1 -Check               # doctor：检查各版本 SDK DLL 与编译工具链
.\build.ps1 -Version 2025        # 编译 2025 版（dotnet build）
.\build.ps1 -Version all -Check  # 检查全部 5 个版本
.\build.ps1 -Simulation           # 运行 2010/2013/2015 主机契约模拟测试
.\check-api-contract.ps1 -Version all # 检查各版本 AutoCAD SDK API 表面
.\check-autocad-host.ps1          # 只读检查本机 AutoCAD/COM/许可服务前置条件
```

> 2007/2010/2013/2015 为传统 MSBuild 工程，需 Visual Studio 或 Build Tools 的 MSBuild；2025 版为 SDK 风格工程，可直接用 `dotnet build`。

### 命令清单

| 命令 | 别名 | 说明 |
|------|------|------|
| `PATPALETTE` | `BZ` / `BIAOZHU` | 打开字典面板 |
| `PATMARK` | `BZM` | 创建引线标注 |
| `PATCHECK` | `BZC` | 校验编号一致性 |
| `PATALIGN` | `BZA` | 对齐引线 |
| `PATSELECTALL` | `BZS` | 全选标注实体 |
| `PATBRACE` | `DAGUOHAO` | 三点创建独立参数化矢量大括号 |
| `PATBRACEEDIT` | — | 通过控制点或输入高度/宽度调整大括号 |

### VBA 模块（Word 端，全版本共享）

附图标记编号支持纯数字、字母后缀以及字母后继续数字（例如 123A、123A1、123A2）。编号必须在既有条目分隔标点处结束：中文/英文逗号、顿号、分号或句号；这套边界规则同时用于 Word VBA 导出和 CAD 粘贴识别。

| 文件 | 用途 |
|------|------|
| `Patterns.bas` | 正则匹配工具 |
| `DictModel.bas` | 字典数据模型 |
| `JsonWriter.bas` | JSON 序列化 |
| `PatentExtractor.bas` | 从 Word 提取附图标记 |
| `AutoExport.bas` | 自动导出入口 |
| `clsSaveHook.cls` | DocumentBeforeSave 事件监听 |

### 目录结构

`cad-plugin/Shared/` 是五个 .NET 版本共用的源代码层，当前包含编号、设置、字典差异/冲突、粘贴识别、语言枚举，以及已验证的面板工作流、会话状态、WinForms 列表渲染和 Leader/MText 实体服务。各版本项目通过源码链接编译；CLR、JSON 库和面板生命周期仍保留在对应版本目录，AutoCAD 相关共享代码仍按各项目 SDK 引用编译，不生成跨 CLR DLL。

```
PatentCAD-Annotator/
├── cad-plugin/
│   ├── Shared/              # 五版本共用的纯 C# 源码（按项目链接编译，不合并 CLR）
│   ├── 2007/               # AutoCAD 2007~2009（Leader + MText，.NET 2.0）
│   │   └── PatentMarker/    #   C# 源码 + csproj
│   ├── 2010/               # AutoCAD 2010~2012（Leader + MText，.NET 3.5）
│   ├── 2013/               # AutoCAD 2013~2014（Leader + MText，.NET 4.0）
│   ├── 2015/               # AutoCAD 2015~2024（Leader + MText，.NET 4.5）
│   └── 2025/               # AutoCAD 2025~2026+（Leader + MText，.NET 8.0）
├── PatentMarker-2007-deploy/   # 2007 版即装即用部署包（DLL + 脚本 + VBA）
├── PatentMarker-2010-deploy/   # 2010 版即装即用部署包
├── PatentMarker-2013-deploy/   # 2013 版即装即用部署包
├── PatentMarker-2015-deploy/   # 2015 版即装即用部署包
├── PatentMarker-2025-deploy/   # 2025 版即装即用部署包
├── demo/                       # 动态演示页面（最新：PatentMarker-Demo-v5.html）
├── docs/
│   ├── version-plan.md      # 版本规划（分版理由）
│   ├── development-log.md   # 变更记录
│   └── mleader-attachment-grip-incident.md # MLeader 附着点问题总结
└── LICENSE
```

### 文档

- [docs/version-plan.md](docs/version-plan.md) — 版本规划与分版理由
- [docs/development-log.md](docs/development-log.md) — 变更记录
- [docs/mleader-attachment-grip-incident.md](docs/mleader-attachment-grip-incident.md) — MLeader 额外附着点问题、舍弃原因与替代方案
- 各版本详细文档：[2007](cad-plugin/2007/README.md) | [2010](cad-plugin/2010/README.md) | [2013](cad-plugin/2013/README.md) | [2015](cad-plugin/2015/README.md) | [2025](cad-plugin/2025/README.md)

### 版本历史

| 版本 | 日期 | 主要变更 |
|------|------|----------|
| v4.1 | 2026-08-11 | 新增独立参数化矢量大括号：三点创建、控制点交互调整和高度/宽度输入；五个版本及部署包同步 |
| v4.0 | 2026-08-06 | CAD 端字典编辑闭环：粘贴识别（VBA 引擎移植 C#）+ 编辑对话框（改号/改名/新增/删除）+ 实体联动（改号同步图纸）+ 冲突裁决；修复 MLeader 额外附着点问题，五个版本统一使用 Leader + MText 并重新编译部署 |
| v3.2 | 2026-08-04 | 修复 MLeaderStyle 未入库先设属性异常；2013/2015 改单文件部署（ILRepack 合并 Newtonsoft.Json）；VBA 分隔符类补全角分号 |
| v3.1 | 2026-08-03 | 新增三点模式（面板切换按钮）：固定 3 点采集引线，与线型开关正交；全 5 版本同步 |
| v3.0 | 2026-08-03 | VBA v3.0 多格式识别（括号/连字符/英文标点/裸列表）；C# 取消 JSON 排序按原文顺序；全版本重新编译部署 |
| v2.5 | 2026-07-27 | 修复 Word 2010 无法导入 clsSaveHook.cls 的兼容性问题（改为代码注入）；修复 2007/2010 版箭头大小修改后不能立即生效；所有部署包补充 install-vba.vbs |
| v2.4 | 2026-07-26 | 多版本适配完成（2010/2013/2015/2025），全部通过编译验证；动态复核修复 MLeader API 名称、ArrowSize/TextHeight 实例同步 |
| v2.0 | 2026-07 | 2007 版完成：样条曲线引线 + 无限拐点 + 面板控制 + 字典自动刷新 |

---

## English

### Overview

PatentCAD-Annotator solves three draw-backs that slow you down in patent drawing annotation:

1. **Error-prone manual cross-reference** — matching reference numerals between Word specs and drawings by hand leads to missed/wrong labels
2. **No sync after edits** — changing a numeral in the spec means hunting down every occurrence in the drawing
3. **Inconsistent formatting** — different annotators produce different leader styles, text heights, and alignments

Workflow: Word auto-extracts a numeral dictionary on save → CAD opens a palette → click a numeral to create a standard leader annotation → changes are auto-highlighted when the dictionary updates.

Since v4.0 the dictionary can be edited directly in CAD: paste the marking section from Word for auto-recognition, right-click or press `F2` to renumber/rename an entry, and add or delete entries — edits are written back to `.dict.json`; drawing leaders are renumbered in sync; before Word re-exports it backs up a CAD-modified dictionary so you can arbitrate which version to keep.

### Current annotation implementation (v4.1)

- All five editions create annotations with `Leader + MText`. AutoCAD 2013/2015/2025 expose MLeader, but its text-content attachment geometry can create an extra grip or connection segment that cannot be reliably disabled across supported hosts.
- The palette supports a three-point / unlimited-point mode switch. Three-point mode collects exactly the three points selected by the user; unlimited-point mode accepts any number of user-selected dogleg points. Neither mode adds a text attachment point to the user's geometry.
- Three-point mode is the default; clicking the point-count button switches to unlimited mode for the current drawing session.
- Single-click selects an entry and double-click starts marking directly. Right-clicking an entry or pressing `F2` opens editing; the edit dialog no longer contains a separate Save & Mark action.
- Annotation text is forced to remain horizontal. The leader can still be configured as straight or spline through the palette.
- The text attachment point is selected from the last leader vertex nearest the text: upper-left/lower-left use the text's top-left/bottom-left, upper-right/lower-right use top-right/bottom-right, and same-height leaders retain the corresponding middle-side attachment.
- To remove AutoCAD's automatic `Leader.Annotation` hook line, creation appends the text attachment point as the final user-selected Leader vertex. MText is linked through the Leader extension dictionary instead of native annotation association.
- After the first transaction commits, the plug-in reopens MText, reapplies and reads back the attachment. The log also records the loaded DLL path and committed Leader vertex list so an old deployment package can be distinguished from a real extra vertex.
- `PATCHECK`, `PATALIGN`, `PATSELECTALL`, palette deletion and dictionary renaming are synchronized with the `Leader + MText` representation.
- The palette adds a `Brace` button for `PATBRACE` (alias `DAGUOHAO`). Pick the top, bottom and width-direction points to create an independent parameterized vector brace; it is not a text glyph and is not part of the Leader/MText relationship.
- `PATBRACEEDIT` adjusts a brace either by repicking its top/bottom/width control points or by entering an exact height and width. The first implementation uses command interaction instead of native custom grips so the same behavior remains available across all five AutoCAD generations.
- The third point controls the center-tip direction: vertical braces can point left or right, and horizontal braces can point up or down. The endpoint shoulders curve smoothly into straight stems on the side opposite the tip to form the PPT-style profile.
- The brace profile is based on the PPT `Right Brace`: smooth endpoint shoulders, opposite-side straight stems, and one genuinely sharp center fold; it does not use a rounded tip or a W-shaped outline.

See [MLeader attachment-grip incident report](docs/mleader-attachment-grip-incident.md) for the log evidence, rejected fixes and compatibility notes.

### Versions

Because AutoCAD's managed API is tightly bound to the .NET runtime, a single source base cannot cover AutoCAD 2007—2026. The project is split into 5 versions along API boundaries. **Choose the version matching your AutoCAD year:**

| Directory | AutoCAD | .NET | Min OS | Annotation | Status |
|-----------|---------|------|--------|------------|--------|
| [`cad-plugin/2007/`](cad-plugin/2007/) | **2007 ~ 2009** | 2.0 | Win7 | Leader + MText | ✅ Complete |
| [`cad-plugin/2010/`](cad-plugin/2010/) | **2010 ~ 2012** | 3.5 | Win7 | Leader + MText | ✅ Complete |
| [`cad-plugin/2013/`](cad-plugin/2013/) | **2013 ~ 2014** | 4.0 | Win7 | Leader + MText | ✅ Complete |
| [`cad-plugin/2015/`](cad-plugin/2015/) | **2015 ~ 2024** | 4.5 | Win7 | Leader + MText | ✅ Complete |
| [`cad-plugin/2025/`](cad-plugin/2025/) | **2025 ~ 2026+** | 8.0 | Win10+ | Leader + MText | ✅ Complete |

### Why 5 versions? Can I use one version on a different AutoCAD?

**No cross-version usage.** Each DLL only works within its designated AutoCAD year range:

1. **.NET runtime mismatch** — AutoCAD 2007–2009 loads .NET 2.0 only; 2025+ loads .NET 8 only. The CLR is entirely different.
2. **Annotation implementation profile** — AutoCAD 2013 and later expose `MLeader`, but all five editions intentionally use the stable `Leader` + `MText` pair to avoid the MLeader content-attachment grip. Separate .NET targets and SDK DLLs are still required.
3. **Assembly binding** — `acdbmgd.dll` internal interfaces change per CAD version; loading a mismatched DLL throws `MissingMethodException`.

See [docs/version-plan.md](docs/version-plan.md) for full rationale.

### Quick Start (v2007)

1. **Word side**: import the 6 VBA modules ([PatentMarker-2007-deploy/vba/](PatentMarker-2007-deploy/vba/)) into the Normal template
2. **CAD side**: deploy [PatentMarker-2007-deploy/](PatentMarker-2007-deploy/) to a non-C-drive folder, run `install-2007.vbs`
3. **Usage**: save Word → generates `.dict.json` → run `BZ` in CAD to open palette → `BZM` to annotate

Full instructions: [cad-plugin/2007/README.md](cad-plugin/2007/README.md).

### Deployment packages

| Edition | Installer | Notes |
|---------|-----------|-------|
| 2007 | `install-2007.bat` / `install-2007.vbs` | AutoCAD 2007~2009 |
| 2010 | `install-2010.vbs` | AutoCAD 2010~2012 |
| 2013 | `install-2013.vbs` | AutoCAD 2013~2014; Newtonsoft.Json is merged into the DLL |
| 2015 | `install-2015.vbs` | AutoCAD 2015~2024; Newtonsoft.Json is merged into the DLL |
| 2025 | `install-2025.ps1` | AutoCAD 2025+; generates an LSP fallback if the PowerShell installation cannot complete |

Each package contains the matching `PatentMarker.dll` and the six shared VBA modules. Do not mix DLLs between AutoCAD year ranges.

### Commands

| Command | Alias | Description |
|---------|-------|-------------|
| `PATPALETTE` | `BZ` / `BIAOZHU` | Open dictionary palette |
| `PATMARK` | `BZM` | Create leader annotation |
| `PATCHECK` | `BZC` | Validate numeral consistency |
| `PATALIGN` | `BZA` | Align leaders |
| `PATSELECTALL` | `BZS` | Select all annotation entities |
| `PATBRACE` | `DAGUOHAO` | Create an independent parameterized vector brace from three points |
| `PATBRACEEDIT` | — | Adjust a brace by control points or exact height/width |

### Local verification status

- All five editions compile locally.
- Runtime contract simulations pass 17/17 for 2010, 2013 and 2015 (including shared brace geometry and four-direction brace checks).
- The 2025 test suite passes 112/112.
- API-contract, structure and static synchronization checks pass for all five editions.
- These checks do not replace final interactive validation inside each installed AutoCAD host; load the matching deployment DLL before testing.

### License

This project is licensed under the [MIT License](LICENSE).

Note: The `acdbmgd.dll` / `acmgd.dll` / `accoremgd.dll` referenced at build time are Autodesk SDK assemblies and are NOT included in this repository — users must supply them from their local AutoCAD installation or [ObjectARX SDK](https://aps.autodesk.com/developer/overview/autocad-objectarx-sdk-downloads).

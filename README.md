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

v4.0 起支持 CAD 端直接编辑字典：从 Word 粘贴附图标记段落自动识别、双击条目改号/改名、新增/删除条目，修改自动回写 `.dict.json`；改号后图纸内旧编号标注同步更新；Word 再次导出前自动备份被 CAD 修改过的字典，由用户裁决保留哪一版。

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
2. **标注 API 断代** — AutoCAD 2013 引入 `MLeader`，之前的版本只有 `Leader` + `MText`；两套 API 的类名、方法签名完全不同。
3. **程序集版本绑定** — 编译时引用的 `acdbmgd.dll` 内部接口随 CAD 版本变化，跨版本加载会抛 `MissingMethodException`。

> 例：把 2007 版装到 AutoCAD 2026 → 无法加载（.NET 2.0 vs .NET 8）；把 2015 版装到 AutoCAD 2012 → 无法加载（.NET 4.5 vs .NET 3.5，且缺少 MLeader）。

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
- 全部 5 个版本均已通过编译验证

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

### VBA 模块（Word 端，全版本共享）

| 文件 | 用途 |
|------|------|
| `Patterns.bas` | 正则匹配工具 |
| `DictModel.bas` | 字典数据模型 |
| `JsonWriter.bas` | JSON 序列化 |
| `PatentExtractor.bas` | 从 Word 提取附图标记 |
| `AutoExport.bas` | 自动导出入口 |
| `clsSaveHook.cls` | DocumentBeforeSave 事件监听 |

### 目录结构

```
PatentCAD-Annotator/
├── cad-plugin/
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
├── demo/                       # 动态演示页面
├── docs/
│   ├── version-plan.md      # 版本规划（分版理由）
│   └── development-log.md   # 变更记录
└── LICENSE
```

### 文档

- [docs/version-plan.md](docs/version-plan.md) — 版本规划与分版理由
- [docs/development-log.md](docs/development-log.md) — 变更记录
- 各版本详细文档：[2007](cad-plugin/2007/README.md) | [2010](cad-plugin/2010/README.md) | [2013](cad-plugin/2013/README.md) | [2015](cad-plugin/2015/README.md) | [2025](cad-plugin/2025/README.md)

### 版本历史

| 版本 | 日期 | 主要变更 |
|------|------|----------|
| v4.0 | 2026-08-04 | CAD 端字典编辑闭环：粘贴识别（VBA 引擎移植 C#）+ 编辑对话框（改号/改名/新增/删除）+ 实体联动（改号同步图纸）+ 冲突裁决（Word 覆盖 CAD 修改时备份+三选一裁决）；全部 5 版本同步 |
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

Since v4.0 the dictionary can be edited directly in CAD: paste the marking section from Word for auto-recognition, double-click an entry to renumber/rename, add or delete entries — edits are written back to `.dict.json`; drawing leaders are renumbered in sync; before Word re-exports it backs up a CAD-modified dictionary so you can arbitrate which version to keep.

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

### Commands

| Command | Alias | Description |
|---------|-------|-------------|
| `PATPALETTE` | `BZ` / `BIAOZHU` | Open dictionary palette |
| `PATMARK` | `BZM` | Create leader annotation |
| `PATCHECK` | `BZC` | Validate numeral consistency |
| `PATALIGN` | `BZA` | Align leaders |
| `PATSELECTALL` | `BZS` | Select all annotation entities |

### License

This project is licensed under the [MIT License](LICENSE).

Note: The `acdbmgd.dll` / `acmgd.dll` / `accoremgd.dll` referenced at build time are Autodesk SDK assemblies and are NOT included in this repository — users must supply them from their local AutoCAD installation or [ObjectARX SDK](https://aps.autodesk.com/developer/overview/autocad-objectarx-sdk-downloads).

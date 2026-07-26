# PatentCAD-Annotator

**AutoCAD 专利图纸标注插件** — 从 Word 说明书提取附图标记，在 AutoCAD 图纸中一键标注并保持双向同步。

**AutoCAD patent drawing annotation plugin** — Extract reference numerals from Word specifications, annotate them in AutoCAD drawings with one click, and keep them in sync.

---

## 中文说明

### 项目简介

PatentCAD-Annotator 解决专利图纸标注的三个痛点：

1. **人工对照易错** — Word 说明书里的附图标记编号与图纸手动对照，容易漏标/错标
2. **修改后不同步** — 说明书改了编号，图纸要逐个找出来改
3. **格式不统一** — 不同人标注的引线样式、文字高度、对齐方式参差不齐

PatentCAD-Annotator 的工作流：Word 保存时自动提取编号字典 → CAD 端打开字典面板 → 点击编号即可创建标准引线标注 → 字典变更时自动高亮差异。

### 版本总览

由于 AutoCAD 托管 API 与 .NET 运行时强绑定，单份源码无法覆盖 2007—2026 全部版本，按 API 断代划分为 5 个版本。**请根据你本机的 AutoCAD 年份选择对应版本：**

| 目录 | 覆盖 AutoCAD | .NET | 最低 OS | 标注方式 | 状态 |
|------|-------------|------|---------|----------|------|
| [`cad-plugin/2007/`](cad-plugin/2007/) | **2007 ~ 2009** | 2.0 | Win7 | Leader + MText | ✅ 已完成 |
| [`cad-plugin/2010/`](cad-plugin/2010/) | **2010 ~ 2012** | 3.5 | Win7 | Leader + MText | ✅ 已完成 |
| [`cad-plugin/2013/`](cad-plugin/2013/) | **2013 ~ 2014** | 4.0 | Win7 | MLeader | ✅ 已完成 |
| [`cad-plugin/2015/`](cad-plugin/2015/) | **2015 ~ 2024** | 4.5 | Win7 | MLeader | ✅ 已完成 |
| [`cad-plugin/2025/`](cad-plugin/2025/) | **2025 ~ 2026+** | 8.0 | Win10+ | MLeader | ✅ 已完成 |

### 为什么分 5 个版本？能否交叉使用？

**不能交叉使用。** 每个版本的 DLL 只能在其对应的 AutoCAD 年份区间内运行，原因：

1. **.NET 运行时不兼容** — 2007~2009 的 CAD 只加载 .NET 2.0 程序集，2025+ 只加载 .NET 8，CLR 完全不同，DLL 无法被加载。
2. **标注 API 断代** — AutoCAD 2013 引入 `MLeader`，之前的版本只有 `Leader` + `MText`；两套 API 的类名、方法签名完全不同。
3. **程序集版本绑定** — 编译时引用的 `acdbmgd.dll` 内部接口随 CAD 版本变化，跨版本加载会抛 `MissingMethodException`。

> 例：把 2007 版装到 AutoCAD 2026 → 无法加载（.NET 2.0 vs .NET 8）；把 2015 版装到 AutoCAD 2012 → 无法加载（.NET 4.5 vs .NET 3.5，且缺少 MLeader）。

详细的分版理由见 [docs/version-plan.md](docs/version-plan.md)。

### 快速开始（2007 版）

1. **Word 端**：导入 VBA 模块（[cad-plugin/2007/deploy/vba/](cad-plugin/2007/deploy/vba/) 6 个文件）到 Normal 模板
2. **CAD 端**：部署 [cad-plugin/2007/deploy/](cad-plugin/2007/deploy/) 到非 C 盘目录，运行 `install-2007.vbs`
3. **使用**：Word 保存 → 生成 `.dict.json` → CAD 中 `BZ` 打开面板 → `BZM` 标注

完整步骤见 [cad-plugin/2007/README.md](cad-plugin/2007/README.md)。

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
│   │   ├── PatentMarker/    #   C# 源码 + csproj
│   │   └── deploy/          #   安装脚本 + DLL + VBA 模块
│   ├── 2010/               # AutoCAD 2010~2012（Leader + MText，.NET 3.5）
│   ├── 2013/               # AutoCAD 2013~2014（MLeader，.NET 4.0）
│   ├── 2015/               # AutoCAD 2015~2024（MLeader，.NET 4.5）
│   └── 2025/               # AutoCAD 2025~2026+（MLeader，.NET 8.0）
├── docs/
│   ├── version-plan.md      # 版本规划（分版理由）
│   └── autocad-2007-downgrade-plan.md  # 2007 降级方案
├── PatentMarker-Demo.html   # 动态演示（10 场景）
└── README.md
```

### 文档

- [docs/development-log.md](docs/development-log.md) — **开发日志与经验教训**（v2.10→v2.12 所有改动、踩坑记录、编码注意事项）
- [docs/version-plan.md](docs/version-plan.md) — 版本规划与分版理由
- [docs/autocad-2007-downgrade-plan.md](docs/autocad-2007-downgrade-plan.md) — 2007 降级实现方案
- [cad-plugin/2007/README.md](cad-plugin/2007/README.md) — 2007 版详细文档

---

## English

### Overview

PatentCAD-Annotator solves three pain points in patent drawing annotation:

1. **Error-prone manual cross-reference** — matching reference numerals between Word specs and drawings by hand leads to missed/wrong labels
2. **No sync after edits** — changing a numeral in the spec means hunting down every occurrence in the drawing
3. **Inconsistent formatting** — different annotators produce different leader styles, text heights, and alignments

Workflow: Word auto-extracts a numeral dictionary on save → CAD opens a palette → click a numeral to create a standard leader annotation → changes are auto-highlighted when the dictionary updates.

### Versions

Because AutoCAD's managed API is tightly bound to the .NET runtime, a single source base cannot cover AutoCAD 2007—2026. The project is split into 5 versions along API boundaries. **Choose the version matching your AutoCAD year:**

| Directory | AutoCAD | .NET | Min OS | Annotation | Status |
|-----------|---------|------|--------|------------|--------|
| [`cad-plugin/2007/`](cad-plugin/2007/) | **2007 ~ 2009** | 2.0 | Win7 | Leader + MText | ✅ Complete |
| [`cad-plugin/2010/`](cad-plugin/2010/) | **2010 ~ 2012** | 3.5 | Win7 | Leader + MText | ✅ Complete |
| [`cad-plugin/2013/`](cad-plugin/2013/) | **2013 ~ 2014** | 4.0 | Win7 | MLeader | ✅ Complete |
| [`cad-plugin/2015/`](cad-plugin/2015/) | **2015 ~ 2024** | 4.5 | Win7 | MLeader | ✅ Complete |
| [`cad-plugin/2025/`](cad-plugin/2025/) | **2025 ~ 2026+** | 8.0 | Win10+ | MLeader | ✅ Complete |

### Why 5 versions? Can I use one version on a different AutoCAD?

**No cross-version usage.** Each DLL only works within its designated AutoCAD year range:

1. **.NET runtime mismatch** — AutoCAD 2007–2009 loads .NET 2.0 only; 2025+ loads .NET 8 only. The CLR is entirely different.
2. **Annotation API break** — `MLeader` was introduced in AutoCAD 2013; earlier versions only have `Leader` + `MText`.
3. **Assembly binding** — `acdbmgd.dll` internal interfaces change per CAD version; loading a mismatched DLL throws `MissingMethodException`.

See [docs/version-plan.md](docs/version-plan.md) for full rationale.

### Quick Start (v2007)

1. **Word side**: import the 6 VBA modules ([cad-plugin/2007/deploy/vba/](cad-plugin/2007/deploy/vba/)) into the Normal template
2. **CAD side**: deploy [cad-plugin/2007/deploy/](cad-plugin/2007/deploy/) to a non-C-drive folder, run `install-2007.vbs`
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

This project is for internal use. The `acdbmgd.dll` / `acmgd.dll` referenced at build time are Autodesk SDK assemblies and are NOT included in this repository — users must supply them from their local AutoCAD installation.

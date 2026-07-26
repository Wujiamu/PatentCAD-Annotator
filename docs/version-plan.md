# PatentMarker 版本规划

## 概述

PatentMarker 面向 2007 至今的全部 AutoCAD 版本，需覆盖 Win7 到 Win11 用户。由于 AutoCAD 的托管 API（acdbmgd/acmgd）与 .NET 运行时强绑定，且不同 AutoCAD 版本的 API 能力不同（最关键的断代是 MLeader 的引入），单份源码无法覆盖全部目标。

最终划分为 **5 个版本**，其中 4 个兼容 Win7，1 个面向 Win10+。

## 版本总览

| 目录 | 覆盖 AutoCAD | .NET | 最低 OS | 标注方式 | JSON 库 |
|---|---|---|---|---|---|
| `cad-plugin/2007/` | 2007, 2008, 2009 | 2.0 | Win7 | Leader + MText（组合） | SimpleJson（手写，零依赖） |
| `cad-plugin/2010/` | 2010, 2011, 2012 | 3.5 | Win7 | Leader + MText（组合） | SimpleJson（手写，零依赖） |
| `cad-plugin/2013/` | 2013, 2014 | 4.0 | Win7 | MLeader | Newtonsoft.Json |
| `cad-plugin/2015/` | 2015—2024 | 4.5 | Win7 | MLeader | Newtonsoft.Json |
| `cad-plugin/2025/` | 2025, 2026+ | 8.0 | Win10+ | MLeader | System.Text.Json（内置） |

## 分版理由

### 1. 标注 API 断代：MLeader 引入（2013）

AutoCAD 2013（R19.0）首次引入 `MLeader` 类。在此之前只能用 `Leader` + `MText` 两个对象拼合实现标注。这是源码层面最大的差异——两个 API 的点击流程、属性设置、对齐逻辑完全不同。

- `cad-plugin/2007/` 和 `cad-plugin/2010/`：使用 `Leader` + `MText` 组合
- `cad-plugin/2013/` 及之后：使用 `MLeader`

### 2. .NET Framework 断代：2.0 / 3.5 / 4.0 / 4.5 / 8.0

| .NET | 区分原因 |
|---|---|
| 2.0 | 2007—2009 的 AutoCAD 仅加载 .NET 2.0 运行时。无 LINQ、无 `Action<T>`、无 `Func<T>`、无 NuGet 生态。 |
| 3.5 | 2010—2012 可加载 .NET 3.5。多了 `System.Core`（LINQ）、`HashSet<T>`、`Action`/`Func`。如果把 2007 版用 2.0 编译给 2010 用户，他们无法使用 3.5 的 API；反过来，用 3.5 编译的 DLL 在 2007 上无法加载。 |
| 4.0 | 2013—2014 使用 .NET 4.0。引入 `dynamic`、`Tuple`、PLINQ。不支持 SDK 风格 csproj（需 MSBuild 15+，.NET 4.0 自带的是 MSBuild 4.0）。 |
| 4.5 | 2015—2024 使用 .NET 4.5→4.8。SDK 风格 csproj 可用，NuGet 完全正常。选 4.5 作为 TargetFramework 是因为它是这个区间各 AutoCAD 版本自带的 .NET 最低版本，用 4.5 编译的 DLL 在 4.5~4.8 全系可跑，不需要用户额外安装 .NET。 |
| 8.0 | 2025+ 迁移到 .NET 8（Core），彻底脱离 .NET Framework。`System.Text.Json` 内置，无需 NuGet 依赖。**不支持 Win7**（.NET 8 最低要求 Win10 1607）。 |

### 3. Win7 兼容性

| .NET 版本 | Win7 支持 | 获取方式 |
|---|---|---|
| 2.0 / 3.0 / 3.5 SP1 | 预装 | 系统自带 |
| 4.0 | 可装 | Windows Update |
| 4.5 | 可装 | Windows Update |
| 4.6.x | 可装 | Windows Update |
| 4.7.x | 可装 | Windows Update |
| 4.8 | 可装（Win7 上限） | Windows Update |
| 8.0 | **不支持** | 最低 Win10 1607 |

- 前 4 个版本全部兼容 Win7，且所需的 .NET 版本要么系统预装，要么 Windows Update 可直接获取。
- `cad-plugin/2025/` 仅面向 Win10+，因为 AutoCAD 2025 本身也不支持 Win7。

### 4. 同组内不同 AutoCAD 年号的适配

同一组内（如 2015—2024），源代码完全相同，唯一差异是 `acdbmgd.dll` / `acmgd.dll` 的 HintPath 指向不同安装目录。用户在 README 指引下修改 `.csproj` 中的 `<HintPath>` 即可编译。无需为每个年号建独立目录。

## 当前工作区状态

| 版本 | 现状 |
|---|---|
| `cad-plugin/2007/` | ✅ 已完成，含自动同步（VBA 钩子 + DictLoader 时间戳检测） |
| `cad-plugin/2010/` | ✅ 已完成，从 2007 派生，.NET 3.5，Leader + MText |
| `cad-plugin/2013/` | ✅ 已完成，MLeader 重写，.NET 4.0，Newtonsoft.Json |
| `cad-plugin/2015/` | ✅ 已完成，从 2013 派生，.NET 4.5，覆盖 2015-2024 |
| `cad-plugin/2025/` | ✅ 已完成，.NET 8，System.Text.Json，零外部依赖 |

### 待补项

全部完成：

1. ✅ **创建 `cad-plugin/2010/`**：从 2007 源码复制，TargetFramework v3.5，添加 System.Core
2. ✅ **创建 `cad-plugin/2013/`**：MLeader 重写，.NET 4.0，Newtonsoft.Json，accoremgd
3. ✅ **创建 `cad-plugin/2015/`**：从 2013 源码复制，TargetFramework v4.5
4. ✅ **创建 `cad-plugin/2025/`**：.NET 8 SDK 风格，System.Text.Json，零依赖
5. ✅ **自动同步功能**：全版本均已包含 DictLoader 时间戳检测 + 字典对比

## VBA 模块

VBA 模块（Word 端）与 CAD 版本无关，全版本共享，位于 `vba/` 目录：

| 文件 | 用途 |
|---|---|
| `Patterns.bas` | 正则匹配工具 |
| `DictModel.bas` | 字典数据模型 |
| `JsonWriter.bas` | JSON 序列化 |
| `PatentExtractor.bas` | 从 Word 提取附图标记 |
| `AutoExport.bas` | 自动导出入口（AutoOpen 注册钩子） |
| `clsSaveHook.cls` | DocumentBeforeSave 事件监听 |

## 仓库结构

```
PatentMarker/
├── cad-plugin/
│   ├── 2007/
│   │   ├── PatentMarker/          # .csproj + 源码
│   │   └── deploy/                # install-2007.ps1, install-2007.vbs
│   ├── 2010/
│   │   ├── PatentMarker/
│   │   └── deploy/
│   ├── 2013/
│   │   ├── PatentMarker/
│   │   └── deploy/
│   ├── 2015/
│   │   ├── PatentMarker/
│   │   └── deploy/
│   └── 2025/
│       ├── PatentMarker/
│       └── deploy/
├── vba/                           # 共享 VBA 模块（全版本通用）
│   ├── AutoExport.bas
│   ├── clsSaveHook.cls
│   ├── DictModel.bas
│   ├── JsonWriter.bas
│   ├── PatentExtractor.bas
│   ├── Patterns.bas
│   └── 导入说明.txt
├── PatentMarker-Demo.html         # 动态演示
├── README.md
└── docs/
    └── version-plan.md            # 本文档
```

## 不创建的分支

- 不使用 `release/2007`、`release/2014` 等分支策略。5 个版本共存于 `main` 分支，避免多分支间同步 11 个源文件的开销。
- 发布时用 Git Tags（如 `v1.0.0`），Release Assets 中上传 5 个 DLL 及对应部署脚本。
# 版本规划

## 概述

PatentCAD-Annotator 面向 AutoCAD 2007 至 2026+ 全部版本，需覆盖 Win7 到 Win11 用户。由于 AutoCAD 的托管 API 与 .NET 运行时强绑定，且不同版本的 API 能力存在断代（最关键的是 MLeader 的引入），单份源码无法覆盖全部目标。

最终划分为 **5 个版本**，其中 4 个兼容 Win7，1 个面向 Win10+。

## 版本总览

| 目录 | 覆盖 AutoCAD | .NET | 最低 OS | 标注方式 | JSON 库 |
|---|---|---|---|---|---|
| `cad-plugin/2007/` | 2007, 2008, 2009 | 2.0 | Win7 | Leader + MText | SimpleJson（零依赖） |
| `cad-plugin/2010/` | 2010, 2011, 2012 | 3.5 | Win7 | Leader + MText | SimpleJson（零依赖） |
| `cad-plugin/2013/` | 2013, 2014 | 4.0 | Win7 | MLeader | Newtonsoft.Json（ILRepack 合并） |
| `cad-plugin/2015/` | 2015—2024 | 4.5 | Win7 | MLeader | Newtonsoft.Json（ILRepack 合并） |
| `cad-plugin/2025/` | 2025, 2026+ | 8.0 | Win10+ | MLeader | System.Text.Json（内置） |

## 分版理由

### 1. 标注 API 断代：MLeader 引入（2013）

AutoCAD 2013 首次引入 `MLeader` 类。在此之前只能用 `Leader` + `MText` 两个对象拼合实现标注。两套 API 的交互流程、属性设置、对齐逻辑完全不同。

### 2. .NET Framework 断代

| .NET | 区分原因 |
|---|---|
| 2.0 | 2007—2009 仅加载 .NET 2.0 运行时。无 LINQ、无 NuGet 生态。 |
| 3.5 | 2010—2012 可加载 .NET 3.5。多了 LINQ、`HashSet<T>`。与 2.0 编译的 DLL 不可互用。 |
| 4.0 | 2013—2014 使用 .NET 4.0。引入 `dynamic`、`Tuple`。 |
| 4.5 | 2015—2024 使用 .NET 4.5+。选 4.5 作为目标是因为它是该区间各 AutoCAD 自带的最低版本，编译的 DLL 在 4.5~4.8 全系可跑。 |
| 8.0 | 2025+ 迁移到 .NET 8（Core），脱离 .NET Framework。**不支持 Win7**。 |

### 3. Win7 兼容性

前 4 个版本全部兼容 Win7。`cad-plugin/2025/` 仅面向 Win10+，因为 AutoCAD 2025 本身也不支持 Win7。

### 4. 同组内不同 AutoCAD 年号的适配

同一组内（如 2015—2024），源代码完全相同，唯一差异是 `acdbmgd.dll` 的引用路径。用户修改 `.csproj` 中的 `<HintPath>` 即可编译。

## 不能交叉使用

每个版本的 DLL 只能在其对应的 AutoCAD 年份区间内运行：

1. **.NET 运行时不兼容** — 2007 只加载 .NET 2.0 程序集，2025 只加载 .NET 8，CLR 完全不同。
2. **标注 API 不同** — 2013 之前只有 `Leader`，之后才有 `MLeader`。
3. **程序集版本绑定** — 编译时引用的 `acdbmgd.dll` 内部接口随 CAD 版本变化，跨版本加载会抛 `MissingMethodException`。

## VBA 模块

VBA 模块（Word 端）与 CAD 版本无关，全版本共享：

| 文件 | 用途 |
|---|---|
| `Patterns.bas` | 正则匹配工具 |
| `DictModel.bas` | 字典数据模型 |
| `JsonWriter.bas` | JSON 序列化 |
| `PatentExtractor.bas` | 从 Word 提取附图标记 |
| `AutoExport.bas` | 自动导出入口 |
| `clsSaveHook.cls` | DocumentBeforeSave 事件监听 |

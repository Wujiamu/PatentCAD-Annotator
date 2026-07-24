# PatentMarker 2025 — AutoCAD 2025/2026+ 适配版

> **状态：规划中（TODO）**
> 本目录尚未实现。AutoCAD 2025 迁移到 .NET 8，不再支持 Win7。

## 目标环境

| 项目 | 值 |
|------|-----|
| 操作系统 | Windows 10 1607+ / Windows 11 |
| AutoCAD | 2025, 2026+ (R25+) |
| .NET | 8.0 (Core，脱离 .NET Framework) |
| 编译器 | VS2022+ / .NET SDK 风格 csproj |

## 与 2015 版的差异

| 特性 | 2015 版 | 2025 版（计划） |
|------|---------|----------------|
| .NET | 4.5 (Framework) | 8.0 (Core) |
| Win7 | 支持 | **不支持**（.NET 8 最低 Win10 1607） |
| JSON | Newtonsoft.Json | System.Text.Json（内置，零 NuGet 依赖） |
| csproj | 旧式或 SDK 风格 | SDK 风格 |
| 标注方式 | MLeader | MLeader（同 2015） |

## 实现计划

1. 新建 SDK 风格 csproj，TargetFramework = `net8.0-windows`
2. 用 `System.Text.Json` 替代 Newtonsoft.Json
3. 可用 C# 12+ 语法（record、pattern matching 等）
4. 部署用 ApplicationPlugins bundle
5. 同步全部功能（MLeader、样条曲线、字典比对、全选）

详见 [docs/version-plan.md](../../docs/version-plan.md)。

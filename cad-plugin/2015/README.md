# PatentMarker 2015 — AutoCAD 2015—2024 适配版

> **状态：规划中（TODO）**
> 本目录尚未实现，源码将从 `cad-plugin/2013/` 派生。

## 目标环境

| 项目 | 值 |
|------|-----|
| 操作系统 | Windows 7 SP1 — Windows 11 |
| AutoCAD | 2015, 2016, ..., 2024 (R20.0—R24.x) |
| .NET | 4.5 (CLR 4.0，兼容 4.5—4.8) |
| 编译器 | VS2015+ (SDK 风格 csproj 可用) |

## 与 2013 版的差异

| 特性 | 2013 版 | 2015 版（计划） |
|------|---------|----------------|
| .NET | 4.0 | 4.5（SDK 风格 csproj，NuGet 完全正常） |
| 标注方式 | MLeader | MLeader（同 2013） |
| JSON | Newtonsoft.Json | Newtonsoft.Json |
| 部署 | HKCU / ApplicationPlugins | ApplicationPlugins bundle（推荐） |

## 派生计划

1. 复制 `cad-plugin/2013/` 全部源码
2. 修改 csproj：TargetFramework = `net45`（SDK 风格或旧式均可）
3. 修改 HintPath 指向 AutoCAD 2015 安装目录
4. 部署改为 ApplicationPlugins bundle 结构（`*.bundle/`）
5. 覆盖最广的用户群体（2015—2024 系列共用同一份 DLL）

详见 [docs/version-plan.md](../../docs/version-plan.md)。

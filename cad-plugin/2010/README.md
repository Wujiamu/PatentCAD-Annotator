# PatentMarker 2010 — AutoCAD 2010/2011/2012 适配版

> **状态：规划中（TODO）**
> 本目录尚未实现，源码将从 `cad-plugin/2007/` 派生。

## 目标环境

| 项目 | 值 |
|------|-----|
| 操作系统 | Windows 7 (x86/x64) |
| AutoCAD | 2010, 2011, 2012 (R18.0—R18.2) |
| .NET | 3.5 (CLR 2.0 + System.Core) |
| 编译器 | VS2010+ (C# 4.0 语法可用，但建议保守) |

## 与 2007 版的差异

| 特性 | 2007 版 | 2010 版（计划） |
|------|---------|----------------|
| .NET | 2.0 | 3.5（可用 LINQ、`HashSet<T>`、`Action<T>`/`Func<T>`） |
| 标注方式 | Leader + MText | Leader + MText（同 2007，MLeader 2013 才引入） |
| JSON | SimpleJson（手写） | SimpleJson（手写，零依赖） |
| 注册表 | R17.0 | R18.0 |
| 部署 | HKCU | HKCU |

## 派生计划

1. 复制 `cad-plugin/2007/PatentMarker/` 全部源码
2. 修改 `PatentMarker.csproj`：`<TargetFrameworkVersion>v3.5</TargetFrameworkVersion>`
3. 修改注册表基础键：`R17.0` → `R18.0`
4. 修改 `lib/` 下 acdbmgd.dll / acmgd.dll 的 HintPath 指向 AutoCAD 2010 安装目录
5. （可选）用 LINQ 重写手动循环，提升可读性

详见 [docs/version-plan.md](../../docs/version-plan.md)。

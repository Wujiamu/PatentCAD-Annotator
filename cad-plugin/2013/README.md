# PatentCAD-Annotator 2013 — AutoCAD 2013/2014 适配版

> **状态：规划中（TODO）**
> 本目录尚未实现。AutoCAD 2013 首次引入 MLeader，源码需重大改写。

## 目标环境

| 项目 | 值 |
|------|-----|
| 操作系统 | Windows 7 (x86/x64) |
| AutoCAD | 2013, 2014 (R19.0—R19.1) |
| .NET | 4.0 (CLR 4.0) |
| 编译器 | VS2012+ (SDK 风格 csproj 不可用，需旧式) |

## 与 2007 版的核心差异：MLeader 引入

AutoCAD 2013（R19.0）首次引入 `MLeader` 类，引线 + 文字合为一体，无需再用 `Leader` + `MText` 拼合。

| 特性 | 2007 版 | 2013 版（计划） |
|------|---------|----------------|
| 标注方式 | Leader + MText（分离实体） | MLeader（一体式） |
| 样式管理 | DimStyle (PAT_DIM) | MLeaderStyle (PAT_STYLE) |
| JSON | SimpleJson（手写） | Newtonsoft.Json 13.x |
| LINQ | 不可用 | 可用（.NET 4.0） |
| accoremgd | 无 | 有（2013 起引入） |
| 部署 | HKCU 注册表 | HKCU 或 ApplicationPlugins |

## 实现计划

1. 新建 `PatentMarker.csproj`，TargetFramework = `v4.0`
2. 引用 acdbmgd / acmgd / accoremgd（AutoCAD 2013 安装目录）
3. NuGet 引入 Newtonsoft.Json 13.x
4. 重写 `PatMarkCommand`：用 `MLeader` 替代 `Leader` + `MText`
5. 重写 `PatEntityHelper`：通过 `MLeaderStyle.Name == "PAT_STYLE"` 识别标注
6. 重写 `PatAlignCommand` / `PatCheckCommand`：适配 MLeader API
7. 同步 2007 版的 v2 增强（样条曲线、无箭头、字典比对、全选）

详见 [docs/version-plan.md](../../docs/version-plan.md) 和 [docs/autocad-2007-downgrade-plan.md](../../docs/autocad-2007-downgrade-plan.md) 的反向对照。

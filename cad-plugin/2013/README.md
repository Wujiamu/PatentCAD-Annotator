# PatentCAD-Annotator 2013 — AutoCAD 2013~2014 适配版

> **状态：已完成**
> AutoCAD 2013 首次引入 MLeader，源码基于 MLeader API 重写。
>
> **兼容性：本版本仅适用于 AutoCAD 2013 / 2014，不可用于其他版本的 AutoCAD。**

## 目标环境

| 项目 | 值 |
|------|-----|
| 操作系统 | Windows 7 (x86/x64) |
| AutoCAD | 2013, 2014 (R19.0—R19.1) |
| .NET | 4.0 (CLR 4.0) |
| 编译器 | VS2015+ (旧式 csproj，C# 6 语法) |
| 托管程序集 | acdbmgd.dll + acmgd.dll + accoremgd.dll |
| JSON | Newtonsoft.Json 13.0.3 |

## 与 2007 版的核心差异：MLeader

| 特性 | 2007 版 | 2013 版 |
|------|---------|---------|
| 标注方式 | Leader + MText（分离实体） | MLeader（一体式） |
| 样式管理 | DimStyle (PAT_DIM) | MLeaderStyle (PAT_STYLE) |
| JSON | SimpleJson（手写） | Newtonsoft.Json 13.x |
| LINQ | 不可用 | 可用 |
| accoremgd | 无 | 有 |

## 编译步骤

1. 从 AutoCAD 2013/2014 安装目录复制 `acdbmgd.dll`、`acmgd.dll`、`accoremgd.dll` 到 `PatentMarker/lib/`
2. 还原 NuGet 包：`nuget restore` 或 VS 自动还原
3. 用 VS2015+ 打开 `PatentMarker/PatentMarker.csproj`
4. 编译 Release|AnyCPU
5. 输出：`PatentMarker/bin/Release/PatentMarker.dll` + `Newtonsoft.Json.dll`

## 部署

1. 将 `PatentMarker.dll` 和 `Newtonsoft.Json.dll` 复制到目标机器同一目录
2. 双击运行 `deploy/install-2013.vbs`
3. 重启 AutoCAD
4. 命令行输入 `BZ` 验证

## 命令清单

| 命令 | 拼音别名 | 功能 |
|------|---------|------|
| `PATPALETTE` | `BIAOZHU` / `BZ` | 打开字典面板 |
| `PATMARK` | `BZM` | 创建多重引线标注 |
| `PATCHECK` | `BZC` | 校验一致性 |
| `PATALIGN` | `BZA` | 对齐引线 |
| `PATSELECTALL` | `BZS` | 全选 PAT 标注实体 |

详见 [docs/version-plan.md](../../docs/version-plan.md)。

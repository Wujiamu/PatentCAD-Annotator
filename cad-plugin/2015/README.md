# PatentCAD-Annotator 2015 — AutoCAD 2015~2024 适配版

> **状态：已完成**
> 源码从 `cad-plugin/2013/` 派生，标注方式相同（Leader + MText）。
>
> **兼容性：本版本仅适用于 AutoCAD 2015 ~ 2024，不可用于其他版本的 AutoCAD。**

## 目标环境

| 项目 | 值 |
|------|-----|
| 操作系统 | Windows 7 SP1 — Windows 11 |
| AutoCAD | 2015, 2016, ..., 2024 (R20.0—R24.x) |
| .NET | 4.5 (CLR 4.0，兼容 4.5—4.8) |
| 编译器 | VS2015+ |
| 托管程序集 | acdbmgd.dll + acmgd.dll + accoremgd.dll |
| JSON | Newtonsoft.Json 13.0.3 |

## 与 2013 版的差异

| 特性 | 2013 版 | 2015 版 |
|------|---------|---------|
| .NET | 4.0 | 4.5 |
| 标注方式 | Leader + MText | Leader + MText（相同） |
| JSON | Newtonsoft.Json | Newtonsoft.Json（相同） |
| 覆盖范围 | 2013-2014 | 2015-2024（覆盖最广） |
| 部署 | HKCU 注册表 | HKCU 注册表 |

## 编译步骤

1. 从 AutoCAD 2015-2024 任一版本安装目录复制 `acdbmgd.dll`、`acmgd.dll`、`accoremgd.dll` 到 `PatentMarker/lib/`
2. 还原 NuGet 包：`nuget restore` 或 VS 自动还原
3. 用 VS2015+ 打开 `PatentMarker/PatentMarker.csproj`
4. 编译 Release|AnyCPU
5. 输出：`PatentMarker/bin/Release/PatentMarker.dll` + `Newtonsoft.Json.dll`

## 部署

1. 将 `PatentMarker-2015-deploy/` 整个文件夹复制到目标机器固定目录
2. 双击运行 `PatentMarker-2015-deploy/install-2015.vbs`
3. 重启 AutoCAD
4. 命令行输入 `BZ` 验证

## 命令清单

| 命令 | 拼音别名 | 功能 |
|------|---------|------|
| `PATPALETTE` | `BIAOZHU` / `BZ` | 打开字典面板 |
| `PATMARK` | `BZM` | 创建 Leader + MText 引线标注 |
| `PATCHECK` | `BZC` | 校验一致性 |
| `PATALIGN` | `BZA` | 对齐引线 |
| `PATSELECTALL` | `BZS` | 全选 PAT 标注实体 |

详见 [docs/version-plan.md](../../docs/version-plan.md)。

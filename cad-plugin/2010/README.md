# PatentCAD-Annotator 2010 — AutoCAD 2010~2012 适配版

> **状态：已完成**
> 源码从 `cad-plugin/2007/` 派生，标注方式相同（Leader + MText）。
>
> **兼容性：本版本仅适用于 AutoCAD 2010 / 2011 / 2012，不可用于其他版本的 AutoCAD。**

## 目标环境

| 项目 | 值 |
|------|-----|
| 操作系统 | Windows 7 (x86/x64) |
| AutoCAD | 2010, 2011, 2012 (R18.0—R18.2) |
| .NET | 3.5 (CLR 2.0 + System.Core) |
| 编译器 | VS2010+ (C# 4.0 语法可用) |
| 托管程序集 | acdbmgd.dll + acmgd.dll（无 accoremgd） |

## 与 2007 版的差异

| 特性 | 2007 版 | 2010 版 |
|------|---------|---------|
| .NET | 2.0 | 3.5（可用 LINQ、`HashSet<T>`、`Action<T>`/`Func<T>`） |
| 标注方式 | Leader + MText | Leader + MText（相同） |
| JSON | SimpleJson（手写） | SimpleJson（手写，零依赖） |
| 注册表 | R17.0 | R18.0 / R18.1 / R18.2 |
| 部署 | HKCU + LSP | HKCU + LSP（相同策略） |

## 编译步骤

1. 从 AutoCAD 2010/2011/2012 安装目录复制 `acdbmgd.dll` 和 `acmgd.dll` 到 `PatentMarker/lib/`
2. 用 VS2010+ 打开 `PatentMarker/PatentMarker.csproj`
3. 编译 Release|x86
4. 输出：`PatentMarker/bin/Release/PatentMarker.dll`

## 部署

1. 将 `PatentMarker-2010-deploy/` 整个文件夹复制到目标机器固定目录
2. 双击运行 `PatentMarker-2010-deploy/install-2010.vbs`
3. 重启 AutoCAD
4. 命令行输入 `BZ` 验证

## 命令清单

| 命令 | 拼音别名 | 功能 |
|------|---------|------|
| `PATPALETTE` | `BIAOZHU` / `BZ` | 打开字典面板 |
| `PATMARK` | `BZM` | 创建引线标注 |
| `PATCHECK` | `BZC` | 校验一致性 |
| `PATALIGN` | `BZA` | 对齐引线 |
| `PATSELECTALL` | `BZS` | 全选 PAT 标注实体 |

## 面板交互

- Word 附图标记识别支持 123A、123A1、123A2 等字母后继续数字的编号；编号在逗号、顿号、分号或句号等既有标点处分隔。
- 默认点数模式为三点；点击“点数”按钮后切换为无限点。
- 单击条目只选择，双击条目直接进入 `PATMARK` 标注。
- 右键条目选择“编辑条目”，或选中后按 `F2` 修改编号/名称；编辑窗口只负责保存、删除或取消。
- 文字附着点按靠近文字的最后一个引线拐点相对文字的位置选择四个象限：左上/左下连接文字左上/左下，右上/右下连接文字右上/右下；同高时连接对应侧中部。
- 新增条目仍通过面板“新增条目”按钮进入编辑窗口。

## 已知限制

- Leader + MText 是分离实体，移动时需同时选中两者（用 `BZS` 全选）
- 无 accoremgd，部分高版本 API 不可用

详见 [docs/version-plan.md](../../docs/version-plan.md)。

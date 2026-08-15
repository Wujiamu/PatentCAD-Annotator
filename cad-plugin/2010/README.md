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
| `PATBRACE` | `DAGUOHAO` | 三点创建独立参数化矢量大括号 |
| `PATBRACEEDIT` | — | 通过控制点或输入高度/宽度调整大括号 |

## 面板交互

- Word 附图标记识别支持 123A、123A1、123A2 等字母后继续数字的编号；编号在逗号、顿号、分号或句号等既有标点处分隔。
- 默认点数模式为三点；点击“点数”按钮后切换为无限点。
- 三点或无限点标注过程中，按 ESC 或右键菜单中的“确认/取消”都可以退出当前标注命令；无限点采集到一半时也可以直接取消。
- 单击条目只选择，双击条目直接进入 `PATMARK` 标注。
- 右键条目选择“编辑条目”，或选中后按 `F2` 修改编号/名称；编辑窗口只负责保存、删除或取消。
- 文字附着点按靠近文字的最后一个引线拐点相对文字的位置选择四个象限：左上/左下连接文字左上/左下，右上/右下连接文字右上/右下；同高或默认文字点重合时连接对应侧上角，不再连接侧面中点。
- 创建时不设置原生 `Leader.Annotation`，而是把文字附着点作为最后一个 Leader 顶点，并用扩展字典保存 MText 关系，避免 AutoCAD 自动生成 hook line。
- 提交后会重新打开 MText 复写附着点并读回实际值；`PatentMarker.log` 同时记录实际加载 DLL 路径和提交后的 Leader 顶点列表，便于核对宿主是否加载了旧部署包或产生了额外顶点。
- 新增条目仍通过面板“新增条目”按钮进入编辑窗口。
- 面板“Brace/大括号”按钮启动 `PATBRACE`；`PATBRACEEDIT` 可重新点选控制点，也可直接输入高度和宽度。大括号是独立 Polyline，不参与 Leader/MText 关联。
- 第三点决定中部尖点方向：竖向可向左/向右，横向可向上/向下；外侧肩部自动位于相反侧。

## 已知限制

- Leader + MText 是分离实体，移动时需同时选中两者（用 `BZS` 全选）
- 无 accoremgd，部分高版本 API 不可用
- 矢量大括号第一版通过 `PATBRACEEDIT` 命令调整，不依赖原生自定义夹点；尺寸输入适合需要精确高度/宽度的场景

详见 [docs/version-plan.md](../../docs/version-plan.md)。

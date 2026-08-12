# PatentCAD-Annotator 2025 — AutoCAD 2025~2026+ 适配版

> **状态：已完成**
> AutoCAD 2025 迁移到 .NET 8，使用 System.Text.Json，零外部依赖。
>
> **兼容性：本版本仅适用于 AutoCAD 2025 及更高版本，不可用于其他版本的 AutoCAD。需要 Windows 10+。**

## 目标环境

| 项目 | 值 |
|------|-----|
| 操作系统 | Windows 10 1607+ / Windows 11 |
| AutoCAD | 2025, 2026+ (R25+) |
| .NET | 8.0 (Core，脱离 .NET Framework) |
| 编译器 | VS2022+ / .NET SDK 风格 csproj |
| 托管程序集 | acdbmgd.dll + acmgd.dll + accoremgd.dll |
| JSON | System.Text.Json（内置，零 NuGet 依赖） |

## 与 2015 版的差异

| 特性 | 2015 版 | 2025 版 |
|------|---------|---------|
| .NET | 4.5 (Framework) | 8.0 (Core) |
| Win7 | 支持 | **不支持** |
| JSON | Newtonsoft.Json | System.Text.Json（内置） |
| csproj | 旧式 | SDK 风格 |
| 外部依赖 | Newtonsoft.Json.dll | 无（单 DLL 部署） |
| 标注方式 | Leader + MText | Leader + MText（相同） |

## 编译步骤

1. 从 AutoCAD 2025+ 安装目录复制 `acdbmgd.dll`、`acmgd.dll`、`accoremgd.dll` 到 `PatentMarker/lib/`
2. 确保已安装 .NET 8 SDK
3. 在 `PatentMarker/` 目录执行：`dotnet build -c Release`
4. 输出：`PatentMarker/bin/Release/net8.0-windows/PatentMarker.dll`

## 部署

1. 将 `PatentMarker-2025-deploy/` 整个文件夹复制到目标机器固定目录
2. 运行 `PatentMarker-2025-deploy/install-2025.ps1`（需 PowerShell）
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
| `PATBRACE` | `DAGUOHAO` | 三点创建独立参数化矢量大括号 |
| `PATBRACEEDIT` | — | 通过控制点或输入高度/宽度调整大括号 |

## 面板交互

- Word 附图标记识别支持 123A、123A1、123A2 等字母后继续数字的编号；编号在逗号、顿号、分号或句号等既有标点处分隔。
- 默认点数模式为三点；点击“点数”按钮后切换为无限点。
- 单击条目只选择，双击条目直接进入 `PATMARK` 标注。
- 右键条目选择“编辑条目”，或选中后按 `F2` 修改编号/名称；编辑窗口只负责保存、删除或取消。
- 文字附着点按靠近文字的最后一个引线拐点相对文字的位置选择四个象限：左上/左下连接文字左上/左下，右上/右下连接文字右上/右下；同高时连接对应侧中部。
- 创建时不设置原生 `Leader.Annotation`，而是把文字附着点作为最后一个 Leader 顶点，并用扩展字典保存 MText 关系，避免 AutoCAD 自动生成 hook line。
- 提交后会重新打开 MText 复写附着点并读回实际值；`PatentMarker.log` 同时记录实际加载 DLL 路径和提交后的 Leader 顶点列表，便于核对宿主是否加载了旧部署包或产生了额外顶点。
- 新增条目仍通过面板“新增条目”按钮进入编辑窗口。
- 面板“Brace/大括号”按钮启动 `PATBRACE`；`PATBRACEEDIT` 可重新点选控制点，也可直接输入高度和宽度。大括号是独立 Polyline，不参与 Leader/MText 关联。
- 第三点决定中部尖点方向：竖向可向左/向右，横向可向上/向下；外侧肩部自动位于相反侧。

详见 [docs/version-plan.md](../../docs/version-plan.md)。

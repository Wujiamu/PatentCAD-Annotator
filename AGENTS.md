# AGENTS.md — PatentCAD-Annotator 代理工作指南

## 1. 项目概览

PatentCAD-Annotator 是 AutoCAD 专利图纸标注插件：从 Word 说明书提取附图标记生成 `.dict.json` 字典，在 CAD 图纸中一键创建标准引线标注，并保持双向同步（字典变更自动高亮差异）。

核心工作流：Word 保存 → VBA 自动导出 `.dict.json` → CAD 中 `BZ` 打开面板 → `BZM` 创建标注 → 字典变更时面板自动高亮。

## 2. 版本矩阵（关键规则：不能交叉使用）

由于 AutoCAD 托管 API 与 .NET 运行时强绑定，项目按 API 断代划分为 5 个并行版本：

| 目录 | 覆盖 AutoCAD | .NET | 标注 API | JSON 库 |
|------|-------------|------|---------|---------|
| `cad-plugin/2007/` | 2007 ~ 2009 | 2.0（无 LINQ） | Leader + MText | SimpleJson（零依赖） |
| `cad-plugin/2010/` | 2010 ~ 2012 | 3.5 | Leader + MText | SimpleJson（零依赖） |
| `cad-plugin/2013/` | 2013 ~ 2014 | 4.0 | MLeader | Newtonsoft.Json（ILRepack 合并） |
| `cad-plugin/2015/` | 2015 ~ 2024 | 4.5 | MLeader | Newtonsoft.Json（ILRepack 合并） |
| `cad-plugin/2025/` | 2025 ~ 2026+ | 8.0（Win10+） | MLeader | System.Text.Json（内置） |

**每个版本的 DLL 只能在其对应 AutoCAD 年份区间内运行。** 跨版本混装会因 CLR 不兼容、API 缺失或 `MissingMethodException` 无法加载。修改任一版本代码前，先确认该版本的 .NET 目标框架与标注 API 类型。

## 3. 目录约定

- `cad-plugin/<version>/PatentMarker/` — C# 源码，5 个版本内部子目录同构：`Commands/`、`I18n/`、`IO/`、`Palette/`、`Styles/` + `PatentMarkerApp.cs` + `PatentMarker.csproj`
- `PatentMarker-{version}-deploy/` — 即装即用部署包（DLL + 安装/卸载脚本 + `vba/` 模块副本），2007 版另有 `install-2007.bat`
- `cad-plugin/<version>/PatentMarker/lib/` — 存放编译所需 SDK DLL（不入库）
- 6 个 VBA 模块（Word 端，全版本共享）：`Patterns.bas`、`DictModel.bas`、`JsonWriter.bas`、`PatentExtractor.bas`、`AutoExport.bas`、`clsSaveHook.cls`
- 文档：`docs/version-plan.md`（分版理由）、`docs/development-log.md`（变更记录）

### 3.1 部署包逐版本差异

5 套部署包共享 `PatentMarker.dll` + `install-vba.vbs` + `README.txt` + `vba/`（6 模块），差异如下：

| 版本 | 安装脚本 | 卸载脚本 | 额外依赖 |
|------|---------|---------|---------|
| 2007 | `install-2007.bat` + `install-2007.vbs` | `uninstall-2007.vbs` | 无 |
| 2010 | `install-2010.vbs` | `uninstall-2010.vbs` | 无 |
| 2013 | `install-2013.vbs` | `uninstall-2013.vbs` | 无（Newtonsoft.Json 已合并进 DLL） |
| 2015 | `install-2015.vbs` | 无 | 无（Newtonsoft.Json 已合并进 DLL） |
| 2025 | `install-2025.ps1`（PowerShell） | 无 | 无（System.Text.Json 内置） |

- 修改任一部署包脚本时，评估是否需同步到其他 4 套；2025 版用 `.ps1`，其余用 `.vbs`，脚本语法不通用
- 2015/2025 当前缺卸载脚本，新增卸载逻辑时优先补齐这两套

## 4. 修改代码的同步规则

- **先判断变更是否影响其他版本**：功能逻辑、标注样式、字典格式、面板行为类变更通常需要同步到全部 5 个版本；仅针对特定版本 API 的适配（如 MLeader vs Leader）不必同步
- 修改共享行为时，在交付说明中列出**已同步版本清单**和**未同步原因**
- 同组版本（2007/2010 同用 Leader，2013/2015/2025 同用 MLeader）代码逻辑应保持一致，仅允许版本特有差异
- 修改部署包脚本或 VBA 模块时，需同步全部 5 套部署包（或逐套说明差异）

## 5. MLeader API 陷阱速查表（2013 / 2015 / 2025）

AutoCAD SDK 中 MLeader 相关 API 名称与常见误写不一致，编译报 CS1061/CS0246 时优先核对：

| 误写（不存在） | 实际名称 |
|---------------|---------|
| `TextPosition` | `TextLocation` |
| `AddVertex(int, Point3d)` | `AddLastVertex(int, Point3d)` |
| `LeaderLineType.Splines` | `LeaderType.SplineLeader` |
| `LeaderLineType.Straight` | `LeaderType.StraightLeader` |
| `db.MLeaderStyle` | `db.MLeaderstyle`（小写 s） |
| `mleader.GetLeaderLines()` | `mleader.LeaderLineCount > 0` + `mleader.GetLastVertex(0)` |

API 名称不确定时，用 `System.Reflection.MetadataLoadContext` 加载 `PatentMarker/lib/acdbmgd.dll` 元数据探测真实名称（`Assembly.LoadFrom` 会因原生依赖抛 FileNotFoundException，不要用）。

## 6. Leader + MText 注意（2007 / 2010）

- 箭头大小属性名为 `Dimasz`（不是 `DimensionArrowSize`）
- 2007/2010 版 DLL 必须目标 .NET 2.0 CLR，代码中不得使用 LINQ 等 2.0 不支持的特性（2010 版可用 LINQ）
- 引线由 `Leader` + `MText` 两个对象拼合，无 MLeader 相关 API

## 7. 编译与验证

- 各版本编译前需将 SDK DLL 放入 `PatentMarker/lib/`：2007/2010 需 `acdbmgd.dll`、`acmgd.dll`；2013/2015/2025 另需 `accoremgd.dll`
- 2013/2015 版依赖 NuGet 包 Newtonsoft.Json 13.0.3（编译时引用），**发布前必须用 ILRepack 合并进 `PatentMarker.dll`**（单文件部署，安装脚本不再检查/要求外部 DLL）；2025 版零外部依赖
  - 合并命令示例：`ILRepack.exe /out:PatentMarker.merged.dll /target:library /internalize /lib:lib PatentMarker.dll ..\..\packages\Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll`（2013 用 net35 版，2015 用 net45 版；`/lib` 指向 SDK DLL 目录）
  - 2013 目标 v4.0：13.0.x 包 `lib/net40` 的 DLL 实际 TFM 为 v4.5，编译会报 MSB3274，必须引用 `lib/net35`（无 TFM 标记、CLR4 兼容）；HintPath 为 `..\..\packages\...`（packages 在 `cad-plugin/packages`）
  - 合并后必须确认程序集不再引用外部 Newtonsoft.Json（`GetReferencedAssemblies()` 无该条目）
- **项目无自动化测试与 CI 编译检查**：验证手段为本地编译 + 在对应 AutoCAD 版本中实测（`NETLOAD` 加载 DLL）
- 不得声称"已通过 CI/测试验证"；只报告实际执行的编译或实测结果

## 8. 文档同步要求

- 功能变更需更新 `docs/development-log.md`（遵循语义化版本，格式见 v2.5/v2.4 条目）
- 影响版本矩阵、命令或目录结构时，同步更新根目录 `README.md` 与受影响版本的 `README.md`
- 版本规划类决策写入 `docs/version-plan.md`

## 9. 命令清单

| 命令 | 别名 | 说明 |
|------|------|------|
| `PATPALETTE` | `BZ` / `BIAOZHU` | 打开字典面板 |
| `PATMARK` | `BZM` | 创建引线标注 |
| `PATCHECK` | `BZC` | 校验编号一致性 |
| `PATALIGN` | `BZA` | 对齐引线 |
| `PATSELECTALL` | `BZS` | 全选标注实体 |

## 10. 禁止事项

- 不提交 Autodesk SDK DLL（`acdbmgd.dll` / `acmgd.dll` / `accoremgd.dll`，版权限制，`.gitignore` 已排除）
- 不修改 `.gitignore` 排除的目录（`bin/`、`obj/`、`lib/` 等）
- 不将部署包与源码版本混用：部署包 DLL 必须与 `cad-plugin/` 源码对应版本一致
- 不删除或重命名 6 个 VBA 共享模块（部署包与 Word 端依赖其固定文件名）

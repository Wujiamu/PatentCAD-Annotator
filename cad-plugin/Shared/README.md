# Shared source layer

`Commands/PatLeaderTextAttachment.cs` is the canonical Leader/MText geometry and relationship helper: it selects one of the four text-corner attachments from the last dogleg, appends the text anchor as the final Leader vertex without using native `Leader.Annotation` hook geometry, stores the MText link in an extension dictionary, reapplies the text attachment after commit, and records committed Leader geometry for diagnostics. It is linked into all five edition projects. `PATMARK` uses `AllowNone` on each point prompt so AutoCAD's Enter/right-click result can leave the command cleanly.

`Commands/PatBraceGeometry.cs`, `Commands/PatBraceEntity.cs`, and `Commands/PatBraceCommand.cs` provide the version-neutral vector brace implementation. A brace is a sampled `Polyline` generated from the DrawingML/PowerPoint `rightBrace` profile: four quarter-ellipse transitions, straight stems at half the selected width, and one sharp center cusp; the full profile stays between the endpoint axis and the tip. Its extension-dictionary definition contains endpoints, side, and tip width. `PATBRACE` creates it from three points and `PATBRACEEDIT` changes its control points or exact dimensions. These files are linked into all five edition projects and deliberately remain independent from Leader/MText annotations.

`Palette/DictPaletteSession.cs` is also compiled from the canonical shared source tree; it owns palette dictionary state, diff baseline, filtering, and counters.

`Palette/DictPaletteViewRenderer.cs` is the WinForms rendering boundary for list rows, Diff highlighting, compare columns, and empty/filter states. It does not access AutoCAD documents or commands.

该目录保存五个 AutoCAD 版本共用、且不依赖具体 AutoCAD 实体 API 的 C# 源码。

各版本项目通过 MSBuild `Compile Include` 直接链接这些文件，而不是生成一个跨 CLR 的共享 DLL。这样可以保留 2007/2010/2013/2015/2025 各自的目标框架和 Autodesk SDK 绑定，同时让共享业务规则只有一个源文件。

当前共享模块：

- `IO/NumberIdentity.cs`：附图标记规范化和比较规则；
- `IO/PatSettings.cs`：按图纸隔离的运行设置；
- `IO/DictDiff.cs`：字典 Diff 规则；
- `IO/DictConflict.cs`：Word/CAD 字典冲突裁决的文件操作；
- `IO/MarkingTextParser.cs`：纯文本附图标记识别；
- `I18n/Language.cs`：语言枚举。
- `Palette/DictPaletteWorkflow.cs`：字典/缓存/路径和冲突生命周期门面；
- `Palette/DictPaletteCadService.cs`：Leader + MText 编号同步和批量删除事务服务。
- `Cad/PatEntityHelper.cs`：Leader/MText/DBText 实体识别和文字更新适配。
- `Commands/PatBraceGeometry.cs`、`PatBraceEntity.cs`、`PatBraceCommand.cs`：参数化矢量大括号的几何、扩展字典元数据和创建/调整命令；几何以 DrawingML/PPT `Right Brace` 为基准，四段四分之一椭圆连接位于所选宽度中线的直干与中心尖锐折角，完整轮廓保持在端点轴线和尖点之间；大括号使用独立 Polyline，不参与 Leader/MText 关联。

`Diagnostics/PatDiagnostics.cs`、`PatDoctorReport.cs`、`PatDoctorCommand.cs`：自动诊断机制。`PatDiagnostics` 是进程内错误环形缓冲（100 条），由各版本 `PatentMarkerApp.RawLog` 入口的 `OnRawLog` 钩子自动汇入 error/failed/fatal/exception 类日志行；`PATDOCTOR`（别名 `BZD`）自检报告目录、PAT_DIM/TIMES_ROMAN 样式、运行设置、字典加载和模型空间实体，并将检查结果、环境信息与最近错误写入 DLL 旁的 `PatentMarker-doctor-report.txt`。诊断模块保持 .NET 2.0 / C# 3.0 兼容语法、无 JSON 依赖，五个版本按源码链接编译同一份实现。

`RuntimeHost` 与 JSON/配置读写适配仍保留在版本目录中；`DictPaletteSession`、`DictPaletteControl`、`PatPaletteCommand`、三个对话框和版本文案均已位于本共享目录，并按源码链接方式编译到五个版本。面板到 `PATMARK` 的请求由 `PatPaletteCommand` 按 Document 隔离、去重并在宿主空闲后重试，不生成跨 CLR 的共享 DLL。

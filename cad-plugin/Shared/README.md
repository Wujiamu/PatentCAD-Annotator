# Shared source layer

`Commands/PatLeaderTextAttachment.cs` is the canonical Leader/MText geometry and relationship helper: it selects the text corner or middle-side attachment from the last dogleg, appends the text anchor as the final Leader vertex without using native `Leader.Annotation` hook geometry, stores the MText link in an extension dictionary, reapplies the text attachment after commit, and records committed Leader geometry for diagnostics. It is linked into all five edition projects.

`Commands/PatBraceGeometry.cs`, `Commands/PatBraceEntity.cs`, and `Commands/PatBraceCommand.cs` provide the version-neutral vector brace implementation. A brace is a sampled `Polyline` with an extension-dictionary definition containing its endpoints, side, and width; `PATBRACE` creates it from three points and `PATBRACEEDIT` changes its control points or exact dimensions. These files are linked into all five edition projects and deliberately remain independent from Leader/MText annotations.

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
- `Commands/PatBraceGeometry.cs`、`PatBraceEntity.cs`、`PatBraceCommand.cs`：参数化矢量大括号的几何、扩展字典元数据和创建/调整命令；大括号使用独立 Polyline，不参与 Leader/MText 关联。

`RuntimeHost`、`DictPaletteSession`、`DictPaletteControl`、对话框和版本文案仍保留在版本目录中，因为它们分别受宿主/UI 生命周期、语言特性或版本文案约束。上面两个 `Palette/` 文件也按源码链接方式编译到五个版本，不生成跨 CLR 的共享 DLL。

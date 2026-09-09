# Changelog

本项目的对外版本管理自 **1.0.0** 开始。早于 1.0.0 的 v2.0–v5.3 均为发布前开发里程碑，已并入 1.0.0 的功能总览（见根 `README.md` 的"版本历史"），不再作为独立对外版本号呈现。

This project's public versioning starts at **1.0.0**. Internal iterations v2.0–v5.3 are pre-release milestones merged into the feature overview of 1.0.0 (see the "Version history" section of the root `README.md`) and are no longer exposed as standalone public releases.

Adopts [Semantic Versioning](https://semver.org/).

***

## [Unreleased] - 1.0.3 candidate (2026-09-08)

### Added

- CAD 的字典面板现在可以用“显示 JSON/隐藏 JSON”切换当前 `.dict.json` 的 Explorer 可见性；Word 面板提供对应的手动编辑开关。切换只改变 Hidden/System 文件属性，不改变 JSON 内容。
- Word 与五个 CAD 版本的字典写回会保留用户选择的可见状态，新建字典仍默认隐藏；增加了 2025 版属性往返和写回保留测试。

### Fixed

- 修复 Word VBA 安装器在旧版 Word 上直接导入 `PatentDictPanel.frm` 后把窗体头当作普通模块代码、触发“无效外部过程”的问题。五套安装器现在校验 `.frm/.frx` 配套资产，提取 `.frm` 代码段，通过 `VBComponents.Add(3)` 和 `Designer.Controls.Add` 创建真实 UserForm，再注入代码；已有面板会先改名，避免同一模板会话中删除后立即复用名称触发错误 75。
- 修复安装器注入旧版 `clsSaveHook` 的漂移：现在五套安装器都从当前 `clsSaveHook.cls` 提取并注入干净代码，并在退出前释放自动导出事件钩子、关闭其创建的文档和 Word 实例。
- 修复 Word 回归 VBS 在异常路径未关闭测试文档、导致后续启动 Word 弹出多个 `.docm` 的问题。各脚本现在只清理自己创建的测试文档，同时保留日志和 JSON 诊断文件。

- Fixed legacy-host VBA installation by avoiding direct `PatentDictPanel.frm` import: all five installers validate the `.frm/.frx` pair, extract the `.frm` code section, create a real type-3 UserForm with `VBComponents.Add(3)` and `Designer.Controls.Add`, and inject the code. An existing panel is renamed before replacement so same-session form-name reuse does not trigger error 75.
- Fixed installer/source drift for `clsSaveHook`: each installer now injects the clean body extracted from the current class source and releases the auto-export event sink, documents, and Word instance it created during shutdown.
- Fixed regression VBS cleanup on failure paths; each script now closes and deletes only its own generated `.docm` files while retaining logs and JSON diagnostics.

***

## \[Unreleased] - 1.0.2 candidate (2026-09-05)

**修复参数化大括号越过端点轴线、呈反向/W 形的问题。**

**Fixed parameterized braces crossing the endpoint axis and appearing reversed/W-shaped.**

### 修复 / Fixes

- 真实 AutoCAD 截图确认当前部署 DLL 正在生成错误轮廓：中部尖点位于第三点一侧，但两段直干被放在端点轴线的另一侧，使实际跨度变成所选宽度的 `1.42×`。因此此前“现场仍加载旧 DLL”的判断被推翻。
- Rebuilt the shared geometry from the DrawingML/PowerPoint `rightBrace` definition: endpoints at one bounding edge, straight stems at half the selected width, center cusp at the selected width, and four quarter-ellipse transitions using 8.333% of the shorter side.
- 更正契约测试：左右/上下及旋转后的轮廓必须全部位于端点轴线与第三点之间；不再把“直干位于尖点反侧”当成正确行为。

### 兼容 / Compatibility

- 修复 Word 2010 打开面板时的“无效外部过程”：截图中的 `VERSION` / `Begin` / `OleObjectBlob` 出现在代码窗口，说明 `.frm` 被当成普通模块导入。5 份安装器现在检查导入结果的 type=3 和必需控件；若导入报错、类型错误或控件缺失，会删除坏组件，用 `VBComponents.Add(3)` + `Designer.Controls.Add` 重建 `PatentDictPanel`，再注入窗体代码并记录原始失败原因。安装前保留 `Normal.dotm` 临时备份，保存失败时恢复原模板；`.frm/.frx` 仍须在包内配对，根 VBA 与 5 套部署包已同步。
- Fixed the Word 2010 "invalid outside procedure" panel failure caused by importing `PatentDictPanel.frm` as a standard module. All five installers now validate the imported component as type 3 with the required controls; on an import error, wrong type, or missing controls they remove the bad component, rebuild `PatentDictPanel` with `VBComponents.Add(3)` and `Designer.Controls.Add`, inject the form code, and log the original failure. The installer keeps a temporary `Normal.dotm` backup and restores it if saving fails; `.frm/.frx` must still remain paired in the package, with the canonical VBA synchronized to all five deployments.

- 几何源码由五个 AutoCAD 版本共享，2007/2010/2013/2015/2025 部署 DLL 已同步重打包。大括号 Xrecord 参数格式未变；已有错误轮廓可通过 `PATBRACEEDIT` 重新输入原尺寸来重建，或直接删除后重画。
- 修复面板双击条目时无条件重复排队 `PATMARK` 的问题：请求改为按当前图纸保存并去重；已有 CAD 命令运行时延迟到空闲后再启动，运行中的 `PATMARK` 可在提示边界切换到最新请求。
- 修复 `PATMARK` 命令实例在异常、取消或空编号返回后残留状态的问题，五个版本的 Leader/MLeader 路径统一清理；部署 DLL 已同步更新。
- Fixed repeated palette double-clicks stacking asynchronous `PATMARK` commands: requests are now isolated per drawing, de-duplicated, and retried after AutoCAD becomes idle.
- PATMARK now clears its per-document command-instance state on cancellation, early return, and unhandled failure across all five Leader/MLeader editions.
- 修复 MLeader 标注在面板改号、删除全部标注时被漏掉的问题；冲突裁决现在有可见入口，Diff 对照保留 Removed 条目并在筛选后保留旧值与颜色。
- 修复 CI YAML/API 质量门与 2015 安装脚本误扫描 AutoCAD 2025 的版本号；发行 staging 现在排除日志、报告、LSP 和历史 DLL 备份。
- Fixed palette renumber/delete handling for Plan-F MLeaders; conflict arbitration now has a visible entry point, and filtered diff views preserve removed rows, old values, and status colors.
- Fixed CI YAML/API quality gates and the 2015 installer scanning AutoCAD 2025; release staging now excludes logs, reports, LSP fallbacks, and historical DLL backups.
- 修复 PATCHECK 结果跨图纸互相覆盖：现在按 AutoCAD 文档保存并在文档关闭时释放；Word 导出前 CAD 备份失败会中止覆盖并写入 `autoexport-error.txt`；新增 `sync-mleader-group.ps1` 自动同步四个 MLeader 版本组，2025 回归测试增至 117 项。
- PATCHECK results are now isolated by AutoCAD document and released when a document closes; CAD-backup failures abort Word overwrite and write `autoexport-error.txt`; `sync-mleader-group.ps1` automates the four-edition MLeader fork, and the 2025 regression suite is now 117 tests.
- PATCHECK 未标注编号查询与 `NumberIdentity` 保持大小写不敏感，避免 `1342A`/`1342a` 在面板高亮中出现口径不一致。
- PATCHECK unmarked-number lookup now follows the case-insensitive `NumberIdentity` rule, so `1342A` and `1342a` highlight consistently.
- 修复首次 Word 导出对不存在目标文件调用 `SetAttr` 的错误路径；Word COM 面板/导出/开关/标题与段落边界回归均通过，且非活动图纸期间发生字典变更时重新激活会清理过期 PATCHECK 高亮。
- Fixed the first-export `SetAttr` failure path; Word COM panel/export/toggle/caption and marking-boundary smoke checks pass, and reactivating a drawing whose dictionary changed while inactive now clears stale PATCHECK highlights.
- 新增 8 份脱敏 VBA/C# 对比语料与受跟踪的 `generate-vba-corpus.vbs`，干净检出不再因缺少本机语料而跳过跨语言解析对比。
- Added eight sanitized VBA/C# parity fixtures and a tracked `generate-vba-corpus.vbs`, so clean checkouts no longer silently skip the cross-language parser comparison.
- Word 导出现在动态枚举 DWG：无 DWG/单 DWG 保持原有规则；同目录有多个 DWG 时，面板“手动导出字典”列出文件并要求明确选择，字典按所选 DWG 主名生成，后续自动保存复用本次文档选择；未选择时安全拒绝写入并记录 `autoexport-error.txt`。新增回归覆盖目标选择与自动保存复用，并通过本机 Word COM 验证。
- Word export now enumerates DWGs dynamically: the no-DWG and single-DWG rules remain unchanged; when multiple DWGs share the folder, the panel's manual export lists them and requires an explicit target, writes the dictionary under the selected DWG base name, and lets later auto-saves reuse that selection for the current document. Before a selection, export fails closed with `autoexport-error.txt`; regression coverage verifies selection and reuse on local Word COM.
- `JsonWriter.WriteToFile` now writes a same-directory temporary UTF-8 file and replaces the destination with a Unicode same-volume rename; failed replacement leaves the previous dictionary intact and propagates failure to `AutoExport`. The 2025 installer registers every detected R25.0/R25.1/R26.0 profile instead of stopping at the first release, covering side-by-side hosts.
- 五版安装与卸载脚本现在合并 HKCU/HKLM 并处理支持范围内检测到的全部配置，不再在第一个版本处停止；2010 的 HKLM 与 Support/acad.lsp 兜底会针对每个配置尝试，卸载时同步清理生成的 acad.lsp 片段，重复配置会去重。
- All five installers and uninstallers now merge HKCU/HKLM profile lists and process every detected supported profile instead of stopping at the first release; the 2010 HKLM and Support/acad.lsp fallbacks are attempted for each profile, generated acad.lsp blocks are removed on uninstall, and duplicate profiles are de-duplicated.
- 修复 `PATBRACEEDIT` 尺寸模式在真实 AutoCAD 中抛出 `eDegenerateGeometry` 的问题：原实现先把 Polyline 顶点删到 0 个，现改为原位更新并保持有效顶点数；四版契约模拟与 AutoCAD 2026 Core Console 尺寸编辑回放均通过。
- Fixed `PATBRACEEDIT` size-mode `eDegenerateGeometry`: the old replacement emptied the Polyline before rebuilding it; geometry is now updated in place while preserving a valid vertex list, covered by four simulated editions and an AutoCAD 2026 Core Console size-edit replay.

***

## \[1.0.1] - 2026-09-02

**Word 工具面板 UI 优化 + 导出状态反馈。**

**Word tool panel UI redesign + export status feedback.**

### 变更 / Changes

- **面板整体放大、字号加大** / **Larger panel & fonts**：窗体扩至 300×178 磅，字体统一为微软雅黑（按钮 14pt 加粗、复选框 11pt、状态行 10pt），蓝色主按钮（RGB 0,120,215），整体更清晰、更简洁、更美观。

- **导出状态反馈** / **Export status feedback**：新增底部状态行，点击"手动导出字典"后实时反馈结果（成功：绿色"√ 已导出 HH:MM:SS"；失败：红色"× 导出失败（文档未保存？）"），解决此前点击后无任何提示的问题。

- **坐标单位根治** / **Coordinate-unit fix**：所有控件布局在 `UserForm_Initialize` 内以磅（points）为单位显式设置，不再依赖 `.frx` 设计数据（该二进制由程序生成、坐标曾误用 twip 导致控件画出窗体的"小空框"），并改用 Word 设计器重新生成 `.frx` 消除手工编辑导致的控件残影。已通过 Word 实机（导入重放 + 运行时属性查询 + 截屏像素分析）验证按钮带、文字、状态行均正常显示。

### 修复 / Fixes

- 修复由手工修改 `.frx` 二进制引起的左上角"幽灵文字"残影（用 Word 设计器重新生成权威 `.frm`/`.frx`）。

- Fixed the stray "ghost text" artifact in the top-left corner caused by hand-editing the `.frx` binary; regenerated the canonical `.frm`/`.frx` through the Word form designer.

### 兼容 / Compatibility

- 已同步 5 套部署包 + 2007-v2（`PatentDictPanel.frm`/`.frx` 哈希一致，`build.ps1 -Static` VBA 跨包校验通过）。用户重新运行 `install-vba.vbs`（或手动重导入 VBA）即可获得本版面板。

***

## \[1.0.0] - 2026-08-18

**首个正式版本。** 首个对外稳定发布，覆盖 AutoCAD 2007—2026+，Word 说明书 → 附图标记字典 → CAD 一键标注的全流程闭环。

**First official release.** Initial stable public release covering AutoCAD 2007–2026+, with a full closed loop from Word specs → reference-numeral dictionary → one-click CAD annotation.

### 功能总览 / Feature overview

- **Word 端自动导出** / **Word-side auto export**：Word 保存时自动提取附图标记，生成 `.dict.json`（1.0.0 起为隐藏+系统属性文件，资源管理器默认不可见；文件夹拷贝/共享不受影响）。

- **CAD 引线标注** / **CAD leader annotation**：BZM 一键创建标准引线标注，支持样条/直线、箭头开关与尺寸调节、三点/无限点模式。

- **字典双向同步与比对** / **Dictionary two-way sync & diff**：字典变更自动高亮新增/删除/编号变/名称变；CAD 端支持直接编辑、粘贴识别、冲突裁决。

- **漏标检测与对齐** / **Unmarked check & align**：BZC 报告"字典有·图纸未标注"清单；BZA 基于选择集的线/框对齐，空间不足时自动延伸。

- **参数化矢量大括号** / **Parameterized vector brace**：Brace 三点创建、控制点或尺寸调整。

- **自检诊断** / **Self-check diagnostics**：BZD 一键生成插件状态与最近错误报告。

- **多版本适配** / **Multi-edition support**：5 个版本覆盖 AutoCAD 2007—2026+（版本矩阵见根 `README.md`）。

### 修复 / Fixes

- **Word 工具面板空白小框**：`PatentDictPanel.frm` 配套的 `.frx` 设计数据由程序生成，控件坐标误用了 twip 量级数值（如 `Width=3000`），而 MSForms 实际单位为磅（points），所有控件被排到客户区之外——打开面板只显示一个空的小边框。修复：`UserForm_Initialize` 中以磅为单位显式设置窗体与控件布局（不依赖 `.frx` 设计数据），并把窗体标题从 "UserForm1" 改为"专利标注字典工具"。已通过 Word 实机（重放导入 + 运行时属性查询 + 屏幕像素分析）验证按钮与复选框正常显示。

- **Word tool panel rendered as an empty frame**: the generated `.frx` design blob stored twip-scale control coordinates (e.g. `Width=3000`) while MSForms expects points, pushing every control outside the client area - the panel showed up as an empty little frame. Fixed by setting the form and control layout explicitly in points inside `UserForm_Initialize` (independent of `.frx` design data), and the form caption is now "专利标注字典工具" instead of "UserForm1". Verified on a live Word instance via runtime property queries and screen-pixel analysis.

### 里程碑时间线 / Milestone timeline (pre-1.0.0, for reference)

| 里程碑 / Milestone | 日期 / Date       | 关键内容 / Key content                                      |
| --------------- | --------------- | ------------------------------------------------------- |
| M5 (v5.2–5.3)   | 2026-08-18      | 引线末端与文字间距、字典文件隐藏化+孤儿自动清理                                |
| M4 (v4.9–5.1)   | 2026-08-15/16   | 面板单一入口、PATCHECK 漏标检测、PATALIGN v2、MLeader F 方案、2026 实机测试 |
| M3 (v4.5–4.8)   | 2026-08-15      | PATDOCTOR 诊断、共享层收敛、五套部署包重打包                             |
| M2 (v4.0–4.1)   | 2026-08-06/11   | CAD 端字典编辑闭环、矢量大括号                                       |
| M1 (v2.0–3.2)   | 2026-07 至 08-04 | 2007 版完成、多版本适配、多格式识别                                    |

<!-- Compare / 比较： `1.0.0` 是首个已发布版本，无早期可比版本。 -->

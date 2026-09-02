# Changelog

本项目的对外版本管理自 **1.0.0** 开始。早于 1.0.0 的 v2.0–v5.3 均为发布前开发里程碑，已并入 1.0.0 的功能总览（见根 `README.md` 的"版本历史"），不再作为独立对外版本号呈现。

This project's public versioning starts at **1.0.0**. Internal iterations v2.0–v5.3 are pre-release milestones merged into the feature overview of 1.0.0 (see the "Version history" section of the root `README.md`) and are no longer exposed as standalone public releases.

Adopts [Semantic Versioning](https://semver.org/).

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

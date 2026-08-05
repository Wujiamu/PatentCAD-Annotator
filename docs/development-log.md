# 变更记录

本项目遵循语义化版本管理。

---

## v4.0 (2026-08-04)

- Runtime contract hardening (2026-08-05): 2025 版的版本矩阵明确覆盖 AutoCAD 2025–2026+；新增五版 `IO/RuntimeHost.cs` 主机边界并将 `PATMARK` 接入，新增 2010/2013/2015 严格 CAD/事务模拟测试（各 5/5，共 15/15），直接驱动对应版本生产命令。新增 `check-api-contract.ps1` 与 `tools/ApiSurfaceCheck/`，通过本机 2010 Leader、2013/2015/2025 MLeader SDK 元数据检查。AutoCAD 2026 COM 自动化返回 `80080005 (CO_E_SERVER_EXEC_FAILURE)`；Core Console 对 2025 DLL 的 `NETLOAD`/`PATCHECK` 脚本退出码为 0，但因无交互面板和独立业务断言，仅记录为部分冒烟，不替代完整主机回归。最终验证：2025 测试 106/106、模拟测试 15/15、五版编译、结构/静态/API 检查和发布暂存均通过。

- Host boundary completion (2026-08-05): extended `IO/RuntimeHost.cs` usage from `PATMARK` to all active-document reads in commands, palette, style initialization, configuration and dictionary paths across all five editions. Added a static invariant to reject direct `MdiActiveDocument` reads outside the host seam. Added read-only `check-autocad-host.ps1`; it finds AutoCAD 2026/Core Console, a running Autodesk licensing service and registered COM ProgIDs without launching or modifying the CAD installation. This narrows the remaining blocker to legitimate interactive licensing/host behavior rather than an untracked code path.

- 2025 installer fallback fix (2026-08-06): corrected the PowerShell installer to create typed registry values with `New-ItemProperty`, always register under the current user's HKCU hive even when AutoCAD is detected through HKLM, generate a usable `load-patent-marker.lsp` fallback, write an installation log, show fatal errors, and pause on interactive runs instead of closing immediately. Local PowerShell 5.1 smoke verification registered both detected AutoCAD 2026 products, generated the LSP fallback, and verified `LOADCTRLS=14`, `MANAGED=1`, and the DLL path.

- Maintainability repair execution (2026-08-05): discovered MSBuild through the local Build Tools installation, fixed legacy NuGet/RID false failures and native build exit-code propagation, and verified all five editions compile. Added a tracked eight-case parser contract fixture, optional local corpus handling, explicit per-drawing activation/release for `ConfigLoader`, `DictLoader`, and `PatSettings`, and `DictPaletteSession` as the first non-UI layer extracted from `DictPaletteControl`. Added the test project to `PatentCAD.sln` and a staging-only `package.ps1` that validates shared VBA modules and 2013/2015 ILRepack references without overwriting deployment packages. Verification: 104/104 tests, five-edition build, structure/static/sync checks, dry-run downlevel check, and release staging passed. AutoCAD 2007/2025 in-process regression remains unexecuted because those hosts are not installed locally.

- Verification and hardening (2026-08-05): added 93 automated tests; all five edition projects compile locally. `DictWriter` now clones merge inputs, repairs null model collections, and uses atomic replacement for existing dictionary files; `DictLoader` normalizes explicit null/nested values before UI use. Backup names now require the documented timestamp format, and CAD restore validates a backup before replacing the current dictionary. Edit/delete dialogs roll back in-memory changes when disk write-back fails. The 2007 project target is aligned to .NET 2.0. Deployment DLLs were rebuilt for all five editions, with Newtonsoft.Json re-merged for 2013/2015. AutoCAD in-process workflow still requires manual validation in each installed AutoCAD year.

- MLeader creation fix (2026-08-05): corrected the 2013/2015/2025 creation order so a valid `MText` (with database defaults), its text location, and the PAT MLeader style are attached before `AddLeaderLine`/`AddLastVertex`. This addresses the 2013 symptom where all points could be picked but no visible leader was created. The fix is synchronized across the MLeader editions; 2007/2010 Leader creation is unchanged.

- MLeader geometry/text fix (2026-08-06): disabled automatic dogleg, landing, and leader-to-text extension in the 2013/2015/2025 MLeader path so three-point mode keeps only the vertices supplied by the user. MLeader text is forced to horizontal angle with zero MText rotation, preventing text from becoming slanted after repositioning. 2007/2010 remain on the existing Leader + MText path, which has no MLeader auto-vertex behavior.

- Marking-section boundary fix (2026-08-05): all five VBA packages and all five CAD paste parsers now honor the document contract `附图标记说明如下：...。` by stopping at the first Chinese full stop before the following `具体实施方式` body. The boundary regression case passes through real Word COM VBA and excludes a body-only `200` marking from the generated JSON; the 2025 parser suite passes 96/96. Existing blank-paragraph handling remains only as a fallback for malformed/legacy text without the required terminator.

- VBA live verification (2026-08-05): Word COM opened the prepared `MU26005942.2稿(1).docx`, imported and executed the six actual VBA modules from all five deployment packages, and produced valid UTF-8 JSON. Fixed Word's CR-only paragraph separators not being recognized when locating the marking section; fixed uppercase suffixes such as `1342A`/`1342B` being lost or misclassified. The prepared document now exports 24 entries with 0 warnings/conflicts; CAD `MarkingTextParser` and all VBA copies are synchronized, with regression tests for CR-only sections and uppercase suffixes. A local eight-document corpus baseline was regenerated from the actual VBA modules; the 2025 test suite passes 95/95.

- 新增 CAD 端字典编辑闭环（全部 5 个版本同步）：
  - **粘贴识别**：面板新增「粘贴识别」按钮，从 Word 说明书粘贴附图标记段落，C# 移植 VBA 识别引擎（`MarkingTextParser`：段落定位 + 表格预处理 + 多格式解析），预览表格可编辑，支持「覆盖整个字典 / 按编号合并」两种写回方式
  - **编辑对话框**：双击面板条目打开，支持改编号 / 改名称 / 新增 / 删除，编号冲突（忽略大小写）即时校验；「保存并标注」保存后直接创建引线
  - **实体联动**：改号后自动更新图纸内旧编号的 PAT 引线文字（2013/2015/2025 走 MLeader，2007/2010 走 Leader 的 MText/DBText 两种 annotation）并 `Regen`
  - **冲突裁决**：Word 端导出前检测旧字典的 `modified_by: cad` 标记，若 CAD 曾修改则备份为 `<主名>.dict.json.word-<时间戳>.bak`（只保留最新一个）；CAD 面板轮询检测到 Word 已覆盖时状态栏提示并点亮「裁决」按钮，弹窗三选：采用 Word 版（删备份）/ 恢复 CAD 版（备份覆盖回 + 清 CAD 标记）/ 稍后再说
- 字典格式 v4.0：metadata 新增可选 `modified_by` / `modified_at`（CAD 手动修改标记），旧字典文件完全兼容
- 写回通道：CAD 端 `DictWriter` 输出与 VBA `JsonWriter` 逐字符兼容（2 空格缩进、\r\n、UTF-8 无 BOM、键顺序固定）；2007/2010 因 SimpleJson 仅解析不序列化，手写实现同格式序列化器
- 2007/2010 平移要点：目标 .NET 3.5 无 LINQ；降级 `out int` 内联声明与自动属性初始化器；编译用 `MSBuild /tv:4.0`（否则回退 C# 2.0 编译器）；2010 版 csproj 额外引用 PresentationCore（SDK 的 PaletteSet 依赖 WPF IWin32Window）
- 验证：2025 版识别器单测 61 项全过 + 9 份真实语料 C# vs VBA 预期一致；2013/2015/2010/2007 本地 MSBuild 编译通过；2013/2015 重新 ILRepack 合并 Newtonsoft.Json 并更新部署包；2007/2010 组内共享文件 SHA256 一致
- 5 套部署包同步：`PatentMarker.dll` 全部替换为 v4.0 编译产物；6 个 VBA 模块跨包 SHA256 一致（`AutoExport.bas` 新增导出前备份逻辑）
- 新增内部实施计划文档 `docs/v4.0-cad-edit-plan.md`（阶段任务跟踪，与 development-log / version-plan 配套）

---

## v3.2 (2026-08-04)

- 修复 2013/2015/2025 创建 MLeaderStyle 时未入库先设属性的 `eOwnerNotSet` 异常（`PatStyleInitializer` 改为先 `SetAt` + `AddNewlyCreatedDBObject` 再设置属性），双击字典面板条目触发 PATMARK 不再中断
- 2013/2015 改为单文件部署：编译后经 ILRepack 将 Newtonsoft.Json 13.0.3 合并进 `PatentMarker.dll`，安装不再要求/附带 `Newtonsoft.Json.dll`（移除 `install-2013.vbs`、`install-2015.vbs` 的依赖检查，删除两套部署包中的旧 DLL）
- 修复 VBA `Patterns.bas` 模式 1 分隔符类缺全角分号 `；` 的问题：分号/逗号混用（如 `3叶片，31第一叶片，…；4环状部；5沟槽。`）时仅识别逗号条目；补 `；` 后 18 条示例全部命中
- 验证“附图标记说明如下：”冒号前带前缀文字（如“在附图1-7中，附图标记说明如下：”）的段落定位与提取正确
- 5 套部署包 `vba/Patterns.bas` 同步更新（GBK 编码、SHA256 一致）；2013/2015 部署包 `PatentMarker.dll` 替换为 ILRepack 合并版
- 2013/2015 部署包 `README.txt` 重写为 UTF-8：移除 `Newtonsoft.Json.dll` 依赖说明，注明单文件部署
- `build.ps1` 静态检查更新：2013/2015/2025 部署包不得再含外部 `Newtonsoft.Json.dll`
- 本地编译验证：2013（.NET 4.0 / MSB3274 规避：HintPath 改 `lib/net35`）、2015（.NET 4.5）、2025（net8.0）全部通过；ILRepack 合并后程序集不再引用外部 Newtonsoft.Json（类型已内嵌）
- 修正 2013/2015 `PatentMarker.csproj` 的 `Newtonsoft.Json` HintPath（`..\packages` → `..\..\packages`，此前命令行 MSBuild 无法解析引用）

---

## v3.1 (2026-08-03)

- 新增三点模式：面板新增「点数:无限/三点」切换按钮，与线型开关正交
- 三点模式下引线固定 3 点采集：附着点 → 1 个拐点 → 文字位置，第 3 点点击后自动创建
- 关闭时保持原有无限拐点循环采集行为（默认关闭，不影响老用户）
- Esc/回车取消本次（硬性三点，不允许多拐）
- 全部 5 个版本同步：采集层逻辑相同，2007/2010 调用 CreateLeaderWithText，2013/2015/2025 调用 CreateMLeader
- 实体创建函数无改动（接受任意长度拐点列表）
- dotnet test 27/27 通过；check-version-sync 组内一致性确认

---

## v3.0 (2026-08-03)

- VBA v3.0：多格式附图标记识别（括号/连字符/英文标点/裸列表）
- VBA 支持新格式（名称+编号）提取 + 自动导出检测 DWG 命名 + `<br/>` 标签处理
- C# 面板取消 JSON 排序，按原文顺序显示（用户自定义顺序优先）
- 部署包 VBA 全部同步 v3.0（含 2007-v2 / 2007-deploy 的 JsonWriter / PatentExtractor 统一）
- 全部 5 个版本重新编译并更新部署包 DLL（含取消排序改动）

工程基础设施（不改变插件运行时行为）：

- 新增 `AGENTS.md` 代理工作指南：版本矩阵、目录约定、MLeader API 陷阱对照表、跨版本同步规则、部署包逐版本差异
- 新增 `build.ps1` 构建与环境检查脚本：SDK DLL 齐全性检查、2025 版 `dotnet build` 编译、`-Check` doctor 模式、`-Structure` 结构检查
- 新增 `.github/workflows/build.yml`：push/PR 时执行结构完整性检查；因 SDK DLL 版权不入库，真编译需本地执行 `build.ps1`
- 新增 `PatentCAD.sln` 解决方案文件（含 2025 版，可直接 `dotnet build`）

## v2.5 (2026-07-27)

- 修复 Word 2010 无法导入 `clsSaveHook.cls` 的兼容性问题（改为代码注入方式）
- 修复 2007/2010 版箭头大小修改后不能立即生效的问题
- 所有部署包补充 `install-vba.vbs` 脚本

## v2.4 (2026-07-26)

- 完成多版本适配：2010 / 2013 / 2015 / 2025，全部通过编译验证
- 修复 MLeader API 名称适配问题（`TextLocation`、`AddLastVertex` 等）
- 修复 ArrowSize / TextHeight 实例同步问题
- 统一部署包结构：根目录 `PatentMarker-{version}-deploy/`

## v2.0 (2026-07)

- 2007 版完成：样条曲线引线 + 无限拐点 + 默认无箭头
- 面板控制：字高调节、箭头开关、箭头大小、线型切换
- 字典自动刷新（2 秒轮询 `.dict.json` 时间戳）
- 字典对比功能（双向匹配 + 6 色高亮 + 对照列切换）
- 命令拼音别名（`BZ` / `BZM` / `BZC` / `BZA` / `BZS`）
- 全选 PAT 标注实体命令（`PATSELECTALL` / `BZS`）
- VBA 安装脚本：自动导入模块到 Word Normal 模板
- 部署方式：VBScript 注册表 + APPLOAD/LSP 兜底

## v1.0 (2026-06)

- 初始版本：Leader + MText 组合标注方案
- 基础面板：字典列表、搜索、字高调节
- Word VBA 提取器：从说明书提取附图标记字典

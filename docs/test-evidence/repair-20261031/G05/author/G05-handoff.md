# G05 — 开发候选与交接

状态：G05 开发候选和开发者 L1/L2 自测已完成；公共测试迁移、整合及独立验收尚未完成，整体 BLOCKED，不得关闭或部署。工作副本为 `<USERPROFILE>\AppData\Local\Temp\PatentCAD-repair-w5hkb8c3\G05`；冻结清单 `../G05-manifest.json` SHA-256 `e261ffb8581c12360187c3379f27e05430a804122a71ea8d7ce17ef221ceef8a`。

职责：R-003、R-005、R-006、R-009、R-010、R-013、R-023。计划 G05 五阶段及状态表是主契约，本文仅整理入口、拟议交接和待执行的红绿矩阵。G06/G07 均须等本包接口与回归集成后开始。

## 全部写入口与状态绑定

| 用户入口 | 现有路径与风险 | 需要绑定的上下文／调用结果 |
|---|---|---|
| 编辑名称／编号，F2 或右键 | `DictPaletteControl.EditSelectedEntry` → `EditEntryDialog.ApplyAndWrite` → `TryApplyEdit` → `WriteBack` → `DictWriter.Write` → `DictFileVisibility.TryWriteAtomic`。模型在写前原地变更；失败走 `RestoreModelMutation` | 对话框打开时原 Document、它所对应的完整字典路径、模型及该模型的同次字节内容身份；锁内复核身份、待裁决和文档／路径，再写；拒绝时恢复模型并保留文本框输入。改号后的 CAD 实体结果属 G06。 |
| 新增／删除 | `DictPaletteControl.BtnAddEntry_Click` → 同一个 `EditEntryDialog`；`BtnDelete_Click` → `TryRemoveEntry` → `WriteBack` | 同上，不可只拦修改而漏新增、删除。失败后 `Entries`／`Metadata` 和所有字段恢复，UI 留在对话框。 |
| 粘贴识别并覆盖／合并 | `DictPaletteControl.BtnPaste_Click` → `PasteRecognizeDialog.BtnConfirm_Click`：当前先取 `CurrentPath ?? ResolveDictPath`，等待用户选择后才 `LoadForCurrentDrawing`；若文档变化，路径与模型可能属于不同图纸 | 打开时抓取原文档、原路径和对应的模型／身份，不在确认后用新的 ActiveDocument 补另一半；检查冲突和版本失败保留原预览内容、提示重载。 |
| 采用 Word / 恢复 CAD / 稍后 | `DictPaletteControl.BtnArbitrate_Click` → `ArbitrateDialog` → `DictConflict.ResolveKeepWord` 或 `ResolveRestoreCad`。前者当前只有 backupPath；后者加载备份、无条件写当前 JSON、再删除备份 | 协调者同意把 `Shared/Palette/ArbitrateDialog.cs` 纳入 G05。打开时明确携带原文档、当前 JSON 路径／内容身份和选中备份；避免按时机重新推断活动图纸。锁内重核当前 JSON 和选中备份仍为所选内容；“稍后”不修改文件；恢复须 G04 验证先通过。 |
| 可见性按钮 | CAD `DictPaletteControl` → `DictPaletteWorkflow.TrySetDictVisible` → `DictFileVisibility.TrySetVisible`；Word `PatentDictPanel.chkJsonVisible_Click` → `AutoExport.SetCurrentDictVisibility` | 二者也修改目标属性。协调者同意复用同一产品锁：覆盖读属性→改属性；与 Word 导出清除／恢复属性、CAD 原子替换串行，锁忙明确失败，不改当前属性。顶层只取一次锁，内部不重取同一锁。 |

搜索 `DictWriter.Write` / `DictFileVisibility.TryWriteAtomic` / `File.Replace` / `File.Delete` 后，当前 CAD 字典内容的生产写入口为上述编辑／新增／删除、粘贴及恢复 CAD；采用 Word 会删除备份并需要更新基线。`PatPaletteCommand`、`PatCheckCommand` 和面板轮询只读取；`SetVisible` 是独立属性写入。PPT/Visio 的 `DictionaryReader` 当前只读该 JSON，不参与产品写锁，不能将其读操作称作 CAD 写入口。

`DictPaletteControl` 的 `Timer.Interval = 2000`，轮询 `DictLoader.IsFileChanged` 后加载；每版 `DictEntry.cs` 现按完整路径存 `_modelsByPath` / `_timesByPath` / `_previousByPath`，失败时留旧模型。自写由 `NotifySelfWrite` 刷缓存、清自己的 Diff；`DictPaletteSession` 还保留 Removed 差异；刷新通过 `PatCheckResult` 清理相应图纸过期结果。变更时需保持按图纸／路径隔离、关联生命周期及这些结果，内容不变时不重复反序列化。

## Word 路径与允许文件范围

- 启动：`PatentMarkerBootstrap.AutoExec` → `InitializeAutoExport` → 模块级 `m_hook`；`clsSaveHook.App_DocumentBeforeSave` 使用事件参数 `Doc` → `ExportDict` → `ExportDictCore`。已有路径普通保存导出失败设置 `Cancel=True`，首次保存或 Save As 沿用项目现有语义。
- 手动：`ShowPatentDictPanel` → `PatentDictPanel.cmdExport_Click` → `ExportDictManual` → 同一 `ExportDictCore`。`GetOutputPath` 每次按文档和 DWG 选择目标；目标变更后现有 `CleanupOrphanWordDict` 会删除 Word 旧主名字典，R-003 改为保留并记录路径。
- 当前导出先读目标属性，再 `BuildModel` / `JsonWriter.Serialize`；`IsVisibleDictContentChanged` 把现有磁盘与**本次新输出**做 `NormalizeJsonForExportCompare`，因此正常 Word 变化也被判外部修改；`BackupIfCadModified` 秒级命名后如重名先删、复制后再清其他备份；`JsonWriter.WriteToFile` 在同目录唯一临时文件写 UTF-8 无 BOM 再 `MoveFileExW`。该底层函数还写 `autoexport-error.txt`，不能把产品锁无条件塞在其中而造成递归锁或影响诊断。
- 开关：`ShowPatentDictPanel` 对每个 `Not m_enabled` 调初始化；`Property Let IsAutoExportEnabled=False` → `DisableAutoExport`。`PatentDictPanel.frm` 的启用失败分支也将该属性设为 False，不能把所有 False 均误判成“用户主动关闭”。协调者同意把 `.frm` 文本源纳入 G05，仅修改失败分支／主动关闭区分，`.frx` 字节不变；失败待初始化、首次打开、明确关闭、重新勾选、进程重启及 VBA Reset 分别验证。`AutoExport.bas` 和 `.frm` 为 CP936/CRLF；`JsonWriter.bas` 为 ASCII 兼容文本/LF，编辑须保留各自字节编码和行尾。

## 拟议交接和执行顺序

1. **同次内容身份：** 将模型与身份绑定到同一次读取的完整字节及规范化完整路径；SHA-256 可用 .NET 2.0 自带实现，区分“原先不存在”和有效空字典。保留读取失败／无效 JSON 为失败，不让它替换模型或 Diff。注意 `File.ReadAllText` 当前具 BOM 检测；若改为字节流解析，同一输入的解码行为要保持。允许对相同字节复用模型，不另建同步服务。身份来自写出的同一实际内容并在锁内完成缓存状态更新；提交后的基线/缓存故障不得声称磁盘已回滚。
2. **锁：** 同一完整字典路径对应 `<dictPath>.patlock`，Word 与 CAD 均持短时独占句柄，覆盖读取检查、备份、暂存、替换、完整属性恢复及相关成功状态更新；同步覆盖可见性切换和两种裁决。失败分支释放句柄，锁文件的存在不代表占用，不删除公共锁文件。VBA `Open … Lock Read Write` 与 .NET `FileStream(..., FileShare.None)` 的真正跨进程互斥必须先在隔离路径做双向故障测试，不能只靠代码推断；锁忙有界返回。路径别名／映射盘／第三方不遵守协议不属于已经证明的范围。
3. **恢复点：** 备份文件由 Word/CAD 按同一现有 `<dict>.word-yyyyMMdd-HHmmss.bak` 身份规则识别；只处理选中的精确产品对象。发现任何未裁决备份即保持提示，`modified_by=cad` 不清除提示；编辑／新增／删除／粘贴全拦，查看与标注仍可用。多个历史备份逐个保留，明确裁决只处理所选备份；不要清理其他备份。正常 Word 连续导出时若当前等于健康输出基线，保留冻结备份并更新输出；偏离或来源未知且不能证明等于冻结内容时停止并保留两个现有版本。
4. **输出基线（协调者已调整契约）：** `<dictPath>.word-baseline` 只保存固定产品／版本头和归一化后 UTF-8 无 BOM JSON 的 SHA-256 摘要，不保存明文快照。VBA 用 Windows 内置 CryptoAPI，CAD 用 .NET SHA-256，双方必须逐字节一致；拟定 `PATENTMARKER_WORD_BASELINE_V1` + LF + 64 位小写十六进制摘要（无 BOM），由实现和测试冻结。现有规范化先将 CRLF／CR 转 LF，只替换第一个精确 `"extracted_at": "` 的非空值，最后只裁去首尾 ASCII 空格（等价 VBA `Trim$`，C# 不得使用去除所有空白的无参数 `Trim()`）。无／坏／非本产品基线保守保护、不覆盖未知同名对象；更新失败明确“JSON 已写入但基线更新失败”，保留恢复点且不声称回滚。32/64 位 VBA 声明、空文本、中文/emoji、各换行和时间戳均需跨运行时对照；[微软说明](https://learn.microsoft.com/en-us/windows/win32/seccrypto/alg-id) XP SP2 及更早 CryptoAPI 不支持 SHA-256，应失败并明确报告，不能降级算法。
5. **Word 显式关闭：** 用最小状态区分从未成功／启用／用户关闭；不新增跨进程偏好。取消启用后的重开不重启钩子；启用失败与明确关闭不可混淆，手动重勾后可启用，AutoExec、诊断与实际保存事件不被绕开。

## 原始覆盖矩阵（实际执行结果见文末）

每项使用同一个输入／业务断言对修复前与候选重跑；不因新测试始终为绿宣称修复。用脱敏样本、专属临时路径，记录角色、工作树／工具哈希、选择器、退出码、产物 SHA-256；安装和事件链另记录候选包／实际加载路径哈希、Word 版本位数、run ID、Normal 和其他 Startup 哨兵。`SKIP/BLOCKED` 不是 PASS。

| 选择器提案／对应项 | 原始反例及绿灯业务断言 | 分层和关键组合 |
|---|---|---|
| `G05_R003_SwitchedTargetRetainsOld` | Word 原主名已导出，旧 JSON 加 CAD 独有项、完整属性，再出现唯一新 DWG 并成功导出；旧 JSON 和无关哨兵逐字节／属性不变 | L2 真实源码导入；再用同一候选 L3/L4 正常启动与用户保存；不可用只模拟名称替代真实导出。 |
| `G05_R005_FrozenBackupAndCopyFailure` | 存同秒恢复点或多个旧备份，故障注入复制／锁定／拒绝；任何已有备份不被删改，新复制失败阻止覆盖 | 同一故障注入点前后对照；检查隐藏／只读、完整属性、当前 JSON、所有备份、临时文件；成功重试。 |
| `G05_R006_KnownOutputBaseline` | 仅改 Word 同进程及重启不产生伪冲突；只改可见 JSON／两边改仍保护；已有备份时正常连续 Word 保存保留原备份 | 新建／升级无基线、健康／损坏／未知同名、路径切换、基线写失败及 JSON 与基线间中断；严格检查部分成功报告、旧文件与恢复点。 |
| `G05_R009_StaleEditAndPaste` | A 打开编辑／新增／删除／粘贴，B 写新 JSON，A 保存；拒绝 A 的旧快照，B 项不丢失、表单输入与旧有效模型保留 | 含同文档／不同图纸／切换路径、同时打开 A/B 窗口、原本不存在后被创建；同一锁覆盖版本检查后至替换前，第三方绕锁仅列限制。 |
| `G05_R010_SameTimestampAndSelfWrite` | 修改 JSON 但保持原大小与时间戳：下次轮询实际新模型与 Diff 被发现；自写不生成伪 Diff，Removed 不丢 | 有效→损坏→恢复、相同内容不重复解析、A/B 图纸隔离、目标删除／重建、PATCHECK 仅清对应过期结果。 |
| `G05_R013_PendingAllWriteEntrypoints` | 备份存在且当前含／不含 CAD 标记都显示待裁决；“稍后”后改名、改号、增删、覆盖／合并粘贴全部拒绝且可查看／标注 | 两种明确裁决在同锁/身份下核验当前及备份；备份内容／当前文件／文档变化，损坏备份、无权限、清理失败、多个历史备份和重试，均保留可恢复数据。 |
| `G05_Lock_WordCadVisibilityInterleave` | CAD 持锁时 Word 导出／切可见性失败，Word 持锁时 CAD 编辑／切可见性失败；释放后按序成功，不丢属性及内容 | 两向真实跨进程、中文空格路径、锁文件预存在但未持锁、只读目录、锁中异常及进程退出；隔离文件中验证 VBA 与 .NET 的锁确实互斥。 |
| `G05_R023_PanelReopen` | 真正同进程用户入口：关闭勾选→关闭窗体→通过 ShowPatentDictPanel 重开，仍关闭；重新勾选后保存触发钩子 | 区分首次、初始化／目标预检失败、VBA Reset、新进程；L2 测试辅助入口只能说明源码层，真正对话框、自动启动与保存事件须单列实际宿主层级。 |
| `G05_BaselineDigest` | 跨 VBA/C# 对相同规范化结果产生相同 SHA-256；摘要文件及其暂存文件不含 JSON 正文；未知同名对象不覆盖 | 中英文／emoji、时间戳、CR/LF、首尾空格和 Tab、空值；32/64 位声明；损坏摘要／哈希失败及无锁竞争。32 位宿主不可用则 SKIP/BLOCKED。 |

## 现有测试与必须迁移

- `DictLoaderTests`：合法／无效 JSON、缺失文件、归一化、清缓存、配置路径；现无同时间戳内容变化、同次字节身份、跨文档状态或写前版本检查。
- `DictWriterTests`：格式、UTF-8、可见性、写回、模型编辑及合并；`DictWriterBoundaryTests`：只读／锁定、完整属性、原子替换、ACL、命名流和回滚。多处直接调用旧无条件 `Write`，协调者已同意在新接口确定后统一迁移，不能保留生产可绕过的旧入口。
- `DictConflictTests`：查找／命名、两种裁决；`IsPendingConflict_BackupAndCadMark_False` 是旧错误语义，协调者已同意改为仍待裁决。`DictConflictBoundaryTests` 覆盖损坏备份、当前文件只读／锁定、备份删除失败和属性恢复，需保留并补相应身份／锁。G04 `G04JsonRegressionTests` 及实际旧解析器探针必须继续通过，签名变动调用由协调者整合。
- `tools/verify-vba-export.vbs` 为 L2 源码导入／直接测试入口，覆盖路径映射及部分保存取消；`tools/test-vba-panel.vbs` 为 L2 注入窗体处理器，覆盖开关和可见性，未走真正的“用户入口重开”。`tools/verify-vba-installer.ps1` 为隔离 Startup 安装失败矩阵；`tools/verify-vba-installed-e2e.ps1 -ExtendedMatrix` 是实际候选／新 Word 进程链路，现有多文档、Save As 后续保存、锁定／只读和非项目哨兵断言不可退化。不得称这些旧探针已覆盖新增的交错和界面行为。
- 不能先打开面板／手动调用初始化或导出再声称安装启动黑盒通过；不能仅以进程退出码、文件存在、日志存在或修改时间作为业务 PASS。

## 公共文案需求（协调者负责 Strings.cs）

- `Conflict_StatusDetected`（`DictPaletteControl.UpdateConflictState`）：中“发现待裁决的字典备份 — 点击「裁决」处理”；英“A dictionary backup needs a decision — click Arbitrate”。
- `Conflict_Msg`（`ArbitrateDialog.InitializeComponent`）：中“发现尚待裁决的字典备份：\n{0}\n\n请选择处理方式：”；英“A dictionary backup is awaiting a decision:\n{0}\n\nChoose how to proceed:”。保留原“采用 Word／恢复 CAD／稍后”按钮。
- 最少错误语义：锁忙“字典正在使用，请重试。 / The dictionary is busy. Try again.”；版本或上下文变化“字典或图纸已变化，请重载并重新打开对话框。 / The dictionary or drawing changed. Reload and reopen the dialog.”；待裁决“请先裁决待处理的字典备份。 / Resolve the pending dictionary backup first.”。基线失败需要单独表达“字典已写入，但基线更新失败”；诊断按现有 run ID／阶段／路径记录错误，不记正文。

以上为修复前分析；下方记录实际结果。GitHub 外部宿主验收按计划单列；本机不安装、启动或探测四个旧 CAD 宿主。


## 2026-11-01 候选冻结结果

执行身份 local-user-sha256:c683831d6b0066c74ee53f1234eb0c59406496f43c89a3697b6839921c14c6c8，时区 Asia/Shanghai；Word 16.0，实际 WINWORD.EXE 的 PE machine 为 0x8664，即 64 位。测试开始前无既有 WINWORD，完成后确认本次实例退出。Word 测试使用本次创建的脱敏临时 docm、直接导入源码，不是部署包或实际 Startup 验证。

副本没有 .git，以父目录 G05-manifest.json 的 SHA-256 e261ffb8581c12360187c3379f27e05430a804122a71ea8d7ce17ef221ceef8a 为基线。修改前 21 个相关文件冻结在 docs/test-evidence/G05/source-before/，清单 SHA-256 c02ec6aafc906a50304db6b1bb00230e3304624c7f046b964fa7dad5e727c543。1219 项原清单复核无缺失，除 19 项本包生产源和协调者交接的 Strings.cs 之外未变更；未改公共测试、公共项目、桩或部署资产。

候选文件、五版逐一变更、工具及证据哈希见 docs/test-evidence/G05/candidate-manifest.json，SHA-256 55d42137730de580f307675762071c10dca9c5fddad5253d40f9f88e2c01c189。19 项生产文件是：五版各自 IO/DictEntry.cs 与 IO/DictWriter.cs；Shared/IO/PatSettings.cs、Shared/IO/DictConflict.cs；Shared/Palette 的 DictPaletteControl、DictPaletteWorkflow、EditEntryDialog、PasteRecognizeDialog、ArbitrateDialog；vba/AutoExport.bas、vba/PatentDictPanel.frm。五项新测试源是 G05ProtocolTests.cs、tools/test-g05-word.vbs、tools/test-g05-digest.vbs、tools/test-g05-lock.cs、tools/test-g05-lock.vbs。

Strings.cs 由协调者单独交接，原 SHA-256 8b082d61446fba0aef7c44dc4fff495294efc1f644aed09e5fddff1ffbde34a3，新 f764dc755cfcee0b30edd57fa3f270a771658d4c2e037e8b3cc720a7d56b8e7c；不能作为本包覆盖补丁。JsonWriter.bas 原样；.frx 原样，SHA-256 1fbceacb93828e1f35da7b01d54e4544e6f4d3281d509cf26a9d3459776ed7c1。AutoExport.bas、.frm 保持 CP936 可逆与纯 CRLF；可见性失败提示行已获协调者补充授权。

本副本基线早于 G04 严格 UTF-8 最终集成。候选按协调者交接将严格 UTF-8（允许 UTF-8 BOM，拒绝无效字节）合入同次 File.ReadAllBytes 的身份与解析；与根中 G04 最终修补需逐项合并，不能以旧基线整文件覆盖。DictLoader.LoadBytes(path, bytes) 为程序集内部接口，裁决用它解析已经核验的同一字节快照。

## 接口与业务变更

1. 五版按规范路径缓存同次读取的原始字节 SHA-256，内容相同才复用模型；读取、严格解码或解析失败保留旧有效模型和身份。GetIdentity(model,path) 只对同一缓存模型返回身份，NotifySelfWrite(model,path,identity) 接受已写入字节身份。
2. 字典顶层写入、裁决及 CAD/Word 可见性切换共用 <dictPath>.patlock 独占句柄，覆盖读、版本检查、备份、替换、完整属性恢复和状态更新。锁文件存在不等于持锁，失败释放。DictWriter.Write(path,model,expectedIdentity,out error) 锁内复核版本与待裁决状态；旧三参入口因无身份恒拒绝。MissingIdentity 表示实际观察到目标不存在，首次创建锁内再次确认；若他人抢先创建则拒绝。
3. ResolveKeepWord(dictPath,identity,backupPath,backupIdentity,ContextCheck,out error) 和 ResolveRestoreCad 的对应新签名锁内绑定原 Document、路径、当前及所选备份身份。恢复先解析同一备份快照，字典与摘要写入后仅删除所选备份；采用 Word 更新摘要后仅删除所选备份；稍后不写。旧裁决重载恒拒绝。字典已保存但摘要失败时报告 Dict_BaselineFailed，保留恢复点，不声称回滚。
4. 两端摘要 sidecar <dictPath>.word-baseline 是 ASCII 头 PATENTMARKER_WORD_BASELINE_V1、LF、64 个小写 SHA-256 十六进制字符，无 BOM/正文。输入为双方一致归一化的 UTF-8 无 BOM；Word 用系统 CryptoAPI，CAD 用平台 SHA-256。未知同名文件不覆盖；缺或坏产品基线先保护旧字典。存在待裁决备份时，仅当前字节等于冻结备份或内容匹配健康基线才允许继续导出；其他情况阻止并保留两端。目标切换保留旧 Word 字典并记路径。
5. 面板主动关闭使用进程内显式状态；重新打开不擅自启用，重新勾选与初始化失败分开。可见性失败通过诊断状态说明。SetCurrentDictVisibility 先保存原错误，仅对句柄关闭短暂 Resume Next，紧接 On Error GoTo 0。

## 红绿和检查

- CAD L1 同一测试源码：dotnet test ./docs/test-evidence/G05/cad-probe/{before或after}/cad-plugin/2025/PatentMarker.Tests/PatentMarker.Tests.csproj --configuration Release --nologo -v minimal --filter 'FullyQualifiedName~G05ProtocolTests'。before 5 FAIL/exit 1，after 5 PASS/exit 0；两侧源码 SHA-256 均 41f236e4179501cd4326548c3b5f9a7f7edb9fa92f1e3d3ec35400c092ab0510，日志 cad-red-same-source.txt / cad-green-same-source.txt。反例覆盖读后他方写入、锁忙、同大小/时间内容变化、CAD 标记仍待裁决、待裁决阻止写入。after 探针源是“选中备份被删除时报 Changed”末次小修之前，最终源码由下项覆盖。
- 最终 CAD L1：dotnet test ./cad-plugin/2025/PatentMarker.Tests/PatentMarker.Tests.csproj --configuration Release --nologo -v minimal --filter 'FullyQualifiedName~G05ProtocolTests'，18 PASS/exit 0，日志 green-cad-final2.txt。R010_InvalidUtf8RetainsCachedModelAndBlocksStaleWrite 的初始候选在 red-utf8.txt 为 1 FAIL；R013_ChangedSelectedBackupCannotBeDeleted 和 R013_RestoreSelectedBackupWritesDigestAndKeepsOtherBackup 在 red-arbitration.txt 为 2 FAIL，随后 green-arbitration.txt 为 2 PASS。
- 裁决直接入口精确选择器 FullyQualifiedName~R013_ActualGuardedRestoreRejectsCorruptBackup、FullyQualifiedName~R013_ActualGuardedRestoreRejectsInvalidUtf8Backup、FullyQualifiedName~R013_RestoreSelectedBackupWritesDigestAndKeepsOtherBackup 均通过，断言实际解析阶段错误、当前及损坏备份字节不变，及有效恢复内容、摘要和仅删选中备份。另测上下文切换、选中备份被删、未知基线及正常采用 Word。不得用旧三参 G04 预期失败测试替代。
- 首次创建选择器 FullyQualifiedName~R009_FirstCreate：first-create.txt 的 2 项 PASS。目标实际起初不存在，验证默认 Hidden/System；另一写入者抢先创建时拒绝且保留其字节。这是底层写入契约，不伪称通过 UI 新建。
- Word 同一测试脚本命令 cscript //nologo tools/test-g05-word.vbs <vba-dir> <空产物目录>：使用 docs/test-evidence/G05/vba-before 为 17 FAIL/exit 1，word-red-repro.txt；使用 vba 为 0 FAIL/exit 0，word-final2.txt。先决条件失败导致未执行的红灯子断言不计 PASS；绿灯含已有恢复点后正常导出、未知/损坏摘要及摘要写失败后字典确已改变但恢复点和旧摘要保留。
- R005copy 绿灯的错误记录含 injected copy failure，证明实际命中复制故障，字节及属性保留；R005same 绿灯在待裁决检查处更早拒绝，不能说它也执行了复制故障。OS 物理复制中途失败尚未覆盖。
- 摘要命令 cscript //nologo tools/test-g05-digest.vbs vba docs/test-evidence/G05/digest-word-final2，结合 C# 选择器 FullyQualifiedName~BaselineDigest_MatchesWordNormalizationAndUtf8：Word VBA L2 与 C# L1 的六组期望均 PASS，覆盖空、英文、中文/emoji、时间戳、CR/LF/CRLF、空格和 Tab。Word 测试只在导入副本中公开函数以调用，不改生产源；32 位 Word 未测。
- 跨进程命令 cscript //nologo tools/test-g05-lock.vbs vba docs/test-evidence/G05/word-lock-final2 docs/test-evidence/G05/g05-lock-probe.exe：PASS。Word VBA 持锁拒绝 .NET FileStream，.NET 持锁拒绝 Word 锁、导出及可见性变更，属性/内容保留，释放后恢复；CAD 生产 TrySetVisible 另有 R009_HeldProductLockPreventsCadVisibilityChange L1。不能将此称作真实 CAD UI 验收。
- cscript //nologo tools/test-vba-panel.vbs vba 与 cscript //nologo tools/verify-vba-export.vbs vba 均 PASS/exit 0/L2，日志 word-panel-existing.txt / word-export-existing.txt；前者导入 8 个组件和 .frx 并测面板源码，不证明真实 Startup 或 GUI 重开。
- powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Version all 和 ./check-version-sync.ps1 均 PASS/exit 0，日志 build-all-final2.txt / version-sync.txt；五版 SDK 编译不等于旧宿主运行通过。
- 未迁移公共全量 dotnet test ./cad-plugin/2025/PatentMarker.Tests/PatentMarker.Tests.csproj --configuration Release --nologo -v minimal：19 FAIL、135 PASS、0 SKIP/exit 1，public-tests-before-migration.txt。不是放行；旧入口的预期失败测试还可能假绿。

使用实际 VSTest，不使用存在假绿问题的 FactRunner。SDK 8.0.422；dotnet.exe SHA-256 6d12b93599139e67b8dd951a06a9cda10bc0261e732432db483ea5e7e5c1b479；System32/cscript.exe SHA-256 6f15c80751abf5cfe3fdd69744b7c4146de1ef6464b0398c3e89edd011a02c3f。探针、日志的准确哈希均在候选清单内。验收角色仅开发者自测，尚无独立验收。

## 公共测试与 G06 交接

协调者已另建 G05-public-tests 副本准备迁移，尚未与候选组合。必须迁移 DictWriterTests.cs、DictWriterBoundaryTests.cs、DictConflictTests.cs、DictConflictBoundaryTests.cs、G04JsonRegressionTests.cs 和 tools/G04JsonRegression/Program.cs 的所有旧写入及裁决调用，包括以前期待失败的断言；只改 19 个显式 FAIL 会让恒拒绝的旧接口产生假绿。已存在字典应在模拟外部修改前记录对应字节身份；真实首次创建记录 MissingIdentity，不能写前临时重读冒充原身份。

裁决测试应提供目标/所选备份双身份和上下文守卫，核验锁忙、版本变化、备份解析失败、删除失败及恢复成功的具体阶段。ResolveKeepWord_MissingBackup_StillTrue 改为文件变化拒绝，IsPendingConflict_BackupAndCadMark_False 改为仍待裁决。G04 探针必须直接走新接口与损坏备份解析，不能只断言 null。

G06 必须传递同一原 Document、路径、模型的同次字节身份并复用产品锁，禁止绕回三参写入。JSON 成功而 CAD 事务失败需报告部分成功并基于原文档处理，本包不宣称 JSON 与 DWG 事务原子化。

## SKIP/BLOCKED 与残余风险

- BLOCKED：协调者整合 G04 最终解码、迁移后的公共全量测试、独立验收。当前不得标记关闭；需冻结整合结果并按实际加载文件与工具哈希重跑。
- SKIP/BLOCKED：候选包安装后 Word 自动启动/保存事件、真实面板交互和可视布局、32 位 Office/CryptoAPI、VBA Reset、ACL/目录权限拒绝、备份清理失败及所有保存状态组合、真实 CAD UI/命令和多文档切换。R023 仅证明抑制模态显示的源码入口及独立面板导入，不证明实际用户重开。
- GitHub 外部宿主验收：2007/2010/2013/2015 四组只通过候选包在目标环境验证，本机不探测、安装或启动；五版 SDK 编译不外推为宿主运行通过。
- 产品锁只协调遵守协议的进程；路径别名/映射盘和绕锁第三方不受保护。现有复制故障在调用处注入，未模拟 OS 物理复制中途产生部分文件；意外断电、低资源、备份删除失败需独立故障矩阵。原子替换不保证 JSON–DWG 跨系统事务。

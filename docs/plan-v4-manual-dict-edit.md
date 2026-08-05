# v4.0 实施计划：CAD 字典手动编辑功能

> 状态：**已确认（grill 讨论定稿，2026-08-04）**
> 计划文件：`C:\Users\wjm\AppData\Roaming\QoderCN\SharedClientCache\cache\plans\CAD字典手动编辑v4.0_task-08b.md`（外部缓存，本文件为工作空间内的正式副本 + 执行跟踪）

## 1. 目标与核心原则

用户是数据权威源：双端冲突无默认偏向，每次由用户人工裁决保留哪一版。2025 版先行（本机 AutoCAD 2026 实测 + 单测），验证通过后向 2013/2015 → 2010/2007 平移。

## 2. 已确认的决策清单（grill 结论）

| 决策点 | 结论 |
|--------|------|
| 编辑范围 | name/number 修改 + 新增/删除条目；occurrences 不动（另立任务） |
| 图纸联动 | 仅 number 变更同步图纸标注文字（多条同号全改+提示）；name 变更不碰图纸；删除条目不删图纸 |
| 粘贴识别 | 识别结果预览可编辑；确认时可选覆盖/合并（弹选项，Q9/C） |
| 冲突裁决 | Word 静默备份（只保留最新一个）+ CAD 状态栏提示（不弹窗打断，Q7/B）；裁决后清理备份并清除 modified_by 标记防循环（Q8） |
| 权威源 | 二选一，无合并逻辑（Q5） |
| occurrences | 本次不动（Q4/C） |
| 实施起点 | 2025 版先行（Q10/A，本机 AutoCAD 2026 实测） |
| 识别验收 | 9 份真实语料逐条比对 number/name（Q11） |
| 版本号 | v4.0（Q12） |

## 3. 功能规格

### 3.1 功能 1：粘贴识别生成字典

- 面板新增"粘贴识别"入口，弹模态对话框：多行输入框 + 识别按钮 + 预览列表（编号/名称/识别状态，行内可编辑）
- 识别引擎：`Patterns.bas` v3.0 移植为 C# `MarkingTextParser`（.NET Regex；含段落定位 `ExtractMarkingSection` 移植；2007 版不用 LINQ）
- 确认写回时弹选项：覆盖 / 合并（按 number 匹配，已有条目更新 name，新编号追加，保持 JSON 原始顺序）
- 写回 JSON 时在 metadata 增加 `"modified_by": "cad"` + `"modified_at"`

### 3.2 功能 2：面板行内编辑 + JSON 写回

- 双击条目弹编辑对话框（5 版本统一交互）：可改 number/name；新增按钮；删除按钮（确认框）
- 新增 `DictWriter`：序列化 + 写回，5 版本分别实现（SimpleJson 扩展序列化器 / Newtonsoft / System.Text.Json），UTF-8 无 BOM（`new UTF8Encoding(false)`），键顺序与缩进对齐 VBA 输出
- `DictLoader` 新增自写接口：写回后同步更新 `_cachedModel/_cachedTime` 并 `ClearPrevious()`，避免 2s 轮询触发假 Diff 高亮
- number 变更时：复用 BZC 扫描机制，`PatEntityHelper` 新增"按旧编号改文字"（MLeader：`mleader.MText.Contents`；2007/2010：`tr.GetObject(leader.Annotation, ForWrite)` 改 Contents/TextString）；改后 `Editor.Regen()` 刷新

### 3.3 功能 3：双端冲突裁决

- VBA 端（AutoExport.bas）：WriteToFile 前读旧文件，字符串 Contains `"modified_by"` → 先备份旧文件为 `<主名>.dict.json.word-<yyyymmdd-hhnnss>.bak`（删除旧备份只留最新）→ 再写新文件
- CAD 端：轮询发现同目录存在 `.word-*.bak` 且当前 JSON 无 CAD 标记 → 状态栏显示"检测到 Word 已覆盖 CAD 修改" + 裁决按钮点亮
- 裁决对话框三选：采用 Word 版（删备份）/ 恢复 CAD 版（备份覆盖回 + 删备份 + **清除 modified_by**）/ 稍后再说

## 4. 实施顺序与验收

### 阶段 1：2025 版（含测试项目）

1. `MarkingTextParser` 移植 + `DictWriter` + `DictLoader` 自写接口
2. 单元测试：识别器用例 + 9 份真实语料对比（`批量测试/` 下 txt 跑 C# 识别 vs VBA 预期输出，逐条比对 number/name）
3. 面板 UI（粘贴识别对话框、编辑对话框、裁决提示）+ PatEntityHelper 改文字
4. 本地编译 + 本机 AutoCAD 2026 NETLOAD 实测全流程（粘贴→识别→确认→编辑→改号同步图纸→Word 保存→裁决）

### 阶段 2：2013/2015 平移（Newtonsoft.Json，MLeader）

### 阶段 3：2010/2007 平移（2007：无 LINQ、Leader+MText、SimpleJson 扩展序列化器）

每阶段：编译验证（lib/ 下 SDK DLL）+ 与 2025 版逻辑逐项对照

### 阶段 4：VBA 与部署包

- AutoExport.bas 修改同步 5 套部署包（含已部署旧模块的兼容：C# 端容忍旧 JSON 无新字段）
- 编译各版本 DLL 更新对应部署包

## 5. 文档同步（AGENTS.md 要求）

- `docs/development-log.md`：v4.0 条目（沿用 v2.5/v2.4 格式）
- 根 `README.md` + 受影响部署包 README：功能清单、新交互说明
- 5 套部署包：vba/ 模块 + DLL + 安装脚本核验

## 6. 范围外（明确不做）

- occurrences 字段移除（Q4/C，另立任务）
- Word 端新 UI（仅静默备份逻辑）
- 冲突合并逻辑（Q5 二选一）
- 删除条目时联动删除图纸标注（BZC 会报出，用户自理）

---

## 7. 执行跟踪表

> 每步完成后由执行代理更新状态与验证结果，保证全程可追溯。

| # | 步骤 | 版本 | 状态 | 验证结果 |
|---|------|------|------|----------|
| 1 | 移植 `MarkingTextParser`（识别引擎 + 段落定位） | 2025 | ✅ 完成 | 5 模式 + 三梯队 + 去重 + 段落定位 + 表格预处理（`<br>`→vbCr、reTbl A/B、Chr(7)→顿号）+ VBA 第二梯队 keepRanges bug 原样复刻 |
| 2 | 新增 `DictWriter`（序列化写回，UTF-8 无 BOM） | 2025 | ✅ 完成 | 2 空格缩进、\r\n 行尾（正则统一）、键顺序 metadata→entries→warnings；.tmp 原子替换；`new UTF8Encoding(false)`；Encoder 关闭 \uXXXX 转义 |
| 3 | `DictLoader` 自写接口（写回后更新缓存/基线） | 2025 | ✅ 完成 | `CurrentPath`/`ResolveDictPath` 公开；`NotifySelfWrite` 更新缓存 + `ClearPrevious()` 防假 Diff |
| 4 | 识别器单测 + 9 份真实语料对比 | 2025.Tests | ✅ 完成 | 62 项全过：61 项单测（逐模式/重叠/去重/段落/HTML 双路径/表格 3 项）+ 1 项语料对比；8 份真实 txt 跑 C# vs VBA v3.0 权威预期（cscript 独立 vbs 生成）逐条一致（含 header/section/hits 全量比对） |
| 5 | 面板 UI：粘贴识别对话框（预览可编辑 + 覆盖/合并） | 2025 | ✅ 完成 | `PasteRecognizeDialog`（多行输入→识别→DataGridView 预览可编辑→覆盖/合并写回）；`DictWriter.BuildWriteModel` 下沉 IO 层（覆盖/合并/大小写匹配/顺序保持）+ 5 项单测；面板新增「粘贴识别」按钮；编译 0 错 0 新警告 |
| 6 | 面板 UI：编辑对话框（改 number/name、新增、删除） | 2025 | ✅ 完成 | `EditEntryDialog`（保存/保存并标注/删除，OK/Yes/Abort 交互约定）；`DictWriter.TryApplyEdit/TryRemoveEntry`（编号冲突忽略大小写排除自身）+ 8 项单测；面板双击改开编辑对话框，原双击装填行为移至「保存并标注」；新增按钮；编译 0 错 |
| 7 | `PatEntityHelper` 按旧编号改文字 + Regen | 2025 | ✅ 完成 | `SetMLeaderNumber`（MText.Contents 写入，未变化返回 false）+ `RenameNumberInModelSpace`（trim 忽略大小写匹配，与 BZC 口径一致）；面板事务 + Regen + 状态栏/命令行提示；编译 0 错 |
| 8 | 冲突裁决：备份检测 + 状态栏提示 + 裁决对话框 + 清除标记 | 2025 | ✅ 完成 | `DictConflict`（FindWordBackup 按文件名时间戳取最新 / IsPendingConflict / ResolveKeepWord / ResolveRestoreCad 恢复+清标记+删备份）；`ArbitrateDialog` 三选（OK=采用 Word 版/Yes=恢复 CAD 版/Cancel=稍后再说）；面板 2s 轮询 `CheckConflictState`（橙色状态栏提示 + 裁决按钮点亮/熄灭）；13 项单测；编译 0 错，全量 88 项测试通过 |
| 9 | 2025 编译 + 本机 AutoCAD 2026 全流程实测 | 2025 | ⏳ 待开始 | |
| 10 | 平移 2013/2015（Newtonsoft + MLeader）并编译验证 | 2013/2015 | ⏳ 待开始 | |
| 11 | 平移 2010/2007（无 LINQ、Leader+MText、SimpleJson 序列化器）并编译验证 | 2010/2007 | ⏳ 待开始 | |
| 12 | VBA `AutoExport.bas` 备份逻辑 + 同步 5 套部署包 | VBA | ⏳ 待开始 | |
| 13 | 更新 5 套部署包 DLL + 安装脚本核验 | 部署包 | ⏳ 待开始 | |
| 14 | `docs/development-log.md` v4.0 条目 + README 同步 | 文档 | ⏳ 待开始 | |

## 8. 关键风险与对策（沿用可行性验证结论）

| 风险 | 对策 |
|------|------|
| C# 端零写入能力，SimpleJson 无序列化器 | 新增 DictWriter，5 版本分别实现 |
| 写回后触发自动重载 + 假 Diff 高亮 | 写回后更新缓存时间戳/基线（步骤 3） |
| Word 无条件覆盖 + 无版本保留 | VBA 备份机制 + 5 套部署包同步（步骤 12） |
| 粘贴文本无段落定位，识别精度下降 | C# 复刻 ExtractMarkingSection，预览确认 |
| UTF-8 BOM 差异 | UTF8Encoding(false) |
| 面板 UI 空间不足 | 粘贴识别/编辑用独立对话框承载 |
| 裁决后 modified_by 残留导致循环弹窗 | 裁决完成后清除标记（步骤 8） |

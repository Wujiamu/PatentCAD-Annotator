using System;

namespace PatentMarker.I18n
{
    /// <summary>
    /// 集中管理所有用户可见字符串的中英双语映射。
    /// 通过 Strings.Lang 切换语言，各属性自动返回对应文本。
    /// | Central registry for all user-visible strings (zh/en).
    ///   Set Strings.Lang to switch; properties return the matching text.
    ///
    /// 用法 | Usage:
    ///   Strings.Lang = Language.English;
    ///   ed.WriteMessage(Strings.PatMark_EnterNumber);
    ///   ed.WriteMessage(string.Format(Strings.PatMark_Created, num, vertexCount));
    /// </summary>
    public static class Strings
    {
        private static Language _lang = Language.Chinese;

        /// <summary>当前语言 | Current language</summary>
        public static Language Lang
        {
            get { return _lang; }
            set { _lang = value; }
        }

        private static bool En { get { return _lang == Language.English; } }

        // ================================================================
        // 通用 | Common
        // ================================================================

        /// <summary>"PatentMarker 错误: " 前缀 | error prefix</summary>
        public static string ErrorPrefix
        {
            get { return En ? "\nPatentMarker error: " : "\nPatentMarker 错误: "; }
        }

        // ================================================================
        // PatMarkCommand
        // ================================================================

        public static string PatMark_EnterNumber
        {
            get { return En ? "\nEnter part number: " : "\n输入零件编号: "; }
        }

        public static string PatMark_EnterName
        {
            get { return En ? "\nEnter part name (optional): " : "\n输入零件名称（可选）: "; }
        }

        public static string PatMark_NoNumber
        {
            get { return En ? "\nPatentMarker: no part number specified.\n" : "\nPatentMarker: 未指定零件编号。\n"; }
        }

        /// <summary>0=number, 1=name | 点击 [num name] 的标注点（Esc 取消）</summary>
        public static string PatMark_PromptAttachPoint
        {
            get { return En ? "\nClick annotation point for [{0} {1}] (Esc to cancel): " : "\n点击 [{0} {1}] 的标注点（Esc 取消）: "; }
        }

        public static string PatMark_PromptFirstDogleg
        {
            get { return En ? "\nClick vertex (Enter to end, at least 1): " : "\n点击拐点（回车结束，至少1个）: "; }
        }

        public static string PatMark_PromptNextDogleg
        {
            get { return En ? "\nClick next vertex (Enter to end): " : "\n点击下一个拐点（回车结束）: "; }
        }

        public static string PatMark_NeedOneDogleg
        {
            get { return En ? "\n  At least 1 vertex required, keep clicking.\n" : "\n  至少需要1个拐点，请继续点击。\n"; }
        }

        public static string PatMark_PromptTextPos
        {
            get { return En ? "\nClick text position (Enter = last vertex): " : "\n点击文字位置（回车=最后拐点）: "; }
        }

        // v3.1：三点模式取点提示 | Three-point mode prompts
        public static string PatMark_PromptDogleg3
        {
            get { return En ? "\n[3-point] Click vertex (point 2 of 3): " : "\n[三点模式] 点击拐点（第 2/3 点）: "; }
        }

        public static string PatMark_PromptTextPos3
        {
            get { return En ? "\n[3-point] Click text position (point 3 of 3, auto-create): " : "\n[三点模式] 点击文字位置（第 3/3 点，自动创建）: "; }
        }

        /// <summary>0=number, 1=vertexCount | 已创建引线: num（N 个顶点）</summary>
        // PATBRACE / PATBRACEEDIT — 参数化矢量大括号
        public static string PatBrace_PromptTop
        {
            get { return En ? "\nBrace top point: " : "\n大括号顶部: "; }
        }

        public static string PatBrace_PromptBottom
        {
            get { return En ? "\nBrace bottom point: " : "\n大括号底部: "; }
        }

        public static string PatBrace_PromptWidth
        {
            get { return En ? "\nClick brace width direction: " : "\n点击大括号宽度方向: "; }
        }

        public static string PatBrace_Created
        {
            get { return En ? "\nBrace created (height={0:F2}, width={1:F2}).\n" : "\n已创建大括号（高度={0:F2}，宽度={1:F2}）。\n"; }
        }

        public static string PatBrace_PromptSelect
        {
            get { return En ? "\nSelect a PAT brace: " : "\n选择一个大括号: "; }
        }

        public static string PatBrace_NotSelected
        {
            get { return En ? "\nNo brace selected.\n" : "\n未选择大括号。\n"; }
        }

        public static string PatBrace_NotFound
        {
            get { return En ? "\nNo PAT brace found in the selection.\n" : "\n选择集中未找到大括号。\n"; }
        }

        public static string PatBrace_EditModePrompt
        {
            get { return En ? "\nEdit mode:" : "\n编辑方式:"; }
        }

        public static string PatBrace_EditPoints
        {
            get { return En ? "Points" : "控制点"; }
        }

        public static string PatBrace_EditSize
        {
            get { return En ? "Size" : "尺寸"; }
        }

        public static string PatBrace_EditTop
        {
            get { return En ? "\nNew top point (Enter=keep): " : "\n新顶部（回车保持原值）: "; }
        }

        public static string PatBrace_EditBottom
        {
            get { return En ? "\nNew bottom point (Enter=keep): " : "\n新底部（回车保持原值）: "; }
        }

        public static string PatBrace_EditWidth
        {
            get { return En ? "\nNew width point (Enter=keep): " : "\n新宽度点（回车保持原值）: "; }
        }

        public static string PatBrace_EditHeight
        {
            get { return En ? "\nHeight <{0:F2}>: " : "\n高度 <{0:F2}>: "; }
        }

        public static string PatBrace_EditWidthValue
        {
            get { return En ? "\nWidth <{0:F2}>: " : "\n宽度 <{0:F2}>: "; }
        }

        public static string PatBrace_Updated
        {
            get { return En ? "\nBrace updated (height={0:F2}, width={1:F2}).\n" : "\n已更新大括号（高度={0:F2}，宽度={1:F2}）。\n"; }
        }

        public static string PatMark_Created
        {
            get { return En ? "\n  Leader created: {0} ({1} vertices)\n" : "\n  已创建引线: {0}（{1} 个顶点）\n"; }
        }

        /// <summary>0=number | >> 已切换为: num</summary>
        public static string PatMark_Switched
        {
            get { return En ? "\n  >> Switched to: {0}\n" : "\n  >> 已切换为: {0}\n"; }
        }

        // ================================================================
        // PatAlignCommand — 关键字需与比较逻辑保持一致
        // ================================================================

        public static string PatAlign_ModePrompt
        {
            get { return En ? "\nAlign mode:" : "\n对齐模式:"; }
        }

        /// <summary>关键字 | Keyword: 选择 / Select</summary>
        public static string PatAlign_KwSelect
        {
            get { return En ? "Select" : "选择"; }
        }

        /// <summary>关键字 | Keyword: 框边 / Frame</summary>
        public static string PatAlign_KwFrame
        {
            get { return En ? "Frame" : "框边"; }
        }

        public static string PatAlign_PromptSelect
        {
            get { return En ? "\nSelect PAT_STYLE leaders to align: " : "\n选择要对齐的 PAT_STYLE 引线: "; }
        }

        public static string PatAlign_NoSelection
        {
            get { return En ? "\nNo objects selected.\n" : "\n未选择对象。\n"; }
        }

        public static string PatAlign_PromptRefPoint
        {
            get { return En ? "\nSelect alignment reference point: " : "\n选择对齐参考点: "; }
        }

        public static string PatAlign_DirectionPrompt
        {
            get { return En ? "\nAlign direction?" : "\n对齐方向?"; }
        }

        /// <summary>关键字 | Keyword: 水平 / Horizontal</summary>
        public static string PatAlign_KwHorizontal
        {
            get { return En ? "Horizontal" : "水平"; }
        }

        /// <summary>关键字 | Keyword: 垂直 / Vertical</summary>
        public static string PatAlign_KwVertical
        {
            get { return En ? "Vertical" : "垂直"; }
        }

        /// <summary>0=aligned, 1=skipped, 2=errors</summary>
        public static string PatAlign_ResultSelect
        {
            get { return En ? "\nAligned {0} leaders (skipped {1}, errors {2}).\n" : "\n对齐 {0} 条引线（跳过 {1}，错误 {2}）。\n"; }
        }

        public static string PatAlign_PromptFrameCorner1
        {
            get { return En ? "\nReference frame first corner: " : "\n参考框第一角: "; }
        }

        public static string PatAlign_PromptFrameCorner2
        {
            get { return En ? "\nOpposite corner: " : "\n对角: "; }
        }

        public static string PatAlign_SidePrompt
        {
            get { return En ? "\nAlign to which side?" : "\n对齐到哪边?"; }
        }

        /// <summary>关键字 | Keyword: 左 / Left</summary>
        public static string PatAlign_KwLeft
        {
            get { return En ? "Left" : "左"; }
        }

        /// <summary>关键字 | Keyword: 右 / Right</summary>
        public static string PatAlign_KwRight
        {
            get { return En ? "Right" : "右"; }
        }

        /// <summary>关键字 | Keyword: 上 / Top</summary>
        public static string PatAlign_KwTop
        {
            get { return En ? "Top" : "上"; }
        }

        /// <summary>关键字 | Keyword: 下 / Bottom</summary>
        public static string PatAlign_KwBottom
        {
            get { return En ? "Bottom" : "下"; }
        }

        /// <summary>0=aligned, 1=side, 2=margin, 3=skipped, 4=errors</summary>
        public static string PatAlign_ResultFrame
        {
            get { return En ? "\nAligned {0} leaders to {1} side (margin={2}, skipped {3}, errors {4}).\n"
                            : "\n对齐 {0} 条引线到{1}边（margin={2}，跳过 {3}，错误 {4}）。\n"; }
        }

        // ================================================================
        // PatCheckCommand
        // ================================================================

        public static string PatCheck_NoDict
        {
            get { return En ? "\nPatentMarker PATCHECK: dictionary not loaded. Place <dwgname>.dict.json in the DWG directory.\n"
                            : "\nPatentMarker PATCHECK: 未加载字典。请将 <dwg名>.dict.json 放在 DWG 同目录。\n"; }
        }

        public static string PatCheck_ReportTitle
        {
            get { return En ? "\n========== PATCHECK Result ==========\n" : "\n========== PATCHECK 结果 ==========\n"; }
        }

        /// <summary>0=dictCount, 1=drawingCount</summary>
        public static string PatCheck_Summary
        {
            get { return En ? "Dict entries: {0}  |  Drawing leaders (PAT_STYLE): {1}\n" : "字典条目: {0}  |  图纸引线 (PAT_STYLE): {1}\n"; }
        }

        /// <summary>0=totalLeaders, 1=patCount, 2=textErrors</summary>
        public static string PatCheck_ScanStats
        {
            get { return En ? "(Scanned {0} Leaders, matched {1} PAT_STYLE, {2} text errors)\n"
                            : "(扫描 {0} 条 Leader，匹配 {1} 条 PAT_STYLE，{2} 个文字错误)\n"; }
        }

        /// <summary>0=count</summary>
        public static string PatCheck_SectionDrawingOnly
        {
            get { return En ? "\n--- In drawing, missing from dict ({0}) ---\n" : "\n--- 图纸有，字典缺失 ({0}) ---\n"; }
        }

        /// <summary>0=count</summary>
        public static string PatCheck_SectionDictOnly
        {
            get { return En ? "\n--- In dict, missing from drawing ({0}) ---\n" : "\n--- 字典有，图纸缺失 ({0}) ---\n"; }
        }

        /// <summary>0=count</summary>
        public static string PatCheck_SectionDuplicates
        {
            get { return En ? "\n--- Duplicates in drawing ({0}) ---\n" : "\n--- 图纸中重复 ({0}) ---\n"; }
        }

        /// <summary>0=number, 1=count</summary>
        public static string PatCheck_DuplicateDetail
        {
            get { return En ? "  #{0} appears {1} times:\n" : "  #{0} 出现 {1} 次:\n"; }
        }

        public static string PatCheck_AllMatch
        {
            get { return En ? "\n*** All match — drawing and dictionary are consistent ***\n"
                            : "\n*** 全部一致 — 图纸与字典匹配 ***\n"; }
        }

        /// <summary>0=totalIssues</summary>
        public static string PatCheck_TotalIssues
        {
            get { return En ? "\nTotal issues: {0}\n" : "\n总问题数: {0}\n"; }
        }

        public static string PatCheck_SavePrompt
        {
            get { return En ? "\nSave report to file?" : "\n保存报告到文件?"; }
        }

        /// <summary>关键字 | Keyword: 是 / Yes</summary>
        public static string PatCheck_KwYes
        {
            get { return En ? "Yes" : "是"; }
        }

        /// <summary>关键字 | Keyword: 否 / No</summary>
        public static string PatCheck_KwNo
        {
            get { return En ? "No" : "否"; }
        }

        /// <summary>0=reportPath</summary>
        public static string PatCheck_ReportSaved
        {
            get { return En ? "\nReport saved to: {0}\n" : "\n报告已保存到: {0}\n"; }
        }

        // ================================================================
        // PatSelectAllCommand
        // ================================================================

        public static string PatSelectAll_None
        {
            get { return En ? "\nPatentMarker: no PAT leaders found.\n" : "\nPatentMarker: 未找到 PAT 引线。\n"; }
        }

        /// <summary>0=count</summary>
        public static string PatSelectAll_Result
        {
            get { return En ? "\nPatentMarker: selected {0} PAT Leader/MText entities. Press Ctrl+1 to modify properties.\n"
                            : "\nPatentMarker: 已选中 {0} 个 PAT Leader/MText 实体。按 Ctrl+1 修改属性。\n"; }
        }

        // ================================================================
        // DictPaletteControl — 面板 UI
        // ================================================================

        public static string Palette_Title
        {
            get { return En ? "PatentCAD-Annotator Dictionary" : "PatentMarker 字典"; }
        }

        public static string Palette_DictNotLoaded
        {
            get { return En ? "Dictionary not loaded" : "未加载字典"; }
        }

        public static string Palette_Search
        {
            get { return En ? "Search" : "搜索"; }
        }

        public static string Palette_TextHeight
        {
            get { return En ? "Height:" : "字高:"; }
        }

        public static string Palette_Reset
        {
            get { return En ? "Reset" : "重置"; }
        }

        public static string Palette_ArrowSize
        {
            get { return En ? "Size:" : "大小:"; }
        }

        public static string Palette_Reload
        {
            get { return En ? "Reload" : "重载"; }
        }

        public static string Palette_Open
        {
            get { return En ? "Open" : "打开"; }
        }

        public static string Palette_Conflicts
        {
            get { return En ? "Conflicts" : "冲突"; }
        }

        public static string Palette_DeleteLeader
        {
            get { return En ? "Delete Leaders" : "删除引线"; }
        }

        public static string Palette_SelectAll
        {
            get { return En ? "Select All" : "全选"; }
        }

        public static string Palette_Compare
        {
            get { return En ? "Compare" : "对照"; }
        }

        // v4.0：粘贴识别入口 | Paste-recognize entry
        public static string Palette_PasteRecognize
        {
            get { return En ? "Paste" : "粘贴识别"; }
        }

        // v4.0：新增条目入口 | Add-entry entry
        public static string Palette_AddEntry
        {
            get { return En ? "Add" : "新增"; }
        }

        // ================================================================
        // PasteRecognizeDialog — v4.0 粘贴识别对话框
        // ================================================================

        public static string Paste_Title
        {
            get { return En ? "Paste & Recognize" : "粘贴识别"; }
        }

        public static string Paste_InputHint
        {
            get { return En ? "Paste the marking description text below:" : "请粘贴附图标记说明文本（可从 Word 说明书复制）:"; }
        }

        public static string Paste_BtnRecognize
        {
            get { return En ? "Recognize" : "识别"; }
        }

        public static string Paste_NoInput
        {
            get { return En ? "Input is empty." : "输入为空。"; }
        }

        /// <summary>0=count | 识别到 N 条</summary>
        public static string Paste_ResultInfo
        {
            get { return En ? "Recognized {0} item(s)." : "识别到 {0} 条。"; }
        }

        /// <summary>0=section length | 已定位段落，长度 N 字符</summary>
        public static string Paste_SectionFound
        {
            get { return En ? "Section located ({0} chars)." : "已定位「附图标记说明」段落（{0} 字符）。"; }
        }

        public static string Paste_SectionFallback
        {
            get { return En ? "Section header not found — full-text scan." : "未找到标记段落头 — 已回退全文扫描。"; }
        }

        /// <summary>0=error message</summary>
        public static string Paste_RecognizeFailed
        {
            get { return En ? "Recognition failed: {0}" : "识别失败: {0}"; }
        }

        public static string Paste_NoRows
        {
            get { return En ? "No valid rows to write (number and name required)." : "没有可写回的有效行（编号和名称不能为空）。"; }
        }

        public static string Paste_NoDict
        {
            get { return En ? "Dictionary not loaded. Click Reload first." : "未加载字典，请先点击「重载」。"; }
        }

        public static string Paste_GridNumber
        {
            get { return En ? "Number" : "编号"; }
        }

        public static string Paste_GridName
        {
            get { return En ? "Name" : "名称"; }
        }

        public static string Paste_BtnConfirm
        {
            get { return En ? "Write Back" : "确认写回"; }
        }

        public static string Paste_BtnCancel
        {
            get { return En ? "Cancel" : "取消"; }
        }

        public static string Paste_ConfirmTitle
        {
            get { return En ? "Write Mode" : "写回方式"; }
        }

        public static string Paste_ConfirmMsg
        {
            get { return En ? "Choose write mode:\n\nYes = Overwrite the whole dictionary\nNo = Merge (update existing entries by number, append new numbers)\nCancel = do not write"
                            : "选择写回方式:\n\n是(Y) = 覆盖当前整个字典\n否(N) = 合并（按编号更新已有条目，新编号追加到末尾）\n取消 = 不写回"; }
        }

        /// <summary>0=count, 1=path | 已写回 N 条到 path</summary>
        public static string Paste_WriteOk
        {
            get { return En ? "Written {0} entries to:\n{1}" : "已写回 {0} 条到:\n{1}"; }
        }

        /// <summary>0=error message</summary>
        public static string Paste_WriteFail
        {
            get { return En ? "Write failed: {0}" : "写回失败: {0}"; }
        }

        // ================================================================
        // EditEntryDialog — v4.0 单条目编辑对话框
        // ================================================================

        public static string Edit_TitleEdit
        {
            get { return En ? "Edit Entry" : "编辑条目"; }
        }

        public static string Edit_TitleAdd
        {
            get { return En ? "Add Entry" : "新增条目"; }
        }

        public static string Edit_Number
        {
            get { return En ? "Number:" : "编号:"; }
        }

        public static string Edit_Name
        {
            get { return En ? "Name:" : "名称:"; }
        }

        public static string Edit_BtnSave
        {
            get { return En ? "Save" : "保存"; }
        }

        public static string Edit_BtnDelete
        {
            get { return En ? "Delete" : "删除"; }
        }

        public static string Edit_EmptyField
        {
            get { return En ? "Number and name are required." : "编号和名称不能为空。"; }
        }

        /// <summary>0=number | 编号重复</summary>
        public static string Edit_NumberConflict
        {
            get { return En ? "Number already exists: {0}" : "编号已存在: {0}"; }
        }

        public static string Edit_DeleteConfirm
        {
            get { return En ? "Delete this entry from the dictionary?\n(Leaders in the drawing are NOT deleted.)"
                            : "确定从字典删除该条目？\n（图纸中的引线不会被删除。）"; }
        }

        public static string Edit_DeleteFailed
        {
            get { return En ? "Delete failed: entry not found." : "删除失败: 未找到该条目。"; }
        }

        // ================================================================
        // ArbitrateDialog — v4.0 双端冲突裁决
        // ================================================================

        // 面板裁决入口 | Palette arbitrate entry
        public static string Palette_Arbitrate
        {
            get { return En ? "Arbitrate" : "裁决"; }
        }

        /// <summary>状态栏：检测到 Word 已覆盖 CAD 修改</summary>
        public static string Conflict_StatusDetected
        {
            get { return En ? "Word has overwritten CAD edits — click Arbitrate" : "检测到 Word 已覆盖 CAD 修改 — 点击「裁决」处理"; }
        }

        public static string Conflict_Title
        {
            get { return En ? "Resolve Conflict" : "冲突裁决"; }
        }

        /// <summary>0=backup file name | 冲突说明（含备份文件名）</summary>
        public static string Conflict_Msg
        {
            get { return En ? "Word has overwritten CAD edits. The CAD version was backed up to:\n{0}\n\nChoose which version to keep:"
                            : "Word 端已覆盖 CAD 的修改。CAD 原版已备份为:\n{0}\n\n请选择保留哪一版:"; }
        }

        public static string Conflict_BtnKeepWord
        {
            get { return En ? "Keep Word version" : "采用 Word 版"; }
        }

        public static string Conflict_BtnRestoreCad
        {
            get { return En ? "Restore CAD version" : "恢复 CAD 版"; }
        }

        public static string Conflict_BtnLater
        {
            get { return En ? "Later" : "稍后再说"; }
        }

        /// <summary>状态栏：采用 Word 版结果</summary>
        public static string Conflict_KeepWordOk
        {
            get { return En ? "Word version kept (backup cleaned)." : "已采用 Word 版（备份已清理）。"; }
        }

        /// <summary>状态栏：恢复 CAD 版结果</summary>
        public static string Conflict_RestoreOk
        {
            get { return En ? "CAD version restored (mark cleared)." : "已恢复 CAD 版（CAD 标记已清除）。"; }
        }

        /// <summary>0=error message | 裁决失败</summary>
        public static string Conflict_Failed
        {
            get { return En ? "Arbitration failed: {0}" : "裁决失败: {0}"; }
        }

        // v4.0：改号同步图纸 | Renumber sync to drawing
        /// <summary>0=changed, 1=oldNumber, 2=newNumber | 已同步 N 条图纸标注: old → new</summary>
        public static string Status_NumberSynced
        {
            get { return En ? "Synced {0} leader(s) to drawing: {1} → {2}" : "已同步 {0} 条图纸标注: {1} → {2}"; }
        }

        /// <summary>0=changed, 1=oldNumber, 2=newNumber | 命令行同步结果</summary>
        public static string Status_NumberSyncedCmd
        {
            get { return En ? "\nSynced {0} PAT leader(s) text: {1} → {2}\n" : "\n已同步 {0} 条 PAT 引线文字: {1} → {2}\n"; }
        }

        /// <summary>0=error message</summary>
        public static string Status_NumberSyncFailed
        {
            get { return En ? "Drawing sync failed: {0}" : "图纸同步失败: {0}"; }
        }

        // 语言切换按钮 | Language toggle button
        public static string Palette_Language
        {
            get { return En ? "中文" : "EN"; }
        }

        // 动态按钮文本 | Dynamic button text
        /// <summary>0=on/off 状态文本</summary>
        public static string Palette_ArrowOnOff
        {
            get { return En ? "Arrow:{0}" : "箭头:{0}"; }
        }

        public static string Palette_On
        {
            get { return En ? "On" : "开"; }
        }

        public static string Palette_Off
        {
            get { return En ? "Off" : "关"; }
        }

        public static string Palette_LineTypeSpline
        {
            get { return En ? "Line:Spline" : "线型:样条"; }
        }

        public static string Palette_LineTypeStraight
        {
            get { return En ? "Line:Straight" : "线型:直线"; }
        }

        // v3.1：点数模式按钮文本 | Point-count mode button text
        public static string Palette_PointsUnlimited
        {
            get { return En ? "Points:Unlimited" : "点数:无限"; }
        }

        public static string Palette_PointsThree
        {
            get { return En ? "Points:Three" : "点数:三点"; }
        }

        // ListView 列头 | Column headers
        public static string Palette_Brace
        {
            get { return En ? "Brace" : "大括号"; }
        }

        public static string Col_Number
        {
            get { return En ? "Number" : "编号"; }
        }

        public static string Col_Name
        {
            get { return En ? "Name" : "名称"; }
        }

        public static string Col_Occ
        {
            get { return En ? "Count" : "次数"; }
        }

        public static string Col_OldNumber
        {
            get { return En ? "Old#" : "旧编号"; }
        }

        public static string Col_OldName
        {
            get { return En ? "Old Name" : "旧名称"; }
        }

        // 状态栏 | Status bar
        public static string Status_DictAutoUpdated
        {
            get { return En ? "Dictionary auto-updated." : "字典已自动更新。"; }
        }

        public static string Status_Ready
        {
            get { return En ? "Ready." : "就绪。"; }
        }

        /// <summary>0=summary | 字典已更新 — summary</summary>
        public static string Status_DictUpdated
        {
            get { return En ? "Dictionary updated — {0}" : "字典已更新 — {0}"; }
        }

        public static string Status_DictLoaded
        {
            get { return En ? "Dictionary loaded." : "字典已加载。"; }
        }

        public static string Status_PlaceDictHint
        {
            get { return En ? "Place <dwgname>.dict.json in the DWG directory." : "请将 <dwg名>.dict.json 放在 DWG 同目录。"; }
        }

        /// <summary>0=number, 1=name | 已选: [num] name</summary>
        public static string Status_Selected
        {
            get { return En ? "Selected: [{0}] {1}" : "已选: [{0}] {1}"; }
        }

        /// <summary>0=on/off 状态文本 | 箭头: 开/关（影响后续新建引线）</summary>
        public static string Status_ArrowToggled
        {
            get { return En ? "Arrow: {0} (affects new leaders)" : "箭头: {0}（影响后续新建引线）"; }
        }

        /// <summary>0=线型描述 | 线型: 样条曲线/直线段（影响后续新建引线）</summary>
        public static string Status_SplineToggled
        {
            get { return En ? "Line type: {0} (affects new leaders)" : "线型: {0}（影响后续新建引线）"; }
        }

        public static string Status_SplineDesc
        {
            get { return En ? "Spline" : "样条曲线"; }
        }

        public static string Status_StraightDesc
        {
            get { return En ? "Straight" : "直线段"; }
        }

        /// <summary>0=点数模式描述 | v3.1：点数: 无限/三点（影响后续新建引线）</summary>
        public static string Status_PointsToggled
        {
            get { return En ? "Point mode: {0} (affects new leaders)" : "点数模式: {0}（影响后续新建引线）"; }
        }

        public static string Status_PointsUnlimitedDesc
        {
            get { return En ? "Unlimited" : "无限点"; }
        }

        public static string Status_PointsThreeDesc
        {
            get { return En ? "Three-point" : "三点"; }
        }

        public static string Status_CompareShown
        {
            get { return En ? "Old version comparison columns shown." : "已显示旧版对照列。"; }
        }

        public static string Status_CompareHidden
        {
            get { return En ? "Old version comparison columns hidden." : "已隐藏旧版对照列。"; }
        }

        /// <summary>0=number, 1=name | 已装填: [num] — 请在图纸中点击</summary>
        public static string Status_Loaded
        {
            get { return En ? "Loaded: [{0}] — click in drawing" : "已装填: [{0}] — 请在图纸中点击"; }
        }

        public static string Status_Reloaded
        {
            get { return En ? "Reloaded." : "已重载。"; }
        }

        /// <summary>0=error message</summary>
        public static string Status_LoadFailed
        {
            get { return En ? "Load failed: {0}" : "加载失败: {0}"; }
        }

        public static string Status_NoDictFile
        {
            get { return En ? "No .dict.json found" : "未找到 .dict.json"; }
        }

        /// <summary>0=error message</summary>
        public static string Status_OpenFailed
        {
            get { return En ? "Open failed: {0}" : "打开失败: {0}"; }
        }

        /// <summary>0=deleted, 1=skipped | 已删除 N 条引线（跳过 M）。</summary>
        public static string Status_Deleted
        {
            get { return En ? "Deleted {0} leaders (skipped {1})." : "已删除 {0} 条引线（跳过 {1}）。"; }
        }

        /// <summary>0=deleted, 1=skipped | 命令行删除结果</summary>
        public static string Status_DeletedCmd
        {
            get { return En ? "\nDeleted {0} PAT_STYLE leaders (skipped {1}).\n" : "\n已删除 {0} 条 PAT_STYLE 引线（跳过 {1}）。\n"; }
        }

        /// <summary>0=number, 1=name | 命令行装填切换</summary>
        public static string Status_LoadedCmd
        {
            get { return En ? "\nSwitched to #{0} {1}\n" : "\n已切换到 #{0} {1}\n"; }
        }

        // 字典信息标签 | Dict info label
        /// <summary>0=entryCount, 1=warnCount, 2=conflictCount</summary>
        public static string Palette_DictInfo
        {
            get { return En ? "{0} entries | {1} warnings | {2} conflicts" : "{0} 条 | {1} 警告 | {2} 冲突"; }
        }

        // 消息框 | MessageBox
        public static string Msg_NoConflicts
        {
            get { return En ? "No conflicts." : "无冲突。"; }
        }

        public static string Msg_ConflictTitle
        {
            get { return En ? "Conflict Check" : "冲突检查"; }
        }

        public static string Msg_WarningsSection
        {
            get { return En ? "=== Warnings ===\r\n" : "=== 警告 ===\r\n"; }
        }

        public static string Msg_ConflictsSection
        {
            get { return En ? "\r\n=== Conflicts ===\r\n" : "\r\n=== 冲突 ===\r\n"; }
        }

        public static string Msg_DeleteConfirm
        {
            get { return En ? "Delete all PAT_STYLE leaders?\nThis cannot be undone (type UNDO in command line to recover)."
                            : "确定删除所有 PAT_STYLE 引线？\n此操作不可撤销（请在命令行输入 UNDO 恢复）。"; }
        }

        public static string Msg_DeleteTitle
        {
            get { return En ? "Confirm Delete" : "确认删除"; }
        }

        // ================================================================
        // PatPaletteCommand
        // ================================================================

        public static string Palette_TabTitle
        {
            get { return En ? "Dictionary" : "字典"; }
        }

        // ================================================================
        // PatStyleInitializer
        // ================================================================

        /// <summary>0=error message</summary>
        public static string StyleInit_Warning
        {
            get { return En ? "\nPatentMarker: warning - could not fully configure PAT_STYLE: {0}\n"
                            : "\nPatentMarker: 警告 - 无法完全配置 PAT_STYLE: {0}\n"; }
        }

        // ================================================================
        // DictDiff — 摘要字符串
        // ================================================================

        /// <summary>0=added, 1=removed, 2=numChg, 3=nameChg, 4=bothChg</summary>
        public static string Diff_Summary
        {
            get { return En ? "Added {0}, Removed {1}, Number changed {2}, Name changed {3}, Unmatched {4}"
                            : "新增 {0}，删除 {1}，编号变 {2}，名称变 {3}，无法匹配 {4}"; }
        }
    }
}

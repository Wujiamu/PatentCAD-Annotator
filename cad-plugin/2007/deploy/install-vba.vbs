' PatentMarker VBA 模块安装脚本 (VBScript)
' 编码: GBK (Win7 中文系统兼容)
'
' 功能:
'   - 打开 Word 的 Normal.dotm 全局模板
'   - 导入 6 个 VBA 模块到 Normal 工程
'   - 所有 Word 文档都能使用这些宏
'   - 详细日志输出到 install-vba.log
'   - 所有消息累积, 最后一次性显示
'
' 前提:
'   Word 2010 需启用 "信任对 VBA 工程对象模型的访问":
'     文件 > 选项 > 信任中心 > 信任中心设置 >
'     宏设置 > 勾选 "信任对 VBA 工程对象模型的访问"

Option Explicit

Const ForAppending = 8
Const CreateFlag = True
Const wdFormatTemplate = 1

Dim fso, shell
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")

Dim scriptDir, logPath, logFile
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
logPath = scriptDir & "\install-vba.log"
Set logFile = fso.OpenTextFile(logPath, ForAppending, CreateFlag)

Dim output
output = ""

Sub LogMsg(msg)
    Dim ts
    ts = Now
    logFile.WriteLine "[" & ts & "] " & msg
    output = output & msg & vbCrLf
End Sub

Sub QuitWithMsg(msg)
    LogMsg msg
    WScript.Echo output
    logFile.Close
    WScript.Quit(1)
End Sub

output = output & "========================================" & vbCrLf
output = output & "PatentMarker VBA 模块安装程序" & vbCrLf
output = output & "(安装到 Normal 全局模板)" & vbCrLf
output = output & "========================================" & vbCrLf

' --- 0. 系统信息 ---
LogMsg "--- 系统信息 ---"
LogMsg "  用户名: " & shell.ExpandEnvironmentStrings("%USERNAME%")
LogMsg "  脚本目录: " & scriptDir

' --- 1. 定位 VBA 文件 ---
Dim vbaDir
vbaDir = scriptDir & "\vba"
LogMsg "VBA 文件夹: " & vbaDir

Dim vbaFiles(5)
vbaFiles(0) = "Patterns.bas"
vbaFiles(1) = "DictModel.bas"
vbaFiles(2) = "JsonWriter.bas"
vbaFiles(3) = "PatentExtractor.bas"
vbaFiles(4) = "AutoExport.bas"
vbaFiles(5) = "clsSaveHook.cls"

Dim i, filePath
For i = 0 To UBound(vbaFiles)
    filePath = vbaDir & "\" & vbaFiles(i)
    If Not fso.FileExists(filePath) Then
        QuitWithMsg "错误: 未找到 VBA 文件: " & filePath & vbCrLf & "请确保 \vba\ 子文件夹包含全部 6 个模块"
    End If
Next

LogMsg "VBA 文件检查: 全部存在"

' --- 2. 启动 Word ---
Dim wordApp
On Error Resume Next
Set wordApp = CreateObject("Word.Application")
If Err.Number <> 0 Then
    QuitWithMsg "错误: 无法启动 Word" & vbCrLf & "原因: " & Err.Description
End If
On Error GoTo 0

wordApp.Visible = False
wordApp.DisplayAlerts = False

LogMsg "Word 已启动"

' --- 3. 获取 Normal.dotm 路径 ---
Dim normalPath
On Error Resume Next
normalPath = wordApp.NormalTemplate.FullName
If Err.Number <> 0 Or IsNull(normalPath) Or normalPath = "" Then
    Dim normalErr
    normalErr = Err.Description
    On Error GoTo 0
    wordApp.Quit
    QuitWithMsg "错误: 无法获取 Normal 模板路径" & vbCrLf & "原因: " & normalErr
End If
On Error GoTo 0

LogMsg "Normal 模板路径: " & normalPath

If Not fso.FileExists(normalPath) Then
    wordApp.Quit
    QuitWithMsg "错误: Normal 模板文件不存在: " & normalPath
End If

' --- 4. 打开 Normal.dotm ---
Dim doc
On Error Resume Next
Set doc = wordApp.Documents.Open(normalPath, False, False, False)
If Err.Number <> 0 Then
    Dim openErr
    openErr = Err.Description
    On Error GoTo 0
    wordApp.Quit
    QuitWithMsg "错误: 无法打开 Normal 模板" & vbCrLf & "原因: " & openErr
End If
On Error GoTo 0

LogMsg "Normal 模板已打开"
LogMsg "  只读: " & doc.ReadOnly
LogMsg "  保护: " & doc.ProtectionType

If doc.ReadOnly Then
    doc.Close False
    wordApp.Quit
    QuitWithMsg "错误: Normal 模板是只读状态" & vbCrLf & _
                "可能原因:" & vbCrLf & _
                "  - Word 正在运行 (Normal.dotm 被占用)" & vbCrLf & _
                "  - 文件标记为只读" & vbCrLf & _
                "解决方法:" & vbCrLf & _
                "  - 关闭所有 Word 窗口后重试" & vbCrLf & _
                "  - 右键文件 > 属性 > 取消只读"
End If

' --- 5. 检查 VBA 工程访问 ---
Dim vbProj
On Error Resume Next
Set vbProj = doc.VBProject
If Err.Number <> 0 Then
    Dim vbaErr
    vbaErr = Err.Description
    On Error GoTo 0
    doc.Close False
    wordApp.Quit
    QuitWithMsg "错误: 无法访问 VBA 工程" & vbCrLf & _
                "原因: " & vbaErr & vbCrLf & vbCrLf & _
                "请在 Word 中启用设置:" & vbCrLf & _
                "  1. 文件 > 选项 > 信任中心" & vbCrLf & _
                "  2. 信任中心设置 > 宏设置" & vbCrLf & _
                "  3. 勾选: 信任对 VBA 工程对象模型的访问" & vbCrLf & _
                "  4. 宏安全级别设为: 禁用所有宏并发出通知" & vbCrLf & _
                "  5. 确定后关闭 Word, 重新运行此脚本"
End If
On Error GoTo 0

LogMsg "VBA 工程访问: 正常"
LogMsg "VBA 工程名: " & vbProj.Name

' --- 6. 删除旧模块 ---
Dim moduleNames(5)
moduleNames(0) = "Patterns"
moduleNames(1) = "DictModel"
moduleNames(2) = "JsonWriter"
moduleNames(3) = "PatentExtractor"
moduleNames(4) = "AutoExport"
moduleNames(5) = "clsSaveHook"

LogMsg "删除旧模块 (如有)..."
For i = 0 To UBound(moduleNames)
    On Error Resume Next
    vbProj.VBComponents.Remove vbProj.VBComponents.Item(moduleNames(i))
    If Err.Number = 0 Then
        LogMsg "  已删除: " & moduleNames(i)
    Else
        LogMsg "  (不存在): " & moduleNames(i)
    End If
    On Error GoTo 0
Next

' --- 7. 导入 VBA 模块 ---
LogMsg "导入 VBA 模块..."
Dim imported
imported = 0

For i = 0 To UBound(vbaFiles)
    filePath = vbaDir & "\" & vbaFiles(i)
    On Error Resume Next
    vbProj.VBComponents.Import filePath
    If Err.Number <> 0 Then
        Dim importErr
        importErr = Err.Description
        On Error GoTo 0
        doc.Close False
        wordApp.Quit
        QuitWithMsg "错误: 导入失败: " & vbaFiles(i) & vbCrLf & "原因: " & importErr
    Else
        LogMsg "  成功: " & vbaFiles(i)
        imported = imported + 1
    End If
    On Error GoTo 0
Next

LogMsg "已导入 " & imported & " / 6 个模块"

' --- 8. 保存 Normal.dotm ---
LogMsg "保存 Normal 模板..."
Dim saveOk, saveErr
saveOk = False
saveErr = ""

' 策略1: doc.Save
On Error Resume Next
doc.Save
If Err.Number = 0 Then
    saveOk = True
    LogMsg "  保存成功"
Else
    saveErr = Err.Description & " (0x" & Hex(Err.Number) & ")"
    LogMsg "  保存失败: " & saveErr
End If
On Error GoTo 0

' 策略2: SaveAs (保留原格式)
If Not saveOk Then
    On Error Resume Next
    doc.SaveAs normalPath, wdFormatTemplate
    If Err.Number = 0 Then
        saveOk = True
        LogMsg "  SaveAs 成功"
    Else
        saveErr = Err.Description & " (0x" & Hex(Err.Number) & ")"
        LogMsg "  SaveAs 失败: " & saveErr
    End If
    On Error GoTo 0
End If

If Not saveOk Then
    LogMsg ""
    LogMsg "错误: 无法保存 Normal 模板"
    LogMsg "最后错误: " & saveErr
    LogMsg ""
    LogMsg "模块已导入但未保存。请手动保存:"
    LogMsg "  1. 在弹出的 Word 窗口中按 Ctrl+S"
    LogMsg "  2. 或 文件 > 保存"
    wordApp.Visible = True
    wordApp.DisplayAlerts = True
    WScript.Echo output
    logFile.Close
    WScript.Quit(1)
End If

' --- 9. 关闭 ---
doc.Close False
wordApp.Quit

' --- 10. 总结 ---
LogMsg ""
LogMsg "=== VBA 安装完成 ==="
LogMsg "Normal 模板: " & normalPath
LogMsg "已导入模块: " & imported & " / 6"
LogMsg "已保存: 是"
LogMsg ""
LogMsg "模块已安装到全局模板, 所有 Word 文档均可使用"
LogMsg ""
LogMsg "后续步骤:"
LogMsg "  1. 打开任意 Word 文档"
LogMsg "  2. 按 Alt+F11, 在左侧 'Normal' 下查看模块"
LogMsg "  3. 运行 EnableAutoExport 启用自动导出"
LogMsg "     (或手动运行 ExtractDict)"
LogMsg ""
LogMsg "dict.json 将保存到 Word 文档同目录"
LogMsg "CAD 插件会自动检测 (DWG 需在同一文件夹)"

LogMsg ""
LogMsg "========================================"
LogMsg "安装结束"
LogMsg "========================================"

WScript.Echo output
logFile.Close

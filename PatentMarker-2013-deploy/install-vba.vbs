' PatentMarker VBA Module Installer (VBScript)
'
' Function:
'   - Import 5 standard modules and a class; build the UserForm in Word Normal.dotm
'   - Available in all Word documents after install
'   - Detailed log: install-vba.log
'
' Known issue (file locking):
'   Normal.dotm is a shared template. If Word is already running when this
'   script creates a second hidden Word instance via COM, the first instance
'   holds a file lock on Normal.dotm. The second instance opens it read-only,
'   and any save attempt produces an unresponsive dialog or "file in use"
'   error. The fix is to ensure NO Word processes are running before install.
'
' Prerequisite:
'   Word 2010: Enable "Trust access to the VBA project object model"
'     File > Options > Trust Center > Trust Center Settings >
'     Macro Settings > Check "Trust access to the VBA project object model"

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

' === Internationalization (i18n) ===
Function GetSysLang()
    On Error Resume Next
    Dim r
    Set r = CreateObject("WScript.Shell")
    Dim lid
    lid = r.RegRead("HKLM\SYSTEM\CurrentControlSet\Control\Nls\Language\InstallLanguage")
    If Err.Number <> 0 Then
        lid = r.RegRead("HKLM\SYSTEM\CurrentControlSet\Control\Nls\Language\Default")
    End If
    If Err.Number <> 0 Then lid = "0804"
    On Error GoTo 0
    Select Case lid
        Case "0804", "0404", "0C04", "1404", "7C04"
            GetSysLang = "zh"
        Case Else
            GetSysLang = "en"
    End Select
End Function

Function L(t)
    Dim z
    z = (GetSysLang() = "zh")
    If Not z Then
        L = t
        Exit Function
    End If
    t = Replace(t, "PatentMarker VBA Module Installer" & vbCrLf & _
                           "(Install to Normal global template)", _
                           "PatentMarker VBA 模块安装程序" & vbCrLf & _
                           "（安装到 Normal 全局模板）")
    t = Replace(t, "Please close ALL Word windows, then click OK." & vbCrLf & _
                           "(Click Cancel to abort installation)", _
                           "请关闭所有 Word 窗口，然后点击确定。" & vbCrLf & _
                           "（点击取消中止安装）")
    t = Replace(t, " Word process(es) running.", " 个 Word 进程正在运行。")
    t = Replace(t, " Word process(es) running", " 个 Word 进程正在运行")
    t = Replace(t, "Found ", "检测到 ")
    t = Replace(t, "PatentMarker VBA Install", "PatentMarker VBA 安装")
    t = Replace(t, "ERROR: VBA file not found: ", "错误：找不到 VBA 文件：")
    t = Replace(t, "Ensure \vba\ contains all .bas/.frm modules", "请确保 \vba\ 目录包含所有 .bas/.frm 模块")
    t = Replace(t, "Aborted by user.", "用户取消安装。")
    t = Replace(t, "ERROR: Word is still running." & vbCrLf & "Please close all Word instances and retry.", _
                           "错误：Word 仍在运行。" & vbCrLf & "请关闭所有 Word 实例后重试。")
    t = Replace(t, "ERROR: Cannot create Word" & vbCrLf & "Reason: ", _
                           "错误：无法启动 Word" & vbCrLf & "原因：")
    t = Replace(t, "ERROR: Cannot get Normal template path" & vbCrLf & "Reason: ", _
                           "错误：无法获取 Normal 模板路径" & vbCrLf & "原因：")
    t = Replace(t, "ERROR: Normal template file not found: ", _
                           "错误：找不到 Normal 模板文件：")
    t = Replace(t, "ERROR: Cannot remove read-only attribute" & vbCrLf & _
                    "Path: ", _
                    "错误：无法移除只读属性" & vbCrLf & "路径：")
    t = Replace(t, "Fix: Right-click file > Properties > uncheck Read-only", _
                           "修复：右键文件 > 属性 > 取消只读")
    t = Replace(t, "ERROR: Normal template is read-only" & vbCrLf & _
                "Possible causes:" & vbCrLf & _
                "  - Another Word instance is running (Normal.dotm locked)" & vbCrLf & _
                "  - File attribute is read-only" & vbCrLf & _
                "Fix:" & vbCrLf & _
                "  - Close all Word windows and retry" & vbCrLf & _
                "  - Right-click file > Properties > uncheck Read-only", _
                "错误：Normal 模板为只读" & vbCrLf & _
                "可能原因：" & vbCrLf & _
                "  - 其他 Word 实例正在运行（Normal.dotm 被锁定）" & vbCrLf & _
                "  - 文件属性为只读" & vbCrLf & _
                "修复：" & vbCrLf & _
                "  - 关闭所有 Word 窗口后重试" & vbCrLf & _
                "  - 右键文件 > 属性 > 取消只读")
    t = Replace(t, "ERROR: Cannot open Normal template" & vbCrLf & "Reason: ", _
                           "错误：无法打开 Normal 模板" & vbCrLf & "原因：")
    t = Replace(t, "ERROR: Cannot create class module" & vbCrLf & "Reason: ", _
                           "错误：无法创建类模块" & vbCrLf & "原因：")
    t = Replace(t, "ERROR: Cannot access VBA project" & vbCrLf & _
                "Reason: " & vbCrLf & vbCrLf & _
                "Enable in Word:" & vbCrLf & _
                "  1. File > Options > Trust Center" & vbCrLf & _
                "  2. Trust Center Settings > Macro Settings" & vbCrLf & _
                "  3. Check: Trust access to the VBA project object model" & vbCrLf & _
                "  4. Set macro security to: Disable all macros with notification" & vbCrLf & _
                "  5. Close Word, then re-run this script", _
                "错误：无法访问 VBA 项目" & vbCrLf & _
                "原因：" & vbCrLf & vbCrLf & _
                "请在 Word 中启用：" & vbCrLf & _
                "  1. 文件 > 选项 > 信任中心" & vbCrLf & _
                "  2. 信任中心设置 > 宏设置" & vbCrLf & _
                "  3. 勾选：信任对 VBA 工程对象模型的访问" & vbCrLf & _
                "  4. 宏安全性：禁用所有宏，并发出通知" & vbCrLf & _
                "  5. 关闭 Word，重新运行此脚本")
    t = Replace(t, "ERROR: Import failed: ", "错误：导入失败：")
    t = Replace(t, "ERROR: Cannot save Normal template." & vbCrLf & _
                "Reason: " & vbCrLf & _
                "Please close all Word instances and retry.", _
                "错误：无法保存 Normal 模板。" & vbCrLf & _
                "原因：" & vbCrLf & _
                "请关闭所有 Word 实例后重试。")
    t = Replace(t, "=== VBA Install Complete ===", "=== VBA 安装完成 ===")
    t = Replace(t, "Modules imported: ", "已导入模块：")
    t = Replace(t, "Saved: Yes (verified)", "保存：是（已验证）")
    t = Replace(t, "Saved: Reported OK but file time unchanged - please verify manually", _
                           "保存：报告成功但文件时间未变 - 请手动验证")
    t = Replace(t, "Modules installed to global template, available in all Word documents", _
                           "模块已安装到全局模板，可在所有 Word 文档中使用")
    t = Replace(t, "Verification:", "验证方法：")
    t = Replace(t, "  1. Open a new Word document", "  1. 打开新的 Word 文档")
    t = Replace(t, "  2. In Word: Alt+F8 -> ShowPatentDictPanel (one macro)", _
                           "  2. 在 Word 中：Alt+F8 -> ShowPatentDictPanel（单一宏）")
    t = Replace(t, "  3. In the panel: click Manual Export or toggle Auto-Export on Save", _
                           "  3. 在面板中：点击“手动导出”按钮或切换“保存时自动导出”开关")
    t = Replace(t, "dict.json will be saved to Word document directory", _
                           "dict.json 将保存到 Word 文档所在目录")
    t = Replace(t, "CAD auto-export (DWG in same folder)", "CAD 自动导出（DWG 同目录）")
    t = Replace(t, "Installation complete", "安装完成")
    L = t
End Function
' === End i18n ===

Sub LogMsg(msg)
    Dim ts
    ts = Now
    logFile.WriteLine "[" & ts & "] " & msg
    output = output & msg & vbCrLf
End Sub

Sub CloseInstallWord()
    On Error Resume Next
    Dim n, closeDoc
    If IsObject(wordApp) Then
        wordApp.Run "AutoExport.ReleaseAutoExportForShutdown"
        For n = wordApp.Documents.Count To 1 Step -1
            Set closeDoc = wordApp.Documents.Item(n)
            closeDoc.Close False
            Set closeDoc = Nothing
        Next
        wordApp.Quit False
    End If
    Set closeDoc = Nothing
    Set panelComp = Nothing
    Set designer = Nothing
    Set clsComp = Nothing
    Set importedComp = Nothing
    Set wordProcs = Nothing
    Set wmiSvc = Nothing
    Set vbProj = Nothing
    Set doc = Nothing
    Set wordApp = Nothing
    On Error GoTo 0
    WaitForWordProcessesToExit
End Sub
Sub WaitForWordProcessesToExit()
    On Error Resume Next
    Dim attempt, svc, processes, errNo
    For attempt = 1 To 20
        Set svc = Nothing
        Set processes = Nothing
        Err.Clear
        Set svc = GetObject("winmgmts:\\.\root\cimv2")
        If Err.Number = 0 Then Set processes = svc.ExecQuery("SELECT ProcessId FROM Win32_Process WHERE Name='WINWORD.EXE'")
        errNo = Err.Number
        On Error GoTo 0
        If errNo <> 0 Or processes Is Nothing Then Exit Sub
        If processes.Count = 0 Then Exit Sub
        WScript.Sleep 500
        On Error Resume Next
    Next
    On Error GoTo 0
End Sub


Sub QuitWithMsg(msg)
    LogMsg msg
    CloseInstallWord
    WScript.Echo L(output)
    logFile.Close
    WScript.Quit(1)
End Sub

Function GetVbComponent(vbProj, componentName)
    Dim comp
    Set comp = Nothing
    On Error Resume Next
    Set comp = vbProj.VBComponents.Item(componentName)
    On Error GoTo 0
    Set GetVbComponent = comp
End Function

Function ReadVbaSourceText(filePath, ByRef textValue, ByRef errorText)
    Dim stream, textStream, errNo, errDesc
    textValue = ""
    errorText = ""

    On Error Resume Next
    Err.Clear
    Set stream = CreateObject("ADODB.Stream")
    stream.Type = 2
    stream.Charset = "gb2312"
    stream.Open
    stream.LoadFromFile filePath
    textValue = stream.ReadText(-1)
    stream.Close
    errNo = Err.Number
    errDesc = Err.Description
    On Error GoTo 0
    If errNo = 0 And Len(textValue) > 0 Then
        ReadVbaSourceText = True
        Exit Function
    End If

    On Error Resume Next
    Err.Clear
    Set textStream = fso.OpenTextFile(filePath, 1, False, -2)
    textValue = textStream.ReadAll
    textStream.Close
    errNo = Err.Number
    errDesc = Err.Description
    On Error GoTo 0
    If errNo = 0 And Len(textValue) > 0 Then
        ReadVbaSourceText = True
        Exit Function
    End If

    If errNo = 0 Then
        errorText = "VBA source file is empty: " & fso.GetFileName(filePath)
    Else
        errorText = "cannot read " & fso.GetFileName(filePath) & " (" & CStr(errNo) & "): " & errDesc
    End If
    ReadVbaSourceText = False
End Function

Function ExtractPatentPanelCode(rawText, ByRef codeText, ByRef errorText)
    Dim normalized, lines, n, lineText, started, body
    normalized = Replace(rawText, vbCrLf, vbLf)
    normalized = Replace(normalized, vbCr, vbLf)
    lines = Split(normalized, vbLf)
    started = False
    body = ""

    For n = 0 To UBound(lines)
        lineText = lines(n)
        If Not started Then
            If InStr(1, lineText, "Attribute VB_Exposed = False", vbTextCompare) > 0 Then
                started = True
            End If
        Else
            If InStr(1, LTrim(lineText), "Attribute VB_", vbTextCompare) <> 1 Then
                body = body & lineText & vbCrLf
            End If
        End If
    Next

    If Not started Then
        errorText = "PatentDictPanel.frm has no VBA code section"
        codeText = ""
        ExtractPatentPanelCode = False
        Exit Function
    End If
    If InStr(1, body, "Private Sub UserForm_Initialize", vbTextCompare) = 0 Then
        errorText = "PatentDictPanel.frm code section is incomplete"
        codeText = ""
        ExtractPatentPanelCode = False
        Exit Function
    End If
    If InStr(1, body, "Private Sub cmdExport_Click", vbTextCompare) = 0 Then
        errorText = "PatentDictPanel.frm is missing cmdExport_Click"
        codeText = ""
        ExtractPatentPanelCode = False
        Exit Function
    End If
    If InStr(1, body, "Private Sub chkAutoExport_Click", vbTextCompare) = 0 Then
        errorText = "PatentDictPanel.frm is missing chkAutoExport_Click"
        codeText = ""
        ExtractPatentPanelCode = False
        Exit Function
    End If

    If InStr(1, body, "Private Sub chkJsonVisible_Click", vbTextCompare) = 0 Then
        errorText = "PatentDictPanel.frm is missing chkJsonVisible_Click"
        codeText = ""
        ExtractPatentPanelCode = False
        Exit Function
    End If
    If InStr(1, body, "Option Explicit", vbTextCompare) = 0 Then
        body = "Option Explicit" & vbCrLf & body
    End If
    codeText = body
    errorText = ""
    ExtractPatentPanelCode = True
End Function

Function PanelControlExists(panelComp, controlName)
    Dim ctrl
    Set ctrl = Nothing
    On Error Resume Next
    Set ctrl = panelComp.Designer.Controls.Item(controlName)
    On Error GoTo 0
    If ctrl Is Nothing Then
        PanelControlExists = False
    Else
        PanelControlExists = True
    End If
End Function

Function PanelFormIsUsable(panelComp, ByRef reason)
    Dim compType, errNo, errDesc, lineCount, moduleText
    reason = ""
    If panelComp Is Nothing Then
        reason = "Import returned no component"
        PanelFormIsUsable = False
        Exit Function
    End If

    compType = 0
    On Error Resume Next
    Err.Clear
    compType = panelComp.Type
    errNo = Err.Number
    errDesc = Err.Description
    On Error GoTo 0
    If errNo <> 0 Then
        reason = "Cannot read imported component type (" & CStr(errNo) & "): " & errDesc
        PanelFormIsUsable = False
        Exit Function
    End If
    If compType <> 3 Then
        reason = "Imported component type is " & CStr(compType) & ", expected UserForm type 3"
        PanelFormIsUsable = False
        Exit Function
    End If

    If Not PanelControlExists(panelComp, "cmdExport") Then
        reason = "UserForm is missing control cmdExport"
        PanelFormIsUsable = False
        Exit Function
    End If
    If Not PanelControlExists(panelComp, "chkAutoExport") Then
        reason = "UserForm is missing control chkAutoExport"
        PanelFormIsUsable = False
        Exit Function
    End If
    If Not PanelControlExists(panelComp, "chkJsonVisible") Then
        reason = "UserForm is missing control chkJsonVisible"
        PanelFormIsUsable = False
        Exit Function
    End If
    If Not PanelControlExists(panelComp, "lblStatus") Then
        reason = "UserForm is missing control lblStatus"
        PanelFormIsUsable = False
        Exit Function
    End If

    lineCount = 0
    moduleText = ""
    On Error Resume Next
    Err.Clear
    lineCount = panelComp.CodeModule.CountOfLines
    If lineCount > 0 Then moduleText = panelComp.CodeModule.Lines(1, lineCount)
    errNo = Err.Number
    errDesc = Err.Description
    On Error GoTo 0
    If errNo <> 0 Then
        reason = "Cannot read UserForm code (" & CStr(errNo) & "): " & errDesc
        PanelFormIsUsable = False
        Exit Function
    End If
    If InStr(1, moduleText, "Private Sub UserForm_Initialize", vbTextCompare) = 0 Then
        reason = "UserForm code is missing UserForm_Initialize"
        PanelFormIsUsable = False
        Exit Function
    End If
    If InStr(1, moduleText, "Private Sub chkJsonVisible_Click", vbTextCompare) = 0 Then
        reason = "UserForm code is missing chkJsonVisible_Click"
        PanelFormIsUsable = False
        Exit Function
    End If

    PanelFormIsUsable = True
End Function

Function BuildPatentPanelFallback(vbProj, frmPath, ByRef errorText)
    Dim rawText, readError, codeText, codeError
    Dim oldComp, panelComp, designer, button, checkBox, jsonCheckBox, label
    Dim errNo, errDesc, verifyReason
    errorText = ""

    If Not ReadVbaSourceText(frmPath, rawText, readError) Then
        errorText = readError
        BuildPatentPanelFallback = False
        Exit Function
    End If
    If Not ExtractPatentPanelCode(rawText, codeText, codeError) Then
        errorText = codeError
        BuildPatentPanelFallback = False
        Exit Function
    End If

    Dim oldName
    Set oldComp = GetVbComponent(vbProj, "PatentDictPanel")
    oldName = ""
    If Not oldComp Is Nothing Then
        oldName = "PMOldPanel" & Replace(MakeBackupStamp(), "-", "")
        On Error Resume Next
        Err.Clear
        oldComp.Name = oldName
        errNo = Err.Number
        errDesc = Err.Description
        On Error GoTo 0
        If errNo <> 0 Then
            errorText = "Cannot rename the existing PatentDictPanel component (" & CStr(errNo) & "): " & errDesc
            BuildPatentPanelFallback = False
            Exit Function
        End If
    End If
    On Error Resume Next
    Err.Clear
    Set panelComp = vbProj.VBComponents.Add(3)
    panelComp.Name = "PatentDictPanel"
    Set designer = panelComp.Designer
    Set button = designer.Controls.Add("Forms.CommandButton.1", "cmdExport", True)
    Set checkBox = designer.Controls.Add("Forms.CheckBox.1", "chkAutoExport", True)
    Set jsonCheckBox = designer.Controls.Add("Forms.CheckBox.1", "chkJsonVisible", True)
    Set label = designer.Controls.Add("Forms.Label.1", "lblStatus", True)
    errNo = Err.Number
    errDesc = Err.Description
    On Error GoTo 0
    If errNo <> 0 Then
        errorText = "Cannot create UserForm with VBComponents.Add(3) (" & CStr(errNo) & "): " & errDesc
        BuildPatentPanelFallback = False
        Exit Function
    End If

    On Error Resume Next
    Err.Clear
    panelComp.CodeModule.AddFromString codeText
    errNo = Err.Number
    errDesc = Err.Description
    On Error GoTo 0
    If errNo <> 0 Then
        errorText = "Cannot inject PatentDictPanel code (" & CStr(errNo) & "): " & errDesc
        BuildPatentPanelFallback = False
        Exit Function
    End If

    If Not PanelFormIsUsable(panelComp, verifyReason) Then
        errorText = "Fallback UserForm failed validation: " & verifyReason
        BuildPatentPanelFallback = False
        Exit Function
    End If

    If Not oldComp Is Nothing Then
        On Error Resume Next
        Err.Clear
        vbProj.VBComponents.Remove oldComp
        errNo = Err.Number
        errDesc = Err.Description
        On Error GoTo 0
        Set oldComp = Nothing
        If errNo <> 0 Then
            errorText = "Cannot remove the replaced PatentDictPanel component (" & CStr(errNo) & "): " & errDesc
            BuildPatentPanelFallback = False
            Exit Function
        End If
    End If

    BuildPatentPanelFallback = True
End Function

Function ExtractVbaSourceCode(rawText, ByRef codeText, ByRef errorText)
    Dim normalized, lines, n, lineText, started, body
    normalized = Replace(rawText, vbCrLf, vbLf)
    normalized = Replace(normalized, vbCr, vbLf)
    lines = Split(normalized, vbLf)
    started = False
    body = ""

    For n = 0 To UBound(lines)
        lineText = lines(n)
        If Not started Then
            If InStr(1, lineText, "Attribute VB_Exposed = False", vbTextCompare) > 0 Then
                started = True
            End If
        Else
            If InStr(1, LTrim(lineText), "Attribute VB_", vbTextCompare) <> 1 Then
                body = body & lineText & vbCrLf
            End If
        End If
    Next

    If Not started Then
        errorText = "VBA source has no code section"
        codeText = ""
        ExtractVbaSourceCode = False
        Exit Function
    End If
    If InStr(1, body, "Private Sub Class_Initialize", vbTextCompare) = 0 Then
        errorText = "clsSaveHook is missing Class_Initialize"
        codeText = ""
        ExtractVbaSourceCode = False
        Exit Function
    End If
    If InStr(1, body, "Private Sub App_DocumentBeforeSave", vbTextCompare) = 0 Then
        errorText = "clsSaveHook is missing DocumentBeforeSave"
        codeText = ""
        ExtractVbaSourceCode = False
        Exit Function
    End If
    If InStr(1, body, "Option Explicit", vbTextCompare) = 0 Then
        body = "Option Explicit" & vbCrLf & body
    End If
    codeText = body
    errorText = ""
    ExtractVbaSourceCode = True
End Function
Function Pad2(value)
    If Len(CStr(value)) < 2 Then
        Pad2 = "0" & CStr(value)
    Else
        Pad2 = CStr(value)
    End If
End Function

Function MakeBackupStamp()
    Dim nowValue
    nowValue = Now
    MakeBackupStamp = CStr(Year(nowValue)) & Pad2(Month(nowValue)) & Pad2(Day(nowValue)) & "-" & _
                       Pad2(Hour(nowValue)) & Pad2(Minute(nowValue)) & Pad2(Second(nowValue))
End Function

Function CreateNormalBackup(normalPath, ByRef backupPath, ByRef errorText)
    Dim tempFolder, basePath, counter, errNo, errDesc
    backupPath = ""
    errorText = ""
    tempFolder = fso.GetSpecialFolder(2)
    basePath = tempFolder & "\PatentMarker-Normal-" & MakeBackupStamp()
    backupPath = basePath & ".dotm"
    counter = 0
    Do While fso.FileExists(backupPath)
        counter = counter + 1
        backupPath = basePath & "-" & CStr(counter) & ".dotm"
    Loop

    On Error Resume Next
    Err.Clear
    fso.CopyFile normalPath, backupPath, False
    errNo = Err.Number
    errDesc = Err.Description
    On Error GoTo 0
    If errNo <> 0 Then
        errorText = "Cannot copy Normal.dotm (" & CStr(errNo) & "): " & errDesc
        CreateNormalBackup = False
        Exit Function
    End If
    If Not fso.FileExists(backupPath) Then
        errorText = "Normal.dotm backup was not created"
        CreateNormalBackup = False
        Exit Function
    End If
    CreateNormalBackup = True
End Function

' === End Word 2010 UserForm compatibility helpers ===
output = output & "========================================" & vbCrLf
output = output & "PatentMarker VBA Module Installer" & vbCrLf
output = output & "(Install to Normal global template)" & vbCrLf
output = output & "========================================" & vbCrLf

' --- 0. System info ---
LogMsg "--- System Info ---"
LogMsg "  User: " & shell.ExpandEnvironmentStrings("%USERNAME%")
LogMsg "  Script dir: " & scriptDir

' --- 1. Locate VBA files ---
Dim vbaDir
vbaDir = scriptDir & "\vba"
LogMsg "VBA dir: " & vbaDir

Dim vbaFiles(6)
vbaFiles(0) = "Patterns.bas"
vbaFiles(1) = "DictModel.bas"
vbaFiles(2) = "JsonWriter.bas"
vbaFiles(3) = "PatentExtractor.bas"
vbaFiles(4) = "AutoExport.bas"
vbaFiles(5) = "PatentDictPanel.frm"
vbaFiles(6) = "clsSaveHook.cls"
' Word 2010 may import VERSION/Attribute metadata from .cls as visible code.
' The class source is checked here, then injected into a newly created class
' module below so the installed component has clean code.

Dim i, filePath, frxPath
frxPath = vbaDir & "\PatentDictPanel.frx"
If Not fso.FileExists(frxPath) Then
    QuitWithMsg "ERROR: PatentDictPanel.frx not found: " & frxPath & vbCrLf & _
                "Keep PatentDictPanel.frm and PatentDictPanel.frx together."
End If
If fso.GetFile(frxPath).Size = 0 Then
    QuitWithMsg "ERROR: PatentDictPanel.frx is empty: " & frxPath
End If

For i = 0 To UBound(vbaFiles)
    filePath = vbaDir & "\" & vbaFiles(i)
    If Not fso.FileExists(filePath) Then
        QuitWithMsg "ERROR: VBA file not found: " & filePath & vbCrLf & "Ensure \vba\ contains all .bas/.frm/.frx modules"
    End If
Next

LogMsg "VBA files: all present (5 .bas + 1 .frm + 1 .frx + 1 .cls source)"

' --- 1.5. Check for running Word processes ---
LogMsg "--- Word Process Check ---"
Dim wmiSvc, wordProcs, wmiOk
wmiOk = True
On Error Resume Next
Set wmiSvc = GetObject("winmgmts:\\.\root\cimv2")
If Err.Number <> 0 Then wmiOk = False
On Error GoTo 0

If wmiOk Then
    Set wordProcs = wmiSvc.ExecQuery("SELECT ProcessId FROM Win32_Process WHERE Name='WINWORD.EXE'")
    If wordProcs.Count > 0 Then
        LogMsg "  Found " & wordProcs.Count & " Word process(es) running"
        Dim msgBody, msgTitle, msgResult
        msgBody = "Found " & wordProcs.Count & " Word process(es) running." & vbCrLf & vbCrLf & _
                  "Please close ALL Word windows, then click OK." & vbCrLf & _
                  "(Click Cancel to abort installation)"
        msgTitle = "PatentMarker VBA Install"
        msgResult = MsgBox(L(msgBody), vbOKCancel + vbExclamation, L(msgTitle))
        If msgResult = vbCancel Then
            QuitWithMsg "Aborted by user."
        End If
        WScript.Sleep 2000
        Set wordProcs = wmiSvc.ExecQuery("SELECT ProcessId FROM Win32_Process WHERE Name='WINWORD.EXE'")
        If wordProcs.Count > 0 Then
            QuitWithMsg "ERROR: Word is still running." & vbCrLf & "Please close all Word instances and retry."
        End If
        LogMsg "  All Word processes cleared"
    Else
        LogMsg "  No Word processes running"
    End If
Else
    LogMsg "  WMI unavailable, skipping process check"
End If

' --- 2. Create Word ---
Dim wordApp
On Error Resume Next
Set wordApp = CreateObject("Word.Application")
If Err.Number <> 0 Then
    QuitWithMsg "ERROR: Cannot create Word" & vbCrLf & "Reason: " & Err.Description
End If
On Error GoTo 0

wordApp.Visible = False
wordApp.DisplayAlerts = 0

On Error Resume Next
wordApp.Options.SaveNormalPrompt = False
On Error GoTo 0

LogMsg "Word created (hidden, alerts suppressed)"

' --- 3. Get Normal.dotm path ---
Dim normalPath
On Error Resume Next
normalPath = wordApp.NormalTemplate.FullName
If Err.Number <> 0 Or IsNull(normalPath) Or normalPath = "" Then
    Dim normalErr
    normalErr = Err.Description
    On Error GoTo 0
    wordApp.Quit
    QuitWithMsg "ERROR: Cannot get Normal template path" & vbCrLf & "Reason: " & normalErr
End If
On Error GoTo 0

LogMsg "Normal template path: " & normalPath

If Not fso.FileExists(normalPath) Then
    wordApp.Quit
    QuitWithMsg "ERROR: Normal template file not found: " & normalPath
End If

' --- 3.5. Safety check: remove read-only attribute if present ---
' (The primary issue is file locking by another Word instance, not file
'  attributes. This is just a safety net for edge cases.)
Dim normalFile
Set normalFile = fso.GetFile(normalPath)
If (normalFile.Attributes And 1) Then
    On Error Resume Next
    normalFile.Attributes = normalFile.Attributes And Not 1
    If Err.Number = 0 Then
        LogMsg "  Removed read-only attribute from Normal.dotm"
    Else
        Dim roErr
        roErr = Err.Description
        On Error GoTo 0
        wordApp.Quit
        QuitWithMsg "ERROR: Cannot remove read-only attribute" & vbCrLf & _
                    "Path: " & normalPath & vbCrLf & _
                    "Reason: " & roErr & vbCrLf & _
                    "Fix: Right-click file > Properties > uncheck Read-only"
    End If
    On Error GoTo 0
Else
    LogMsg "  Normal.dotm is writable (not read-only)"
End If

' --- 3.75. Make a recoverable Normal.dotm backup before changing VBA ---
Dim normalBackupPath, backupError
normalBackupPath = ""
backupError = ""
If Not CreateNormalBackup(normalPath, normalBackupPath, backupError) Then
    wordApp.Quit
    QuitWithMsg "ERROR: Cannot backup Normal template before installation." & vbCrLf & _
                "Reason: " & backupError
End If
LogMsg "Normal template backup: " & normalBackupPath
' --- 4. Open Normal.dotm ---
Dim doc
On Error Resume Next
Set doc = wordApp.Documents.Open(normalPath, False, False, False)
If Err.Number <> 0 Then
    Dim openErr
    openErr = Err.Description
    On Error GoTo 0
    wordApp.Quit
    QuitWithMsg "ERROR: Cannot open Normal template" & vbCrLf & "Reason: " & openErr
End If
On Error GoTo 0

LogMsg "Normal template opened"
LogMsg "  ReadOnly: " & doc.ReadOnly
LogMsg "  Protection: " & doc.ProtectionType

If doc.ReadOnly Then
    doc.Close False
    wordApp.Quit
    QuitWithMsg "ERROR: Normal template is read-only" & vbCrLf & _
                "Possible causes:" & vbCrLf & _
                "  - Another Word instance is running (Normal.dotm locked)" & vbCrLf & _
                "  - File attribute is read-only" & vbCrLf & _
                "Fix:" & vbCrLf & _
                "  - Close all Word windows and retry" & vbCrLf & _
                "  - Right-click file > Properties > uncheck Read-only"
End If

' --- 5. Access VBA project ---
Dim vbProj
On Error Resume Next
Set vbProj = doc.VBProject
If Err.Number <> 0 Then
    Dim vbaErr
    vbaErr = Err.Description
    On Error GoTo 0
    doc.Close False
    wordApp.Quit
    QuitWithMsg "ERROR: Cannot access VBA project" & vbCrLf & _
                "Reason: " & vbaErr & vbCrLf & vbCrLf & _
                "Enable in Word:" & vbCrLf & _
                "  1. File > Options > Trust Center" & vbCrLf & _
                "  2. Trust Center Settings > Macro Settings" & vbCrLf & _
                "  3. Check: Trust access to the VBA project object model" & vbCrLf & _
                "  4. Set macro security to: Disable all macros with notification" & vbCrLf & _
                "  5. Close Word, then re-run this script"
End If
On Error GoTo 0

LogMsg "VBA project: accessible"
LogMsg "VBA project name: " & vbProj.Name

' --- 6. Delete old modules ---
Dim moduleNames(6)
moduleNames(0) = "Patterns"
moduleNames(1) = "DictModel"
moduleNames(2) = "JsonWriter"
moduleNames(3) = "PatentExtractor"
moduleNames(4) = "AutoExport"
moduleNames(5) = "clsSaveHook"
moduleNames(6) = "PatentDictPanel"

LogMsg "Deleting old modules (if any)..."
Dim oldPanel
For i = 0 To UBound(moduleNames)
    If moduleNames(i) = "PatentDictPanel" Then
        Set oldPanel = GetVbComponent(vbProj, "PatentDictPanel")
        If oldPanel Is Nothing Then
            LogMsg "  (not found): PatentDictPanel"
        Else
            LogMsg "  Retained: PatentDictPanel until replacement is created"
        End If
        Set oldPanel = Nothing
    Else
        On Error Resume Next
        vbProj.VBComponents.Remove vbProj.VBComponents.Item(moduleNames(i))
        If Err.Number = 0 Then
            LogMsg "  Removed: " & moduleNames(i)
        Else
            LogMsg "  (not found): " & moduleNames(i)
        End If
        On Error GoTo 0
    End If
Next

' --- 7. Install VBA modules (.bas/.frm) ---
' The .frm/.frx pair is validated as a package asset. To avoid Word 2010
' importing the form header as standard-module code, the installer builds
' the UserForm through the VBA designer and injects its clean code section.
LogMsg "Importing VBA modules..."
Dim imported, panelComp, panelReason, fallbackError, importedComp, importErr
imported = 0

For i = 0 To UBound(vbaFiles)
    filePath = vbaDir & "\" & vbaFiles(i)
    If LCase(fso.GetExtensionName(vbaFiles(i))) = "frm" Then
        LogMsg "  Building PatentDictPanel UserForm with Designer..."
        fallbackError = ""
        If Not BuildPatentPanelFallback(vbProj, filePath, fallbackError) Then
            QuitWithMsg "ERROR: Cannot build PatentDictPanel UserForm." & vbCrLf & _
                        "Reason: " & fallbackError & vbCrLf & _
                        "Keep PatentDictPanel.frm and PatentDictPanel.frx together and retry."
        End If
        LogMsg "  OK: PatentDictPanel rebuilt with VBComponents.Add(3) and Designer controls"
        imported = imported + 1
    ElseIf LCase(fso.GetExtensionName(vbaFiles(i))) = "cls" Then
        LogMsg "  OK: clsSaveHook.cls source present (will inject)"
    Else
        On Error Resume Next
        Err.Clear
        Set importedComp = vbProj.VBComponents.Import(filePath)
        If Err.Number <> 0 Then
            importErr = Err.Description
            On Error GoTo 0
            QuitWithMsg "ERROR: Import failed: " & vbaFiles(i) & vbCrLf & "Reason: " & importErr
        Else
            LogMsg "  OK: " & vbaFiles(i)
            imported = imported + 1
        End If
        On Error GoTo 0
    End If
Next

' --- 7.25. Verify UserForm import/reconstruction ---
Set panelComp = GetVbComponent(vbProj, "PatentDictPanel")
panelReason = ""
If Not PanelFormIsUsable(panelComp, panelReason) Then
    On Error Resume Next
    doc.Close False
    wordApp.Quit
    On Error GoTo 0
    QuitWithMsg "ERROR: PatentDictPanel is not a usable UserForm after build." & vbCrLf & _
                "Reason: " & panelReason
End If
LogMsg "  OK: PatentDictPanel is a UserForm (type 3) with required controls"
' --- 7.5. Create clsSaveHook class module via code injection ---
' Word 2010+ (and some Word 2007 configurations) fail to import .cls files
' correctly: the VERSION/Attribute metadata appears as visible code, causing
' a compile error. The workaround is to create a class module and add the
' source code directly with AddFromString.
LogMsg "Creating clsSaveHook class module (code injection)..."

Dim clsComp
On Error Resume Next
Set clsComp = vbProj.VBComponents.Add(2)  ' 2 = vbext_ct_ClassModule
If Err.Number <> 0 Then
    Dim clsErr
    clsErr = Err.Description
    On Error GoTo 0
    doc.Close False
    wordApp.Quit
    QuitWithMsg "ERROR: Cannot create class module" & vbCrLf & "Reason: " & clsErr
End If
On Error GoTo 0

clsComp.Name = "clsSaveHook"

Dim clsCode, clsRawText, clsReadError, clsCodeError, clsInjectError
If Not ReadVbaSourceText(vbaDir & "\clsSaveHook.cls", clsRawText, clsReadError) Then
    QuitWithMsg "ERROR: Cannot read clsSaveHook.cls source." & vbCrLf & "Reason: " & clsReadError
End If
If Not ExtractVbaSourceCode(clsRawText, clsCode, clsCodeError) Then
    QuitWithMsg "ERROR: clsSaveHook.cls source is incomplete." & vbCrLf & "Reason: " & clsCodeError
End If

clsInjectError = ""
On Error Resume Next
Err.Clear
clsComp.CodeModule.AddFromString clsCode
If Err.Number <> 0 Then
    clsInjectError = CStr(Err.Number) & ": " & Err.Description
End If
On Error GoTo 0
If clsInjectError <> "" Then
    QuitWithMsg "ERROR: Cannot inject clsSaveHook code." & vbCrLf & "Reason: " & clsInjectError
End If
LogMsg "  OK: clsSaveHook (class module, source code injected)"
imported = imported + 1

LogMsg "Imported " & imported & " / 7 modules"

' --- 8. Save Normal.dotm (3-level strategy) ---
LogMsg "Saving Normal template..."
Dim saveOk, saveErr, wordClosed, restoreOk, restoreErr
saveOk = False
saveErr = ""
wordClosed = False
restoreOk = False
restoreErr = ""

Dim beforeModTime
beforeModTime = fso.GetFile(normalPath).DateLastModified
LogMsg "  File time before save: " & beforeModTime

On Error Resume Next
doc.Save
If Err.Number = 0 Then
    saveOk = True
    LogMsg "  Save OK (doc.Save)"
Else
    saveErr = Err.Description & " (0x" & Hex(Err.Number) & ")"
    LogMsg "  doc.Save failed: " & saveErr
End If
On Error GoTo 0

If Not saveOk Then
    On Error Resume Next
    doc.SaveAs normalPath, wdFormatTemplate
    If Err.Number = 0 Then
        saveOk = True
        LogMsg "  Save OK (SaveAs)"
    Else
        saveErr = Err.Description & " (0x" & Hex(Err.Number) & ")"
        LogMsg "  SaveAs failed: " & saveErr
    End If
    On Error GoTo 0
End If

If Not saveOk Then
    Dim tempPath
    tempPath = fso.GetSpecialFolder(2) & "\Normal_pm_temp.dotm"
    On Error Resume Next
    doc.SaveAs tempPath, wdFormatTemplate
    If Err.Number = 0 Then
        On Error GoTo 0
        LogMsg "  Saved to temp: " & tempPath
        CloseInstallWord
        wordClosed = True
        WScript.Sleep 2000
        On Error Resume Next
        fso.CopyFile tempPath, normalPath, True
        If Err.Number = 0 Then
            saveOk = True
            LogMsg "  Replaced Normal.dotm via temp file"
        Else
            saveErr = "CopyFile: " & Err.Description
            LogMsg "  " & saveErr
        End If
        fso.DeleteFile tempPath, True
        On Error GoTo 0
    Else
        saveErr = Err.Description & " (0x" & Hex(Err.Number) & ")"
        LogMsg "  Temp save failed: " & saveErr
        On Error GoTo 0
    End If
End If

If Not saveOk Then
    On Error Resume Next
    If Not wordClosed Then CloseInstallWord
    On Error GoTo 0

    On Error Resume Next
    Err.Clear
    fso.CopyFile normalBackupPath, normalPath, True
    If Err.Number = 0 Then
        restoreOk = True
        LogMsg "  Restored Normal.dotm from backup after save failure"
    Else
        restoreErr = Err.Description & " (0x" & Hex(Err.Number) & ")"
        LogMsg "  WARNING: failed to restore Normal.dotm backup: " & restoreErr
    End If
    On Error GoTo 0

    If restoreOk Then
        QuitWithMsg "ERROR: Cannot save Normal template; the original template was restored." & vbCrLf & _
                    "Reason: " & saveErr & vbCrLf & _
                    "Backup: " & normalBackupPath & vbCrLf & _
                    "Please close all Word instances and retry."
    Else
        QuitWithMsg "ERROR: Cannot save Normal template and automatic restore failed." & vbCrLf & _
                    "Save reason: " & saveErr & vbCrLf & _
                    "Restore reason: " & restoreErr & vbCrLf & _
                    "Backup: " & normalBackupPath
    End If
End If

' --- 8.5. Verify save by checking file modification time ---
WScript.Sleep 500
Dim afterModTime, saveVerified
afterModTime = fso.GetFile(normalPath).DateLastModified
If afterModTime > beforeModTime Then
    saveVerified = True
    LogMsg "  Save VERIFIED: file updated (" & afterModTime & ")"
Else
    saveVerified = False
    LogMsg "  WARNING: file time unchanged, save may not have persisted"
    LogMsg "    Before: " & beforeModTime
    LogMsg "    After:  " & afterModTime
End If

' --- 9. Close ---
CloseInstallWord
WScript.Sleep 1000

' --- 10. Summary ---
LogMsg ""
LogMsg "=== VBA Install Complete ==="
LogMsg "Normal template: " & normalPath
LogMsg "Backup retained: " & normalBackupPath
LogMsg "Modules imported: " & imported & " / 7"
If saveVerified Then
    LogMsg "Saved: Yes (verified)"
Else
    LogMsg "Saved: Reported OK but file time unchanged - please verify manually"
End If
LogMsg ""
LogMsg "Modules installed to global template, available in all Word documents"
LogMsg ""
LogMsg "Verification:"
LogMsg "  1. Open a new Word document"
LogMsg "  2. In Word: Alt+F8 -> ShowPatentDictPanel (one macro)"
LogMsg "  3. In the panel: click Manual Export or toggle Auto-Export on Save"
LogMsg ""
LogMsg "dict.json will be saved to Word document directory"
LogMsg "CAD auto-export (DWG in same folder)"

LogMsg ""
LogMsg "========================================"
LogMsg "Installation complete"
LogMsg "========================================"

WScript.Echo L(output)
logFile.Close

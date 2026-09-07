Attribute VB_Name = "AutoExport"
Option Explicit

Private m_hook As clsSaveHook
Private m_enabled As Boolean

' 单一入口宏：打开专利标注字典工具面板（唯一出现在 Word 宏列表中的过程）
Public Sub ShowPatentDictPanel()
    PatentDictPanel.Show
End Sub

' 自动导出状态读取（供面板勾选状态与初始化读取）
Public Property Get IsAutoExportEnabled() As Boolean
    IsAutoExportEnabled = m_enabled
End Property

' 自动导出状态写入（供面板勾选事件调用）
Public Property Let IsAutoExportEnabled(ByVal v As Boolean)
    If v Then
        EnableAutoExport
    Else
        DisableAutoExport
    End If
End Property

' 打开文档时自动开启保存导出
'（Private：不显示在宏列表中，但作为 Word 自动宏仍会自动执行）
Private Sub AutoOpen()
    EnableAutoExport
End Sub

Private Sub EnableAutoExport()
    If m_enabled Then Exit Sub
    Set m_hook = New clsSaveHook
    m_enabled = True
End Sub

Private Sub DisableAutoExport()
    Set m_hook = Nothing
    m_enabled = False
End Sub

' 导出当前文档为 <主名>.dict.json
'（Function：不显示在宏列表中，由保存钩子 clsSaveHook 与面板"手动导出"按钮调用）
Public Function ExportDict(Optional ByVal doc As Document) As Boolean
    On Error GoTo errHandler
    ExportDict = False

    If doc Is Nothing Then Set doc = ActiveDocument

    Dim srcName As String
    srcName = doc.Name

    Dim outPath As String
    outPath = GetOutputPath(doc)
    If outPath = "" Then Exit Function

    Dim timestamp As String
    timestamp = Format(Now, "yyyy-mm-ddTHH:nn:ss")

    Dim root As Object
    Set root = DictModel.BuildModel(doc.Content.Text, srcName, timestamp)

    Dim json As String
    json = JsonWriter.Serialize(root)

    ' v4.0：导出前备份被 CAD 端修改过的旧字典，防止 Word 静默覆盖
    Dim backupFailure As String
    If Not BackupIfCadModified(outPath, backupFailure) Then
        Err.Raise vbObjectError + 510, "AutoExport.BackupIfCadModified", backupFailure
    End If

    ' v5.2: clear Hidden/System attributes so ADODB SaveToFile can overwrite the hidden dict file
    If CreateObject("Scripting.FileSystemObject").FileExists(outPath) Then
        On Error Resume Next
        Err.Clear
        SetAttr outPath, vbNormal
        Dim attributeFailure As String
        If Err.Number <> 0 Then
            attributeFailure = Err.Description & " (" & Err.Number & ")"
            On Error GoTo errHandler
            Err.Raise vbObjectError + 511, "AutoExport.SetAttr", attributeFailure
        End If
        On Error GoTo errHandler
    End If

    If Not JsonWriter.WriteToFile(outPath, json) Then
        Err.Raise vbObjectError + 513, "AutoExport.JsonWriter", "Dictionary write failed"
    End If

    ' v5.2: keep the dict file invisible in Windows Explorer (Hidden + System attributes)
    On Error Resume Next
    SetAttr outPath, vbHidden Or vbSystem
    On Error GoTo errHandler

    ' v5.2: after a DWG appeared, the export target switched from the Word base
    ' name to the DWG base name - remove the orphan dict from the Word-only era
    ' (it is hidden, so the user cannot see or delete it manually)
    CleanupOrphanWordDict doc, outPath

    ExportDict = True
    Exit Function

errHandler:
    Dim errorNumber As Long
    Dim errorDescription As String
    errorNumber = Err.Number
    errorDescription = Err.Description
    On Error Resume Next
    Dim errPath As String
    Dim errorDir As String
    errorDir = GetOutputDir(doc)
    If errorDir <> "" Then
        errPath = errorDir & "\autoexport-error.txt"
        JsonWriter.WriteToFile errPath, "ERROR: " & errorDescription & " (" & errorNumber & ")"
    End If
    ExportDict = False
End Function

' ======================================================================
' Resolve the output dictionary in the Word document directory.
' An exact DWG base name wins; a single compatibility match is accepted.
' Ambiguous multi-DWG directories fail closed instead of guessing.
' ======================================================================
Private Function GetOutputPath(ByVal doc As Document) As String
    Dim dir As String
    dir = GetOutputDir(doc)
    If dir = "" Then
        GetOutputPath = ""
        Exit Function
    End If

    Dim baseName As String
    Dim mappingFailure As String
    baseName = FindDwgBaseName(dir, doc.Name, mappingFailure)
    If mappingFailure <> "" Then
        Err.Raise vbObjectError + 512, "AutoExport.GetOutputPath", mappingFailure
    End If

    ' 若未找到 DWG，回退到 Word 文档名
    If baseName = "" Then
        baseName = doc.Name
        Dim dotPos As Long
        dotPos = InStrRev(baseName, ".")
        If dotPos > 0 Then baseName = Left(baseName, dotPos - 1)
    End If

    GetOutputPath = dir & "\" & baseName & ".dict.json"
End Function

' ======================================================================
' Find the DWG base name safely. The function returns an empty value when
' there is no DWG; failure is populated when multiple candidates are unsafe.
' ======================================================================
Private Function FindDwgBaseName(ByVal dir As String, ByVal wordDocName As String, ByRef failure As String) As String
    On Error GoTo errHandler
    failure = ""

    Dim fso As Object
    Set fso = CreateObject("Scripting.FileSystemObject")

    If Not fso.FolderExists(dir) Then
        FindDwgBaseName = ""
        Exit Function
    End If

    Dim folder As Object
    Set folder = fso.GetFolder(dir)

    Dim dwgCount As Long: dwgCount = 0
    Dim dwgNames() As String
    ReDim dwgNames(0 To 0)

    ' 收集所有 .dwg 文件
    Dim f As Object
    For Each f In folder.Files
        If LCase(fso.GetExtensionName(f.Name)) = "dwg" Then
            ReDim Preserve dwgNames(0 To dwgCount)
            dwgNames(dwgCount) = f.Name
            dwgCount = dwgCount + 1
        End If
    Next

    If dwgCount = 0 Then
        FindDwgBaseName = ""
        Exit Function
    End If

    ' 只有一个 DWG，直接使用
    If dwgCount = 1 Then
        FindDwgBaseName = RemoveExt(dwgNames(0))
        Exit Function
    End If

    ' 多个 DWG：与 Word 文档名匹配
    Dim wordBase As String
    wordBase = RemoveExt(wordDocName)

    Dim i As Long
    For i = 0 To dwgCount - 1
        If StrComp(wordBase, RemoveExt(dwgNames(i)), vbTextCompare) = 0 Then
            FindDwgBaseName = RemoveExt(dwgNames(i))
            Exit Function
        End If
    Next

    Dim matchCount As Long
    Dim matchedBase As String
    matchCount = 0
    For i = 0 To dwgCount - 1
        Dim dwgBase As String
        dwgBase = RemoveExt(dwgNames(i))
        If InStr(1, LCase(wordBase), LCase(dwgBase), vbTextCompare) > 0 Or _
           InStr(1, LCase(dwgBase), LCase(wordBase), vbTextCompare) > 0 Then
            matchCount = matchCount + 1
            matchedBase = dwgBase
        End If
    Next

    If matchCount = 1 Then
        FindDwgBaseName = matchedBase
        Exit Function
    End If

    If matchCount > 1 Then
        failure = "Multiple DWG files match the Word document name; export was not written."
    Else
        failure = "Multiple DWG files are present and no exact name match was found; export was not written."
    End If
    Exit Function

errHandler:
    failure = "DWG discovery failed: " & Err.Description
    FindDwgBaseName = ""
End Function

Private Function RemoveExt(ByVal fileName As String) As String
    Dim dotPos As Long
    dotPos = InStrRev(fileName, ".")
    If dotPos > 0 Then
        RemoveExt = Left(fileName, dotPos - 1)
    Else
        RemoveExt = fileName
    End If
End Function

Private Function GetOutputDir(ByVal doc As Document) As String
    On Error Resume Next
    Dim p As String
    p = doc.Path
    If Err.Number <> 0 Or p = "" Then
        GetOutputDir = ""
        Exit Function
    End If
    On Error GoTo 0
    GetOutputDir = p
End Function

' ======================================================================
' v4.0：导出前备份被 CAD 端修改过的旧字典，防止 Word 静默覆盖。
'
' 检测：旧 dict.json 内容含 "modified_by": "cad"（CAD 端 DictWriter 写入的标记）。
' 备份：<主名>.dict.json.word-<yyyymmdd-hhnnss>.bak，只保留最新一个；
'       CAD 端 DictConflict.FindWordBackup 依赖此命名约定做冲突检测。
' Backup failures are returned to ExportDict so CAD changes are never silently overwritten.
' ======================================================================
Private Function BackupIfCadModified(ByVal dictPath As String, ByRef failure As String) As Boolean
    On Error GoTo errHandler
    BackupIfCadModified = True
    failure = ""

    Dim fso As Object
    Set fso = CreateObject("Scripting.FileSystemObject")
    If Not fso.FileExists(dictPath) Then Exit Function

    Dim content As String
    Dim readFailure As String
    content = ReadUtf8File(dictPath, readFailure)
    If readFailure <> "" Then
        failure = "CAD dictionary read failed: " & readFailure
        BackupIfCadModified = False
        Exit Function
    End If
    If InStr(1, content, """modified_by"": ""cad""", vbBinaryCompare) = 0 Then Exit Function

    Dim bakDir As String
    bakDir = fso.GetParentFolderName(dictPath)
    If bakDir = "" Then Exit Function
    Dim fileName As String
    fileName = fso.GetFileName(dictPath)

    ' Create the new backup before deleting older backups. If copying fails,
    ' the previous CAD backup remains available for arbitration.
    Dim stamp As String
    stamp = Format(Now, "yyyymmdd-hhnnss")
    Dim bakPath As String
    bakPath = bakDir & "\" & fileName & ".word-" & stamp & ".bak"
    If fso.FileExists(bakPath) Then
        SetAttr bakPath, vbNormal
        Kill bakPath
    End If
    FileCopy dictPath, bakPath
    SetAttr bakPath, vbHidden Or vbSystem

    ' Keep only the newest backup, including hidden/system files.
    Dim oldBak As String
    Dim oldPath As String
    oldBak = Dir(bakDir & "\" & fileName & ".word-*.bak", vbHidden Or vbSystem)
    Do While oldBak <> ""
        oldPath = bakDir & "\" & oldBak
        If StrComp(oldPath, bakPath, vbTextCompare) <> 0 Then
            SetAttr oldPath, vbNormal
            Kill oldPath
        End If
        oldBak = Dir()
    Loop

    Exit Function

errHandler:
    failure = Err.Description & " (" & Err.Number & ")"
    BackupIfCadModified = False
End Function

' ======================================================================
' v5.2: delete <Word base>.dict.json when the effective export base is a
' DWG name (a DWG appeared after the first Word-only export). The orphan
' is hidden (v5.2 attributes), so the user cannot see or delete it.
' Only the file named exactly like the current Word document is touched;
' other documents' dict files are never affected.
' ======================================================================
Private Sub CleanupOrphanWordDict(ByVal doc As Document, ByVal outPath As String)
    On Error GoTo done

    Dim dir As String
    dir = GetOutputDir(doc)
    If dir = "" Then Exit Sub

    Dim wordBase As String
    wordBase = RemoveExt(doc.Name)

    Dim orphanPath As String
    orphanPath = dir & "\" & wordBase & ".dict.json"

    ' Target never switched (no DWG rename) - nothing to clean
    If StrComp(orphanPath, outPath, vbTextCompare) = 0 Then Exit Sub

    Dim fso As Object
    Set fso = CreateObject("Scripting.FileSystemObject")
    If Not fso.FileExists(orphanPath) Then Exit Sub

    On Error Resume Next
    ' Hidden files cannot be killed - clear attributes first
    SetAttr orphanPath, vbNormal
    Kill orphanPath

done:
End Sub
' Read UTF-8 file content and return failure details to the caller.
Private Function ReadUtf8File(ByVal path As String, ByRef failure As String) As String
    On Error GoTo errHandler
    failure = ""
    Dim stream As Object
    Set stream = CreateObject("ADODB.Stream")
    stream.Type = 2
    stream.Charset = "utf-8"
    stream.Open
    stream.LoadFromFile path
    ReadUtf8File = stream.ReadText(-1)
    stream.Close
    Exit Function
errHandler:
    Dim errorNumber As Long
    Dim errorDescription As String
    errorNumber = Err.Number
    errorDescription = Err.Description
    On Error Resume Next
    If Not stream Is Nothing Then stream.Close
    failure = errorDescription & " (" & errorNumber & ")"
    ReadUtf8File = ""
End Function

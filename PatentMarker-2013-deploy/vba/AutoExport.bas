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
    BackupIfCadModified outPath

    ' v5.2: clear Hidden/System attributes so ADODB SaveToFile can overwrite the hidden dict file
    On Error Resume Next
    SetAttr outPath, vbNormal
    On Error GoTo errHandler

    JsonWriter.WriteToFile outPath, json

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
    On Error Resume Next
    Dim errPath As String
    errPath = GetOutputDir(doc) & "\autoexport-error.txt"
    JsonWriter.WriteToFile errPath, "ERROR: " & Err.Description & " (" & Err.Number & ")"
    ExportDict = False
End Function

' ======================================================================
' 确定输出目录，优先使用同目录下的 DWG 文件。
'
' 匹配策略：
'   1. 扫描 Word 文档所在目录中的 .dwg 文件
'   2. 如果只有一个 DWG，直接使用其名称
'   3. 如果多个 DWG，尝试与 Word 文档名匹配（双向包含）
'   4. 若无法匹配，使用最近修改的 DWG
'   5. 如果没有 DWG，回退到 Word 文档名
' ======================================================================
Private Function GetOutputPath(ByVal doc As Document) As String
    Dim dir As String
    dir = GetOutputDir(doc)
    If dir = "" Then
        GetOutputPath = ""
        Exit Function
    End If

    Dim baseName As String
    baseName = FindDwgBaseName(dir, doc.Name)

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
' 在指定目录中查找 DWG 文件（最多收集 100 个，过滤其他扩展名）。
' 优先与 Word 文档名双向包含匹配，否则取最近修改的 DWG。
' ======================================================================
Private Function FindDwgBaseName(ByVal dir As String, ByVal wordDocName As String) As String
    On Error GoTo errHandler

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
    Dim dwgDates() As Date
    ReDim dwgNames(0 To 99)
    ReDim dwgDates(0 To 99)

    ' 收集所有 .dwg 文件
    Dim f As Object
    For Each f In folder.Files
        If LCase(fso.GetExtensionName(f.Name)) = "dwg" Then
            If dwgCount <= 99 Then
                dwgNames(dwgCount) = f.Name
                dwgDates(dwgCount) = f.DateLastModified
                dwgCount = dwgCount + 1
            End If
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
        Dim dwgBase As String
        dwgBase = RemoveExt(dwgNames(i))
        ' 双向包含匹配
        If InStr(1, LCase(wordBase), LCase(dwgBase), vbTextCompare) > 0 Or _
           InStr(1, LCase(dwgBase), LCase(wordBase), vbTextCompare) > 0 Then
            FindDwgBaseName = dwgBase
            Exit Function
        End If
    Next

    ' 无法匹配：使用最近修改的 DWG
    Dim newest As Long: newest = 0
    For i = 1 To dwgCount - 1
        If dwgDates(i) > dwgDates(newest) Then newest = i
    Next
    FindDwgBaseName = RemoveExt(dwgNames(newest))
    Exit Function

errHandler:
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
' 任何失败都不阻断导出（备份是尽力而为）。
' ======================================================================
Private Sub BackupIfCadModified(ByVal dictPath As String)
    On Error GoTo done

    Dim fso As Object
    Set fso = CreateObject("Scripting.FileSystemObject")
    If Not fso.FileExists(dictPath) Then Exit Sub

    ' 读旧文件（UTF-8），检查 CAD 修改标记
    Dim content As String
    content = ReadUtf8File(dictPath)
    If InStr(1, content, """modified_by"": ""cad""", vbBinaryCompare) = 0 Then Exit Sub

    Dim bakDir As String
    bakDir = fso.GetParentFolderName(dictPath)
    If bakDir = "" Then Exit Sub
    Dim fileName As String
    fileName = fso.GetFileName(dictPath)

    ' 删除旧备份（只保留最新一个）
    Dim oldBak As String
    ' v5.2: vbHidden+vbSystem required - Dir() default (vbNormal) does not return hidden backup files
    oldBak = Dir(bakDir & "\" & fileName & ".word-*.bak", vbHidden Or vbSystem)
    Do While oldBak <> ""
        On Error Resume Next
        ' v5.2: clear attributes before Kill (hidden files cannot be killed)
        SetAttr bakDir & "\" & oldBak, vbNormal
        Kill bakDir & "\" & oldBak
        On Error GoTo done
        oldBak = Dir()
    Loop

    ' 生成新备份（同一秒内重复导出时先删后复制）
    Dim stamp As String
    stamp = Format(Now, "yyyymmdd-hhnnss")
    Dim bakPath As String
    bakPath = bakDir & "\" & fileName & ".word-" & stamp & ".bak"
    If fso.FileExists(bakPath) Then
        On Error Resume Next
        ' v5.2: clear attributes before Kill (hidden files cannot be killed)
        SetAttr bakPath, vbNormal
        Kill bakPath
        On Error GoTo done
    End If
    FileCopy dictPath, bakPath

    ' v5.2: keep the backup file invisible in Windows Explorer too
    On Error Resume Next
    SetAttr bakPath, vbHidden Or vbSystem
    On Error GoTo done

done:
End Sub

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
' 以 UTF-8 读取整个文件内容（失败返回空串）
Private Function ReadUtf8File(ByVal path As String) As String
    On Error GoTo errHandler
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
    On Error Resume Next
    If Not stream Is Nothing Then stream.Close
    ReadUtf8File = ""
End Function

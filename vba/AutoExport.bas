Attribute VB_Name = "AutoExport"
Option Explicit

Private m_hook As clsSaveHook
Private m_enabled As Boolean

Public Sub AutoOpen()
    EnableAutoExport
End Sub

Public Sub EnableAutoExport()
    If m_enabled Then Exit Sub
    Set m_hook = New clsSaveHook
    m_enabled = True
End Sub

Public Sub DisableAutoExport()
    Set m_hook = Nothing
    m_enabled = False
End Sub

Public Sub ExportDict(Optional ByVal doc As Document)
    On Error GoTo errHandler

    If doc Is Nothing Then Set doc = ActiveDocument

    Dim srcName As String
    srcName = doc.Name

    Dim outPath As String
    outPath = GetOutputPath(doc)
    If outPath = "" Then Exit Sub

    Dim timestamp As String
    timestamp = Format(Now, "yyyy-mm-ddTHH:nn:ss")

    Dim root As Object
    Set root = DictModel.BuildModel(doc.Content.Text, srcName, timestamp)

    Dim json As String
    json = JsonWriter.Serialize(root)

    ' v4.0：导出前备份被 CAD 端修改过的旧字典，防止 Word 静默覆盖
    BackupIfCadModified outPath

    JsonWriter.WriteToFile outPath, json

    Exit Sub

errHandler:
    On Error Resume Next
    Dim errPath As String
    errPath = GetOutputDir(doc) & "\autoexport-error.txt"
    JsonWriter.WriteToFile errPath, "ERROR: " & Err.Description & " (" & Err.Number & ")"
End Sub

' ======================================================================
' ȷ�����·��������ʹ��ͬĿ¼�µ� DWG �ļ���
'
' ���Ҳ��ԣ�
'   1. ɨ�� Word �ĵ�����Ŀ¼�е� .dwg �ļ�
'   2. ���ֻ��һ�� DWG��ֱ��ʹ��������
'   3. ����ж�� DWG�������� Word �ĵ���ƥ�䣨������ϵ��
'   4. ����޷�ƥ�䣬ʹ������޸ĵ� DWG
'   5. ���û�� DWG�����˵� Word �ĵ���
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

    ' ���û�ҵ� DWG�����˵� Word �ĵ���
    If baseName = "" Then
        baseName = doc.Name
        Dim dotPos As Long
        dotPos = InStrRev(baseName, ".")
        If dotPos > 0 Then baseName = Left(baseName, dotPos - 1)
    End If

    GetOutputPath = dir & "\" & baseName & ".dict.json"
End Function

' ======================================================================
' ��ָ��Ŀ¼�в��� DWG �ļ����������������������չ����
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

    ' �ռ����� .dwg �ļ�
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

    ' ֻ��һ�� DWG��ֱ��ʹ��
    If dwgCount = 1 Then
        FindDwgBaseName = RemoveExt(dwgNames(0))
        Exit Function
    End If

    ' ��� DWG�������� Word �ĵ���ƥ��
    Dim wordBase As String
    wordBase = RemoveExt(wordDocName)

    Dim i As Long
    For i = 0 To dwgCount - 1
        Dim dwgBase As String
        dwgBase = RemoveExt(dwgNames(i))
        ' ˫�����ƥ��
        If InStr(1, LCase(wordBase), LCase(dwgBase), vbTextCompare) > 0 Or _
           InStr(1, LCase(dwgBase), LCase(wordBase), vbTextCompare) > 0 Then
            FindDwgBaseName = dwgBase
            Exit Function
        End If
    Next

    ' �޷�ƥ�䣺ʹ������޸ĵ� DWG
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
    oldBak = Dir(bakDir & "\" & fileName & ".word-*.bak")
    Do While oldBak <> ""
        On Error Resume Next
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
        Kill bakPath
        On Error GoTo done
    End If
    FileCopy dictPath, bakPath

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
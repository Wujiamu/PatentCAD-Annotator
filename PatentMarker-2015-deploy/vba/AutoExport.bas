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

    JsonWriter.WriteToFile outPath, json

    Exit Sub

errHandler:
    On Error Resume Next
    Dim errPath As String
    errPath = GetOutputDir(doc) & "\autoexport-error.txt"
    JsonWriter.WriteToFile errPath, "ERROR: " & Err.Description & " (" & Err.Number & ")"
End Sub

' ======================================================================
' 确定输出路径：优先使用同目录下的 DWG 文件名
'
' 查找策略：
'   1. 扫描 Word 文档所在目录中的 .dwg 文件
'   2. 如果只有一个 DWG，直接使用其名称
'   3. 如果有多个 DWG，尝试与 Word 文档名匹配（包含关系）
'   4. 如果无法匹配，使用最近修改的 DWG
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

    ' 如果没找到 DWG，回退到 Word 文档名
    If baseName = "" Then
        baseName = doc.Name
        Dim dotPos As Long
        dotPos = InStrRev(baseName, ".")
        If dotPos > 0 Then baseName = Left(baseName, dotPos - 1)
    End If

    GetOutputPath = dir & "\" & baseName & ".dict.json"
End Function

' ======================================================================
' 在指定目录中查找 DWG 文件，返回其基础名（不含扩展名）
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

    ' 多个 DWG：尝试与 Word 文档名匹配
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
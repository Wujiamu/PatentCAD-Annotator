Attribute VB_Name = "PatentExtractor"
Option Explicit

' 宿主入口：从当前 Word 文档提取标号字典，写出 <dwgname>.dict.json。
' 如果同目录下有 .dwg 文件，自动用 DWG 文件名命名 JSON，
' 这样 CAD 插件能直接找到（DictLoader 查找 <dwgname>.dict.json）。
'
' 调试：VBE 里直接运行 ExtractDict。
' 依赖模块：DictModel、JsonWriter、Patterns。

Public Sub ExtractDict()
    If Documents.Count = 0 Then
        MsgBox "请先打开说明书文档。", vbExclamation
        Exit Sub
    End If

    Dim doc As Document
    Set doc = ActiveDocument

    Dim root As Object
    Set root = DictModel.BuildModel(doc.Content.Text, doc.Name, FormatISO8601(Now))

    Dim outPath As String
    outPath = SuggestOutputPath(doc)
    outPath = InputBox("dict.json 保存路径：", "保存字典", outPath)
    If outPath = "" Then Exit Sub

    JsonWriter.WriteToFile outPath, JsonWriter.Serialize(root)

    Dim entryCount As Long, warnCount As Long
    entryCount = ArrLen(root("entries"))
    warnCount = ArrLen(root("warnings"))
    MsgBox "已生成：" & vbCrLf & outPath & vbCrLf & vbCrLf & _
           "共 " & entryCount & " 个编号，" & warnCount & " 条警告。", vbInformation
End Sub

Private Function ArrLen(ByVal arr As Variant) As Long
    On Error GoTo zero
    ArrLen = UBound(arr) - LBound(arr) + 1
    Exit Function
zero:
    ArrLen = 0
End Function

' 根据 DWG 文件自动命名 JSON 输出路径。
' 逻辑：
'   1. 扫描 .docx 同目录下的 .dwg 文件
'   2. 只有 1 个 → 直接用它的文件名
'   3. 有多个 → 弹出 InputBox 让用户选择编号
'   4. 没有 → 回退到 .docx 文件名
Private Function SuggestOutputPath(ByVal doc As Document) As String
    Dim p As String: p = doc.Path
    If p = "" Then
        ' 文档未保存，回退到桌面
        SuggestOutputPath = Environ$("USERPROFILE") & "\Desktop\" & BaseName(doc.Name) & ".dict.json"
        Exit Function
    End If

    ' 扫描同目录下的 .dwg 文件
    Dim dwgFile As String, dwgCount As Long, dwgNames As String
    Dim dwgBase As String
    dwgFile = Dir$(p & "\*.dwg")
    dwgCount = 0
    dwgNames = ""

    Do While dwgFile <> ""
        dwgCount = dwgCount + 1
        If dwgCount <= 20 Then
            dwgNames = dwgNames & dwgCount & ". " & dwgFile & vbCrLf
        End If
        dwgFile = Dir$
    Loop

    If dwgCount = 0 Then
        ' 没有 DWG，用文档名
        SuggestOutputPath = p & "\" & BaseName(doc.Name) & ".dict.json"
    ElseIf dwgCount = 1 Then
        ' 只有一个 DWG，直接用它
        dwgFile = Dir$(p & "\*.dwg")
        SuggestOutputPath = p & "\" & BaseName(dwgFile) & ".dict.json"
    Else
        ' 多个 DWG，让用户选
        Dim choice As String
        choice = InputBox( _
            "同目录下发现 " & dwgCount & " 个 DWG 文件：" & vbCrLf & vbCrLf & _
            dwgNames & vbCrLf & _
            "输入序号选择（1-" & dwgCount & "），留空则用文档名。", _
            "选择 DWG 文件", "1")
        If choice = "" Then
            SuggestOutputPath = p & "\" & BaseName(doc.Name) & ".dict.json"
        ElseIf IsNumeric(choice) Then
            Dim idx As Long: idx = CLng(choice)
            If idx >= 1 And idx <= dwgCount Then
                dwgFile = Dir$(p & "\*.dwg")
                Dim i As Long
                For i = 1 To idx - 1
                    dwgFile = Dir$
                Next
                SuggestOutputPath = p & "\" & BaseName(dwgFile) & ".dict.json"
            Else
                SuggestOutputPath = p & "\" & BaseName(doc.Name) & ".dict.json"
            End If
        Else
            SuggestOutputPath = p & "\" & BaseName(doc.Name) & ".dict.json"
        End If
    End If
End Function

Private Function BaseName(ByVal fname As String) As String
    Dim dotPos As Long: dotPos = InStrRev(fname, ".")
    If dotPos > 0 Then BaseName = Left$(fname, dotPos - 1) Else BaseName = fname
End Function

Private Function FormatISO8601(ByVal d As Date) As String
    FormatISO8601 = Format$(d, "yyyy-mm-dd") & "T" & Format$(d, "hh:nn:ss")
End Function

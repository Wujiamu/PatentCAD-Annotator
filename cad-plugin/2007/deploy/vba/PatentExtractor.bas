Attribute VB_Name = "PatentExtractor"
Option Explicit

Public Sub ExtractDict()
    If Documents.Count = 0 Then
        MsgBox ChrW(&H8BF7) & ChrW(&H5148) & ChrW(&H6253) & ChrW(&H5F00) & ChrW(&H8BF4) & ChrW(&H660E) & ChrW(&H4E66) & ChrW(&H6587) & ChrW(&H6863) & ChrW(&HFF01), vbExclamation
        Exit Sub
    End If

    Dim doc As Document
    Set doc = ActiveDocument

    Dim root As Object
    Set root = DictModel.BuildModel(doc.Content.Text, doc.Name, FormatISO8601(Now))

    Dim outPath As String
    outPath = SuggestOutputPath(doc)
    outPath = InputBox("dict.json " & ChrW(&H8F93) & ChrW(&H51FA) & ChrW(&H8DEF) & ChrW(&H5F84) & ChrW(&HFF1A), ChrW(&H5BFC) & ChrW(&H51FA) & ChrW(&H5B57) & ChrW(&H5178), outPath)
    If outPath = "" Then Exit Sub

    JsonWriter.WriteToFile outPath, JsonWriter.Serialize(root)

    Dim entryCount As Long, warnCount As Long
    entryCount = ArrLen(root("entries"))
    warnCount = ArrLen(root("warnings"))
    MsgBox ChrW(&H5BFC) & ChrW(&H51FA) & ChrW(&H5B8C) & ChrW(&H6210) & ChrW(&HFF01) & vbCrLf & outPath & vbCrLf & vbCrLf & _
           ChrW(&H5171) & " " & entryCount & " " & ChrW(&H4E2A) & ChrW(&H6807) & ChrW(&H53F7) & ChrW(&HFF0C) & warnCount & " " & ChrW(&H6761) & ChrW(&H8B66) & ChrW(&H544A) & ChrW(&H3002), vbInformation
End Sub

Private Function ArrLen(ByVal arr As Variant) As Long
    On Error GoTo zero
    ArrLen = UBound(arr) - LBound(arr) + 1
    Exit Function
zero:
    ArrLen = 0
End Function

Private Function SuggestOutputPath(ByVal doc As Document) As String
    Dim p As String: p = doc.Path
    If p = "" Then
        SuggestOutputPath = Environ$("USERPROFILE") & "\Desktop\" & BaseName(doc.Name) & ".dict.json"
        Exit Function
    End If

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
        SuggestOutputPath = p & "\" & BaseName(doc.Name) & ".dict.json"
    ElseIf dwgCount = 1 Then
        dwgFile = Dir$(p & "\*.dwg")
        SuggestOutputPath = p & "\" & BaseName(dwgFile) & ".dict.json"
    Else
        Dim choice As String
        choice = InputBox( _
            ChrW(&H540C) & ChrW(&H76EE) & ChrW(&H5F55) & ChrW(&H4E0B) & ChrW(&H53D1) & ChrW(&H73B0) & " " & dwgCount & " " & ChrW(&H4E2A) & " DWG " & ChrW(&H6587) & ChrW(&H4EF6) & ChrW(&HFF1A) & vbCrLf & vbCrLf & _
            dwgNames & vbCrLf & _
            ChrW(&H8BF7) & ChrW(&H8F93) & ChrW(&H5165) & ChrW(&H5E8F) & ChrW(&H53F7) & ChrW(&HFF08) & "1-" & dwgCount & ChrW(&HFF09) & ChrW(&HFF0C) & ChrW(&H6216) & ChrW(&H7559) & ChrW(&H7A7A) & ChrW(&H4F7F) & ChrW(&H7528) & ChrW(&H6587) & ChrW(&H6863) & ChrW(&H540D) & ChrW(&H3002), _
            ChrW(&H9009) & ChrW(&H62E9) & " DWG " & ChrW(&H6587) & ChrW(&H4EF6), "1")
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

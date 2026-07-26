Attribute VB_Name = "JsonWriter"
Option Explicit

Public Function Serialize(ByVal v As Variant, Optional ByVal indent As Long = 0) As String
    Dim pad As String
    pad = String$(indent * 2, " ")

    Select Case True
        Case IsObject(v)
            If v Is Nothing Then
                Serialize = "null"
            ElseIf TypeName(v) = "Dictionary" Then
                Serialize = SerializeDict(v, indent)
            Else
                Err.Raise vbObjectError + 1, , "Unsupported object: " & TypeName(v)
            End If
        Case IsArray(v)
            Serialize = SerializeArray(v, indent)
        Case VarType(v) = vbString
            Serialize = EscapeString(CStr(v))
        Case VarType(v) = vbBoolean
            Serialize = IIf(v, "true", "false")
        Case IsNumeric(v)
            Serialize = CStr(v)
        Case IsNull(v) Or IsEmpty(v)
            Serialize = "null"
        Case Else
            Serialize = EscapeString(CStr(v))
    End Select
End Function

Private Function SerializeDict(ByVal d As Object, ByVal indent As Long) As String
    If d.Count = 0 Then
        SerializeDict = "{}"
        Exit Function
    End If

    Dim pad As String, innerPad As String
    pad = String$(indent * 2, " ")
    innerPad = String$((indent + 1) * 2, " ")

    Dim parts() As String
    ReDim parts(0 To d.Count - 1)

    Dim i As Long, k As Variant
    i = 0
    For Each k In d.Keys
        parts(i) = innerPad & EscapeString(CStr(k)) & ": " & Serialize(d(k), indent + 1)
        i = i + 1
    Next

    SerializeDict = "{" & vbCrLf & Join(parts, "," & vbCrLf) & vbCrLf & pad & "}"
End Function

Private Function SerializeArray(ByVal arr As Variant, ByVal indent As Long) As String
    Dim n As Long
    On Error Resume Next
    n = UBound(arr) - LBound(arr) + 1
    On Error GoTo 0

    If n <= 0 Then
        SerializeArray = "[]"
        Exit Function
    End If

    Dim pad As String, innerPad As String
    pad = String$(indent * 2, " ")
    innerPad = String$((indent + 1) * 2, " ")

    Dim parts() As String
    ReDim parts(0 To n - 1)

    Dim i As Long
    For i = 0 To n - 1
        parts(i) = innerPad & Serialize(arr(LBound(arr) + i), indent + 1)
    Next

    SerializeArray = "[" & vbCrLf & Join(parts, "," & vbCrLf) & vbCrLf & pad & "]"
End Function

Private Function EscapeString(ByVal s As String) As String
    Dim result As String
    result = s
    result = Replace(result, "\", "\\")
    result = Replace(result, """", "\""")
    result = Replace(result, vbCrLf, "\n")
    result = Replace(result, vbLf, "\n")
    result = Replace(result, vbCr, "\n")
    result = Replace(result, vbTab, "\t")
    EscapeString = """" & result & """"
End Function

Public Sub WriteToFile(ByVal path As String, ByVal content As String)
    Dim stream As Object
    Set stream = CreateObject("ADODB.Stream")
    stream.Type = 2
    stream.Charset = "utf-8"
    stream.Open
    stream.WriteText content
    stream.Position = 0
    stream.Type = 1
    stream.Position = 3
    Dim bytes() As Byte
    bytes = stream.Read
    stream.Close

    Dim outStream As Object
    Set outStream = CreateObject("ADODB.Stream")
    outStream.Type = 1
    outStream.Open
    outStream.Write bytes
    outStream.SaveToFile path, 2
    outStream.Close
End Sub

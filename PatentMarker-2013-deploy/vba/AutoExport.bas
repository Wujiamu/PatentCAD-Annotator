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

Private Function GetOutputPath(ByVal doc As Document) As String
    Dim dir As String
    dir = GetOutputDir(doc)
    If dir = "" Then
        GetOutputPath = ""
        Exit Function
    End If

    Dim baseName As String
    baseName = doc.Name
    Dim dotPos As Long
    dotPos = InStrRev(baseName, ".")
    If dotPos > 0 Then baseName = Left(baseName, dotPos - 1)

    GetOutputPath = dir & "\" & baseName & ".dict.json"
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

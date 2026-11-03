Option Explicit
Dim fso, shell, word, doc, vbdir, artifact, exe, path, file, module, code, proc, ready, release, i, result, failures, before, attrs
Set fso = CreateObject("Scripting.FileSystemObject"): Set shell = CreateObject("WScript.Shell")
If WScript.Arguments.Count <> 3 Then WScript.Quit 2
vbdir = fso.GetAbsolutePathName(WScript.Arguments(0))
artifact = fso.GetAbsolutePathName(WScript.Arguments(1))
exe = fso.GetAbsolutePathName(WScript.Arguments(2))
If fso.FolderExists(artifact) Or Not fso.FileExists(exe) Then WScript.Quit 2
fso.CreateFolder artifact
path = fso.BuildPath(artifact, "word.dict.json"): ready = fso.BuildPath(artifact, "ready"): release = fso.BuildPath(artifact, "release")
failures = 0
On Error Resume Next
Set word = CreateObject("Word.Application")
If Err.Number <> 0 Then WScript.Echo "BLOCKED|WORD_CREATE|" & Err.Description: WScript.Quit 2
word.Visible = False: word.DisplayAlerts = 0
Set doc = word.Documents.Add: doc.Content.Text = "synthetic"
doc.SaveAs2 fso.BuildPath(artifact, "word.docm"), 13
For Each file In Array("Patterns.bas", "DictModel.bas", "JsonWriter.bas", "PatentExtractor.bas", "clsSaveHook.cls", "AutoExport.bas", "PatentMarkerBootstrap.bas", "PatentDictPanel.frm")
    doc.VBProject.VBComponents.Import fso.BuildPath(vbdir, file)
    If Err.Number <> 0 Then Exit For
Next
If Err.Number <> 0 Then
    WScript.Echo "FAIL|IMPORT|" & Err.Description: failures = failures + 1: Err.Clear
Else
    Set module = doc.VBProject.VBComponents("AutoExport").CodeModule
    code = "Public Function G05TryLock(ByVal path As String) As Boolean" & vbCrLf & _
        "On Error GoTo failed" & vbCrLf & _
        "Dim n As Integer: n = FreeFile" & vbCrLf & _
        "Open path & "".patlock"" For Binary Access Read Write Lock Read Write As #n" & vbCrLf & _
        "Close #n: G05TryLock = True" & vbCrLf & _
        "failed:" & vbCrLf & _
        "End Function" & vbCrLf & _
        "Public Function G05HoldAndProbe(ByVal path As String, ByVal exe As String) As Long" & vbCrLf & _
        "On Error GoTo failed" & vbCrLf & _
        "Dim n As Integer, command As String: n = FreeFile" & vbCrLf & _
        "Open path & "".patlock"" For Binary Access Read Write Lock Read Write As #n" & vbCrLf & _
        "command = Chr$(34) & exe & Chr$(34) & "" probe-busy "" & Chr$(34) & path & "".patlock"" & Chr$(34)" & vbCrLf & _
        "G05HoldAndProbe = CreateObject(""WScript.Shell"").Run(command, 0, True)" & vbCrLf & _
        "Close #n: Exit Function" & vbCrLf & _
        "failed: G05HoldAndProbe = 9" & vbCrLf & _
        "End Function"
    module.AddFromString code
    doc.Activate
    result = word.Run("AutoExport.ExportDict")
    Check result And fso.FileExists(path), "INITIAL_EXPORT"
    before = ReadUtf8(path): attrs = fso.GetFile(path).Attributes
    Set proc = shell.Exec("""" & exe & """ hold """ & path & ".patlock"" """ & ready & """ """ & release & """")
    For i = 1 To 150
        If fso.FileExists(ready) Or proc.Status <> 0 Then Exit For
        WScript.Sleep 100
    Next
    Check fso.FileExists(ready), "DOTNET_HOLD_READY"
    If fso.FileExists(ready) Then
        result = word.Run("AutoExport.G05TryLock", path)
        Check Not result, "DOTNET_HOLD_BLOCKS_VBA_LOCK"
        result = word.Run("AutoExport.SetCurrentDictVisibility", True, doc)
        Check Not result, "DOTNET_HOLD_BLOCKS_WORD_VISIBILITY"
        Check fso.GetFile(path).Attributes = attrs, "VISIBILITY_ATTRIBUTES_RETAINED"
        doc.Content.Text = "changed synthetic"
        result = word.Run("AutoExport.ExportDict")
        Check Not result, "DOTNET_HOLD_BLOCKS_WORD_EXPORT"
        Check ReadUtf8(path) = before, "DOTNET_HOLD_RETAINS_BYTES"
    End If
    Set file = fso.CreateTextFile(release, True, False): file.Write "release": file.Close
    For i = 1 To 150
        If proc.Status <> 0 Then Exit For
        WScript.Sleep 100
    Next
    Check proc.Status <> 0 And proc.ExitCode = 0, "DOTNET_HOLDER_RELEASED"
    result = word.Run("AutoExport.ExportDict")
    Check result, "WORD_EXPORT_AFTER_RELEASE"
    result = word.Run("AutoExport.G05HoldAndProbe", path, exe)
    Check result = 0, "WORD_HOLD_BLOCKS_DOTNET"
    result = word.Run("AutoExport.G05TryLock", path)
    Check result, "LOCK_REUSABLE"
End If
If Err.Number <> 0 Then WScript.Echo "FAIL|EXCEPTION|" & Err.Number & "|" & Err.Description: failures = failures + 1: Err.Clear
doc.Close False: word.Quit False
Set doc = Nothing: Set word = Nothing
On Error GoTo 0
WScript.Echo "SUMMARY|WORD_DOTNET_LOCK|FAILURES=" & failures
If failures <> 0 Then WScript.Quit 1
WScript.Quit 0

Sub Check(ByVal ok, ByVal label)
    If ok Then WScript.Echo "PASS|" & label Else WScript.Echo "FAIL|" & label: failures = failures + 1
End Sub

Function ReadUtf8(ByVal path)
    Dim s: Set s = CreateObject("ADODB.Stream")
    s.Type = 2: s.Charset = "utf-8": s.Open: s.LoadFromFile path
    ReadUtf8 = s.ReadText(-1): s.Close
End Function

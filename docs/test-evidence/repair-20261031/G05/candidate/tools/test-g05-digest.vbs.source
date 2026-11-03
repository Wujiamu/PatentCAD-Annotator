Option Explicit
Dim fso, word, doc, vbdir, artifact, component, modl, line, i, opened, actual, inputs, expected, failures
Set fso = CreateObject("Scripting.FileSystemObject")
If WScript.Arguments.Count <> 2 Then WScript.Quit 2
vbdir = fso.GetAbsolutePathName(WScript.Arguments(0))
artifact = fso.GetAbsolutePathName(WScript.Arguments(1))
If fso.FolderExists(artifact) Then WScript.Quit 2
fso.CreateFolder artifact
failures = 0
On Error Resume Next
WScript.Echo "INFO|CREATE_WORD"
Set word = CreateObject("Word.Application")
If Err.Number <> 0 Then WScript.Echo "BLOCKED|WORD_CREATE|" & Err.Description: WScript.Quit 2
word.Visible = False: word.DisplayAlerts = 0
WScript.Echo "INFO|DOCUMENT"
Set doc = word.Documents.Add
doc.SaveAs2 fso.BuildPath(artifact, "digest.docm"), 13
WScript.Echo "INFO|IMPORT_START"
For Each component In Array("Patterns.bas", "DictModel.bas", "JsonWriter.bas", "PatentExtractor.bas", "clsSaveHook.cls", "AutoExport.bas", "PatentMarkerBootstrap.bas", "PatentDictPanel.frm")
    doc.VBProject.VBComponents.Import fso.BuildPath(vbdir, component)
    If Err.Number <> 0 Then Exit For
Next
If Err.Number <> 0 Then
    WScript.Echo "FAIL|IMPORT|" & Err.Number & "|" & Err.Description
    failures = failures + 1: Err.Clear
Else
    WScript.Echo "INFO|IMPORT_END"
    Set modl = doc.VBProject.VBComponents("AutoExport").CodeModule
    opened = False
    For i = 1 To modl.CountOfLines
        line = modl.Lines(i, 1)
        If Left(Trim(line), 31) = "Private Function BaselineDigest" Then
            modl.ReplaceLine i, Replace(line, "Private Function", "Public Function")
            opened = True: Exit For
        End If
    Next
    If Not opened Then
        WScript.Echo "FAIL|INSTRUMENTATION": failures = failures + 1
    Else
        inputs = Array("", "abc", ChrW(&H4E2D) & ChrW(&H6587) & ChrW(&HD83D) & ChrW(&HDE00), _
            "  {""extracted_at"": ""2026-11-01T01:23:45""," & vbCrLf & """x"":1}" & vbCr & " ", _
            vbTab & "A" & vbCr & "B" & vbLf & "C" & vbCrLf & "D  ", " {""extracted_at"": """", ""v"":1} ")
        expected = Array("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", _
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", _
            "e973a1c1b41c5c9f4fbac31fcc311536dfffd003bfb914580455170a599953fa", _
            "249ebcad47d760b52ce4776dc3bb173f239215bca242f243148ff3f6255f183a", _
            "433c31786f7176afd5fd19eb3e5cb2536657627b0999b2b52abe93a17399ff00", _
            "a9a00cd774aa6ae76aa74f607dd0657a01ff6002bc92349170bfa92ca3366aa5")
        modl.AddFromString "Public Function G05DigestProbe(ByVal value As String) As String" & vbCrLf & _
            "On Error GoTo failed" & vbCrLf & _
            "G05DigestProbe = BaselineDigest(value)" & vbCrLf & _
            "Exit Function" & vbCrLf & _
            "failed: G05DigestProbe = ""ERROR|"" & Err.Number & ""|"" & Err.Description" & vbCrLf & _
            "End Function"
        doc.Activate
        For i = 0 To UBound(inputs)
            WScript.Echo "INFO|DIGEST_START|" & i
            Err.Clear: actual = word.Run("AutoExport.G05DigestProbe", inputs(i))
            If Err.Number = 0 And actual = expected(i) Then
                WScript.Echo "PASS|DIGEST|" & i & "|" & actual
            Else
                WScript.Echo "FAIL|DIGEST|" & i & "|" & actual & "|" & Err.Number & "|" & Err.Description
                failures = failures + 1
            End If
        Next
    End If
End If
Err.Clear
doc.Close False: word.Quit False
Set doc = Nothing: Set word = Nothing
On Error GoTo 0
WScript.Echo "SUMMARY|L2_DIGEST|FAILURES=" & failures
If failures <> 0 Then WScript.Quit 1
WScript.Quit 0

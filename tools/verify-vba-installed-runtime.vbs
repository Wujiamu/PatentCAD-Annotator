' L4 runtime assertion for an already installed PatentMarker.dotm.
' It does not call any initialization or test-harness macro.
' Usage: cscript //nologo verify-vba-installed-runtime.vbs <expected-addin> <test-directory> <winword-exe>
Option Explicit

Const ForReading = 1

Dim fso, shell, word, doc, expectedAddin, testDir, wordExe, docPath, dictPath
Dim evidence, runId, logPath, wordVersion
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
Set word = Nothing
Set doc = Nothing

If WScript.Arguments.Count <> 3 Then Fail "usage: expected-addin test-directory winword-exe"
expectedAddin = fso.GetAbsolutePathName(WScript.Arguments(0))
testDir = fso.GetAbsolutePathName(WScript.Arguments(1))
wordExe = fso.GetAbsolutePathName(WScript.Arguments(2))
If Not fso.FileExists(expectedAddin) Then Fail "expected add-in is missing: " & expectedAddin
If Not fso.FolderExists(testDir) Then Fail "test directory is missing: " & testDir
If Not fso.FileExists(wordExe) Then Fail "WINWORD.EXE is missing: " & wordExe
docPath = fso.BuildPath(testDir, "fresh-word-save.docx")
dictPath = fso.BuildPath(testDir, "fresh-word-save.dict.json")

Dim failure, count
count = WordProcessCount(failure)
If count < 0 Then Fail "cannot inspect Word processes: " & failure
If count > 0 Then Fail "Word is already running before runtime assertion"

On Error Resume Next
Err.Clear
shell.Run QuoteArgument(wordExe) & " /q /n", 0, False
If Err.Number <> 0 Then CaptureAndFail "cannot launch WINWORD.EXE normally"
Dim attachAttempt
For attachAttempt = 1 To 40
    Err.Clear
    Set word = GetObject(, "Word.Application")
    If Err.Number = 0 Then
        If Not word Is Nothing Then Exit For
    End If
    Set word = Nothing
    WScript.Sleep 500
Next
If word Is Nothing Then CaptureAndFail "cannot attach to the normally launched Word instance"
word.Visible = False
word.DisplayAlerts = 0
wordVersion = word.Version

Dim loaded, template, templateAttempt, loadedTemplatePaths, templateEnumerationError
loaded = False
loadedTemplatePaths = ""
templateEnumerationError = ""
For templateAttempt = 1 To 40
    loaded = False
    loadedTemplatePaths = ""
    Err.Clear
    For Each template In word.Templates
        loadedTemplatePaths = loadedTemplatePaths & "[" & template.FullName & "]"
        If StrComp(fso.GetAbsolutePathName(template.FullName), expectedAddin, 1) = 0 Then loaded = True
    Next
    If Err.Number <> 0 Then
        templateEnumerationError = CStr(Err.Number) & ": " & Err.Description
        Err.Clear
    Else
        templateEnumerationError = ""
    End If
    If loaded Then Exit For
    WScript.Sleep 500
Next
If Not loaded Then
    If templateEnumerationError <> "" Then
        Fail "cannot enumerate loaded Word templates: " & templateEnumerationError
    Else
        Fail "fresh Word did not load the expected Startup add-in; loaded=" & loadedTemplatePaths
    End If
End If

Dim evidenceAttempt, evidenceError
evidence = ""
evidenceError = ""
For evidenceAttempt = 1 To 40
    Err.Clear
    evidence = CStr(word.Run("AutoExport.GetRuntimeEvidenceForDiagnostics"))
    If Err.Number = 0 Then
        evidenceError = ""
        If FieldValue(evidence, "hook") = "ENABLED" Then Exit For
    Else
        evidenceError = CStr(Err.Number) & ": " & Err.Description
        Err.Clear
    End If
    WScript.Sleep 500
Next
If FieldValue(evidence, "hook") <> "ENABLED" Then
    If evidenceError <> "" Then
        Fail "runtime evidence macro failed: " & evidenceError
    Else
        Fail "AutoExec did not enable the save hook: " & evidence
    End If
End If
If StrComp(fso.GetAbsolutePathName(FieldValue(evidence, "template")), expectedAddin, 1) <> 0 Then Fail "runtime evidence came from the wrong template: " & evidence
runId = FieldValue(evidence, "run_id")
logPath = FieldValue(evidence, "log_path")
If runId = "" Then Fail "runtime evidence has no run_id"
If logPath = "" Then Fail "runtime evidence has no log_path"

Err.Clear
Set doc = word.Documents.Add
If Err.Number <> 0 Then CaptureAndFail "cannot create validation document"
doc.Content.Text = MarkingText()
doc.SaveAs2 docPath, 16
If Err.Number <> 0 Then CaptureAndFail "cannot establish validation document path"
doc.Content.InsertAfter vbCr & "AUTO-SAVE-E2E"
doc.Save
If Err.Number <> 0 Then CaptureAndFail "ordinary document save failed"
If Not doc.Saved Then Fail "ordinary document save did not complete"
If Not fso.FileExists(dictPath) Then Fail "ordinary save did not create fresh-word-save.dict.json"

Dim jsonText, expectedName1, expectedName2
jsonText = ReadUtf8File(dictPath, failure)
If failure <> "" Then Fail "cannot read exported UTF-8 JSON: " & failure
expectedName1 = ChrW(&H5E95) & ChrW(&H5EA7)
expectedName2 = ChrW(&H652F) & ChrW(&H67B6)
If InStr(1, jsonText, Chr(34) & "number" & Chr(34) & ": " & Chr(34) & "1" & Chr(34), 0) = 0 Then Fail "JSON lacks number 1"
If InStr(1, jsonText, Chr(34) & "name" & Chr(34) & ": " & Chr(34) & expectedName1 & Chr(34), 0) = 0 Then Fail "JSON lacks expected name for number 1"
If InStr(1, jsonText, Chr(34) & "number" & Chr(34) & ": " & Chr(34) & "2" & Chr(34), 0) = 0 Then Fail "JSON lacks number 2"
If InStr(1, jsonText, Chr(34) & "name" & Chr(34) & ": " & Chr(34) & expectedName2 & Chr(34), 0) = 0 Then Fail "JSON lacks expected name for number 2"

If Not fso.FileExists(logPath) Then Fail "diagnostic log is missing: " & logPath
Dim logFile, logText, logTextAfterShutdown
Set logFile = fso.OpenTextFile(logPath, ForReading, False, -1)
logText = logFile.ReadAll
logFile.Close
If Err.Number <> 0 Then CaptureAndFail "cannot read diagnostic log"
If Not HasRunStage(logText, runId, "hook.initialize", "PASS", "origin=AutoExec") Then Fail "run log lacks successful AutoExec initialization"
If Not HasRunStage(logText, runId, "save.before.enter", "INFO", "") Then Fail "run log lacks DocumentBeforeSave entry"
If Not HasRunStage(logText, runId, "export.success", "PASS", "") Then Fail "run log lacks export success"
If Not HasRunStage(logText, runId, "save.before.exit", "INFO", "export_ok=true") Then Fail "run log lacks successful save-event exit"

doc.Close False
Set doc = Nothing
word.Quit False
Set word = Nothing
On Error GoTo 0
If Not WaitForNoWord(20) Then Fail "owned fresh Word process did not exit within 20 seconds"
logTextAfterShutdown = ReadUnicodeFile(logPath, failure)
If failure <> "" Then Fail "cannot read diagnostic log after Word shutdown: " & failure
AssertShutdownLogRunId logText, logTextAfterShutdown, runId

WScript.Echo "WORD_VERSION|" & wordVersion
WScript.Echo "RUN_ID|" & runId
WScript.Echo "LOG_PATH|" & logPath
WScript.Echo "RUNTIME_EVIDENCE|" & evidence
WScript.Echo "PASS|L4_RUNTIME|startup_loaded=true|autoexec=true|ordinary_save_export=true"
WScript.Quit 0

Function MarkingText()
    MarkingText = ChrW(&H9644) & ChrW(&H56FE) & ChrW(&H6807) & ChrW(&H8BB0) & _
        ChrW(&H8BF4) & ChrW(&H660E) & ChrW(&HFF1A) & "1" & ChrW(&H5E95) & _
        ChrW(&H5EA7) & ChrW(&HFF0C) & "2" & ChrW(&H652F) & ChrW(&H67B6) & ChrW(&HFF1B)
End Function

Function QuoteArgument(ByVal value)
    QuoteArgument = Chr(34) & Replace(CStr(value), Chr(34), Chr(34) & Chr(34)) & Chr(34)
End Function

Function FieldValue(ByVal value, ByVal fieldName)
    Dim part, prefix
    prefix = fieldName & "="
    FieldValue = ""
    For Each part In Split(value, ";")
        If Left(part, Len(prefix)) = prefix Then
            FieldValue = Mid(part, Len(prefix) + 1)
            Exit Function
        End If
    Next
End Function

Function HasRunStage(ByVal content, ByVal targetRunId, ByVal stage, ByVal result, ByVal detail)
    Dim line
    HasRunStage = False
    For Each line In Split(Replace(content, vbCrLf, vbLf), vbLf)
        If InStr(1, line, vbTab & targetRunId & vbTab, 0) > 0 And _
                InStr(1, line, vbTab & stage & vbTab & result & vbTab, 0) > 0 Then
            If detail = "" Or InStr(1, line, detail, 0) > 0 Then
                HasRunStage = True
                Exit Function
            End If
        End If
    Next
End Function

Function ReadUtf8File(ByVal path, ByRef errorText)
    On Error Resume Next
    Dim stream, errNo, errDesc
    Set stream = CreateObject("ADODB.Stream")
    stream.Type = 2
    stream.Charset = "utf-8"
    stream.Open
    stream.LoadFromFile path
    ReadUtf8File = stream.ReadText(-1)
    stream.Close
    errNo = Err.Number
    errDesc = Err.Description
    On Error GoTo 0
    If errNo <> 0 Then
        errorText = CStr(errNo) & ": " & errDesc
        ReadUtf8File = ""
    Else
        errorText = ""
    End If
End Function

Function ReadUnicodeFile(ByVal path, ByRef errorText)
    On Error Resume Next
    Dim file, errNo, errDesc
    Set file = fso.OpenTextFile(path, ForReading, False, -1)
    ReadUnicodeFile = file.ReadAll
    file.Close
    errNo = Err.Number
    errDesc = Err.Description
    On Error GoTo 0
    If errNo <> 0 Then
        errorText = CStr(errNo) & ": " & errDesc
        ReadUnicodeFile = ""
    Else
        errorText = ""
    End If
End Function

Sub AssertShutdownLogRunId(ByVal beforeText, ByVal afterText, ByVal expectedRunId)
    Dim appendedText, line
    If Len(afterText) < Len(beforeText) Or Left(afterText, Len(beforeText)) <> beforeText Then _
        Fail "diagnostic log was rewritten during Word shutdown"
    appendedText = Mid(afterText, Len(beforeText) + 1)
    For Each line In Split(Replace(appendedText, vbCrLf, vbLf), vbLf)
        If Trim(line) <> "" And InStr(1, line, vbTab & expectedRunId & vbTab, 0) = 0 Then _
            Fail "Word shutdown created a foreign diagnostic run ID: " & line
    Next
End Sub

Function WordProcessCount(ByRef errorText)
    On Error Resume Next
    Dim svc, processes, errNo, errDesc
    Set svc = Nothing
    Set processes = Nothing
    errorText = ""
    Set svc = GetObject("winmgmts:\\.\root\cimv2")
    If Err.Number = 0 Then Set processes = svc.ExecQuery("SELECT ProcessId FROM Win32_Process WHERE Name='WINWORD.EXE'")
    errNo = Err.Number
    errDesc = Err.Description
    On Error GoTo 0
    If errNo <> 0 Or processes Is Nothing Then
        errorText = CStr(errNo) & ": " & errDesc
        WordProcessCount = -1
    Else
        WordProcessCount = processes.Count
    End If
End Function

Function WaitForNoWord(ByVal seconds)
    Dim attempt, processCount, checkFailure
    WaitForNoWord = False
    For attempt = 1 To seconds * 2
        processCount = WordProcessCount(checkFailure)
        If processCount = 0 Then
            WaitForNoWord = True
            Exit Function
        End If
        If processCount < 0 Then Exit Function
        WScript.Sleep 500
    Next
End Function

Sub CaptureAndFail(ByVal prefix)
    Dim number, description
    number = Err.Number
    description = Err.Description
    On Error Resume Next
    If IsObject(doc) Then doc.Close False
    Set doc = Nothing
    If IsObject(word) Then word.Quit False
    Set word = Nothing
    On Error GoTo 0
    Fail prefix & " (" & CStr(number) & "): " & description
End Sub

Sub Fail(ByVal message)
    On Error Resume Next
    If IsObject(doc) Then doc.Close False
    Set doc = Nothing
    If IsObject(word) Then word.Quit False
    Set word = Nothing
    On Error GoTo 0
    WScript.Echo "FAIL|L4_RUNTIME|" & message
    WScript.Quit 1
End Sub

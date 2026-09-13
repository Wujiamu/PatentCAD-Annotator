' Extended installed-host acceptance matrix for PatentMarker.dotm.
' The path starts WINWORD.EXE normally and never calls the initializer or
' export routines. Failure-path coverage comes from real file locks and
' attributes instead of mutating the add-in's private event-hook state.
' Usage: cscript //nologo verify-vba-installed-matrix.vbs <expected-addin> <test-directory> <winword-exe> <installer>
Option Explicit

Const ForReading = 1

Dim fso, shell, word, docA, docB, docRename, docLock, docReadOnly
Dim expectedAddin, testDir, wordExe, installerPath, uninstallerPath
Dim runId, logPath, wordVersion, saveAsImmediate, logTextBeforeShutdown, logTextAfterShutdown
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
Set word = Nothing
Set docA = Nothing
Set docB = Nothing
Set docRename = Nothing
Set docLock = Nothing
Set docReadOnly = Nothing

If WScript.Arguments.Count <> 4 Then Fail "usage: expected-addin test-directory winword-exe installer"
expectedAddin = fso.GetAbsolutePathName(WScript.Arguments(0))
testDir = fso.GetAbsolutePathName(WScript.Arguments(1))
wordExe = fso.GetAbsolutePathName(WScript.Arguments(2))
installerPath = fso.GetAbsolutePathName(WScript.Arguments(3))
uninstallerPath = fso.BuildPath(fso.GetParentFolderName(installerPath), "uninstall-vba.vbs")
If Not fso.FileExists(expectedAddin) Then Fail "expected add-in is missing: " & expectedAddin
If Not fso.FolderExists(testDir) Then Fail "test directory is missing: " & testDir
If Not fso.FileExists(wordExe) Then Fail "WINWORD.EXE is missing: " & wordExe
If Not fso.FileExists(installerPath) Then Fail "installer is missing: " & installerPath
If Not fso.FileExists(uninstallerPath) Then Fail "uninstaller is missing: " & uninstallerPath

Dim failure, processCount
processCount = WordProcessCount(failure)
If processCount < 0 Then Fail "cannot inspect Word processes: " & failure
If processCount > 0 Then Fail "Word is already running before extended matrix"

StartWordNormally " /q /n"
WaitForExpectedTemplate
Dim evidence
evidence = WaitForHookEvidence(True)
runId = FieldValue(evidence, "run_id")
logPath = FieldValue(evidence, "log_path")
wordVersion = word.Version
If runId = "" Then Fail "positive run has no run_id"
If logPath = "" Then Fail "positive run has no log_path"

TestMultiDocumentIsolation
saveAsImmediate = TestSaveAsRetarget
TestLockedTargetFailure
TestReadOnlyTargetFailure
TestInstallerRefusesRunningWord
TestUninstallerRefusesRunningWord

CloseOwnedDocuments
logTextBeforeShutdown = ReadUnicodeFile(logPath, failure)
If failure <> "" Then Fail "cannot read diagnostic log before Word shutdown: " & failure
word.Quit False
Set word = Nothing
If Not WaitForNoWord(20) Then Fail "positive matrix Word process did not exit"
logTextAfterShutdown = ReadUnicodeFile(logPath, failure)
If failure <> "" Then Fail "cannot read diagnostic log after Word shutdown: " & failure
AssertShutdownLogRunId logTextBeforeShutdown, logTextAfterShutdown, runId

WScript.Echo "WORD_VERSION|" & wordVersion
WScript.Echo "RUN_ID|" & runId
WScript.Echo "LOG_PATH|" & logPath
WScript.Echo "SAVE_AS_IMMEDIATE_EXPORT|" & BoolText(saveAsImmediate)
WScript.Echo "PASS|L4_EXTENDED_MATRIX|multi_document_isolation=true|save_as_followup_export=true|locked_target_cancel=true|locked_target_recovery=true|readonly_target_cancel=true|readonly_target_recovery=true|installer_running_word_rejected=true|uninstaller_running_word_rejected=true"
WScript.Quit 0

Sub TestMultiDocumentIsolation()
    Dim dirA, dirB, pathA, pathB, dictA, dictB, bBefore, bAfter
    dirA = fso.BuildPath(testDir, "multi-a")
    dirB = fso.BuildPath(testDir, "multi-b")
    EnsureFolder dirA
    EnsureFolder dirB
    pathA = fso.BuildPath(dirA, "document-a.docx")
    pathB = fso.BuildPath(dirB, "document-b.docx")
    dictA = fso.BuildPath(dirA, "document-a.dict.json")
    dictB = fso.BuildPath(dirB, "document-b.dict.json")

    CreateSavedDocument pathA, MarkingText("1", NameBase()), docA
    AssertJson dictA, "1", NameBase(), "multi-document A initial"
    CreateSavedDocument pathB, MarkingText("2", NameBracket()), docB
    AssertJson dictB, "2", NameBracket(), "multi-document B initial"
    bBefore = ReadUtf8File(dictB, failure)
    If failure <> "" Then Fail "cannot snapshot document B dictionary: " & failure

    ' Keep B active while explicitly saving A. An ActiveDocument-based event
    ' implementation would write the wrong document or target here.
    docB.Activate
    docA.Content.Text = MarkingText("3", NameCover()) & vbCr & "UPDATED-A"
    docA.Save
    If Not docA.Saved Then Fail "document A save was unexpectedly cancelled"
    AssertJson dictA, "3", NameCover(), "multi-document A update"
    AssertJsonLacksNumber dictA, "1", "multi-document A stale entry"
    bAfter = ReadUtf8File(dictB, failure)
    If failure <> "" Then Fail "cannot re-read document B dictionary: " & failure
    If bAfter <> bBefore Then Fail "saving inactive document A changed document B dictionary"

    docA.Close False
    Set docA = Nothing
    docB.Close False
    Set docB = Nothing
End Sub

Function TestSaveAsRetarget()
    Dim oldDir, newDir, oldPath, newPath, oldDict, newDict
    oldDir = fso.BuildPath(testDir, "rename-old")
    newDir = fso.BuildPath(testDir, "rename-new")
    EnsureFolder oldDir
    EnsureFolder newDir
    oldPath = fso.BuildPath(oldDir, "before.docx")
    newPath = fso.BuildPath(newDir, "after.docx")
    oldDict = fso.BuildPath(oldDir, "before.dict.json")
    newDict = fso.BuildPath(newDir, "after.dict.json")

    CreateSavedDocument oldPath, MarkingText("10", NameOld()), docRename
    AssertJson oldDict, "10", NameOld(), "Save As old target initial"
    docRename.Content.Text = MarkingText("11", NameNew()) & vbCr & "SAVE-AS-RETARGET"
    docRename.SaveAs2 newPath, 16
    If StrComp(fso.GetAbsolutePathName(docRename.FullName), fso.GetAbsolutePathName(newPath), 1) <> 0 Then _
        Fail "Save As did not move document to requested path"

    TestSaveAsRetarget = fso.FileExists(newDict)
    If TestSaveAsRetarget Then
        AssertJson newDict, "11", NameNew(), "Save As immediate new target"
    Else
        ' Word exposes only DocumentBeforeSave. The existing path is still the
        ' old one at that point, so the documented recovery is one ordinary
        ' save after Save As establishes the new path.
        docRename.Content.InsertAfter vbCr & "FOLLOW-UP-SAVE"
        docRename.Save
        If Not docRename.Saved Then Fail "ordinary save after Save As was cancelled"
        AssertJson newDict, "11", NameNew(), "Save As follow-up new target"
    End If
    AssertJson oldDict, "11", NameNew(), "Save As old-path pre-save export"

    docRename.Close False
    Set docRename = Nothing
End Function

Sub TestLockedTargetFailure()
    Dim lockDir, lockPath, lockDict, beforeText, afterText, beforeAttributes
    Dim afterAttributes, lockFile, saveErr, saveErrDescription, errorPath
    lockDir = fso.BuildPath(testDir, "locked-target")
    EnsureFolder lockDir
    lockPath = fso.BuildPath(lockDir, "locked.docx")
    lockDict = fso.BuildPath(lockDir, "locked.dict.json")
    errorPath = fso.BuildPath(lockDir, "autoexport-error.txt")

    CreateSavedDocument lockPath, MarkingText("20", NameOld()), docLock
    AssertJson lockDict, "20", NameOld(), "locked target initial"
    beforeText = ReadUtf8File(lockDict, failure)
    If failure <> "" Then Fail "cannot snapshot locked target: " & failure
    beforeAttributes = fso.GetFile(lockDict).Attributes

    On Error Resume Next
    Err.Clear
    Set lockFile = fso.OpenTextFile(lockDict, ForReading, False, 0)
    saveErr = Err.Number
    saveErrDescription = Err.Description
    On Error GoTo 0
    If saveErr <> 0 Or lockFile Is Nothing Then _
        Fail "cannot acquire target lock (" & CStr(saveErr) & "): " & saveErrDescription

    docLock.Content.Text = MarkingText("21", NameNew()) & vbCr & "LOCKED-WRITE"
    On Error Resume Next
    Err.Clear
    docLock.Save
    saveErr = Err.Number
    saveErrDescription = Err.Description
    On Error GoTo 0
    lockFile.Close
    Set lockFile = Nothing

    If docLock.Saved Then Fail "ordinary save succeeded even though dictionary replacement was locked"
    afterText = ReadUtf8File(lockDict, failure)
    If failure <> "" Then Fail "cannot read dictionary after locked failure: " & failure
    If afterText <> beforeText Then Fail "locked export changed the previous valid dictionary"
    afterAttributes = fso.GetFile(lockDict).Attributes
    If afterAttributes <> beforeAttributes Then _
        Fail "locked export changed dictionary attributes from " & CStr(beforeAttributes) & " to " & CStr(afterAttributes)
    If CountTemporaryDictionaryFiles(lockDir) <> 0 Then Fail "locked export left a temporary dictionary file"
    If Not fso.FileExists(errorPath) Then Fail "locked export did not create autoexport-error.txt"

    docLock.Save
    If Not docLock.Saved Then _
        Fail "ordinary save did not recover after releasing target lock; prior error=" & CStr(saveErr) & ": " & saveErrDescription
    AssertJson lockDict, "21", NameNew(), "locked target recovery"
    If fso.GetFile(lockDict).Attributes <> beforeAttributes Then _
        Fail "successful retry did not restore original dictionary attributes"

    Dim logText
    logText = ReadUnicodeFile(logPath, failure)
    If failure <> "" Then Fail "cannot read positive-run log: " & failure
    If Not HasRunStage(logText, runId, "export.failure", "FAIL", "") Then _
        Fail "log lacks export.failure for locked target"
    If Not HasRunStage(logText, runId, "save.before.exit", "INFO", "cancel=true;export_ok=false") Then _
        Fail "log lacks save cancellation for locked target"
    If Not HasRunStage(logText, runId, "export.success", "PASS", "") Then _
        Fail "log lacks successful export after releasing target lock"

    docLock.Close False
    Set docLock = Nothing
End Sub

Sub TestReadOnlyTargetFailure()
    Dim targetDir, docPath, dictPath, errorPath, beforeText, afterText
    Dim normalAttributes, readOnlyAttributes
    targetDir = fso.BuildPath(testDir, "readonly-target")
    EnsureFolder targetDir
    docPath = fso.BuildPath(targetDir, "readonly.docx")
    dictPath = fso.BuildPath(targetDir, "readonly.dict.json")
    errorPath = fso.BuildPath(targetDir, "autoexport-error.txt")

    CreateSavedDocument docPath, MarkingText("40", NameOld()), docReadOnly
    AssertJson dictPath, "40", NameOld(), "read-only target initial"
    beforeText = ReadUtf8File(dictPath, failure)
    If failure <> "" Then Fail "cannot snapshot read-only target: " & failure
    normalAttributes = fso.GetFile(dictPath).Attributes
    readOnlyAttributes = normalAttributes Or 1
    fso.GetFile(dictPath).Attributes = readOnlyAttributes

    docReadOnly.Content.Text = MarkingText("41", NameNew()) & vbCr & "READONLY-WRITE"
    On Error Resume Next
    Err.Clear
    docReadOnly.Save
    Err.Clear
    On Error GoTo 0
    If docReadOnly.Saved Then Fail "ordinary save succeeded against a read-only dictionary"
    afterText = ReadUtf8File(dictPath, failure)
    If failure <> "" Then Fail "cannot read dictionary after read-only failure: " & failure
    If afterText <> beforeText Then Fail "read-only export changed the previous valid dictionary"
    If fso.GetFile(dictPath).Attributes <> readOnlyAttributes Then _
        Fail "read-only export changed dictionary attributes"
    If CountTemporaryDictionaryFiles(targetDir) <> 0 Then Fail "read-only export left a temporary dictionary file"
    If Not fso.FileExists(errorPath) Then Fail "read-only export did not create autoexport-error.txt"

    fso.GetFile(dictPath).Attributes = normalAttributes
    docReadOnly.Save
    If Not docReadOnly.Saved Then Fail "ordinary save did not recover after clearing read-only attribute"
    AssertJson dictPath, "41", NameNew(), "read-only target recovery"
    If fso.GetFile(dictPath).Attributes <> normalAttributes Then _
        Fail "successful read-only retry did not restore original dictionary attributes"

    docReadOnly.Close False
    Set docReadOnly = Nothing
End Sub

Sub TestInstallerRefusesRunningWord()
    Dim installerLog, command, exitCode
    installerLog = fso.BuildPath(testDir, "installer-running-word.log")
    command = QuoteArgument(WScript.FullName) & " //nologo " & QuoteArgument(installerPath) & _
        " /NoPrompt " & QuoteArgument("/LogPath:" & installerLog)
    On Error Resume Next
    Err.Clear
    exitCode = shell.Run(command, 0, True)
    If Err.Number <> 0 Then CaptureAndFail "cannot launch installer running-Word check"
    On Error GoTo 0
    If exitCode = 0 Then Fail "installer succeeded while the tested Word process was running"
    If Not fso.FileExists(expectedAddin) Then Fail "running-Word installer check removed active add-in"
End Sub

Sub TestUninstallerRefusesRunningWord()
    Dim command, exitCode
    command = QuoteArgument(WScript.FullName) & " //nologo " & QuoteArgument(uninstallerPath)
    On Error Resume Next
    Err.Clear
    exitCode = shell.Run(command, 0, True)
    If Err.Number <> 0 Then CaptureAndFail "cannot launch uninstaller running-Word check"
    On Error GoTo 0
    If exitCode = 0 Then Fail "uninstaller succeeded while the tested Word process was running"
    If Not fso.FileExists(expectedAddin) Then Fail "running-Word uninstaller check removed active add-in"
End Sub

Sub CreateSavedDocument(ByVal path, ByVal marking, ByRef targetDoc)
    On Error Resume Next
    Err.Clear
    Set targetDoc = word.Documents.Add
    If Err.Number <> 0 Then CaptureAndFail "cannot create matrix document"
    targetDoc.Content.Text = marking
    targetDoc.SaveAs2 path, 16
    If Err.Number <> 0 Then CaptureAndFail "cannot establish matrix document path"
    targetDoc.Content.InsertAfter vbCr & "ORDINARY-SAVE"
    targetDoc.Save
    If Err.Number <> 0 Then CaptureAndFail "matrix ordinary save raised an error"
    On Error GoTo 0
    If Not targetDoc.Saved Then Fail "matrix ordinary save did not complete"
End Sub

Sub StartWordNormally(ByVal arguments)
    On Error Resume Next
    Err.Clear
    shell.Run QuoteArgument(wordExe) & arguments, 0, False
    If Err.Number <> 0 Then CaptureAndFail "cannot launch WINWORD.EXE"
    Dim attempt
    For attempt = 1 To 40
        Err.Clear
        Set word = GetObject(, "Word.Application")
        If Err.Number = 0 Then
            If Not word Is Nothing Then Exit For
        End If
        Set word = Nothing
        WScript.Sleep 500
    Next
    If word Is Nothing Then CaptureAndFail "cannot attach to launched Word instance"
    word.Visible = False
    word.DisplayAlerts = 0
    On Error GoTo 0
End Sub

Sub WaitForExpectedTemplate()
    Dim attempt, template, loaded, loadedPaths, enumerationError
    loaded = False
    loadedPaths = ""
    enumerationError = ""
    On Error Resume Next
    For attempt = 1 To 40
        loaded = False
        loadedPaths = ""
        Err.Clear
        For Each template In word.Templates
            loadedPaths = loadedPaths & "[" & template.FullName & "]"
            If StrComp(fso.GetAbsolutePathName(template.FullName), expectedAddin, 1) = 0 Then loaded = True
        Next
        If Err.Number <> 0 Then
            enumerationError = CStr(Err.Number) & ": " & Err.Description
            Err.Clear
        Else
            enumerationError = ""
        End If
        If loaded Then Exit For
        WScript.Sleep 500
    Next
    On Error GoTo 0
    If Not loaded Then
        If enumerationError <> "" Then
            Fail "cannot enumerate loaded templates: " & enumerationError
        Else
            Fail "expected Startup add-in did not load: " & loadedPaths
        End If
    End If
End Sub

Function WaitForHookEvidence(ByVal requireEnabled)
    Dim attempt, value, macroError
    value = ""
    macroError = ""
    On Error Resume Next
    For attempt = 1 To 40
        Err.Clear
        value = CStr(word.Run("AutoExport.GetRuntimeEvidenceForDiagnostics"))
        If Err.Number = 0 Then
            macroError = ""
            If Not requireEnabled Or FieldValue(value, "hook") = "ENABLED" Then Exit For
        Else
            macroError = CStr(Err.Number) & ": " & Err.Description
            Err.Clear
        End If
        WScript.Sleep 500
    Next
    On Error GoTo 0
    If requireEnabled And FieldValue(value, "hook") <> "ENABLED" Then
        If macroError <> "" Then
            Fail "runtime evidence macro failed: " & macroError
        Else
            Fail "AutoExec hook is not enabled: " & value
        End If
    End If
    WaitForHookEvidence = value
End Function

Sub AssertJson(ByVal path, ByVal number, ByVal expectedName, ByVal label)
    Dim content
    If Not fso.FileExists(path) Then Fail label & " dictionary is missing: " & path
    content = ReadUtf8File(path, failure)
    If failure <> "" Then Fail label & " dictionary cannot be read: " & failure
    If InStr(1, content, Chr(34) & "number" & Chr(34) & ": " & Chr(34) & number & Chr(34), 0) = 0 Then _
        Fail label & " lacks number " & number
    If InStr(1, content, Chr(34) & "name" & Chr(34) & ": " & Chr(34) & expectedName & Chr(34), 0) = 0 Then _
        Fail label & " lacks expected name"
End Sub

Sub AssertJsonLacksNumber(ByVal path, ByVal number, ByVal label)
    Dim content
    content = ReadUtf8File(path, failure)
    If failure <> "" Then Fail label & " dictionary cannot be read: " & failure
    If InStr(1, content, Chr(34) & "number" & Chr(34) & ": " & Chr(34) & number & Chr(34), 0) > 0 Then _
        Fail label & " still contains number " & number
End Sub

Function CountTemporaryDictionaryFiles(ByVal directory)
    Dim file, count
    count = 0
    For Each file In fso.GetFolder(directory).Files
        If InStr(1, file.Name, ".dict.json.tmp-", 1) > 0 Then count = count + 1
    Next
    CountTemporaryDictionaryFiles = count
End Function

Function MarkingText(ByVal number, ByVal name)
    MarkingText = ChrW(&H9644) & ChrW(&H56FE) & ChrW(&H6807) & ChrW(&H8BB0) & _
        ChrW(&H8BF4) & ChrW(&H660E) & ChrW(&HFF1A) & number & name & ChrW(&HFF1B)
End Function

Function NameBase()
    NameBase = ChrW(&H5E95) & ChrW(&H5EA7)
End Function

Function NameBracket()
    NameBracket = ChrW(&H652F) & ChrW(&H67B6)
End Function

Function NameCover()
    NameCover = ChrW(&H76D6) & ChrW(&H677F)
End Function

Function NameOld()
    NameOld = ChrW(&H65E7) & ChrW(&H4EF6)
End Function

Function NameNew()
    NameNew = ChrW(&H65B0) & ChrW(&H4EF6)
End Function

Sub EnsureFolder(ByVal path)
    If Not fso.FolderExists(path) Then fso.CreateFolder path
End Sub

Function BoolText(ByVal value)
    If value Then
        BoolText = "true"
    Else
        BoolText = "false"
    End If
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
    Dim attempt, count, checkFailure
    WaitForNoWord = False
    For attempt = 1 To seconds * 2
        count = WordProcessCount(checkFailure)
        If count = 0 Then
            WaitForNoWord = True
            Exit Function
        End If
        If count < 0 Then Exit Function
        WScript.Sleep 500
    Next
End Function

Sub CloseOwnedDocuments()
    On Error Resume Next
    If IsObject(docLock) Then docLock.Close False
    If IsObject(docReadOnly) Then docReadOnly.Close False
    If IsObject(docRename) Then docRename.Close False
    If IsObject(docB) Then docB.Close False
    If IsObject(docA) Then docA.Close False
    Set docLock = Nothing
    Set docReadOnly = Nothing
    Set docRename = Nothing
    Set docB = Nothing
    Set docA = Nothing
    On Error GoTo 0
End Sub

Sub CaptureAndFail(ByVal prefix)
    Dim number, description
    number = Err.Number
    description = Err.Description
    On Error Resume Next
    CloseOwnedDocuments
    If IsObject(word) Then word.Quit False
    Set word = Nothing
    On Error GoTo 0
    Fail prefix & " (" & CStr(number) & "): " & description
End Sub

Sub Fail(ByVal message)
    On Error Resume Next
    CloseOwnedDocuments
    If IsObject(word) Then word.Quit False
    Set word = Nothing
    On Error GoTo 0
    WScript.Echo "FAIL|L4_EXTENDED_MATRIX|" & message
    WScript.Quit 1
End Sub

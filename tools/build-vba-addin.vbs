' build-vba-addin.vbs - build the canonical PatentMarker.dotm from vba/.
' This script owns one hidden Word instance and never edits or saves Normal.dotm.
' Usage: cscript //nologo tools\build-vba-addin.vbs vba word-addin\PatentMarker.dotm

Option Explicit

Const wdDoNotSaveChanges = 0
Const wdFormatXMLTemplateMacroEnabled = 15
Const msoAutomationSecurityForceDisable = 3

Dim fso, shell, scriptDir, vbaDir, outputPath, outputDir, tempPath, backupPath
Dim word, doc, project, component, normalPath, normalExisted, normalSnapshotPath
Dim failure, buildSucceeded, oldOutputMoved

Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
Set word = Nothing
Set doc = Nothing
Set project = Nothing
Set component = Nothing
buildSucceeded = False
oldOutputMoved = False

If WScript.Arguments.Count <> 2 Then Fail "usage: build-vba-addin.vbs <vba-directory> <output-dotm>"
vbaDir = fso.GetAbsolutePathName(WScript.Arguments(0))
outputPath = fso.GetAbsolutePathName(WScript.Arguments(1))
If Not fso.FolderExists(vbaDir) Then Fail "VBA directory not found: " & vbaDir

Dim requiredFiles, fileName
requiredFiles = Array("Patterns.bas", "DictModel.bas", "JsonWriter.bas", "PatentExtractor.bas", _
    "AutoExport.bas", "PatentMarkerBootstrap.bas", "clsSaveHook.cls", _
    "PatentDictPanel.frm", "PatentDictPanel.frx")
For Each fileName In requiredFiles
    If Not fso.FileExists(fso.BuildPath(vbaDir, fileName)) Then Fail "required VBA source missing: " & fileName
Next

outputDir = fso.GetParentFolderName(outputPath)
If outputDir = "" Then Fail "output directory is empty"
If Not fso.FolderExists(outputDir) Then fso.CreateFolder outputDir
tempPath = outputPath & ".pm-build-" & Stamp() & ".dotm"
backupPath = outputPath & ".previous-" & Stamp() & ".bak"
If fso.FileExists(tempPath) Or fso.FileExists(backupPath) Then Fail "staging path exists; retry after one second"

Dim wordCount
wordCount = CountWordProcesses(failure)
If wordCount < 0 Then Fail "cannot determine whether Word is running: " & failure
If wordCount > 0 Then Fail "Word is already running; close it so the builder cannot attach to user work"

On Error Resume Next
Err.Clear
Set word = CreateObject("Word.Application")
If Err.Number <> 0 Then CaptureAndFail "cannot create Word.Application"
word.Visible = False
word.DisplayAlerts = 0
word.AutomationSecurity = msoAutomationSecurityForceDisable

normalPath = word.NormalTemplate.FullName
normalExisted = fso.FileExists(normalPath)
normalSnapshotPath = ""
If normalExisted Then
    normalSnapshotPath = fso.BuildPath(fso.GetSpecialFolder(2), "PatentMarker-Normal-" & fso.GetTempName & ".snapshot")
    Err.Clear
    fso.CopyFile normalPath, normalSnapshotPath, False
    If Err.Number <> 0 Then CaptureAndFail "cannot snapshot Normal.dotm before build"
End If

Set doc = word.Documents.Add
If Err.Number <> 0 Then CaptureAndFail "cannot create the build document"
Set project = doc.VBProject
If Err.Number <> 0 Or project Is Nothing Then CaptureAndFail "cannot access the build document VBProject; enable Trust access to the VBA project object model for this build only"
project.Name = "PatentMarkerAddin"
If Err.Number <> 0 Then CaptureAndFail "cannot name the VBA project"

For Each fileName In Array("Patterns.bas", "DictModel.bas", "JsonWriter.bas", "PatentExtractor.bas", _
        "AutoExport.bas", "PatentMarkerBootstrap.bas", "clsSaveHook.cls", "PatentDictPanel.frm")
    Err.Clear
    Set component = project.VBComponents.Import(fso.BuildPath(vbaDir, fileName))
    If Err.Number <> 0 Or component Is Nothing Then CaptureAndFail "cannot import " & fileName
    Set component = Nothing
Next

Dim compileProbe
Err.Clear
compileProbe = word.Run("AutoExport.GetDiagnosticStatus")
If Err.Number <> 0 Then CaptureAndFail "VBA compile/execution probe failed"

Err.Clear
doc.SaveAs2 tempPath, wdFormatXMLTemplateMacroEnabled
If Err.Number <> 0 Then CaptureAndFail "cannot save staged macro-enabled template"
If Not fso.FileExists(tempPath) Then CaptureAndFail "Word reported success but staged add-in is missing"

' The first SaveAs converts a document into a template but Word can omit
' word/vbaData.xml (the macro discovery metadata used by AutoExec). Mark the
' new template dirty and save it once in template form so Word emits that
' metadata. Package-level verification independently checks the result.
Err.Clear
doc.Saved = False
doc.Save
If Err.Number <> 0 Then CaptureAndFail "cannot finalize staged template macro metadata"

If Not HasExpectedComponents(project, failure) Then CaptureAndFail "component contract failed: " & failure

doc.Close wdDoNotSaveChanges
Set component = Nothing
Set project = Nothing
Set doc = Nothing
word.Quit wdDoNotSaveChanges
Set word = Nothing
WaitForWordProcessesToExit
On Error GoTo 0

If normalExisted <> fso.FileExists(normalPath) Then FailAfterWord "Normal.dotm existence changed during build"
If normalExisted Then
    If Not FilesEqual(normalSnapshotPath, normalPath, failure) Then FailAfterWord "Normal.dotm changed or could not be verified after build: " & failure
    On Error Resume Next
    fso.DeleteFile normalSnapshotPath, True
    normalSnapshotPath = ""
    On Error GoTo 0
End If

Dim finalizerPath, verifierPath, powershellPath, packageCommand, packageExit
finalizerPath = fso.BuildPath(scriptDir, "finalize-dotm-package.ps1")
verifierPath = fso.BuildPath(scriptDir, "verify-dotm-package.ps1")
powershellPath = shell.ExpandEnvironmentStrings("%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")
If Not fso.FileExists(finalizerPath) Then FailAfterWord "DOTM finalizer is missing: " & finalizerPath
If Not fso.FileExists(verifierPath) Then FailAfterWord "DOTM verifier is missing: " & verifierPath
If Not fso.FileExists(powershellPath) Then FailAfterWord "Windows PowerShell is missing: " & powershellPath

packageCommand = QuoteArgument(powershellPath) & " -NoProfile -ExecutionPolicy Bypass -File " & _
    QuoteArgument(finalizerPath) & " -Path " & QuoteArgument(tempPath)
packageExit = shell.Run(packageCommand, 0, True)
If packageExit <> 0 Then FailAfterWord "DOTM supplemental-data finalization failed (exit=" & CStr(packageExit) & ")"
packageCommand = QuoteArgument(powershellPath) & " -NoProfile -ExecutionPolicy Bypass -File " & _
    QuoteArgument(verifierPath) & " -Path " & QuoteArgument(tempPath)
packageExit = shell.Run(packageCommand, 0, True)
If packageExit <> 0 Then FailAfterWord "DOTM package verification failed (exit=" & CStr(packageExit) & ")"

If fso.FileExists(outputPath) Then
    On Error Resume Next
    Err.Clear
    fso.MoveFile outputPath, backupPath
    If Err.Number <> 0 Then CaptureAndFailAfterWord "cannot back up the previous canonical add-in"
    On Error GoTo 0
    oldOutputMoved = True
End If

On Error Resume Next
Err.Clear
fso.MoveFile tempPath, outputPath
If Err.Number <> 0 Then
    Dim moveNumber, moveDescription
    moveNumber = Err.Number
    moveDescription = Err.Description
    Err.Clear
    If oldOutputMoved And Not fso.FileExists(outputPath) Then fso.MoveFile backupPath, outputPath
    On Error GoTo 0
    FailAfterWord "cannot activate built add-in (" & CStr(moveNumber) & "): " & moveDescription
End If
On Error GoTo 0

If Not fso.FileExists(outputPath) Then FailAfterWord "canonical add-in is missing after activation"
buildSucceeded = True
WScript.Echo "PASS|L2_ADDIN_BUILD|output=" & outputPath & "|normal_unchanged=true|components=8"
If oldOutputMoved Then WScript.Echo "INFO|previous_addin_backup=" & backupPath
WScript.Quit 0

Function HasExpectedComponents(ByVal vbaProject, ByRef errorText)
    On Error Resume Next
    Dim names, expectedName, c, found
    names = Array("Patterns", "DictModel", "JsonWriter", "PatentExtractor", "AutoExport", _
        "PatentMarkerBootstrap", "clsSaveHook", "PatentDictPanel")
    errorText = ""
    For Each expectedName In names
        found = False
        Err.Clear
        For Each c In vbaProject.VBComponents
            If StrComp(c.Name, expectedName, 1) = 0 Then
                found = True
                Exit For
            End If
        Next
        If Err.Number <> 0 Then
            errorText = CStr(Err.Number) & ": " & Err.Description
            HasExpectedComponents = False
            Exit Function
        End If
        If Not found Then
            errorText = "missing " & expectedName
            HasExpectedComponents = False
            Exit Function
        End If
    Next
    HasExpectedComponents = True
    On Error GoTo 0
End Function

Function Stamp()
    Stamp = Year(Now) & Right("0" & Month(Now), 2) & Right("0" & Day(Now), 2) & "-" & _
        Right("0" & Hour(Now), 2) & Right("0" & Minute(Now), 2) & Right("0" & Second(Now), 2)
End Function

Function CountWordProcesses(ByRef errorText)
    On Error Resume Next
    Dim svc, processes, errNo, errDesc
    errorText = ""
    Set svc = Nothing
    Set processes = Nothing
    Set svc = GetObject("winmgmts:\\.\root\cimv2")
    If Err.Number = 0 Then Set processes = svc.ExecQuery("SELECT ProcessId FROM Win32_Process WHERE Name='WINWORD.EXE'")
    errNo = Err.Number
    errDesc = Err.Description
    On Error GoTo 0
    If errNo <> 0 Or processes Is Nothing Then
        errorText = CStr(errNo) & ": " & errDesc
        CountWordProcesses = -1
    Else
        CountWordProcesses = processes.Count
    End If
End Function

Function FilesEqual(ByVal leftPath, ByVal rightPath, ByRef errorText)
    FilesEqual = False
    errorText = ""
    If Not fso.FileExists(leftPath) Or Not fso.FileExists(rightPath) Then
        errorText = "one or both files are missing"
        Exit Function
    End If
    If fso.GetFile(leftPath).Size <> fso.GetFile(rightPath).Size Then
        errorText = "file sizes differ"
        Exit Function
    End If

    Dim command, exitCode
    command = QuoteArgument(shell.ExpandEnvironmentStrings("%ComSpec%")) & _
        " /d /c fc.exe /b " & QuoteArgument(leftPath) & " " & QuoteArgument(rightPath) & " >nul 2>&1"
    On Error Resume Next
    Err.Clear
    exitCode = shell.Run(command, 0, True)
    If Err.Number <> 0 Then
        errorText = CStr(Err.Number) & ": " & Err.Description
        Err.Clear
        On Error GoTo 0
        Exit Function
    End If
    On Error GoTo 0
    FilesEqual = (exitCode = 0)
    If exitCode = 1 Then
        errorText = "file bytes differ"
    ElseIf exitCode <> 0 Then
        errorText = "binary comparison failed (exit=" & CStr(exitCode) & ")"
    End If
End Function

Function QuoteArgument(ByVal value)
    QuoteArgument = Chr(34) & Replace(CStr(value), Chr(34), Chr(34) & Chr(34)) & Chr(34)
End Function

Sub WaitForWordProcessesToExit()
    Dim attempt, count, checkFailure
    For attempt = 1 To 30
        count = CountWordProcesses(checkFailure)
        If count < 0 Then FailAfterWord "cannot verify that the owned Word process exited: " & checkFailure
        If count = 0 Then Exit Sub
        WScript.Sleep 500
    Next
    FailAfterWord "owned Word process did not exit within 15 seconds"
End Sub

Sub CloseOwnedWord()
    On Error Resume Next
    If IsObject(doc) Then doc.Close wdDoNotSaveChanges
    Set component = Nothing
    Set project = Nothing
    Set doc = Nothing
    If IsObject(word) Then
        word.Quit wdDoNotSaveChanges
    End If
    Set word = Nothing
    On Error GoTo 0
End Sub

Sub CaptureAndFail(ByVal prefix)
    Dim number, description
    number = Err.Number
    description = Err.Description
    On Error GoTo 0
    CloseOwnedWord
    FailAfterWord prefix & " (" & CStr(number) & "): " & description
End Sub

Sub CaptureAndFailAfterWord(ByVal prefix)
    Dim number, description
    number = Err.Number
    description = Err.Description
    On Error GoTo 0
    FailAfterWord prefix & " (" & CStr(number) & "): " & description
End Sub

Sub FailAfterWord(ByVal message)
    On Error Resume Next
    If fso.FileExists(tempPath) Then fso.DeleteFile tempPath, True
    If oldOutputMoved And Not fso.FileExists(outputPath) And fso.FileExists(backupPath) Then fso.MoveFile backupPath, outputPath
    On Error GoTo 0
    Fail message
End Sub

Sub Fail(ByVal message)
    On Error Resume Next
    CloseOwnedWord
    If tempPath <> "" Then
        If fso.FileExists(tempPath) Then fso.DeleteFile tempPath, True
    End If
    On Error GoTo 0
    If normalSnapshotPath <> "" Then
        If fso.FileExists(normalSnapshotPath) Then
            Dim snapshotCheck
            If FilesEqual(normalSnapshotPath, normalPath, snapshotCheck) Then
                On Error Resume Next
                fso.DeleteFile normalSnapshotPath, True
                On Error GoTo 0
            Else
                message = message & "|NORMAL_SNAPSHOT_PRESERVED=" & normalSnapshotPath & "|normal_check=" & snapshotCheck
            End If
        End If
    End If
    WScript.Echo "FAIL|L2_ADDIN_BUILD|" & message
    WScript.Quit 1
End Sub

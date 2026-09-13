' PatentMarker Word add-in installer
' Installs PatentMarker.dotm into Word's Startup folder.
' It never opens, edits, saves, or replaces Normal.dotm.

Option Explicit

Const ForAppending = 8
Const ForWriting = 2

Dim fso, shell, scriptDir, logPath, logFile, output, noPrompt
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
noPrompt = WScript.Arguments.Named.Exists("NoPrompt")
output = ""

Sub OpenLog()
    On Error Resume Next
    If WScript.Arguments.Named.Exists("LogPath") Then
        logPath = WScript.Arguments.Named.Item("LogPath")
    Else
        logPath = fso.BuildPath(scriptDir, "install-vba.log")
    End If
    Set logFile = fso.OpenTextFile(logPath, ForAppending, True, 0)
    If Err.Number <> 0 Then
        Err.Clear
        logPath = fso.BuildPath(fso.GetSpecialFolder(2), "PatentMarker-install-vba.log")
        Set logFile = fso.OpenTextFile(logPath, ForAppending, True, 0)
    End If
    On Error GoTo 0
End Sub

Sub LogMsg(ByVal message)
    output = output & message & vbCrLf
    On Error Resume Next
    If IsObject(logFile) Then logFile.WriteLine FormatDateTime(Now, 0) & " " & message
    On Error GoTo 0
End Sub

Sub CloseLog()
    On Error Resume Next
    If IsObject(logFile) Then logFile.Close
    Set logFile = Nothing
    On Error GoTo 0
End Sub

Sub Finish(ByVal exitCode)
    CloseLog
    WScript.Echo output
    WScript.Quit exitCode
End Sub

Sub Fail(ByVal message)
    LogMsg "FAIL|" & message
    Finish 1
End Sub

Function Stamp()
    Dim value
    value = Year(Now) & Right("0" & Month(Now), 2) & Right("0" & Day(Now), 2) & "-" & _
        Right("0" & Hour(Now), 2) & Right("0" & Minute(Now), 2) & Right("0" & Second(Now), 2)
    Stamp = value
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

Function ResolveWordStartup(ByRef startupPath, ByRef wordVersion, ByRef errorText)
    On Error Resume Next
    Dim app, errNo, errDesc
    startupPath = ""
    wordVersion = ""
    errorText = ""
    Set app = CreateObject("Word.Application")
    If Err.Number = 0 Then
        app.Visible = False
        app.DisplayAlerts = 0
        startupPath = app.StartupPath
        wordVersion = app.Version
        app.Quit False
    End If
    errNo = Err.Number
    errDesc = Err.Description
    Set app = Nothing
    On Error GoTo 0
    If errNo <> 0 Or startupPath = "" Then
        errorText = CStr(errNo) & ": " & errDesc
        ResolveWordStartup = False
    Else
        ResolveWordStartup = True
    End If
End Function

Function WaitForNoWord(ByVal seconds, ByRef errorText)
    Dim attempt, processCount
    errorText = ""
    WaitForNoWord = False
    For attempt = 1 To seconds * 2
        processCount = CountWordProcesses(errorText)
        If processCount = 0 Then
            WaitForNoWord = True
            Exit Function
        End If
        If processCount < 0 Then Exit Function
        WScript.Sleep 500
    Next
    errorText = "owned Word process did not exit within " & CStr(seconds) & " seconds"
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

Function MoveFileChecked(ByVal sourcePath, ByVal targetPath, ByRef errorText)
    On Error Resume Next
    Err.Clear
    fso.MoveFile sourcePath, targetPath
    Dim errNo, errDesc
    errNo = Err.Number
    errDesc = Err.Description
    On Error GoTo 0
    If errNo <> 0 Then
        errorText = CStr(errNo) & ": " & errDesc
        MoveFileChecked = False
    Else
        MoveFileChecked = True
    End If
End Function

OpenLog
LogMsg "START|PatentMarker Word add-in installer"
LogMsg "LOG|" & logPath

Dim sourcePath, startupPath, wordVersion, errorText, wordCount
sourcePath = fso.BuildPath(scriptDir, "PatentMarker.dotm")
If WScript.Arguments.Named.Exists("SourcePath") Then sourcePath = WScript.Arguments.Named.Item("SourcePath")
If Not fso.FileExists(sourcePath) Then Fail "source add-in missing: " & sourcePath

startupPath = ""
wordVersion = "NOT_ACCESSED_TEST_OVERRIDE"
If WScript.Arguments.Named.Exists("StartupPath") Then
    startupPath = WScript.Arguments.Named.Item("StartupPath")
    LogMsg "MODE|isolated startup override"
Else
    wordCount = CountWordProcesses(errorText)
    If wordCount < 0 Then Fail "cannot determine whether Word is running: " & errorText
    If wordCount > 0 Then Fail "Word is running; close all Word windows before installation"
    If Not ResolveWordStartup(startupPath, wordVersion, errorText) Then Fail "cannot resolve Word Startup path: " & errorText
    If Not WaitForNoWord(20, errorText) Then Fail "Word path probe did not shut down cleanly: " & errorText
End If

If startupPath = "" Then Fail "Word Startup path is empty"
If Not fso.FolderExists(startupPath) Then
    On Error Resume Next
    Err.Clear
    fso.CreateFolder startupPath
    errorText = Err.Description
    Dim createErr
    createErr = Err.Number
    On Error GoTo 0
    If createErr <> 0 Then Fail "cannot create Word Startup folder: " & errorText
End If

Dim targetPath, tempPath, backupPath, manifestPath, manifestBackupPath, compareError, backupMade, manifestBackupMade
targetPath = fso.BuildPath(startupPath, "PatentMarker.dotm")
manifestPath = fso.BuildPath(startupPath, "PatentMarker.addin.install.txt")
tempPath = targetPath & ".pm-new-" & Stamp()
backupPath = targetPath & ".pm-backup-" & Stamp()
manifestBackupPath = manifestPath & ".pm-backup-" & Stamp()
backupMade = False
manifestBackupMade = False

If LCase(fso.GetAbsolutePathName(sourcePath)) = LCase(fso.GetAbsolutePathName(targetPath)) Then Fail "source and target add-in paths are identical"
If fso.FileExists(tempPath) Or fso.FileExists(backupPath) Then Fail "staging path already exists; retry after one second"

On Error Resume Next
Err.Clear
fso.CopyFile sourcePath, tempPath, False
Dim copyErr, copyDescription
copyErr = Err.Number
copyDescription = Err.Description
On Error GoTo 0
If copyErr <> 0 Then Fail "cannot stage add-in: " & CStr(copyErr) & ": " & copyDescription
If Not FilesEqual(sourcePath, tempPath, compareError) Then
    On Error Resume Next
    fso.DeleteFile tempPath, True
    On Error GoTo 0
    Fail "staged add-in verification failed: " & compareError
End If

If fso.FileExists(targetPath) Then
    If Not MoveFileChecked(targetPath, backupPath, errorText) Then
        On Error Resume Next
        fso.DeleteFile tempPath, True
        On Error GoTo 0
        Fail "cannot back up existing PatentMarker.dotm: " & errorText
    End If
    backupMade = True
End If

If Not MoveFileChecked(tempPath, targetPath, errorText) Then
    On Error Resume Next
    If backupMade And Not fso.FileExists(targetPath) Then fso.MoveFile backupPath, targetPath
    On Error GoTo 0
    Fail "cannot activate staged add-in; previous add-in restored: " & errorText
End If

If Not FilesEqual(sourcePath, targetPath, compareError) Then
    On Error Resume Next
    fso.DeleteFile targetPath, True
    If backupMade Then fso.MoveFile backupPath, targetPath
    On Error GoTo 0
    Fail "installed add-in verification failed; previous add-in restored: " & compareError
End If

If fso.FileExists(manifestPath) Then
    If Not MoveFileChecked(manifestPath, manifestBackupPath, errorText) Then
        On Error Resume Next
        fso.DeleteFile targetPath, True
        If backupMade Then fso.MoveFile backupPath, targetPath
        On Error GoTo 0
        Fail "cannot back up the existing ownership manifest; previous add-in restored: " & errorText
    End If
    manifestBackupMade = True
End If

On Error Resume Next
Dim manifest
Set manifest = fso.OpenTextFile(manifestPath, ForWriting, True, 0)
manifest.WriteLine "product=PatentMarker"
manifest.WriteLine "file=PatentMarker.dotm"
manifest.WriteLine "size=" & CStr(fso.GetFile(targetPath).Size)
manifest.WriteLine "installed_at=" & FormatDateTime(Now, 0)
manifest.Close
Dim manifestErr, manifestDescription
manifestErr = Err.Number
manifestDescription = Err.Description
On Error GoTo 0
If manifestErr <> 0 Then
    On Error Resume Next
    fso.DeleteFile targetPath, True
    If backupMade Then fso.MoveFile backupPath, targetPath
    If manifestBackupMade Then fso.MoveFile manifestBackupPath, manifestPath
    On Error GoTo 0
    Fail "cannot write ownership manifest; previous add-in restored: " & manifestDescription
End If

LogMsg "WORD_VERSION|" & wordVersion
LogMsg "STARTUP_PATH|" & startupPath
LogMsg "NORMAL_TEMPLATE|not_read_or_written"
LogMsg "ADDIN_PATH|" & targetPath
If backupMade Then LogMsg "PREVIOUS_ADDIN_BACKUP|" & backupPath
If manifestBackupMade Then LogMsg "PREVIOUS_MANIFEST_BACKUP|" & manifestBackupPath
LogMsg "PASS|add-in installed byte-for-byte; restart Word for AutoExec"
Finish 0

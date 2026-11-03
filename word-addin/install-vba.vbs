' PatentMarker Word add-in installer
' Installs PatentMarker.dotm into Word's Startup folder.
' It never opens, edits, saves, or replaces Normal.dotm.
'
' Upgrade contract (G08 / R-007): before anything is renamed, an independent
' reference copy of the previous add-in and of the previous ownership manifest is
' taken and verified. The new manifest is written to a per-run staging file, closed,
' read back and compared, and only then activated at the product path. Every rollback
' step records its own error and re-checks the bytes and attributes of the restored
' file against that reference copy. Rollback deletes only files this run created. A
' failed restore never claims the previous add-in was restored; it reports the
' recovery file that still holds it.
'
' /InjectFault:<stage> is a test-only seam used by tools/test-g08-rollback.ps1 to
' drive the isolated failure matrix. It can only add a failure; it never skips work.

Option Explicit

Const ForReading = 1
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

Function InjectedFault(ByVal stageName)
    InjectedFault = False
    If WScript.Arguments.Named.Exists("InjectFault") Then
        If LCase(WScript.Arguments.Named.Item("InjectFault")) = LCase(stageName) Then InjectedFault = True
    End If
    If Not InjectedFault Then
        If WScript.Arguments.Named.Exists("InjectFault2") Then
            If LCase(WScript.Arguments.Named.Item("InjectFault2")) = LCase(stageName) Then InjectedFault = True
        End If
    End If
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

Function CopyFileChecked(ByVal sourcePath, ByVal targetPath, ByRef errorText)
    On Error Resume Next
    Err.Clear
    fso.CopyFile sourcePath, targetPath, False
    Dim errNo, errDesc
    errNo = Err.Number
    errDesc = Err.Description
    On Error GoTo 0
    If errNo <> 0 Then
        errorText = CStr(errNo) & ": " & errDesc
        CopyFileChecked = False
    Else
        CopyFileChecked = True
    End If
End Function

Function SameProtectedAttributes(ByVal leftPath, ByVal rightPath)
    ' Compares ReadOnly, Hidden and System only; the Archive bit changes on writes
    ' and is not part of the ownership contract.
    SameProtectedAttributes = False
    On Error Resume Next
    Dim leftFlags, rightFlags, errNo
    leftFlags = fso.GetFile(leftPath).Attributes
    rightFlags = fso.GetFile(rightPath).Attributes
    errNo = Err.Number
    On Error GoTo 0
    If errNo <> 0 Then Exit Function
    SameProtectedAttributes = ((leftFlags And 7) = (rightFlags And 7))
End Function

Sub AppendRestore(ByVal text)
    If restoreReport <> "" Then restoreReport = restoreReport & " "
    restoreReport = restoreReport & text
End Sub

Function RemoveOwnFile(ByVal path, ByVal label)
    ' Deletes a path this run created. Never touches a directory or an object this
    ' run did not create, and always checks that the path is really gone.
    RemoveOwnFile = True
    If Not fso.FileExists(path) Then
        AppendRestore label & "=already_absent"
        Exit Function
    End If
    On Error Resume Next
    Err.Clear
    fso.DeleteFile path, True
    Dim errNo, errDesc, stillThere
    errNo = Err.Number
    errDesc = Err.Description
    On Error GoTo 0
    stillThere = fso.FileExists(path)
    If errNo <> 0 Or stillThere Then
        RemoveOwnFile = False
        AppendRestore label & "=remove_failed(" & CStr(errNo) & ":" & errDesc & ")"
    Else
        AppendRestore label & "=removed_own_residue"
    End If
End Function

Function CheckRestored(ByVal actualPath, ByVal referencePath, ByVal label)
    Dim compareText
    CheckRestored = False
    If Not fso.FileExists(actualPath) Then
        AppendRestore label & "=missing|recovery=" & referencePath
        Exit Function
    End If
    If Not FilesEqual(referencePath, actualPath, compareText) Then
        AppendRestore label & "=bytes_differ(" & compareText & ")|recovery=" & referencePath
        Exit Function
    End If
    If Not SameProtectedAttributes(referencePath, actualPath) Then
        AppendRestore label & "=attributes_differ|recovery=" & referencePath
        Exit Function
    End If
    AppendRestore label & "=restored_verified"
    CheckRestored = True
End Function

Function RestoreAddin()
    Dim moveErrNo, moveErrDesc
    RestoreAddin = True
    If Not backupMade Then
        If addinActivated Then
            ' First installation: the only product file that exists is the one this
            ' run created, so rollback means removing that residue.
            If Not RemoveOwnFile(targetPath, "addin") Then RestoreAddin = False
        ElseIf fso.FileExists(targetPath) Then
            AppendRestore "addin=unchanged"
        Else
            AppendRestore "addin=none_before"
        End If
        Exit Function
    End If
    If addinActivated Then
        If Not RemoveOwnFile(targetPath, "addin") Then
            AppendRestore "addin=restore_blocked|recovery=" & oldCopyPath
            RestoreAddin = False
            Exit Function
        End If
    End If
    If InjectedFault("addin-restore") Then
        ' A genuine filesystem obstruction, so the canonical rollback line below is
        ' exercised for real instead of being skipped by a raised error.
        LogMsg "INJECTED_FAULT|addin-restore"
        On Error Resume Next
        Err.Clear
        fso.CreateFolder targetPath
        On Error GoTo 0
    End If
    On Error Resume Next
    Err.Clear
    If backupMade Then fso.MoveFile backupPath, targetPath
    moveErrNo = Err.Number
    moveErrDesc = Err.Description
    On Error GoTo 0
    If moveErrNo <> 0 Then
        AppendRestore "addin=restore_failed(" & CStr(moveErrNo) & ":" & moveErrDesc & ")|recovery=" & oldCopyPath
        RestoreAddin = False
        Exit Function
    End If
    If InjectedFault("addin-restore-corrupt") Then
        LogMsg "INJECTED_FAULT|addin-restore-corrupt"
        On Error Resume Next
        Err.Clear
        fso.OpenTextFile(targetPath, ForWriting, True, 0).Write "injected corruption"
        On Error GoTo 0
    End If
    If Not CheckRestored(targetPath, oldCopyPath, "addin") Then RestoreAddin = False
End Function

Function RestoreManifest()
    Dim moveErrNo, moveErrDesc
    RestoreManifest = True
    If Not manifestBackupMade Then
        If manifestActivated Then
            If Not RemoveOwnFile(manifestPath, "manifest") Then RestoreManifest = False
        ElseIf fso.FileExists(manifestPath) Then
            AppendRestore "manifest=unchanged"
        Else
            AppendRestore "manifest=none_before"
        End If
        Exit Function
    End If
    If manifestActivated Then
        If Not RemoveOwnFile(manifestPath, "manifest") Then
            AppendRestore "manifest=restore_blocked|recovery=" & manifestOldCopyPath
            RestoreManifest = False
            Exit Function
        End If
    End If
    If InjectedFault("manifest-restore") Then
        LogMsg "INJECTED_FAULT|manifest-restore"
        On Error Resume Next
        Err.Clear
        fso.CreateFolder manifestPath
        On Error GoTo 0
    End If
    On Error Resume Next
    Err.Clear
    If manifestBackupMade Then fso.MoveFile manifestBackupPath, manifestPath
    moveErrNo = Err.Number
    moveErrDesc = Err.Description
    On Error GoTo 0
    If moveErrNo <> 0 Then
        AppendRestore "manifest=restore_failed(" & CStr(moveErrNo) & ":" & moveErrDesc & ")|recovery=" & manifestOldCopyPath
        RestoreManifest = False
        Exit Function
    End If
    If InjectedFault("manifest-restore-corrupt") Then
        LogMsg "INJECTED_FAULT|manifest-restore-corrupt"
        On Error Resume Next
        Err.Clear
        fso.OpenTextFile(manifestPath, ForWriting, True, 0).Write "injected corruption"
        On Error GoTo 0
    End If
    If Not CheckRestored(manifestPath, manifestOldCopyPath, "manifest") Then RestoreManifest = False
End Function

Sub DiscardOwnFile(ByVal path)
    If Not fso.FileExists(path) Then Exit Sub
    On Error Resume Next
    Err.Clear
    fso.DeleteFile path, True
    If Err.Number <> 0 Then LogMsg "WARN|cannot remove temporary file " & path & ": " & CStr(Err.Number) & ": " & Err.Description
    On Error GoTo 0
End Sub

Function RecoveryPaths()
    Dim list
    list = ""
    If fso.FileExists(oldCopyPath) Then list = list & oldCopyPath & ";"
    If fso.FileExists(backupPath) Then list = list & backupPath & ";"
    If fso.FileExists(manifestOldCopyPath) Then list = list & manifestOldCopyPath & ";"
    If fso.FileExists(manifestBackupPath) Then list = list & manifestBackupPath & ";"
    If fso.FileExists(tempPath) Then list = list & tempPath & ";"
    If fso.FileExists(manifestTempPath) Then list = list & manifestTempPath & ";"
    If list = "" Then list = "none"
    RecoveryPaths = list
End Function

Sub Rollback(ByVal headline, ByVal detail)
    ' Every step records its own error and re-verifies the restored bytes, so a
    ' partial restore can never be reported as a successful one.
    LogMsg "ROLLBACK|" & headline & "|" & detail
    Dim addinOk, manifestOk
    addinOk = RestoreAddin()
    manifestOk = RestoreManifest()
    DiscardOwnFile tempPath
    DiscardOwnFile manifestTempPath
    If addinOk And manifestOk Then
        DiscardOwnFile oldCopyPath
        DiscardOwnFile manifestOldCopyPath
        LogMsg "RESTORE|" & restoreReport
        If previousAddinExisted Or previousManifestExisted Then
            Fail headline & ": " & detail & "|previous add-in restored and verified|" & restoreReport
        Else
            Fail headline & ": " & detail & "|first installation rolled back; no previous add-in existed|" & restoreReport
        End If
    End If
    LogMsg "RECOVERY|" & RecoveryPaths()
    Fail headline & ": " & detail & "|ROLLBACK_INCOMPLETE|" & restoreReport & "|recovery=" & RecoveryPaths()
End Sub

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

Dim runStamp, targetPath, tempPath, backupPath, oldCopyPath
Dim manifestPath, manifestTempPath, manifestBackupPath, manifestOldCopyPath
Dim backupMade, manifestBackupMade, addinActivated, manifestActivated
Dim previousAddinExisted, previousManifestExisted
Dim compareError, restoreReport, installedSize, manifestText, manifestStamp

runStamp = Stamp()
targetPath = fso.BuildPath(startupPath, "PatentMarker.dotm")
manifestPath = fso.BuildPath(startupPath, "PatentMarker.addin.install.txt")
tempPath = targetPath & ".pm-new-" & runStamp
backupPath = targetPath & ".pm-backup-" & runStamp
oldCopyPath = targetPath & ".pm-old-" & runStamp
manifestTempPath = manifestPath & ".pm-new-" & runStamp
manifestBackupPath = manifestPath & ".pm-backup-" & runStamp
manifestOldCopyPath = manifestPath & ".pm-old-" & runStamp
backupMade = False
manifestBackupMade = False
addinActivated = False
manifestActivated = False
restoreReport = ""

If LCase(fso.GetAbsolutePathName(sourcePath)) = LCase(fso.GetAbsolutePathName(targetPath)) Then Fail "source and target add-in paths are identical"
If fso.FileExists(tempPath) Or fso.FileExists(backupPath) Or fso.FileExists(oldCopyPath) Then Fail "staging path already exists; retry after one second"
If fso.FileExists(manifestTempPath) Or fso.FileExists(manifestBackupPath) Or fso.FileExists(manifestOldCopyPath) Then Fail "manifest staging path already exists; retry after one second"

' ---- 1. Stage the add-in and prove the staged bytes before touching the target.
If Not CopyFileChecked(sourcePath, tempPath, errorText) Then Fail "cannot stage add-in: " & errorText
If Not FilesEqual(sourcePath, tempPath, compareError) Then
    DiscardOwnFile tempPath
    Fail "staged add-in verification failed: " & compareError
End If

' ---- 2. Keep a verified reference copy of the previous add-in, then move it aside.
'         The reference copy exists before any rename, so every later restore has
'         something to check against.
previousAddinExisted = fso.FileExists(targetPath)
If previousAddinExisted Then
    If Not CopyFileChecked(targetPath, oldCopyPath, errorText) Then
        DiscardOwnFile tempPath
        DiscardOwnFile oldCopyPath
        Fail "cannot keep a reference copy of the existing add-in: " & errorText
    End If
    If Not FilesEqual(targetPath, oldCopyPath, compareError) Then
        DiscardOwnFile tempPath
        DiscardOwnFile oldCopyPath
        Fail "reference copy of the existing add-in does not match: " & compareError
    End If
    If Not MoveFileChecked(targetPath, backupPath, errorText) Then
        DiscardOwnFile tempPath
        DiscardOwnFile oldCopyPath
        Fail "cannot back up existing PatentMarker.dotm: " & errorText
    End If
    backupMade = True
End If

' ---- 3. Activate the staged add-in and prove the installed bytes.
If InjectedFault("addin-activate") Then
    LogMsg "INJECTED_FAULT|addin-activate"
    Rollback "cannot activate staged add-in", "injected failure before activation"
End If
If Not MoveFileChecked(tempPath, targetPath, errorText) Then
    Rollback "cannot activate staged add-in", errorText
End If
addinActivated = True

If Not FilesEqual(sourcePath, targetPath, compareError) Then
    Rollback "installed add-in verification failed", compareError
End If

' ---- 4. Keep a verified reference copy of the previous manifest, then move it aside.
previousManifestExisted = fso.FileExists(manifestPath)
If previousManifestExisted Then
    If Not CopyFileChecked(manifestPath, manifestOldCopyPath, errorText) Then
        Rollback "cannot keep a reference copy of the existing ownership manifest", errorText
    End If
    If Not FilesEqual(manifestPath, manifestOldCopyPath, compareError) Then
        Rollback "reference copy of the existing ownership manifest does not match", compareError
    End If
    If Not MoveFileChecked(manifestPath, manifestBackupPath, errorText) Then
        Rollback "cannot back up the existing ownership manifest", errorText
    End If
    manifestBackupMade = True
End If

' ---- 5. Write the new ownership manifest to this run's staging file, close it,
'         read it back and compare before it is allowed near the product path.
On Error Resume Next
Err.Clear
installedSize = fso.GetFile(targetPath).Size
compareError = Err.Description
Dim sizeErr
sizeErr = Err.Number
On Error GoTo 0
If sizeErr <> 0 Then
    Rollback "cannot measure the installed add-in", CStr(sizeErr) & ": " & compareError
End If

manifestStamp = FormatDateTime(Now, 0)
manifestText = "product=PatentMarker" & vbCrLf & _
    "file=PatentMarker.dotm" & vbCrLf & _
    "size=" & CStr(installedSize) & vbCrLf & _
    "installed_at=" & manifestStamp & vbCrLf

Dim manifestStream, writeErr, writeDescription
On Error Resume Next
Err.Clear
Set manifestStream = fso.OpenTextFile(manifestTempPath, ForWriting, True, 0)
writeErr = Err.Number
writeDescription = Err.Description
On Error GoTo 0
If writeErr <> 0 Then
    Rollback "cannot create the staged ownership manifest", CStr(writeErr) & ": " & writeDescription
End If
LogMsg "STAGED_MANIFEST|" & manifestTempPath

On Error Resume Next
Err.Clear
manifestStream.WriteLine "product=PatentMarker"
If InjectedFault("manifest-write") Then
    LogMsg "INJECTED_FAULT|manifest-write"
    Err.Raise 70, "PatentMarker.install-vba", "injected failure while writing the staged manifest"
End If
If Err.Number = 0 Then
    manifestStream.WriteLine "file=PatentMarker.dotm"
    manifestStream.WriteLine "size=" & CStr(installedSize)
    manifestStream.WriteLine "installed_at=" & manifestStamp
End If
If InjectedFault("manifest-close") Then
    LogMsg "INJECTED_FAULT|manifest-close"
    Err.Raise 70, "PatentMarker.install-vba", "injected failure while closing the staged manifest"
End If
manifestStream.Close
writeErr = Err.Number
writeDescription = Err.Description
On Error GoTo 0
If writeErr <> 0 Then
    Rollback "cannot write the staged ownership manifest", CStr(writeErr) & ": " & writeDescription
End If

Dim readBack, readErr, readDescription
On Error Resume Next
Err.Clear
Set manifestStream = fso.OpenTextFile(manifestTempPath, ForReading, False, 0)
readBack = manifestStream.ReadAll
manifestStream.Close
readErr = Err.Number
readDescription = Err.Description
On Error GoTo 0
If readErr <> 0 Then
    Rollback "cannot read back the staged ownership manifest", CStr(readErr) & ": " & readDescription
End If
If InjectedFault("manifest-content") Then
    LogMsg "INJECTED_FAULT|manifest-content"
    readBack = readBack & "injected" & vbCrLf
End If
If readBack <> manifestText Then
    Rollback "staged ownership manifest content mismatch", _
        "readback_bytes=" & CStr(Len(readBack)) & "|expected_bytes=" & CStr(Len(manifestText))
End If

' ---- 6. Activate the verified manifest. Only a file this run staged may land here.
If InjectedFault("manifest-activate") Then
    LogMsg "INJECTED_FAULT|manifest-activate"
    Rollback "cannot activate the ownership manifest", "injected failure before activation"
End If
If fso.FileExists(manifestPath) Then
    Rollback "cannot activate the ownership manifest", "target path became occupied during installation"
End If
If Not MoveFileChecked(manifestTempPath, manifestPath, errorText) Then
    Rollback "cannot activate the ownership manifest", errorText
End If
manifestActivated = True

On Error Resume Next
Err.Clear
Set manifestStream = fso.OpenTextFile(manifestPath, ForReading, False, 0)
readBack = manifestStream.ReadAll
manifestStream.Close
readErr = Err.Number
readDescription = Err.Description
On Error GoTo 0
If readErr <> 0 Then
    Rollback "cannot verify the activated ownership manifest", CStr(readErr) & ": " & readDescription
End If
If readBack <> manifestText Then
    Rollback "activated ownership manifest content mismatch", _
        "readback_bytes=" & CStr(Len(readBack)) & "|expected_bytes=" & CStr(Len(manifestText))
End If

DiscardOwnFile oldCopyPath
DiscardOwnFile manifestOldCopyPath

LogMsg "WORD_VERSION|" & wordVersion
LogMsg "STARTUP_PATH|" & startupPath
LogMsg "NORMAL_TEMPLATE|not_read_or_written"
LogMsg "ADDIN_PATH|" & targetPath
LogMsg "MANIFEST_PATH|" & manifestPath
If backupMade Then LogMsg "PREVIOUS_ADDIN_BACKUP|" & backupPath
If manifestBackupMade Then LogMsg "PREVIOUS_MANIFEST_BACKUP|" & manifestBackupPath
LogMsg "PASS|add-in installed byte-for-byte; restart Word for AutoExec"
Finish 0

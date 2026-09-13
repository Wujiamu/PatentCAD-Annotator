' PatentMarker Word add-in uninstaller
' Removes only the owned Startup add-in. Normal.dotm is never opened or edited.

Option Explicit

Dim fso, startupPath, targetPath, manifestPath, backupPath, manifestBackupPath, app, errorText
Set fso = CreateObject("Scripting.FileSystemObject")

Function Stamp()
    Stamp = Year(Now) & Right("0" & Month(Now), 2) & Right("0" & Day(Now), 2) & "-" & _
        Right("0" & Hour(Now), 2) & Right("0" & Minute(Now), 2) & Right("0" & Second(Now), 2)
End Function

Function WordProcessCount(ByRef failure)
    On Error Resume Next
    Dim svc, processes, errNo, errDesc
    failure = ""
    Set svc = Nothing
    Set processes = Nothing
    Set svc = GetObject("winmgmts:\\.\root\cimv2")
    If Err.Number = 0 Then Set processes = svc.ExecQuery("SELECT ProcessId FROM Win32_Process WHERE Name='WINWORD.EXE'")
    errNo = Err.Number
    errDesc = Err.Description
    On Error GoTo 0
    If errNo <> 0 Or processes Is Nothing Then
        failure = CStr(errNo) & ": " & errDesc
        WordProcessCount = -1
    Else
        WordProcessCount = processes.Count
    End If
End Function

Sub Fail(ByVal message)
    WScript.Echo "FAIL|" & message
    WScript.Quit 1
End Sub

If WScript.Arguments.Named.Exists("StartupPath") Then
    startupPath = WScript.Arguments.Named.Item("StartupPath")
Else
    Dim count
    count = WordProcessCount(errorText)
    If count < 0 Then Fail "cannot determine whether Word is running: " & errorText
    If count > 0 Then Fail "Word is running; close all Word windows before uninstalling"
    On Error Resume Next
    Set app = CreateObject("Word.Application")
    If Err.Number = 0 Then startupPath = app.StartupPath
    If IsObject(app) Then app.Quit False
    Dim errNo, errDesc
    errNo = Err.Number
    errDesc = Err.Description
    Set app = Nothing
    On Error GoTo 0
    If errNo <> 0 Or startupPath = "" Then Fail "cannot resolve Word Startup path: " & CStr(errNo) & ": " & errDesc
End If

targetPath = fso.BuildPath(startupPath, "PatentMarker.dotm")
manifestPath = fso.BuildPath(startupPath, "PatentMarker.addin.install.txt")
If Not fso.FileExists(targetPath) Then
    WScript.Echo "PASS|PatentMarker.dotm is not installed"
    WScript.Quit 0
End If
If Not fso.FileExists(manifestPath) And Not WScript.Arguments.Named.Exists("Force") Then
    Fail "ownership manifest is missing; refusing to remove the file without /Force"
End If

Dim uninstallStamp
uninstallStamp = Stamp()
backupPath = targetPath & ".uninstalled-" & uninstallStamp & ".bak"
manifestBackupPath = manifestPath & ".uninstalled-" & uninstallStamp & ".bak"
If fso.FileExists(backupPath) Then Fail "backup path already exists; retry after one second"
If fso.FileExists(manifestBackupPath) Then Fail "manifest backup path already exists; retry after one second"
On Error Resume Next
Err.Clear
fso.MoveFile targetPath, backupPath
errNo = Err.Number
errDesc = Err.Description
On Error GoTo 0
If errNo <> 0 Then Fail "cannot move installed add-in to recoverable backup: " & CStr(errNo) & ": " & errDesc

If fso.FileExists(manifestPath) Then
    On Error Resume Next
    Err.Clear
    fso.MoveFile manifestPath, manifestBackupPath
    errNo = Err.Number
    errDesc = Err.Description
    If errNo <> 0 Then
        Err.Clear
        fso.MoveFile backupPath, targetPath
    End If
    On Error GoTo 0
    If errNo <> 0 Then Fail "cannot preserve the ownership manifest; installed add-in restored: " & CStr(errNo) & ": " & errDesc
End If

WScript.Echo "PASS|PatentMarker Word add-in removed; recoverable backup: " & backupPath
WScript.Quit 0

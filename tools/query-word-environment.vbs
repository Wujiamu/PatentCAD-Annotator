' Query Word paths in an owned process without editing templates.
Option Explicit

Dim fso, app, startupPath, normalPath, wordVersion, wordExe, failure
Set fso = CreateObject("Scripting.FileSystemObject")
Set app = Nothing

Dim count
count = WordProcessCount(failure)
If count < 0 Then Fail "cannot inspect Word processes: " & failure
If count > 0 Then Fail "Word is already running"

On Error Resume Next
Err.Clear
Set app = CreateObject("Word.Application")
If Err.Number <> 0 Then CaptureAndFail "cannot create Word.Application"
app.Visible = False
app.DisplayAlerts = 0
startupPath = app.StartupPath
normalPath = app.NormalTemplate.FullName
wordVersion = app.Version
wordExe = fso.BuildPath(app.Path, "WINWORD.EXE")
If Err.Number <> 0 Then CaptureAndFail "cannot read Word environment"
app.Quit False
Set app = Nothing
On Error GoTo 0

If Not WaitForNoWord(20) Then Fail "owned Word process did not exit within 20 seconds"
WScript.Echo "STARTUP_PATH|" & startupPath
WScript.Echo "NORMAL_PATH|" & normalPath
WScript.Echo "WORD_VERSION|" & wordVersion
WScript.Echo "WORD_EXE|" & wordExe
WScript.Echo "PASS|WORD_ENVIRONMENT|owned_process_exited=true"
WScript.Quit 0

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
    If IsObject(app) Then app.Quit False
    Set app = Nothing
    On Error GoTo 0
    Fail prefix & " (" & CStr(number) & "): " & description
End Sub

Sub Fail(ByVal message)
    On Error Resume Next
    If IsObject(app) Then app.Quit False
    Set app = Nothing
    On Error GoTo 0
    WScript.Echo "FAIL|WORD_ENVIRONMENT|" & message
    WScript.Quit 1
End Sub

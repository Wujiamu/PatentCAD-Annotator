' Read-only inventory of the user's Normal template VBA components.
' It never exports, removes, imports, edits, or saves a component/template.
Option Explicit

Dim app, project, component, codeModule, failure
Dim totalCount, productCount, unrelatedCount
Set app = Nothing
Set project = Nothing
Set component = Nothing
Set codeModule = Nothing

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
Set project = app.NormalTemplate.VBProject
If Err.Number <> 0 Or project Is Nothing Then CaptureAndFail "cannot read Normal VBA project"

For Each component In project.VBComponents
    totalCount = totalCount + 1
    Set codeModule = component.CodeModule
    Dim lineCount
    lineCount = 0
    If Err.Number = 0 Then lineCount = codeModule.CountOfLines
    Err.Clear
    If IsProductComponent(component.Name) Then
        productCount = productCount + 1
        WScript.Echo "PRODUCT_COMPONENT|" & component.Name & "|type=" & CStr(component.Type) & "|lines=" & CStr(lineCount)
    Else
        unrelatedCount = unrelatedCount + 1
        WScript.Echo "UNRELATED_COMPONENT|" & component.Name & "|type=" & CStr(component.Type) & "|lines=" & CStr(lineCount)
    End If
    Set codeModule = Nothing
Next
If Err.Number <> 0 Then CaptureAndFail "cannot enumerate Normal VBA components"

Set component = Nothing
Set project = Nothing
app.Quit False
Set app = Nothing
On Error GoTo 0
If Not WaitForNoWord(20) Then Fail "owned Word process did not exit within 20 seconds"
WScript.Echo "PASS|NORMAL_VBA_INVENTORY|total=" & CStr(totalCount) & "|product=" & CStr(productCount) & "|unrelated=" & CStr(unrelatedCount) & "|writes=0"
WScript.Quit 0

Function IsProductComponent(ByVal componentName)
    Dim name
    IsProductComponent = False
    For Each name In Array("Patterns", "DictModel", "JsonWriter", "PatentExtractor", "AutoExport", "clsSaveHook", "PatentDictPanel", "PatentMarkerBootstrap")
        If StrComp(componentName, name, 1) = 0 Then
            IsProductComponent = True
            Exit Function
        End If
    Next
End Function

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
    If InStr(1, prefix, "Normal VBA", 1) > 0 Then
        description = "normal_vbproject_unreadable (trust policy or project protection)"
    End If
    On Error Resume Next
    Set codeModule = Nothing
    Set component = Nothing
    Set project = Nothing
    If IsObject(app) Then app.Quit False
    Set app = Nothing
    On Error GoTo 0
    Fail prefix & " (" & CStr(number) & "): " & description
End Sub

Sub Fail(ByVal message)
    On Error Resume Next
    Set codeModule = Nothing
    Set component = Nothing
    Set project = Nothing
    If IsObject(app) Then app.Quit False
    Set app = Nothing
    On Error GoTo 0
    WScript.Echo "FAIL|NORMAL_VBA_INVENTORY|" & message
    WScript.Quit 1
End Sub

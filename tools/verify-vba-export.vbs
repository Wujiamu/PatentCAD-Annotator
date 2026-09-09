Option Explicit

' verify-vba-export.vbs - exercise Word-side mapping and save failure policy.
' Usage: cscript //nologo verify-vba-export.vbs <vba-directory>

Dim fso, word, vbaDir, rootDir
Set fso = CreateObject("Scripting.FileSystemObject")
If WScript.Arguments.Count < 1 Then Fail "missing VBA directory"
vbaDir = WScript.Arguments(0)
If Not fso.FolderExists(vbaDir) Then Fail "VBA directory not found: " & vbaDir
vbaDir = fso.GetAbsolutePathName(vbaDir)

rootDir = fso.BuildPath(fso.GetSpecialFolder(2), "PatentMarkerExport-" & Replace(fso.GetTempName, ".tmp", ""))
fso.CreateFolder rootDir

Set word = CreateObject("Word.Application")
word.Visible = False
word.DisplayAlerts = 0

' One DWG-free document falls back to its own base name.
Dim singleDir, singleDoc, singleJson
singleDir = MakeDir("single")
Set singleDoc = OpenDoc(singleDir, "single.docm")
singleDoc.Activate
word.Run "AutoExport.ExportDict"
singleJson = fso.BuildPath(singleDir, "single.dict.json")
If Not fso.FileExists(singleJson) Then Fail "single-DWG-free output missing"
singleDoc.Close False
Set singleDoc = Nothing

' Multiple compatibility matches must fail closed and write a diagnostic.
Dim ambiguousDir, ambiguousDoc, errorPath
ambiguousDir = MakeDir("ambiguous")
Touch fso.BuildPath(ambiguousDir, "ambiguous-alpha.dwg")
Touch fso.BuildPath(ambiguousDir, "ambiguous-beta.dwg")
Set ambiguousDoc = OpenDoc(ambiguousDir, "ambiguous.docm")
ambiguousDoc.Activate
word.Run "AutoExport.ExportDict"
If fso.FileExists(fso.BuildPath(ambiguousDir, "ambiguous.dict.json")) Then Fail "ambiguous mapping unexpectedly exported"
errorPath = fso.BuildPath(ambiguousDir, "autoexport-error.txt")
If Not fso.FileExists(errorPath) Then Fail "ambiguous mapping error report missing"
ambiguousDoc.Close False
Set ambiguousDoc = Nothing

' Manual export can select a DWG by full path, and the selection is reused by auto-save.
Dim manualDir, manualDoc, manualTarget, manualResult, manualJson, manualOtherJson
manualDir = MakeDir("manual-selection")
Touch fso.BuildPath(manualDir, "manual-alpha.dwg")
Touch fso.BuildPath(manualDir, "manual-beta.dwg")
Set manualDoc = OpenDoc(manualDir, "manual.docm")
manualDoc.Activate
manualTarget = fso.BuildPath(manualDir, "manual-beta.dwg")
manualResult = word.Run("ExportHarness.ManualExportTo", manualTarget)
If Not manualResult Then Fail "manual DWG selection export failed"
manualJson = fso.BuildPath(manualDir, "manual-beta.dict.json")
manualOtherJson = fso.BuildPath(manualDir, "manual-alpha.dict.json")
If Not fso.FileExists(manualJson) Then Fail "selected-DWG dictionary missing"
If fso.FileExists(manualOtherJson) Then Fail "unselected-DWG dictionary was written"
word.Run "ExportHarness.EnableAutoExportForTest"
manualDoc.Content.Text = MarkingText() & vbCr & "changed"
manualDoc.Save
If Not manualDoc.Saved Then Fail "automatic save did not reuse manual DWG selection"
word.Run "ExportHarness.DisableAutoExportForTest"
manualDoc.Close False
Set manualDoc = Nothing

' An exact base-name match does not bypass the manual-selection requirement.
Dim exactDir, exactDoc, exactJson, exactErrorPath
exactDir = MakeDir("exact")
Touch fso.BuildPath(exactDir, "exact.dwg")
Touch fso.BuildPath(exactDir, "exact-other.dwg")
Set exactDoc = OpenDoc(exactDir, "exact.docm")
exactDoc.Activate
word.Run "AutoExport.ExportDict"
exactJson = fso.BuildPath(exactDir, "exact.dict.json")
If fso.FileExists(exactJson) Then Fail "exact-name mapping bypassed manual selection"
exactErrorPath = fso.BuildPath(exactDir, "autoexport-error.txt")
If Not fso.FileExists(exactErrorPath) Then Fail "exact-name mapping error report missing"
exactDoc.Close False
Set exactDoc = Nothing

' Automatic save cancels when an already-saved document cannot export safely.
Dim saveDir, saveDoc
saveDir = MakeDir("save-hook")
Touch fso.BuildPath(saveDir, "save-hook-alpha.dwg")
Touch fso.BuildPath(saveDir, "save-hook-beta.dwg")
Set saveDoc = OpenDoc(saveDir, "save-hook.docm")
saveDoc.Activate
word.Run "ExportHarness.EnableAutoExportForTest"
saveDoc.Content.Text = MarkingText() & vbCr & "changed"
Dim saveError
saveError = 0
On Error Resume Next
Err.Clear
saveDoc.Save
saveError = Err.Number
Err.Clear
On Error GoTo 0
If saveDoc.Saved Then Fail "save hook did not cancel an unsafe export"
word.Run "ExportHarness.DisableAutoExportForTest"
saveDoc.Close False
Set saveDoc = Nothing

CloseAllWordDocuments
word.Quit False
Set word = Nothing
WaitForWordProcessesToExit
DeleteGeneratedDocuments rootDir
WScript.Echo "PASS|Word export mapping and save-hook checks"
WScript.Quit 0

Sub CloseAllWordDocuments()
    On Error Resume Next
    Dim n, closeDoc
    If IsObject(word) Then
        For n = word.Documents.Count To 1 Step -1
            Set closeDoc = word.Documents.Item(n)
            closeDoc.Close False
            Set closeDoc = Nothing
        Next
    End If
    On Error GoTo 0
End Sub

Sub WaitForWordProcessesToExit()
    On Error Resume Next
    Dim attempt, svc, processes, errNo
    For attempt = 1 To 20
        Set svc = Nothing
        Set processes = Nothing
        Err.Clear
        Set svc = GetObject("winmgmts:\\.\root\cimv2")
        If Err.Number = 0 Then Set processes = svc.ExecQuery("SELECT ProcessId FROM Win32_Process WHERE Name='WINWORD.EXE'")
        errNo = Err.Number
        On Error GoTo 0
        If errNo <> 0 Or processes Is Nothing Then Exit Sub
        If processes.Count = 0 Then Exit Sub
        WScript.Sleep 500
        On Error Resume Next
    Next
    On Error GoTo 0
End Sub

Sub DeleteGeneratedDocuments(ByVal folderPath)
    On Error Resume Next
    Dim folder, file, child
    Set folder = fso.GetFolder(folderPath)
    For Each file In folder.Files
        If LCase(fso.GetExtensionName(file.Name)) = "docm" Then
            fso.DeleteFile file.Path, True
        End If
    Next
    For Each child In folder.SubFolders
        DeleteGeneratedDocuments child.Path
    Next
    On Error GoTo 0
End Sub

Function MakeDir(ByVal name)
    Dim p
    p = fso.BuildPath(rootDir, name)
    fso.CreateFolder p
    MakeDir = p
End Function

Function OpenDoc(ByVal dir, ByVal name)
    Dim doc, moduleName, path, errNo, errDesc
    path = fso.BuildPath(dir, name)
    On Error Resume Next
    Err.Clear
    Set doc = word.Documents.Add
    If Err.Number <> 0 Then
        errNo = Err.Number
        errDesc = Err.Description
        On Error GoTo 0
        Fail "cannot create " & name & " (" & CStr(errNo) & "): " & errDesc
    End If
    doc.Content.Text = MarkingText()
    doc.SaveAs2 path, 13
    If Err.Number <> 0 Then
        errNo = Err.Number
        errDesc = Err.Description
        On Error GoTo 0
        Fail "cannot save " & name & " (" & CStr(errNo) & "): " & errDesc
    End If
    For Each moduleName In Array("Patterns.bas", "DictModel.bas", "JsonWriter.bas", "PatentExtractor.bas", "clsSaveHook.cls", "AutoExport.bas")
        doc.VBProject.VBComponents.Import fso.BuildPath(vbaDir, moduleName)
        If Err.Number <> 0 Then
            errNo = Err.Number
            errDesc = Err.Description
            On Error GoTo 0
            Fail "cannot import " & moduleName & " into " & name & " (" & CStr(errNo) & "): " & errDesc
        End If
    Next
    InstallHarness doc
    On Error GoTo 0
    doc.Save
    Set OpenDoc = doc
End Function

Sub InstallHarness(ByVal doc)
    Dim component, code
    Set component = doc.VBProject.VBComponents.Add(1)
    component.Name = "ExportHarness"
    code = "Option Explicit" & vbCrLf & _
        "Public Sub EnableAutoExportForTest()" & vbCrLf & _
        "    AutoExport.IsAutoExportEnabled = True" & vbCrLf & _
        "End Sub" & vbCrLf & _
        "Public Sub DisableAutoExportForTest()" & vbCrLf & _
        "    AutoExport.IsAutoExportEnabled = False" & vbCrLf & _
        "End Sub" & vbCrLf & _
        "Public Function ManualExportTo(ByVal targetPath As String) As Boolean" & vbCrLf & _
        "    ManualExportTo = AutoExport.ExportDictManual(targetPath)" & vbCrLf & _
        "End Function"
    component.CodeModule.AddFromString code
End Sub

Function MarkingText()
    MarkingText = ChrW(&H9644) & ChrW(&H56FE) & ChrW(&H6807) & ChrW(&H8BB0) & ChrW(&H8BF4) & ChrW(&H660E) & ChrW(&HFF1A) & _
        "1" & ChrW(&H5E95) & ChrW(&H5EA7) & ChrW(&HFF0C) & "2" & ChrW(&H652F) & ChrW(&H67B6) & ChrW(&HFF1B)
End Function

Sub Touch(ByVal path)
    Dim file
    Set file = fso.CreateTextFile(path, True, False)
    file.Close
End Sub

Sub Fail(ByVal message)
    On Error Resume Next
    If IsObject(word) Then word.Run "ExportHarness.DisableAutoExportForTest"
    CloseAllWordDocuments
    Set saveDoc = Nothing
    Set exactDoc = Nothing
    Set manualDoc = Nothing
    Set ambiguousDoc = Nothing
    Set singleDoc = Nothing
    If IsObject(word) Then word.Quit False
    Set word = Nothing
    WaitForWordProcessesToExit
    DeleteGeneratedDocuments rootDir
    WScript.Echo "FAIL|" & message
    WScript.Quit 1
End Sub

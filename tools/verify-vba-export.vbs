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

' An exact base-name match wins even when other DWGs are present.
Dim exactDir, exactDoc, exactJson
exactDir = MakeDir("exact")
Touch fso.BuildPath(exactDir, "exact.dwg")
Touch fso.BuildPath(exactDir, "exact-other.dwg")
Set exactDoc = OpenDoc(exactDir, "exact.docm")
exactDoc.Activate
word.Run "AutoExport.ExportDict"
exactJson = fso.BuildPath(exactDir, "exact.dict.json")
If Not fso.FileExists(exactJson) Then Fail "exact mapping output missing"
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

word.Quit
Set word = Nothing
WScript.Echo "PASS|Word export mapping and save-hook checks"
WScript.Quit 0

Function MakeDir(ByVal name)
    Dim p
    p = fso.BuildPath(rootDir, name)
    fso.CreateFolder p
    MakeDir = p
End Function

Function OpenDoc(ByVal dir, ByVal name)
    Dim doc, moduleName, path
    path = fso.BuildPath(dir, name)
    Set doc = word.Documents.Add
    doc.Content.Text = MarkingText()
    doc.SaveAs2 path, 13
    For Each moduleName In Array("Patterns.bas", "DictModel.bas", "JsonWriter.bas", "PatentExtractor.bas", "clsSaveHook.cls", "AutoExport.bas")
        doc.VBProject.VBComponents.Import fso.BuildPath(vbaDir, moduleName)
    Next
    InstallHarness doc
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
        "End Sub"
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
    If Not word Is Nothing Then word.Quit
    WScript.Echo "FAIL|" & message
    WScript.Quit 1
End Sub

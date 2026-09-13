Option Explicit

' test-vba-panel.vbs - L2 source-import check for the PatentDictPanel workflow:
'   1. All 8 VBA components / 9 physical files import or pair cleanly
'   2. Standard modules expose one user macro plus AutoExec/AutoExit lifecycle Subs
'   3. Manual export path (AutoExport.ExportDict) produces <name>.dict.json
'   4. Auto-export toggle (AutoExport.IsAutoExportEnabled) works on/off
'
' Usage: cscript test-vba-panel.vbs [vbaDir]

Dim fso, shell, scriptDir, workspace, vbaDir, tempDir, logPath
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
workspace = fso.GetParentFolderName(scriptDir)

If WScript.Arguments.Count > 0 Then
    vbaDir = WScript.Arguments(0)
Else
    vbaDir = fso.BuildPath(workspace, "PatentMarker-2025-deploy\vba")
End If
vbaDir = fso.GetAbsolutePathName(vbaDir)

tempDir = fso.BuildPath(fso.GetSpecialFolder(2), "PatentMarkerPanel-" & Replace(fso.GetTempName, ".tmp", ""))
fso.CreateFolder tempDir
logPath = fso.BuildPath(tempDir, "test.log")

Dim word, doc, docPath, jsonPath, moduleName, modules
docPath = fso.BuildPath(tempDir, "panel.docm")
jsonPath = fso.BuildPath(tempDir, "panel.dict.json")

On Error GoTo 0
LogLine "START"

If Not fso.FolderExists(vbaDir) Then Fail "VBA_DIR_NOT_FOUND " & vbaDir
' All 9 files must exist (6 standard modules + class + UserForm .frm/.frx pair)
modules = Array("Patterns.bas", "DictModel.bas", "JsonWriter.bas", "PatentExtractor.bas", _
                "clsSaveHook.cls", "AutoExport.bas", "PatentMarkerBootstrap.bas", _
                "PatentDictPanel.frm", "PatentDictPanel.frx")
For Each moduleName In modules
    If Not fso.FileExists(fso.BuildPath(vbaDir, moduleName)) Then Fail "FILE_NOT_FOUND " & moduleName
Next
LogLine "FILES_OK"

Set word = CreateObject("Word.Application")
word.Visible = False
word.DisplayAlerts = 0
LogLine "WORD_STARTED"

Set doc = word.Documents.Add
doc.SaveAs2 docPath, 13
LogLine "DOC_SAVED"

' --- 1. Import all modules. The .frm auto-loads its .frx companion blob
'        (referenced by OleObjectBlob); .frx must NOT be imported directly.
For Each moduleName In modules
    If LCase(fso.GetExtensionName(moduleName)) <> "frx" Then
        On Error Resume Next
        Err.Clear
        doc.VBProject.VBComponents.Import fso.BuildPath(vbaDir, moduleName)
        importErrNo = Err.Number
        importErrDesc = Err.Description
        On Error GoTo 0
        If importErrNo <> 0 Then Fail "MODULE_IMPORT_FAILED " & moduleName & " " & CStr(importErrNo) & ": " & importErrDesc
        LogLine "MODULE_IMPORTED " & moduleName
    Else
        LogLine "FRX_PRESENT (auto-loaded by .frm) " & moduleName
    End If
Next
doc.Save
LogLine "ALL_IMPORTED"

' --- 2. Source shape contains exactly three no-argument public Subs: one
'        supported user entry plus AutoExec/AutoExit lifecycle entries.
'        This is not visual evidence of the Alt+F8 macro dialog.
Dim comp, comps, compType, src, lines, i, publicSubs, publicSubNames, procedureName, importErrNo, importErrDesc
Set comps = doc.VBProject.VBComponents
publicSubs = 0
publicSubNames = ""
For Each comp In comps
    compType = comp.Type
    If compType = 1 Then ' vbext_ct_StdModule
        For i = 1 To comp.CodeModule.CountOfLines
            procedureName = NoArgumentPublicSubName(comp.CodeModule.Lines(i, 1))
            If procedureName <> "" Then
                publicSubs = publicSubs + 1
                If publicSubNames <> "" Then publicSubNames = publicSubNames & ","
                publicSubNames = publicSubNames & procedureName
            End If
        Next
    End If
Next
LogLine "NO_ARG_PUBLIC_SUBS=" & publicSubs & " names=" & publicSubNames
If publicSubs <> 3 Then Fail "EXPECTED_3_PUBLIC_SUBS_GOT_" & publicSubs
For Each procedureName In Array("ShowPatentDictPanel", "AutoExec", "AutoExit")
    If InStr(1, "," & publicSubNames & ",", "," & procedureName & ",", vbTextCompare) = 0 Then Fail "PUBLIC_SUB_MISSING_" & procedureName
Next
src = comps("AutoExport").CodeModule.Lines(1, comps("AutoExport").CodeModule.CountOfLines)
If InStr(1, src, "Sub ShowPatentDictPanel", vbTextCompare) = 0 Then Fail "SHOW_MACRO_MISSING"
src = comps("PatentMarkerBootstrap").CodeModule.Lines(1, comps("PatentMarkerBootstrap").CodeModule.CountOfLines)
If InStr(1, src, "Public Sub AutoExec()", vbTextCompare) = 0 Then Fail "AUTOEXEC_MISSING"
If InStr(1, src, "Public Sub AutoExit()", vbTextCompare) = 0 Then Fail "AUTOEXIT_MISSING"
LogLine "MACRO_LIST_OK"

' --- 3. UserForm present with expected controls ---
Dim frmComp
Set frmComp = doc.VBProject.VBComponents("PatentDictPanel")
' vbext_ct_MSForm = 3 (100 is vbext_ct_Document, used by ThisDocument)
If frmComp.Type <> 3 Then Fail "FORM_TYPE_UNEXPECTED " & frmComp.Type
LogLine "FORM_IMPORTED"
frmComp.CodeModule.AddFromString "Public Sub InvokeAutoExportHandler()" & vbCrLf & _
    "    chkAutoExport.Value = 1" & vbCrLf & _
    "    chkAutoExport_Click" & vbCrLf & _
    "End Sub" & vbCrLf & _
    "Public Sub InvokeJsonVisibilityOn()" & vbCrLf & _
    "    chkJsonVisible.Enabled = True" & vbCrLf & _
    "    chkJsonVisible.Value = 1" & vbCrLf & _
    "    chkJsonVisible_Click" & vbCrLf & _
    "End Sub" & vbCrLf & _
    "Public Sub InvokeJsonVisibilityOff()" & vbCrLf & _
    "    chkJsonVisible.Enabled = True" & vbCrLf & _
    "    chkJsonVisible.Value = 0" & vbCrLf & _
    "    chkJsonVisible_Click" & vbCrLf & _
    "End Sub"

' --- 4. Manual export + toggle via the SAME code paths the form uses ---
' Inject a probe Sub into ThisDocument that mirrors cmdExport_Click and
' chkAutoExport_Click behavior (direct in-process calls), and asserts the
' form + control captions match the expected Chinese labels.
Dim thisDoc, stub
Set thisDoc = doc.VBProject.VBComponents("ThisDocument").CodeModule
stub = "Public Sub PanelProbe()" & vbCrLf & _
       "    Dim dictPath, bakPath, fileNo" & vbCrLf & _
       "    If Not AutoExport.ExportDict Then Err.Raise 1001, , ""export_failed""" & vbCrLf & _
       "    If Len(AutoExport.GetCurrentDictPath) = 0 Then Err.Raise 1007, , ""dict_path_missing""" & vbCrLf & _
       "    If AutoExport.IsCurrentDictVisible Then Err.Raise 1008, , ""dict_should_start_hidden""" & vbCrLf & _
       "    If Not AutoExport.SetCurrentDictVisibility(True) Then Err.Raise 1009, , ""show_json_failed""" & vbCrLf & _
       "    If Not AutoExport.IsCurrentDictVisible Then Err.Raise 1010, , ""show_json_not_visible""" & vbCrLf & _
       "    If Not AutoExport.SetCurrentDictVisibility(False) Then Err.Raise 1011, , ""hide_json_failed""" & vbCrLf & _
       "    If AutoExport.IsCurrentDictVisible Then Err.Raise 1012, , ""hide_json_not_hidden""" & vbCrLf & _
       "    dictPath = AutoExport.GetCurrentDictPath" & vbCrLf & _
       "    If Not AutoExport.SetCurrentDictVisibility(True) Then Err.Raise 1014, , ""manual_edit_show_failed""" & vbCrLf & _
       "    fileNo = FreeFile" & vbCrLf & _
       "    Open dictPath For Output As #fileNo" & vbCrLf & _
       "    Print #fileNo, ""{}""" & vbCrLf & _
       "    Close #fileNo" & vbCrLf & _
       "    If Not AutoExport.ExportDict Then Err.Raise 1015, , ""manual_edit_export_failed""" & vbCrLf & _
       "    bakPath = Dir(dictPath & "".word-*.bak"", vbHidden Or vbSystem)" & vbCrLf & _
       "    If Len(bakPath) = 0 Then Err.Raise 1016, , ""manual_edit_backup_missing""" & vbCrLf & _
       "    If Not AutoExport.SetCurrentDictVisibility(False) Then Err.Raise 1017, , ""manual_edit_hide_failed""" & vbCrLf & _
       "    AutoExport.IsAutoExportEnabled = True" & vbCrLf & _
       "    If Not AutoExport.IsAutoExportEnabled Then Err.Raise 1002, , ""toggle_on_failed""" & vbCrLf & _
       "    AutoExport.IsAutoExportEnabled = False" & vbCrLf & _
       "    If AutoExport.IsAutoExportEnabled Then Err.Raise 1003, , ""toggle_off_failed""" & vbCrLf & _
       "    Load PatentDictPanel" & vbCrLf & _
       "    PatentDictPanel.InvokeAutoExportHandler" & vbCrLf & _
       "    If Not AutoExport.IsAutoExportEnabled Then Err.Raise 1018, , ""form_auto_handler_failed""" & vbCrLf & _
       "    AutoExport.IsAutoExportEnabled = False" & vbCrLf & _
       "    PatentDictPanel.InvokeJsonVisibilityOn" & vbCrLf & _
       "    If Not AutoExport.IsCurrentDictVisible Then Err.Raise 1019, , ""form_json_show_handler_failed""" & vbCrLf & _
       "    PatentDictPanel.InvokeJsonVisibilityOff" & vbCrLf & _
       "    If AutoExport.IsCurrentDictVisible Then Err.Raise 1020, , ""form_json_hide_handler_failed""" & vbCrLf & _
       "    If Len(PatentDictPanel.Caption) = 0 Then Err.Raise 1004, , ""form_caption_empty""" & vbCrLf & _
       "    If PatentDictPanel.Controls(""chkJsonVisible"").Name <> ""chkJsonVisible"" Then Err.Raise 1013, , ""json_visibility_control_missing""" & vbCrLf & _
       "    If PatentDictPanel.cmdExport.Caption <> ChrW(&H624B) & ChrW(&H52A8) & ChrW(&H5BFC) & ChrW(&H51FA) & ChrW(&H5B57) & ChrW(&H5178) Then Err.Raise 1005, , ""btn_caption""" & vbCrLf & _
       "    If PatentDictPanel.chkAutoExport.Caption <> ChrW(&H4FDD) & ChrW(&H5B58) & "" Word "" & ChrW(&H65F6) & ChrW(&H81EA) & ChrW(&H52A8) & ChrW(&H5BFC) & ChrW(&H51FA) Then Err.Raise 1006, , ""chk_caption""" & vbCrLf & _
       "End Sub"
thisDoc.AddFromString stub
doc.Save
doc.Activate

On Error Resume Next
word.Run "PanelProbe"
If Err.Number <> 0 Then
    Dim probeErr
    probeErr = Err.Description
    On Error GoTo 0
    Fail "PANEL_PROBE_FAILED " & probeErr
End If
On Error GoTo 0
LogLine "PANEL_PROBE_OK"

' --- 5. Manual export must have produced the JSON ---
If Not fso.FileExists(jsonPath) Then Fail "JSON_NOT_FOUND " & jsonPath
LogLine "JSON_CREATED bytes=" & fso.GetFile(jsonPath).Size
LogLine "JSON_PATH " & jsonPath

LogLine "PASS"
CloseAllWordDocuments
Set frmComp = Nothing
Set thisDoc = Nothing
Set comps = Nothing
Set comp = Nothing
Set doc = Nothing
word.Quit False
Set word = Nothing
DeleteGeneratedDocument docPath
WScript.Echo "PASS|L2_PANEL_SOURCE_IMPORT|" & jsonPath & "|" & logPath
WScript.Quit 0

Function NoArgumentPublicSubName(ByVal sourceLine)
    Dim declaration, lowerDeclaration, remainder, openParen, closeParen, parameterText
    NoArgumentPublicSubName = ""
    declaration = Trim(sourceLine)
    lowerDeclaration = LCase(declaration)
    If Left(lowerDeclaration, 11) = "public sub " Then
        remainder = Trim(Mid(declaration, 12))
    ElseIf Left(lowerDeclaration, 18) = "public static sub " Then
        remainder = Trim(Mid(declaration, 19))
    ElseIf Left(lowerDeclaration, 11) = "static sub " Then
        ' A procedure without an access modifier is Public in a standard module.
        remainder = Trim(Mid(declaration, 12))
    ElseIf Left(lowerDeclaration, 4) = "sub " Then
        remainder = Trim(Mid(declaration, 5))
    Else
        Exit Function
    End If
    openParen = InStr(1, remainder, "(", vbBinaryCompare)
    If openParen = 0 Then
        If InStr(1, remainder, " ", vbBinaryCompare) = 0 Then NoArgumentPublicSubName = remainder
        Exit Function
    End If
    closeParen = InStr(openParen + 1, remainder, ")", vbBinaryCompare)
    If closeParen = 0 Then Exit Function
    parameterText = Trim(Mid(remainder, openParen + 1, closeParen - openParen - 1))
    If parameterText = "" Then NoArgumentPublicSubName = Trim(Left(remainder, openParen - 1))
End Function

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

Sub DeleteGeneratedDocument(ByVal path)
    On Error Resume Next
    If fso.FileExists(path) Then fso.DeleteFile path, True
    On Error GoTo 0
End Sub

Sub LogLine(ByVal message)
    Dim logFile
    Set logFile = fso.OpenTextFile(logPath, 8, True, 0)
    logFile.WriteLine Now & " " & message
    logFile.Close
    WScript.Echo message
End Sub

Sub Fail(ByVal message)
    On Error Resume Next
    LogLine "FAIL " & message
    CloseAllWordDocuments
    Set frmComp = Nothing
    Set thisDoc = Nothing
    Set comps = Nothing
    Set comp = Nothing
    If IsObject(word) Then word.Quit False
    Set doc = Nothing
    Set word = Nothing
    DeleteGeneratedDocument docPath
    WScript.Echo "FAIL|" & message & "|" & logPath
    WScript.Quit 1
End Sub

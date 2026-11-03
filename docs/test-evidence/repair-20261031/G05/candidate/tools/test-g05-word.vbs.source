Option Explicit
Dim fso, word, vbaDir, rootDir, failures, doc
Set fso = CreateObject("Scripting.FileSystemObject")
If WScript.Arguments.Count <> 2 Then
    WScript.Echo "FAIL|USAGE|cscript test-g05-word.vbs <vba-dir> <empty-artifact-dir>"
    WScript.Quit 2
End If
vbaDir = fso.GetAbsolutePathName(WScript.Arguments(0))
rootDir = fso.GetAbsolutePathName(WScript.Arguments(1))
If Not fso.FolderExists(vbaDir) Then WScript.Quit 2
If fso.FolderExists(rootDir) Then
    If fso.GetFolder(rootDir).Files.Count <> 0 Or fso.GetFolder(rootDir).SubFolders.Count <> 0 Then WScript.Quit 2
Else
    fso.CreateFolder rootDir
End If
failures = 0
On Error Resume Next
Set word = CreateObject("Word.Application")
If Err.Number <> 0 Or word Is Nothing Then
    WScript.Echo "BLOCKED|WORD_CREATE|" & Err.Number & "|" & Err.Description
    WScript.Quit 2
End If
word.Visible = False
word.DisplayAlerts = 0
WScript.Echo "L2_SOURCE_IMPORT|WORD_VERSION=" & word.Version
Err.Clear
On Error GoTo 0

RunR003
RunR005
RunR005SameSecond
RunR005CopyFailure
RunWordLock
RunR006
RunBaselineCases
RunR023

On Error Resume Next
If IsObject(doc) Then doc.Close False
Set doc = Nothing
word.Quit False
Set word = Nothing
On Error GoTo 0
WScript.Echo "SUMMARY|L2_SOURCE_IMPORT|FAILURES=" & failures
If failures <> 0 Then WScript.Quit 1
WScript.Quit 0

Function OpenSynthetic(ByVal caseName)
    On Error Resume Next
    Dim dir, path, name
    dir = fso.BuildPath(rootDir, caseName)
    fso.CreateFolder dir
    Set doc = word.Documents.Add
    doc.Content.Text = MarkingText(False)
    path = fso.BuildPath(dir, "word.docm")
    doc.SaveAs2 path, 13
    For Each name In Array("Patterns.bas", "DictModel.bas", "JsonWriter.bas", "PatentExtractor.bas", "clsSaveHook.cls", "AutoExport.bas", "PatentMarkerBootstrap.bas", "PatentDictPanel.frm")
        doc.VBProject.VBComponents.Import fso.BuildPath(vbaDir, name)
        If Err.Number <> 0 Then Exit For
    Next
    If Err.Number <> 0 Then
        WScript.Echo "FAIL|" & caseName & "|setup|" & Err.Number & "|" & Err.Description
        failures = failures + 1
        Err.Clear
        Set OpenSynthetic = Nothing
    Else
        doc.Activate
        Set OpenSynthetic = doc
    End If
    On Error GoTo 0
End Function

Sub CloseSynthetic()
    On Error Resume Next
    If IsObject(doc) Then doc.Close False
    Set doc = Nothing
    On Error GoTo 0
End Sub

Sub Check(ByVal ok, ByVal label)
    If ok Then
        WScript.Echo "PASS|" & label
    Else
        WScript.Echo "FAIL|" & label
        failures = failures + 1
    End If
End Sub

Sub CheckRuntimeError(ByVal caseName)
    If Err.Number <> 0 Then
        WScript.Echo "FAIL|" & caseName & "|exception|" & Err.Number & "|" & Err.Description
        failures = failures + 1
        Err.Clear
    End If
End Sub

Function MarkingText(ByVal changed)
    MarkingText = ChrW(&H9644) & ChrW(&H56FE) & ChrW(&H6807) & ChrW(&H8BB0) & ChrW(&H8BF4) & ChrW(&H660E) & ChrW(&HFF1A) & _
        "1" & ChrW(&H5E95) & ChrW(&H5EA7) & ChrW(&HFF0C) & "2" & ChrW(&H652F) & ChrW(&H67B6) & ChrW(&HFF1B)
    If changed Then MarkingText = MarkingText & " 3" & ChrW(&H4E0A) & ChrW(&H76D6) & ChrW(&HFF1B)
End Function

Function ReadUtf8(ByVal path)
    Dim s
    Set s = CreateObject("ADODB.Stream")
    s.Type = 2
    s.Charset = "utf-8"
    s.Open
    s.LoadFromFile path
    ReadUtf8 = s.ReadText(-1)
    s.Close
End Function

Sub WriteAscii(ByVal path, ByVal text)
    Dim t, oldAttrs
    oldAttrs = 0
    If fso.FileExists(path) Then
        oldAttrs = fso.GetFile(path).Attributes
        fso.GetFile(path).Attributes = 0
    End If
    Set t = fso.CreateTextFile(path, True, False)
    t.Write text
    t.Close
    fso.GetFile(path).Attributes = oldAttrs
End Sub

Function BackupCount(ByVal dir, ByVal dictName)
    Dim file, count, prefix
    count = 0
    prefix = LCase(dictName & ".word-")
    For Each file In fso.GetFolder(dir).Files
        If Left(LCase(file.Name), Len(prefix)) = prefix And LCase(Right(file.Name, 4)) = ".bak" Then count = count + 1
    Next
    BackupCount = count
End Function

Function FindBackup(ByVal dir, ByVal dictName)
    Dim file, prefix
    prefix = LCase(dictName & ".word-")
    FindBackup = ""
    For Each file In fso.GetFolder(dir).Files
        If Left(LCase(file.Name), Len(prefix)) = prefix And LCase(Right(file.Name, 4)) = ".bak" Then
            FindBackup = file.Path: Exit Function
        End If
    Next
End Function

Sub RunBaselineCases()
    On Error Resume Next
    Dim d, dir, path, baseline, exported, before, sentinel, backup, oldBackup, oldBase, module, i, injected, errFile
    Set d = OpenSynthetic("R006pending")
    If d Is Nothing Then Exit Sub
    dir = fso.GetParentFolderName(d.FullName): path = fso.BuildPath(dir, "word.dict.json"): baseline = path & ".word-baseline"
    exported = word.Run("AutoExport.ExportDict")
    Check exported And fso.FileExists(baseline), "R006pending.initial_digest_created"
    If fso.FileExists(baseline) Then
        sentinel = ReadUtf8(baseline)
        Check Left(sentinel, 30) = "PATENTMARKER_WORD_BASELINE_V1" & vbLf And Len(sentinel) = 94, "R006pending.header_and_digest_only"
        Check InStr(sentinel, "entries") = 0, "R006pending.no_plaintext"
    End If
    WriteAscii path, "{""entries"":[{""number"":""9"",""name"":""external""}]}"
    before = ReadUtf8(path)
    exported = word.Run("AutoExport.ExportDict")
    backup = FindBackup(dir, "word.dict.json")
    Check exported And backup <> "", "R006pending.external_edit_backed_up"
    If backup <> "" Then
        Check ReadUtf8(backup) = before, "R006pending.backup_is_external_content"
        oldBackup = ReadUtf8(backup)
        d.Content.Text = MarkingText(True)
        exported = word.Run("AutoExport.ExportDict")
        Check exported, "R006pending.word_only_change_allowed"
        Check ReadUtf8(backup) = oldBackup, "R006pending.backup_retained"
        Check BackupCount(dir, "word.dict.json") = 1, "R006pending.no_extra_backup"
        oldBase = ReadUtf8(baseline): before = ReadUtf8(path)
        Set module = d.VBProject.VBComponents("AutoExport").CodeModule
        injected = False
        For i = 1 To module.CountOfLines
            If InStr(module.Lines(i, 1), "If Not JsonWriter.WriteToFile(path, BASELINE_HEADER") > 0 Then
                module.ReplaceLine i, "    Err.Raise vbObjectError + 598, ""G05FaultInjection"", ""baseline failure"""
                injected = True: Exit For
            End If
        Next
        Check injected, "R006pending.baseline_failure_injected"
        If injected Then
            d.Content.Text = MarkingText(False)
            exported = word.Run("AutoExport.ExportDict")
            errFile = fso.BuildPath(dir, "autoexport-error.txt")
            Check Not exported, "R006pending.partial_save_reported"
            Check ReadUtf8(path) <> before, "R006pending.dictionary_really_changed"
            Check ReadUtf8(backup) = oldBackup, "R006pending.backup_survives_failure"
            Check ReadUtf8(baseline) = oldBase, "R006pending.baseline_unchanged_on_failure"
            Check fso.FileExists(errFile), "R006pending.failure_logged"
            If fso.FileExists(errFile) Then Check InStr(ReadUtf8(errFile), "Dictionary was saved but baseline update failed") > 0, "R006pending.error_says_dictionary_saved"
        End If
    End If
    CheckRuntimeError "R006pending": CloseSynthetic: Set d = Nothing

    Set d = OpenSynthetic("R006frozen")
    If d Is Nothing Then Exit Sub
    dir = fso.GetParentFolderName(d.FullName): path = fso.BuildPath(dir, "word.dict.json")
    exported = word.Run("AutoExport.ExportDict")
    Check exported, "R006frozen.initial_export"
    WriteAscii path, "{""entries"":[{""number"":""17"",""name"":""frozen""}]}"
    backup = path & ".word-20261101-010101.bak"
    fso.CopyFile path, backup, False: oldBackup = ReadUtf8(backup)
    d.Content.Text = MarkingText(True)
    exported = word.Run("AutoExport.ExportDict")
    Check exported, "R006frozen.original_frozen_content_can_be_exported"
    Check ReadUtf8(backup) = oldBackup, "R006frozen.backup_retained"
    Check BackupCount(dir, "word.dict.json") = 1, "R006frozen.no_extra_backup"
    CheckRuntimeError "R006frozen": CloseSynthetic: Set d = Nothing

    Set d = OpenSynthetic("R006unknown")
    If d Is Nothing Then Exit Sub
    dir = fso.GetParentFolderName(d.FullName): path = fso.BuildPath(dir, "word.dict.json"): baseline = path & ".word-baseline"
    WriteAscii baseline, "unknown object sentinel": sentinel = ReadUtf8(baseline)
    exported = word.Run("AutoExport.ExportDict")
    Check Not exported, "R006unknown.export_blocked"
    Check ReadUtf8(baseline) = sentinel, "R006unknown.sentinel_unchanged"
    Check Not fso.FileExists(path), "R006unknown.no_dictionary_created"
    CheckRuntimeError "R006unknown": CloseSynthetic: Set d = Nothing

    Set d = OpenSynthetic("R006bad")
    If d Is Nothing Then Exit Sub
    dir = fso.GetParentFolderName(d.FullName): path = fso.BuildPath(dir, "word.dict.json"): baseline = path & ".word-baseline"
    exported = word.Run("AutoExport.ExportDict")
    Check exported, "R006bad.initial_export"
    before = ReadUtf8(path)
    WriteAscii baseline, "PATENTMARKER_WORD_BASELINE_V1" & vbLf & "broken"
    d.Content.Text = MarkingText(True)
    exported = word.Run("AutoExport.ExportDict")
    backup = FindBackup(dir, "word.dict.json")
    Check exported And backup <> "", "R006bad.corrupt_digest_forces_backup"
    If backup <> "" Then Check ReadUtf8(backup) = before, "R006bad.original_recoverable"
    CheckRuntimeError "R006bad": CloseSynthetic: Set d = Nothing
    On Error GoTo 0
End Sub

Sub RunR003()
    On Error Resume Next
    Dim d, dir, oldPath, newPath, oldBytes, oldAttrs, exported
    Set d = OpenSynthetic("R003")
    If d Is Nothing Then Exit Sub
    dir = fso.GetParentFolderName(d.FullName)
    oldPath = fso.BuildPath(dir, "word.dict.json")
    exported = word.Run("AutoExport.ExportDict")
    Check exported And fso.FileExists(oldPath), "R003.initial_export"
    If fso.FileExists(oldPath) Then
        WriteAscii oldPath, "{""metadata"":{""modified_by"": ""cad""},""entries"":[{""number"":""9"",""name"":""sentinel""}]}"
        fso.GetFile(oldPath).Attributes = 6
        oldBytes = ReadUtf8(oldPath)
        oldAttrs = fso.GetFile(oldPath).Attributes
        WriteAscii fso.BuildPath(dir, "drawing.dwg"), "synthetic"
        newPath = fso.BuildPath(dir, "drawing.dict.json")
        exported = word.Run("AutoExport.ExportDict")
        Check exported And fso.FileExists(newPath), "R003.new_target_exported"
        If fso.FileExists(oldPath) Then
            Check ReadUtf8(oldPath) = oldBytes, "R003.old_dict_bytes_retained"
            Check fso.GetFile(oldPath).Attributes = oldAttrs, "R003.old_dict_attributes_retained"
        Else
            Check False, "R003.old_dict_bytes_retained"
            Check False, "R003.old_dict_attributes_retained"
        End If
    End If
    CheckRuntimeError "R003"
    CloseSynthetic
    Set d = Nothing
    On Error GoTo 0
End Sub

Sub RunR005()
    On Error Resume Next
    Dim d, dir, path, backup, oldJson, oldBackup, oldAttrs, exported
    Set d = OpenSynthetic("R005")
    If d Is Nothing Then Exit Sub
    dir = fso.GetParentFolderName(d.FullName)
    path = fso.BuildPath(dir, "word.dict.json")
    exported = word.Run("AutoExport.ExportDict")
    Check exported And fso.FileExists(path), "R005.initial_export"
    WriteAscii path, "{""metadata"":{""modified_by"": ""cad""},""entries"":[{""number"":""9"",""name"":""newer""}]}"
    backup = path & ".word-20261031-235959.bak"
    WriteAscii backup, "{""entries"":[{""number"":""8"",""name"":""frozen""}]}"
    fso.GetFile(backup).Attributes = 6
    oldJson = ReadUtf8(path): oldBackup = ReadUtf8(backup): oldAttrs = fso.GetFile(backup).Attributes
    exported = word.Run("AutoExport.ExportDict")
    Check Not exported, "R005.pending_new_cad_content_blocks_export"
    Check ReadUtf8(path) = oldJson, "R005.current_retained"
    If fso.FileExists(backup) Then
        Check ReadUtf8(backup) = oldBackup, "R005.frozen_backup_bytes_retained"
        Check fso.GetFile(backup).Attributes = oldAttrs, "R005.frozen_backup_attributes_retained"
    Else
        Check False, "R005.frozen_backup_bytes_retained"
        Check False, "R005.frozen_backup_attributes_retained"
    End If
    CheckRuntimeError "R005"
    CloseSynthetic
    Set d = Nothing
    On Error GoTo 0
End Sub

Sub RunR005CopyFailure()
    On Error Resume Next
    Dim d, dir, path, oldJson, oldAttrs, exported, module, i, injected, errFile
    Set d = OpenSynthetic("R005copy")
    If d Is Nothing Then Exit Sub
    dir = fso.GetParentFolderName(d.FullName)
    path = fso.BuildPath(dir, "word.dict.json")
    exported = word.Run("AutoExport.ExportDict")
    Check exported And fso.FileExists(path), "R005copy.initial_export"
    WriteAscii path, "{""metadata"":{""modified_by"": ""cad""},""entries"":[{""number"":""9"",""name"":""copy-sentinel""}]}"
    fso.GetFile(path).Attributes = 6
    oldJson = ReadUtf8(path): oldAttrs = fso.GetFile(path).Attributes
    Set module = d.VBProject.VBComponents("AutoExport").CodeModule
    injected = False
    For i = 1 To module.CountOfLines
        If Trim(module.Lines(i, 1)) = "FileCopy dictPath, bakPath" Then
            module.ReplaceLine i, "    Err.Raise vbObjectError + 599, ""G05FaultInjection"", ""injected copy failure"""
            injected = True: Exit For
        End If
    Next
    Check injected, "R005copy.fault_injected"
    If injected Then
        exported = word.Run("AutoExport.ExportDict")
        errFile = fso.BuildPath(dir, "autoexport-error.txt")
        Check Not exported, "R005copy.export_failed"
        Check fso.FileExists(errFile), "R005copy.error_recorded"
        If fso.FileExists(errFile) Then Check InStr(ReadUtf8(errFile), "injected copy failure") > 0, "R005copy.injection_reached"
        Check ReadUtf8(path) = oldJson, "R005copy.current_bytes_retained"
        Check fso.GetFile(path).Attributes = oldAttrs, "R005copy.current_attributes_retained"
        Check BackupCount(dir, "word.dict.json") = 0, "R005copy.no_partial_backup"
    End If
    CheckRuntimeError "R005copy"
    CloseSynthetic
    Set d = Nothing
    On Error GoTo 0
End Sub

Sub RunR005SameSecond()
    On Error Resume Next
    Dim d, dir, path, backup, oldJson, oldBackup, oldAttrs, exported
    Dim module, i, line, stampInjected, copyInjected
    Set d = OpenSynthetic("R005same")
    If d Is Nothing Then Exit Sub
    dir = fso.GetParentFolderName(d.FullName)
    path = fso.BuildPath(dir, "word.dict.json")
    exported = word.Run("AutoExport.ExportDict")
    Check exported And fso.FileExists(path), "R005same.initial_export"
    WriteAscii path, "{""metadata"":{""modified_by"": ""cad""},""entries"":[{""number"":""9"",""name"":""newer""}]}"
    backup = path & ".word-20261031-235959.bak"
    WriteAscii backup, "{""entries"":[{""number"":""8"",""name"":""frozen""}]}"
    fso.GetFile(backup).Attributes = 6
    oldJson = ReadUtf8(path): oldBackup = ReadUtf8(backup): oldAttrs = fso.GetFile(backup).Attributes
    Set module = d.VBProject.VBComponents("AutoExport").CodeModule
    stampInjected = False: copyInjected = False
    For i = 1 To module.CountOfLines
        line = Trim(module.Lines(i, 1))
        If Left(line, 14) = "stamp = Format" Then
            module.ReplaceLine i, "    stamp = ""20261031-235959"""
            stampInjected = True
        End If
        If line = "FileCopy dictPath, bakPath" Then
            module.ReplaceLine i, "    Err.Raise vbObjectError + 599, ""G05FaultInjection"", ""injected copy failure"""
            copyInjected = True
        End If
    Next
    Check stampInjected And copyInjected, "R005same.clock_and_copy_fault_injected"
    If stampInjected And copyInjected Then
        exported = word.Run("AutoExport.ExportDict")
        Check Not exported, "R005same.export_failed"
        Check ReadUtf8(path) = oldJson, "R005same.current_retained"
        If fso.FileExists(backup) Then
            Check ReadUtf8(backup) = oldBackup, "R005same.old_backup_bytes_retained"
            Check fso.GetFile(backup).Attributes = oldAttrs, "R005same.old_backup_attributes_retained"
        Else
            Check False, "R005same.old_backup_bytes_retained"
            Check False, "R005same.old_backup_attributes_retained"
        End If
    End If
    CheckRuntimeError "R005same"
    CloseSynthetic
    Set d = Nothing
    On Error GoTo 0
End Sub

Sub RunWordLock()
    On Error Resume Next
    Dim d, dir, path, before, attrs, exported, testComp, code, held
    Set d = OpenSynthetic("R009lock")
    If d Is Nothing Then Exit Sub
    dir = fso.GetParentFolderName(d.FullName)
    path = fso.BuildPath(dir, "word.dict.json")
    exported = word.Run("AutoExport.ExportDict")
    Check exported And fso.FileExists(path), "R009lock.initial_export"
    before = ReadUtf8(path): attrs = fso.GetFile(path).Attributes
    Set testComp = d.VBProject.VBComponents.Add(1)
    testComp.Name = "G05LockHarness"
    code = "Private lockFile As Integer" & vbCrLf & _
        "Public Function HoldProductLock(ByVal path As String) As Boolean" & vbCrLf & _
        "    lockFile = FreeFile" & vbCrLf & _
        "    Open path For Binary Access Read Write Lock Read Write As #lockFile" & vbCrLf & _
        "    HoldProductLock = True" & vbCrLf & _
        "End Function" & vbCrLf & _
        "Public Function ReleaseProductLock() As Boolean" & vbCrLf & _
        "    Close #lockFile" & vbCrLf & _
        "    ReleaseProductLock = True" & vbCrLf & _
        "End Function"
    testComp.CodeModule.AddFromString code
    held = word.Run("G05LockHarness.HoldProductLock", path & ".patlock")
    Check held, "R009lock.held"
    d.Content.Text = MarkingText(True)
    exported = word.Run("AutoExport.ExportDict")
    Check Not exported, "R009lock.word_export_rejected"
    Check ReadUtf8(path) = before, "R009lock.original_bytes_retained"
    Check fso.GetFile(path).Attributes = attrs, "R009lock.original_attributes_retained"
    held = word.Run("G05LockHarness.ReleaseProductLock")
    Check held, "R009lock.released"
    CheckRuntimeError "R009lock"
    CloseSynthetic
    Set d = Nothing
    On Error GoTo 0
End Sub

Sub RunR006()
    On Error Resume Next
    Dim d, dir, path, before, after, exported, madeVisible
    Set d = OpenSynthetic("R006")
    If d Is Nothing Then Exit Sub
    dir = fso.GetParentFolderName(d.FullName)
    path = fso.BuildPath(dir, "word.dict.json")
    exported = word.Run("AutoExport.ExportDict")
    Check exported And fso.FileExists(path), "R006.initial_export"
    madeVisible = word.Run("AutoExport.SetCurrentDictVisibility", True)
    Check madeVisible, "R006.visible_enabled"
    before = ReadUtf8(path)
    d.Content.Text = MarkingText(True)
    exported = word.Run("AutoExport.ExportDict")
    after = ReadUtf8(path)
    Check exported And before <> after, "R006.word_change_exported"
    Check BackupCount(dir, "word.dict.json") = 0, "R006.word_only_no_false_conflict"
    CheckRuntimeError "R006"
    CloseSynthetic
    Set d = Nothing
    On Error GoTo 0
End Sub

Sub RunR023()
    On Error Resume Next
    Dim d, comp, module, i, replaced, testComp, code, ok
    Set d = OpenSynthetic("R023")
    If d Is Nothing Then Exit Sub
    Set comp = d.VBProject.VBComponents("AutoExport")
    Set module = comp.CodeModule
    replaced = False
    For i = 1 To module.CountOfLines
        If Trim(module.Lines(i, 1)) = "PatentDictPanel.Show" Then
            module.ReplaceLine i, "    ' G05 L2 only: suppress modal UI after executing entry preamble"
            replaced = True
            Exit For
        End If
    Next
    Check replaced, "R023.only_modal_display_suppressed"
    Set testComp = d.VBProject.VBComponents.Add(1)
    testComp.Name = "G05Harness"
    code = "Public Function DisabledPanelReopen() As Boolean" & vbCrLf & _
        "    AutoExport.IsAutoExportEnabled = True" & vbCrLf & _
        "    AutoExport.IsAutoExportEnabled = False" & vbCrLf & _
        "    AutoExport.ShowPatentDictPanel" & vbCrLf & _
        "    DisabledPanelReopen = Not AutoExport.IsAutoExportEnabled" & vbCrLf & _
        "    AutoExport.IsAutoExportEnabled = False" & vbCrLf & _
        "End Function"
    testComp.CodeModule.AddFromString code
    ok = word.Run("G05Harness.DisabledPanelReopen")
    Check ok, "R023.explicit_disable_survives_panel_entry"
    CheckRuntimeError "R023"
    CloseSynthetic
    Set d = Nothing
    On Error GoTo 0
End Sub

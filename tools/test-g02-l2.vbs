Option Explicit

' ======================================================================
' G02 - VBA L2 probe for DictModel.BuildModel (R-017 / R-021)
'
' Imports the real vba/Patterns.bas and vba/DictModel.bas into a temporary
' Word document, calls DictModel.BuildModel through Word.Run, and asserts
' the model it returns:
'   * occurrences are counted from the un-deduplicated hits (R-017)
'   * the entry number keeps the first original spelling (R-021)
'   * same number with different names lands in ONE entry's conflicts (R-021)
'
' Evidence layer: L2_SOURCE_IMPORT. It proves the VBA source behaves this
' way when imported into a real Word document. It does NOT prove the
' deployment package or the installed add-in, which stay with the
' coordinator's install/E2E gates.
'
' Usage:
'   cscript //nologo tools\test-g02-l2.vbs <vba-dir>
'
' Output: PASS|/FAIL| lines, then SUMMARY. Exit 1 on any failure, 2 when no
' business assertion ran or the modules cannot be imported.
' ======================================================================

Dim fso, word, doc, tempDir, docPath, vbaDir, passed, failed
Set fso = CreateObject("Scripting.FileSystemObject")
passed = 0
failed = 0

If WScript.Arguments.Count < 1 Then
    WScript.Echo "SKIP/BLOCKED|usage|test-g02-l2.vbs <vba-dir>"
    WScript.Quit 2
End If
vbaDir = fso.GetAbsolutePathName(WScript.Arguments(0))
If Not fso.FileExists(fso.BuildPath(vbaDir, "Patterns.bas")) Then
    WScript.Echo "SKIP/BLOCKED|vba|Patterns.bas not found under " & vbaDir
    WScript.Quit 2
End If
If Not fso.FileExists(fso.BuildPath(vbaDir, "DictModel.bas")) Then
    WScript.Echo "SKIP/BLOCKED|vba|DictModel.bas not found under " & vbaDir
    WScript.Quit 2
End If

tempDir = fso.BuildPath(fso.GetSpecialFolder(2), "PatentMarkerG02L2-" & Replace(fso.GetTempName, ".tmp", ""))
fso.CreateFolder tempDir
docPath = fso.BuildPath(tempDir, "g02l2.docm")

On Error Resume Next
Set word = CreateObject("Word.Application")
If Err.Number <> 0 Then
    WScript.Echo "SKIP/BLOCKED|word|CreateObject failed: " & Err.Number & " " & Err.Description
    Cleanup
    WScript.Quit 2
End If
On Error GoTo 0

word.Visible = False
word.DisplayAlerts = 0
Set doc = word.Documents.Add
doc.SaveAs2 docPath, 13
doc.Activate

On Error Resume Next
doc.VBProject.VBComponents.Import fso.BuildPath(vbaDir, "Patterns.bas")
If Err.Number <> 0 Then
    WScript.Echo "SKIP/BLOCKED|import|Patterns.bas: " & Err.Number & " " & Err.Description
    Cleanup
    WScript.Quit 2
End If
doc.VBProject.VBComponents.Import fso.BuildPath(vbaDir, "DictModel.bas")
If Err.Number <> 0 Then
    WScript.Echo "SKIP/BLOCKED|import|DictModel.bas: " & Err.Number & " " & Err.Description
    Cleanup
    WScript.Quit 2
End If
On Error GoTo 0
WScript.Echo "MODULES_IMPORTED"

' ---------------------------------------------------------------- R-017
' "1???1???1???" has three real occurrences; ?? appears twice.
' Before the fix ExtractAll de-duplicated first, so occurrences was 2 and
' ?? counted once, which distorted the name majority.
Dim model
Set model = BuildModelFor( _
    ChrW(38468) & ChrW(22270) & ChrW(26631) & ChrW(35760) & ChrW(35828) & ChrW(26126) & ChrW(65306) & _
    "1" & ChrW(25903) & ChrW(26550) & ChrW(65292) & _
    "1" & ChrW(24213) & ChrW(24231) & ChrW(65292) & _
    "1" & ChrW(24213) & ChrW(24231) & ChrW(65292))
If model Is Nothing Then
    Fail "r017|BuildModel returned nothing"
Else
    Dim occurrences, total, entryCount
    total = 0
    entryCount = ModelEntryCount(model)
    Dim i
    For i = 0 To entryCount - 1
        total = total + ModelOccurrences(model, i)
    Next
    If total = 3 Then
        Pass "r017|total occurrences=" & total & " (un-deduped)"
    Else
        Fail "r017|total occurrences expected=3 actual=" & total
    End If
End If

' ---------------------------------------------------------------- R-021
' "1342A???????1342a?????" is one number with two names.
' Identity must group them (case-insensitive), keep the first spelling
' "1342A" for display, and record both names as conflict candidates.
Dim model2
Set model2 = BuildModelFor( _
    ChrW(38468) & ChrW(22270) & ChrW(26631) & ChrW(35760) & ChrW(35828) & ChrW(26126) & ChrW(65306) & _
    "1342A" & ChrW(36807) & ChrW(28193) & ChrW(36830) & ChrW(25509) & ChrW(26495) & ChrW(37096) & ChrW(65292) & _
    "1342a" & ChrW(20985) & ChrW(38519) & ChrW(26495) & ChrW(37096) & ChrW(65292))
If model2 Is Nothing Then
    Fail "r021|BuildModel returned nothing"
Else
    If ModelEntryCount(model2) = 1 Then
        Pass "r021|same number grouped into one entry"
    Else
        Fail "r021|entry count expected=1 actual=" & ModelEntryCount(model2)
    End If
    Dim shownNumber
    shownNumber = ModelNumber(model2, 0)
    If shownNumber = "1342A" Then
        Pass "r021|display keeps first spelling 1342A"
    Else
        Fail "r021|display number expected=1342A actual=" & shownNumber
    End If
    If ModelConflictCount(model2, 0) >= 1 Then
        Pass "r021|conflict recorded for same number different names"
    Else
        Fail "r021|conflict count expected>=1 actual=" & ModelConflictCount(model2, 0)
    End If
End If

' ---------------------------------------------------------------- R-021 control
' Two genuinely different numbers must stay two entries (identity is not
' over-grouping), and 10-1 is not 101.
Dim model3
Set model3 = BuildModelFor( _
    ChrW(38468) & ChrW(22270) & ChrW(26631) & ChrW(35760) & ChrW(35828) & ChrW(26126) & ChrW(65306) & _
    "10-1" & ChrW(23433) & ChrW(35013) & ChrW(24231) & ChrW(65292) & _
    "10-2" & ChrW(25903) & ChrW(26495) & ChrW(65292))
If model3 Is Nothing Then
    Fail "r021-control|BuildModel returned nothing"
Else
    If ModelEntryCount(model3) = 2 Then
        Pass "r021-control|distinct sub-numbers stay distinct"
    Else
        Fail "r021-control|entry count expected=2 actual=" & ModelEntryCount(model3)
    End If
End If

' ---------------------------------------------------------------- G15 (R-016)
' Over-long numbers must not be truncated to their tail. Before the fix the
' name-first branch handed the leading digit to the name: number=23456.
Dim modelG15
Set modelG15 = BuildModelFor( _
    ChrW(38468) & ChrW(22270) & ChrW(26631) & ChrW(35760) & ChrW(35828) & ChrW(26126) & ChrW(65306) & _
    ChrW(24213) & ChrW(24231) & "123456" & ChrW(65292) & _
    ChrW(31471) & ChrW(30422) & "301" & ChrW(65292))
If modelG15 Is Nothing Then
    Fail "g15|BuildModel returned nothing"
Else
    If ModelEntryCount(modelG15) = 1 Then
        Pass "g15|over-long number rejected, only 301 kept"
    Else
        Fail "g15|entry count expected=1 actual=" & ModelEntryCount(modelG15)
    End If
    If ModelHasNumber(modelG15, "23456") Then
        Fail "g15|truncated number 23456 must not appear"
    Else
        Pass "g15|no truncated 23456 entry"
    End If
End If


' ---------------------------------------------------------------- G19 (R-016)
' The name body may contain digits, so a name can carry the same text as the
' number that follows it. The pre-fix locator searched the whole match for the
' first occurrence of the number text and therefore checked the boundary at the
' copy inside the NAME, rejecting legal entries. Each case below is a legal
' entry that must survive, plus the over-long control that must still fail.
AssertEntry ChrW(22771) & ChrW(20307) & "X123 " & ChrW(25903) & ChrW(26550) & " 123" & ChrW(12289), _
    "123", ChrW(22771) & ChrW(20307) & "X123 " & ChrW(25903) & ChrW(26550), "g19-repeat-after-letter"
AssertEntry ChrW(22771) & ChrW(20307) & "1123 " & ChrW(25903) & ChrW(26550) & " 123" & ChrW(12289), _
    "123", ChrW(22771) & ChrW(20307) & "1123 " & ChrW(25903) & ChrW(26550), "g19-repeat-after-digit"
AssertEntry ChrW(22771) & ChrW(20307) & "AX123 " & ChrW(25903) & ChrW(26550) & " X123" & ChrW(12289), _
    "X123", ChrW(22771) & ChrW(20307) & "AX123 " & ChrW(25903) & ChrW(26550), "g19-repeat-prefixed-number"
AssertEntry ChrW(31532) & "123" & ChrW(23450) & ChrW(20301) & ChrW(20214) & " 123" & ChrW(12289), _
    "123", ChrW(31532) & "123" & ChrW(23450) & ChrW(20301) & ChrW(20214), "g19-repeat-after-chinese"
AssertEntry ChrW(22771) & ChrW(20307) & "X123 " & ChrW(25903) & ChrW(26550) & "(" & "123" & ")" & ChrW(12289), _
    "123", ChrW(22771) & ChrW(20307) & "X123 " & ChrW(25903) & ChrW(26550), "g19-paren-halfwidth"
AssertEntry ChrW(22771) & ChrW(20307) & "X123 " & ChrW(25903) & ChrW(26550) & ChrW(65288) & "123" & ChrW(65289) & ChrW(12289), _
    "123", ChrW(22771) & ChrW(20307) & "X123 " & ChrW(25903) & ChrW(26550), "g19-paren-fullwidth"
AssertEntry ChrW(22771) & ChrW(20307) & "X12345 " & ChrW(25903) & ChrW(26550) & " 12345" & ChrW(12289), _
    "12345", ChrW(22771) & ChrW(20307) & "X12345 " & ChrW(25903) & ChrW(26550), "g19-five-digits"
AssertEntry ChrW(31532) & "2" & ChrW(23450) & ChrW(20301) & ChrW(20214) & " 301" & ChrW(12289), _
    "301", ChrW(31532) & "2" & ChrW(23450) & ChrW(20301) & ChrW(20214), "g19-name-digits-not-repeated"
' Six digits stay rejected: the left boundary check is not weakened by the fix.
AssertNoEntry ChrW(22771) & ChrW(20307) & "X123456 " & ChrW(25903) & ChrW(26550) & " 123456" & ChrW(12289), "g19-six-digits-rejected"
' Order: the name-first hit comes first in the text, so it must be listed first.
AssertEntryOrder ChrW(22771) & ChrW(20307) & "X123 " & ChrW(25903) & ChrW(26550) & " 123" & ChrW(12289) & "1" & ChrW(24213) & ChrW(24231) & ChrW(65292), _
    "123|" & ChrW(22771) & ChrW(20307) & "X123 " & ChrW(25903) & ChrW(26550) & ";1|" & ChrW(24213) & ChrW(24231), "g19-mixed-order"
WScript.Echo "SUMMARY: passed=" & passed & " failed=" & failed
Cleanup
If failed > 0 Then WScript.Quit 1
If passed = 0 Then
    WScript.Echo "SKIP/BLOCKED: no business assertion executed"
    WScript.Quit 2
End If
WScript.Quit 0

' ======================================================================
' Model access
' BuildModel returns a Scripting.Dictionary (root) whose "entries" value is
' an array of Scripting.Dictionary entries.
' ======================================================================
' word.Run returns an Object, so the result must be captured with Set.
Function BuildModelFor(ByVal text)
    Dim result
    On Error Resume Next
    Set result = word.Run("DictModel.BuildModel", text, "L2", "2026-08-05T00:00:00")
    If Err.Number <> 0 Then
        WScript.Echo "SKIP/BLOCKED|macro|BuildModel: " & Err.Number & " " & Err.Description
        Err.Clear
        Set BuildModelFor = Nothing
        Exit Function
    End If
    On Error GoTo 0
    Set BuildModelFor = result
End Function

' "entries" is an array, so it must be returned as a Variant (no Set).
Function ModelEntries(ByVal model)
    ModelEntries = model("entries")
End Function

Function ModelEntryCount(ByVal model)
    Dim entries
    entries = ModelEntries(model)
    If IsArray(entries) Then
        ModelEntryCount = UBound(entries) - LBound(entries) + 1
    Else
        ModelEntryCount = 0
    End If
End Function

Function ModelEntry(ByVal model, ByVal index)
    Dim entries
    entries = ModelEntries(model)
    Set ModelEntry = entries(LBound(entries) + index)
End Function

Function ModelOccurrences(ByVal model, ByVal index)
    ModelOccurrences = CLng(ModelEntry(model, index)("occurrences"))
End Function

Function ModelNumber(ByVal model, ByVal index)
    ModelNumber = CStr(ModelEntry(model, index)("number"))
End Function

' Conflicts live on the ENTRY (one group per number), not on the root model.
' The root holds only metadata / entries / warnings.
Function ModelConflictCount(ByVal model, ByVal index)
    Dim conflicts
    conflicts = ModelEntry(model, index)("conflicts")
    If IsArray(conflicts) Then
        ModelConflictCount = UBound(conflicts) - LBound(conflicts) + 1
    Else
        ModelConflictCount = 0
    End If
End Function

' ======================================================================
' G19 assertions: one legal entry (exact number + name) or an explicit zero-hit
' result, driven through the real DictModel.BuildModel.
' ======================================================================
Sub AssertEntry(ByVal text, ByVal expectedNumber, ByVal expectedName, ByVal id)
    Dim m
    Set m = BuildModelFor(text)
    If m Is Nothing Then
        Fail id & "|BuildModel returned nothing"
        Exit Sub
    End If
    If ModelEntryCount(m) <> 1 Then
        Fail id & "|entry count expected=1 actual=" & ModelEntryCount(m) & " numbers=" & ModelNumbers(m)
        Exit Sub
    End If
    If StrComp(CStr(ModelNumber(m, 0)), CStr(expectedNumber), vbBinaryCompare) <> 0 Then
        Fail id & "|number expected=" & expectedNumber & " actual=" & ModelNumber(m, 0)
        Exit Sub
    End If
    If StrComp(CStr(ModelName(m, 0)), CStr(expectedName), vbBinaryCompare) <> 0 Then
        Fail id & "|name expected=[" & expectedName & "] actual=[" & ModelName(m, 0) & "]"
        Exit Sub
    End If
    Pass id & "|number=" & ModelNumber(m, 0) & " name=" & ModelName(m, 0)
End Sub

Sub AssertNoEntry(ByVal text, ByVal id)
    Dim m
    Set m = BuildModelFor(text)
    If m Is Nothing Then
        Fail id & "|BuildModel returned nothing"
        Exit Sub
    End If
    If ModelEntryCount(m) = 0 Then
        Pass id & "|no entry (over-long number rejected)"
    Else
        Fail id & "|entry count expected=0 actual=" & ModelEntryCount(m) & " numbers=" & ModelNumbers(m)
    End If
End Sub

' Order is asserted on the joined "number|name;number|name" sequence.
Sub AssertEntryOrder(ByVal text, ByVal expected, ByVal id)
    Dim m, k, actual
    Set m = BuildModelFor(text)
    If m Is Nothing Then
        Fail id & "|BuildModel returned nothing"
        Exit Sub
    End If
    actual = ""
    For k = 0 To ModelEntryCount(m) - 1
        If k > 0 Then actual = actual & ";"
        actual = actual & ModelNumber(m, k) & "|" & ModelName(m, k)
    Next
    If StrComp(actual, expected, vbBinaryCompare) = 0 Then
        Pass id & "|order=" & actual
    Else
        Fail id & "|order expected=[" & expected & "] actual=[" & actual & "]"
    End If
End Sub

Function ModelName(ByVal model, ByVal index)
    ModelName = CStr(ModelEntry(model, index)("name"))
End Function

' Diagnostic only: the numbers of all entries, for FAIL messages.
Function ModelNumbers(ByVal model)
    Dim k, s
    s = ""
    For k = 0 To ModelEntryCount(model) - 1
        If k > 0 Then s = s & ","
        s = s & ModelNumber(model, k)
    Next
    ModelNumbers = "[" & s & "]"
End Function


' ======================================================================
' Cleanup / reporting
' ======================================================================
Sub Cleanup()
    On Error Resume Next
    If Not doc Is Nothing Then
        doc.Close False
        Set doc = Nothing
    End If
    If Not word Is Nothing Then
        word.Quit False
        Set word = Nothing
    End If
    On Error GoTo 0
    On Error Resume Next
    If fso.FileExists(docPath) Then fso.DeleteFile docPath, True
    If fso.FolderExists(tempDir) Then fso.DeleteFolder tempDir, True
    On Error GoTo 0
End Sub

Sub Pass(ByVal selector)
    passed = passed + 1
    WScript.Echo "PASS|" & selector
End Sub

Sub Fail(ByVal selector)
    failed = failed + 1
    WScript.Echo "FAIL|" & selector
End Sub

' True when any entry carries exactly this number.
Function ModelHasNumber(ByVal model, ByVal wanted)
    Dim i, n
    ModelHasNumber = False
    For i = 0 To ModelEntryCount(model) - 1
        n = ModelNumber(model, i)
        If StrComp(CStr(n), CStr(wanted), vbTextCompare) = 0 Then
            ModelHasNumber = True
            Exit Function
        End If
    Next
End Function

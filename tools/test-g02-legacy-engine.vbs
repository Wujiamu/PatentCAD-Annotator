Option Explicit

' ======================================================================
' G02 - pre-fix VBScript engine probe (red evidence)
'
' Purpose: prove that the PRE-FIX VBA pattern text, run through the real
' VBScript.RegExp engine, actually produces the defects R-016, R-032 and
' R-033 describe. The pattern text is read from the frozen pre-fix source
' copy (docs/test-evidence/repair-20261031/G02/red-source) by
' tools/G02ParserProbe/extract-legacy-patterns.ps1 - it is never typed
' into this file by hand.
'
' This probe is pattern-level on purpose. R-017 (raw counts), R-018
' (source order) and R-021 (number identity) are algorithm-level, not
' regex-level, so their red evidence comes from
'   * the real pre-fix C# source (tools/G02ParserProbe run-probe.ps1 -Variant red)
'   * the real pre-fix VBA source imported into Word (tools/test-g02-parser-l2.vbs)
' rather than from this file.
'
' Usage:
'   cscript //nologo tools\test-g02-legacy-engine.vbs <legacy-patterns-file>
'
' <legacy-patterns-file> is produced by extract-legacy-patterns.ps1 and holds
' "<name> U+0001 <pattern>" lines in UTF-16LE.
'
' Expected result on the pre-fix text: FAIL on R-016, R-032, R-033 (that is the
' point), PASS on the control cases. Exit code 1 when any assertion fails.
' ======================================================================

' VBScript Const only accepts literals, so the separator is a variable.
Dim SEP_FIELD, fso, patternsPath, patterns, passed, failed
SEP_FIELD = Chr(1)
Set fso = CreateObject("Scripting.FileSystemObject")

If WScript.Arguments.Count <> 1 Then
    WScript.Echo "Usage: test-g02-legacy-engine.vbs <legacy-patterns-file>"
    WScript.Quit 2
End If
patternsPath = fso.GetAbsolutePathName(WScript.Arguments(0))
Set patterns = ReadPatternDump(patternsPath)
If patterns.Count = 0 Then
    WScript.Echo "SKIP/BLOCKED|patterns|no legacy pattern text loaded"
    WScript.Quit 2
End If

passed = 0
failed = 0

' ---------------------------------------------------------------- R-016
' Pre-fix number-first lexeme is (\d{1,5}(?:[A-Za-z]\d*)?): no letter prefix,
' no hyphen sub-number. A complete "S1" / "10-1" must therefore be cut.
CheckR016 "S1" & ChrW(24213) & ChrW(24322) & ChrW(65292), "S1", "r016-number-first-s1"
CheckR016 "10-1" & ChrW(25903) & ChrW(26550) & ChrW(65307), "10-1", "r016-number-first-hyphen"
CheckR016 "S1(" & ChrW(24213) & ChrW(24322) & ")" & ChrW(12289), "S1", "r016-number-first-paren-s1"

' ---------------------------------------------------------------- R-032
' Pre-fix section end uses \Z, which VBScript treats as a literal Z.
' "2Z" must therefore terminate the section and drop the later entries.
CheckR032 "2Z", "r032-z-name"
CheckR032 "2z", "r032-z-lower"
' Control: no Z at all - the pre-fix expression behaves correctly here.
CheckR032Control "r032-no-z"

' ---------------------------------------------------------------- R-033
' Pre-fix bare-list uses \s* between name and number and at both anchors,
' so an ordinary paragraph followed by an isolated number line matches.
CheckR033 vbLf, "r033-plain-then-number-lf"
CheckR033 vbCr & vbLf, "r033-plain-then-number-crlf"
' Controls: same line, and a genuine two-line list.
CheckR033Control "r033-bare-one-line", "r033-bare-two-lines"

WScript.Echo "SUMMARY: passed=" & passed & " failed=" & failed
If failed > 0 Then WScript.Quit 1
If passed = 0 Then
    WScript.Echo "SKIP/BLOCKED: no business assertion executed"
    WScript.Quit 2
End If
WScript.Quit 0

' ======================================================================
' R-016: the number-first pattern must capture the whole number.
' Asserting the REQUIRED behaviour, so it FAILs on the pre-fix text.
' ======================================================================
Sub CheckR016(ByVal text, ByVal expectedNumber, ByVal id)
    Dim re, m, actual
    Set re = NewRegExp(patterns("G02_P1"), True, False)
    Set m = re.Execute(text)
    If m.Count = 0 Then
        Fail "r016|" & id & "|no match at all, expected number " & expectedNumber
        Exit Sub
    End If
    actual = CStr(m(0).SubMatches(0))
    If actual = expectedNumber Then
        Pass "r016|" & id & "|number=" & actual
    Else
        Fail "r016|" & id & "|expected number=" & expectedNumber & " actual=" & actual
    End If
End Sub

' ======================================================================
' R-032: a capital Z in the name must not end the marking section.
' The section is cut at the first full stop, otherwise at the first blank
' paragraph, otherwise at the end of the text - using the production
' pattern text under test.
' ======================================================================
Sub CheckR032(ByVal zForm, ByVal id)
    Dim text, after, re, m, sectionText
    ' Full stop is U+3002; the id after it must survive the section cut.
    text = ChrW(38468) & ChrW(22270) & ChrW(26631) & ChrW(35760) & ChrW(35828) & ChrW(26126) & _
        ChrW(65306) & "1" & ChrW(24213) & ChrW(24322) & ChrW(65292) & "2" & zForm & _
        ChrW(24418) & ChrW(25903) & ChrW(26550) & ChrW(65292) & "3" & ChrW(22806) & ChrW(22771) & _
        ChrW(12290)
    after = Mid(text, 8)
    Set re = NewRegExp(patterns("G02_SECTIONEND"), False, False)
    Set m = re.Execute(after)
    If m.Count > 0 Then
        sectionText = m(0).Value
    Else
        sectionText = after
    End If
    ' The section must reach the full stop, i.e. contain the "3" entry.
    If InStr(1, sectionText, ChrW(22806) & ChrW(22771), vbBinaryCompare) > 0 Then
        Pass "r032|" & id & "|section kept the entries after " & zForm
    Else
        Fail "r032|" & id & "|section cut at " & zForm & " and lost the later entries: [" & Escape(sectionText) & "]"
    End If
End Sub

Sub CheckR032Control(ByVal id)
    Dim text, after, re, m, sectionText
    text = ChrW(38468) & ChrW(22270) & ChrW(26631) & ChrW(35760) & ChrW(35828) & ChrW(26126) & _
        ChrW(65306) & "1" & ChrW(24213) & ChrW(24322) & ChrW(65292) & "2" & ChrW(25903) & _
        ChrW(26550) & ChrW(12290)
    after = Mid(text, 8)
    Set re = NewRegExp(patterns("G02_SECTIONEND"), False, False)
    Set m = re.Execute(after)
    If m.Count > 0 Then
        sectionText = m(0).Value
    Else
        sectionText = after
    End If
    If InStr(1, sectionText, "2" & ChrW(25903) & ChrW(26550), vbBinaryCompare) > 0 Then
        Pass "r032|" & id & "|control section intact"
    Else
        Fail "r032|" & id & "|control section lost content: [" & Escape(sectionText) & "]"
    End If
End Sub

' ======================================================================
' R-033: name and number must stay on the same line.
' ======================================================================
Sub CheckR033(ByVal lineBreak, ByVal id)
    Dim text, re, m
    text = ChrW(26222) & ChrW(36890) & ChrW(27573) & ChrW(33853) & lineBreak & "123" & lineBreak
    Set re = NewRegExp(patterns("G02_P3"), True, True)
    Set m = re.Execute(text)
    If m.Count = 0 Then
        Pass "r033|" & id & "|no cross-line match"
    Else
        Fail "r033|" & id & "|cross-line match count=" & m.Count & _
            " number=" & CStr(m(0).SubMatches(1)) & " name=[" & Escape(CStr(m(0).SubMatches(0))) & "]"
    End If
End Sub

Sub CheckR033Control(ByVal idOneLine, ByVal idTwoLines)
    Dim re, m, text
    Set re = NewRegExp(patterns("G02_P3"), True, True)
    text = ChrW(25903) & ChrW(26550) & " 123"
    Set m = re.Execute(text)
    If m.Count = 1 Then
        Pass "r033|" & idOneLine & "|same-line list still matches"
    Else
        Fail "r033|" & idOneLine & "|same-line list count=" & m.Count
    End If
    text = ChrW(24213) & ChrW(24322) & " 1" & vbLf & ChrW(25903) & ChrW(26550) & " 2"
    Set m = re.Execute(text)
    If m.Count = 2 Then
        Pass "r033|" & idTwoLines & "|two-line list still matches"
    Else
        Fail "r033|" & idTwoLines & "|two-line list count=" & m.Count
    End If
End Sub

' ======================================================================
' Helpers
' ======================================================================
Function NewRegExp(ByVal pattern, ByVal globalFlag, ByVal multilineFlag)
    Dim re
    Set re = CreateObject("VBScript.RegExp")
    re.Global = globalFlag
    re.Multiline = multilineFlag
    re.IgnoreCase = False
    re.Pattern = pattern
    Set NewRegExp = re
End Function

' Render non-ASCII characters as <U+XXXX> so evidence text does not depend on
' the console code page.
Function Escape(ByVal value)
    Dim i, ch, code, out
    out = ""
    For i = 1 To Len(value)
        ch = Mid(value, i, 1)
        code = AscW(ch)
        If code < 0 Then code = code + 65536
        If code >= 32 And code <= 126 Then
            out = out & ch
        ElseIf code = 13 Then
            out = out & "<CR>"
        ElseIf code = 10 Then
            out = out & "<LF>"
        ElseIf code = 9 Then
            out = out & "<TAB>"
        Else
            out = out & "<U+" & code & ">"
        End If
    Next
    Escape = out
End Function

Function ReadPatternDump(ByVal path)
    Dim map, lines, k, line, bar
    Set map = CreateObject("Scripting.Dictionary")
    lines = Split(ReadUtf16(path), vbCrLf)
    For k = 0 To UBound(lines)
        line = lines(k)
        If Len(line) > 0 Then
            bar = InStr(1, line, SEP_FIELD, vbBinaryCompare)
            If bar > 0 Then map(Left(line, bar - 1)) = Mid(line, bar + 1)
        End If
    Next
    Set ReadPatternDump = map
End Function

Function ReadUtf16(ByVal path)
    Dim stream
    Set stream = CreateObject("ADODB.Stream")
    stream.Type = 2
    stream.Charset = "unicode"
    stream.Open
    stream.LoadFromFile path
    ReadUtf16 = stream.ReadText(-1)
    stream.Close
End Function

Sub Pass(ByVal selector)
    passed = passed + 1
    WScript.Echo "PASS|" & selector
End Sub

Sub Fail(ByVal selector)
    failed = failed + 1
    WScript.Echo "FAIL|" & selector
End Sub

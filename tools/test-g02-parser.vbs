Option Explicit

' ======================================================================
' G02 parser - VBScript engine cross-check probe
'
' Purpose: run the regexes that vba/Patterns.bas and vba/DictModel.bas
' actually use through the real VBScript.RegExp engine, and compare
' number / name / order against the real C# production parser on the same
' corpus.
'
' Key constraints:
'   * Pattern text is extracted from the production files, never hand
'     copied. The G02_* constants live in Patterns.bas as single-line
'     string literals; this probe parses "Const NAME = ""...""" lines and
'     fails loudly (SKIP/BLOCKED, exit 2) if a constant is no longer a
'     single-line literal.
'   * Three cross-engine differences are handled the same way the
'     production code handles them:
'       1) VBScript has no lookbehind, so the number start boundary is
'          decided by NumberStartOk, using the same algorithm as C#.
'       2) Number-first patterns use the same G02_NUMN lexeme as C#, so
'          a name such as "Z-shaped bracket" after the number is kept in
'          the name in both engines.
'       3) Inline whitespace uses G02_INLINE (space/TAB) and never
'          crosses a line break.
'   * This source file is ASCII only (the repository's other VBS tools
'     are ASCII too). Chinese punctuation is built with ChrW() and all
'     corpus text comes from UTF-16/UTF-8 data files.
'
' Usage:
'   cscript //nologo tools\test-g02-parser.vbs <vba-dir> <patterns-file>
'       <cases-file> <expected-file>
'
'   <patterns-file> is produced by tools/G02ParserProbe --dump-patterns
'   and holds the exact regex text the C# production code uses.
'
' Output: PASS|/FAIL| lines with a selector, then SUMMARY. Exit code 1
' when any assertion fails, 2 when no business assertion ran.
' ======================================================================

' VBScript Const only accepts literals, so Chr(1) is assigned to a variable.
Dim SEP_FIELD, IDEOGRAPHIC_COMMA, IDEOGRAPHIC_FULLSTOP
SEP_FIELD = Chr(1)
IDEOGRAPHIC_COMMA = ChrW(12289)      ' U+3001
IDEOGRAPHIC_FULLSTOP = ChrW(12290)   ' U+3002

Dim fso, vbaDir, patternsPath, casesPath, expectedPath
Set fso = CreateObject("Scripting.FileSystemObject")

If WScript.Arguments.Count <> 4 Then
    WScript.Echo "Usage: test-g02-parser.vbs <vba-dir> <patterns-file> <cases-file> <expected-file>"
    WScript.Quit 2
End If
vbaDir = fso.GetAbsolutePathName(WScript.Arguments(0))
patternsPath = fso.GetAbsolutePathName(WScript.Arguments(1))
casesPath = fso.GetAbsolutePathName(WScript.Arguments(2))
expectedPath = fso.GetAbsolutePathName(WScript.Arguments(3))

Dim passed, failed
passed = 0
failed = 0

' ------------------------------------------------------ read production source
Dim patternsBas
patternsBas = ReadTextCp936(fso.BuildPath(vbaDir, "Patterns.bas"))

' ------------------------------------------------------ parse pattern constants
Dim vbaConsts, csPatterns
Set vbaConsts = ParseStringConsts(patternsBas, "Patterns.bas")
Set csPatterns = ReadPatternDump(patternsPath)

' Compare the text on both sides verbatim. This proves the two files carry
' identical pattern text; it does NOT prove the two engines behave the same.
' Behavioural equivalence is asserted below by running both engines.
Dim constNames, i, nm
constNames = Array("G02_NUM", "G02_WS", "G02_INLINE", "G02_NAME", _
    "G02_NAMEFIRST", "G02_NAMEBARE", "G02_SEP", "G02_SEPN", "G02_CELLSTART", _
    "G02_P1", "G02_PB", "G02_P2", "G02_PA", "G02_P3", _
    "G02_HDR0", "G02_HDR1", "G02_HDR2", "G02_HDR3", "G02_HDR4", "G02_HDR5", _
    "G02_SECTIONEND", "G02_TABLEA", "G02_TABLEB")

For i = 0 To UBound(constNames)
    nm = constNames(i)
    If Not vbaConsts.Exists(nm) Then
        Fail "pattern-text|" & nm & "|missing in Patterns.bas"
    ElseIf Not csPatterns.Exists(nm) Then
        Fail "pattern-text|" & nm & "|missing in C# pattern dump"
    ElseIf vbaConsts(nm) <> csPatterns(nm) Then
        Fail "pattern-text|" & nm & "|VBA and C# pattern text differ"
    Else
        Pass "pattern-text|" & nm
    End If
Next

' ------------------------------------------------------ corpus and expectations
Dim cases, expectations, ids, id, eids, eid
Set cases = ReadCases(casesPath)
Set expectations = ReadExpectations(expectedPath)

ids = cases.Keys
For i = 0 To UBound(ids)
    id = ids(i)
    If Not expectations.Exists(id & ".hits") Then
        Fail "corpus|" & id & "|case has no independent expectation"
    Else
        RunCase id, cases(id), expectations(id & ".hits"), expectations(id & ".items"), expectations(id & ".raw")
    End If
Next

' An expectation whose case id is missing from the corpus is also a failure.
eids = expectations.Keys
For i = 0 To UBound(eids)
    eid = eids(i)
    If Right(eid, 5) = ".hits" Then
        Dim bareId
        bareId = Left(eid, Len(eid) - 5)
        If Not cases.Exists(bareId) Then Fail "corpus|" & bareId & "|case id missing from corpus"
    End If
Next

WScript.Echo "SUMMARY: passed=" & passed & " failed=" & failed
If failed > 0 Then
    WScript.Quit 1
ElseIf passed = 0 Then
    WScript.Echo "SKIP/BLOCKED: no business assertion executed"
    WScript.Quit 2
End If
WScript.Quit 0

' ======================================================================
' One case, following the DictModel.BuildModel call chain:
'   preprocess -> ExtractMarkingSection -> ExtractAll / ExtractAllRaw
' ======================================================================
Sub RunCase(ByVal id, ByVal text, ByVal expectedHits, ByVal itemsText, ByVal rawText)
    Dim prepared, sectionText, display, raw, k, nDisplay, items, item, bar, rawItems, key, count, seen

    prepared = Preprocess(text)
    sectionText = ExtractMarkingSection(prepared)

    display = ExtractAllImpl(sectionText, False)
    raw = ExtractAllImpl(sectionText, True)

    ' --- display view: exact count ---
    nDisplay = HitCount(display)
    If expectedHits >= 0 And nDisplay <> expectedHits Then
        Fail "display|" & id & "|count expected=" & expectedHits & " actual=" & nDisplay & _
            " hits=" & FormatHits(display)
        Exit Sub
    End If

    ' --- display view: exact number/name/order ---
    If expectedHits > 0 Then
        items = Split(itemsText, Chr(2))
        For k = 0 To expectedHits - 1
            bar = InStr(1, items(k), "|", vbBinaryCompare)
            If display(LBound(display) + k)(0) <> Left(items(k), bar - 1) Or _
               display(LBound(display) + k)(1) <> Mid(items(k), bar + 1) Then
                Fail "display|" & id & "|hit[" & k & "] expected=(" & Left(items(k), bar - 1) & _
                    "," & Mid(items(k), bar + 1) & ") actual=(" & display(LBound(display) + k)(0) & _
                    "," & display(LBound(display) + k)(1) & ") hits=" & FormatHits(display)
                Exit Sub
            End If
        Next
    End If

    ' --- raw view: true occurrence counts (R-017) ---
    If rawText <> "" Then
        rawItems = Split(rawText, Chr(2))
        For k = 0 To UBound(rawItems)
            item = rawItems(k)
            bar = InStrRev(item, "|", -1, vbBinaryCompare)
            key = Left(item, bar - 1)
            count = CLng(Mid(item, bar + 1))
            seen = 0
            Dim j
            For j = LBound(raw) To UBound(raw)
                If raw(j)(0) & "|" & raw(j)(1) = key Then seen = seen + 1
            Next
            If seen <> count Then
                Fail "raw|" & id & "|(" & key & ") expected=" & count & _
                    " actual=" & seen & " rawHits=" & FormatHits(raw)
                Exit Sub
            End If
        Next
    End If
    If HitCount(raw) < HitCount(display) Then
        Fail "raw|" & id & "|raw count " & HitCount(raw) & " < display count " & HitCount(display)
        Exit Sub
    End If

    Pass "display|" & id & "|hits=" & HitCount(display) & " raw=" & HitCount(raw)
End Sub

' Named HitCount because VBScript already defines Count as a built-in member name.
Function HitCount(ByVal arr)
    If UBound(arr) < LBound(arr) Then
        HitCount = 0
    Else
        HitCount = UBound(arr) - LBound(arr) + 1
    End If
End Function

Function FormatHits(ByVal arr)
    Dim s, k
    s = "["
    If UBound(arr) >= LBound(arr) Then
        For k = LBound(arr) To UBound(arr)
            If k > LBound(arr) Then s = s & " "
            s = s & arr(k)(0) & "|" & arr(k)(1)
        Next
    End If
    FormatHits = s & "]"
End Function

' ======================================================================
' DictModel.BuildModel preprocessing (table stream + HTML line breaks)
' ======================================================================
Function Preprocess(ByVal text)
    Dim reTbl
    text = Replace(text, "<br/>", vbCr)
    text = Replace(text, "<br />", vbCr)
    text = Replace(text, "<br>", vbCr)

    Set reTbl = CreateObject("VBScript.RegExp")
    reTbl.Global = True
    reTbl.Multiline = True
    reTbl.IgnoreCase = False
    reTbl.Pattern = vbaConsts("G02_TABLEA")
    text = reTbl.Replace(text, "$1$3$2" & IDEOGRAPHIC_COMMA)
    reTbl.Pattern = vbaConsts("G02_TABLEB")
    text = reTbl.Replace(text, "$1$2" & IDEOGRAPHIC_COMMA)
    text = Replace(text, Chr(7), IDEOGRAPHIC_COMMA)
    Preprocess = text
End Function

' ======================================================================
' DictModel.ExtractMarkingSection equivalent, using the production patterns
' ======================================================================
Function ExtractMarkingSection(ByVal text)
    Dim re, m, idx, hdrNames, found, startPos, after, sentenceEndPos
    Set re = CreateObject("VBScript.RegExp")
    re.IgnoreCase = False
    re.Global = False

    hdrNames = Array("G02_HDR0", "G02_HDR1", "G02_HDR2", "G02_HDR3", "G02_HDR4", "G02_HDR5")
    found = False
    For idx = 0 To UBound(hdrNames)
        re.Pattern = vbaConsts(hdrNames(idx))
        Set m = re.Execute(text)
        If m.Count > 0 Then
            found = True
            Exit For
        End If
    Next

    If Not found Then
        ExtractMarkingSection = text
        Exit Function
    End If

    startPos = m(0).FirstIndex + m(0).Length
    after = Mid(text, startPos + 1)
    sentenceEndPos = InStr(1, after, IDEOGRAPHIC_FULLSTOP, vbBinaryCompare)

    re.Pattern = vbaConsts("G02_SECTIONEND")
    Set m = re.Execute(after)
    If sentenceEndPos > 0 Then
        If m.Count = 0 Then
            ExtractMarkingSection = Left(after, sentenceEndPos)
            Exit Function
        ElseIf sentenceEndPos < m(0).Length Then
            ExtractMarkingSection = Left(after, sentenceEndPos)
            Exit Function
        End If
    End If
    If m.Count > 0 Then
        ExtractMarkingSection = m(0).Value
    Else
        ExtractMarkingSection = after
    End If
End Function

' ======================================================================
' Patterns.ExtractAll / ExtractAllRaw equivalent, using production patterns
' ======================================================================
Function ExtractAllImpl(ByVal text, ByVal keepRaw)
    Dim allHits, keepRanges, candHits, candRanges, ci, ch, re3, m3, cand3, sorted
    Set allHits = ListNew()
    Set keepRanges = ListNew()

    text = Replace(text, "<br/>", " ")
    text = Replace(text, "<br />", " ")
    text = Replace(text, "<br>", " ")
    text = Replace(text, "<BR/>", " ")
    text = Replace(text, vbCr, vbLf)

    CollectHits allHits, keepRanges, text, vbaConsts("G02_P1"), False
    CollectHits allHits, keepRanges, text, vbaConsts("G02_PB"), False

    Set candHits = ListNew()
    Set candRanges = ListNew()
    CollectHits candHits, candRanges, text, vbaConsts("G02_P2"), True
    CollectHits candHits, candRanges, text, vbaConsts("G02_PA"), True
    For ci = 0 To ListCount(candHits) - 1
        ch = ListItem(candHits, ci)
        If Not Overlaps(ch, keepRanges) Then
            ListAdd allHits, ch
            ListAdd keepRanges, Array(ch(2), ch(2) + ch(3) - 1)
        End If
    Next

    Set re3 = CreateObject("VBScript.RegExp")
    re3.Global = True
    re3.Multiline = True
    re3.IgnoreCase = False
    re3.Pattern = vbaConsts("G02_P3")
    For Each m3 In re3.Execute(text)
        cand3 = Array(CStr(m3.SubMatches(1)), CStr(m3.SubMatches(0)), _
            CLng(m3.FirstIndex), CLng(m3.Length))
        If Not Overlaps(cand3, keepRanges) Then ListAdd allHits, cand3
    Next

    sorted = SortByPosition(ListToArray(allHits))
    If keepRaw Then
        ExtractAllImpl = sorted
    Else
        ExtractAllImpl = Dedupe(sorted)
    End If
End Function

Sub CollectHits(ByRef hits, ByRef ranges, ByVal text, ByVal pattern, ByVal nameFirst)
    Dim re, m, number, name, accept
    Set re = CreateObject("VBScript.RegExp")
    re.Global = True
    re.IgnoreCase = False
    re.Multiline = False
    re.Pattern = pattern
    For Each m In re.Execute(text)
        accept = True
        If nameFirst Then
            name = CStr(m.SubMatches(0))
            number = CStr(m.SubMatches(1))
            ' The name body may contain digits, so the engine can hand the first
            ' digit of an over-long number to the name. Apply the same number-start
            ' boundary rule as the number-first branch and as production VBA.
            ' The number can only start AFTER this name capture: a name may itself
            ' contain the same text as the number (X123 / 1123), and searching from
            ' the start of m.Value would find that earlier occurrence inside the name.
            ' SubMatches(0) is the untrimmed capture, so its length is the group
            ' length; G02_WS* between name and number is skipped by searching on.
            Dim numberOffset
            numberOffset = InStr(Len(CStr(m.SubMatches(0))) + 1, CStr(m.Value), number, vbBinaryCompare)
            accept = (numberOffset > 0)
            If accept Then accept = NumberStartOk(text, CLng(m.FirstIndex) + numberOffset - 1)
        Else
            number = CStr(m.SubMatches(0))
            name = CStr(m.SubMatches(1))
            ' Number-first patterns start at the number, so a match that starts in
            ' the middle of a longer number must be rejected (same rule as C#).
            accept = NumberStartOk(text, CLng(m.FirstIndex))
        End If
        If accept Then
            ListAdd hits, Array(number, name, CLng(m.FirstIndex), CLng(m.Length))
            ListAdd ranges, Array(CLng(m.FirstIndex), CLng(m.FirstIndex) + CLng(m.Length) - 1)
        End If
    Next
End Sub

' Identical to C# NumberStartOk: the character before the number must not be
' a digit, an ASCII letter or a hyphen.
Function NumberStartOk(ByVal text, ByVal startPos)
    Dim code
    If startPos <= 0 Then
        NumberStartOk = True
        Exit Function
    End If
    code = AscW(Mid(text, startPos, 1))
    If code < 0 Then code = code + 65536
    If (code >= 48 And code <= 57) Or (code >= 65 And code <= 90) Or _
       (code >= 97 And code <= 122) Or code = 45 Then
        NumberStartOk = False
    Else
        NumberStartOk = True
    End If
End Function

Function Overlaps(ByVal hit, ByVal ranges)
    Dim startPos, endPos, k, r
    startPos = hit(2)
    endPos = startPos + hit(3) - 1
    Overlaps = False
    For k = 0 To ListCount(ranges) - 1
        r = ListItem(ranges, k)
        If startPos <= r(1) And endPos >= r(0) Then
            Overlaps = True
            Exit Function
        End If
    Next
End Function

' Stable insertion sort by position, identical to C# SortByPosition.
Function SortByPosition(ByVal items)
    Dim n, off, k, j, cur, sorted
    If UBound(items) < LBound(items) Then
        SortByPosition = Array()
        Exit Function
    End If
    n = UBound(items) - LBound(items) + 1
    If n < 2 Then
        SortByPosition = items
        Exit Function
    End If
    off = LBound(items)
    sorted = items
    For k = 1 To n - 1
        cur = sorted(off + k)
        j = k - 1
        Do While j >= 0
            If CLng(sorted(off + j)(2)) <= CLng(cur(2)) Then Exit Do
            sorted(off + j + 1) = sorted(off + j)
            j = j - 1
        Loop
        sorted(off + j + 1) = cur
    Next
    SortByPosition = sorted
End Function

Function Dedupe(ByVal hits)
    Dim dict, out, k, key, h
    Set dict = CreateObject("Scripting.Dictionary")
    Set out = ListNew()
    If UBound(hits) < LBound(hits) Then
        Dedupe = Array()
        Exit Function
    End If
    For k = LBound(hits) To UBound(hits)
        h = hits(k)
        key = CStr(h(0)) & "|" & CStr(h(1))
        If Not dict.Exists(key) Then
            dict(key) = True
            ListAdd out, h
        End If
    Next
    Dedupe = ListToArray(out)
End Function

Function ListToArray(ByVal lst)
    Dim arr, k, n
    n = ListCount(lst)
    If n = 0 Then
        ListToArray = Array()
        Exit Function
    End If
    ReDim arr(n - 1)
    For k = 0 To n - 1
        arr(k) = ListItem(lst, k)
    Next
    ListToArray = arr
End Function

' ======================================================================
' Readers
' ======================================================================

' Parse `Public Const G02_xxx As String = <expression>` lines.
' The right-hand side may span `_` continuation lines and may be built from
' string literals concatenated with previously declared G02_* constants and
' the VBA line-break constants vbCr / vbLf. The expression is evaluated here
' with a tiny recursive parser; anything the parser does not understand is a
' hard SKIP/BLOCKED, so the probe can never silently fall back to hand-copied
' text or silently drop part of a pattern.
Function ParseStringConsts(ByVal source, ByVal fileName)
    Dim map, lines, k, line, joined, namePart, spacePos, name, valuePart, ok
    Set map = CreateObject("Scripting.Dictionary")
    lines = Split(Replace(source, vbCrLf, vbLf), vbLf)
    k = 0
    Do While k <= UBound(lines)
        line = Trim(lines(k))
        If InStr(1, line, "Public Const G02_", vbBinaryCompare) = 1 Then
            joined = line
            Do While Right(joined, 1) = "_" And k + 1 <= UBound(lines)
                k = k + 1
                joined = Trim(Left(joined, Len(joined) - 1)) & " " & Trim(lines(k))
            Loop
            namePart = Trim(Mid(joined, Len("Public Const ") + 1))
            spacePos = InStr(1, namePart, " ", vbBinaryCompare)
            If spacePos = 0 Then ConstParseBlocked fileName, namePart, "no type clause"
            name = Left(namePart, spacePos - 1)
            valuePart = Trim(Mid(joined, InStr(1, joined, "=", vbBinaryCompare) + 1))
            ok = EvalConstExpr(valuePart, map, valuePart)
            If Not ok Then ConstParseBlocked fileName, name, "unsupported expression"
            map(name) = valuePart
        End If
        k = k + 1
    Loop
    Set ParseStringConsts = map
End Function

' Evaluate a VBA string expression made of "literal" / G02_xxx / vbCr / vbLf
' joined with &. Returns False (and leaves the caller to block) on anything else.
Function EvalConstExpr(ByVal expr, ByVal map, ByRef result)
    Dim i, ch, parts, lit, tok, total
    i = 1
    total = ""
    Do While i <= Len(expr)
        ch = Mid(expr, i, 1)
        If ch = " " Then
            i = i + 1
        ElseIf ch = Chr(34) Then
            ' string literal: scan to the closing quote ("" is an escaped quote)
            i = i + 1
            lit = ""
            Do While i <= Len(expr)
                If Mid(expr, i, 1) = Chr(34) Then
                    If i + 1 <= Len(expr) Then
                        If Mid(expr, i + 1, 1) = Chr(34) Then
                            lit = lit & Chr(34)
                            i = i + 2
                        Else
                            i = i + 1
                            Exit Do
                        End If
                    Else
                        i = i + 1
                        Exit Do
                    End If
                Else
                    lit = lit & Mid(expr, i, 1)
                    i = i + 1
                End If
            Loop
            total = total & lit
        ElseIf ch = "&" Then
            i = i + 1
        Else
            ' identifier or keyword
            tok = ""
            Do While i <= Len(expr)
                ch = Mid(expr, i, 1)
                If (ch >= "A" And ch <= "Z") Or (ch >= "a" And ch <= "z") Or _
                   (ch >= "0" And ch <= "9") Or ch = "_" Then
                    tok = tok & ch
                    i = i + 1
                Else
                    Exit Do
                End If
            Loop
            If tok = "" Then
                EvalConstExpr = False
                Exit Function
            End If
            If tok = "vbCr" Then
                total = total & vbCr
            ElseIf tok = "vbLf" Then
                total = total & vbLf
            ElseIf tok = "vbCrLf" Then
                total = total & vbCrLf
            ElseIf map.Exists(tok) Then
                total = total & map(tok)
            Else
                EvalConstExpr = False
                Exit Function
            End If
        End If
    Loop
    result = total
    EvalConstExpr = True
End Function

Sub ConstParseBlocked(ByVal fileName, ByVal name, ByVal reason)
    WScript.Echo "SKIP/BLOCKED|const-parse|" & fileName & "|" & name & "|" & reason
    WScript.Quit 2
End Sub

' C# pattern dump: <name> U+0001 <pattern>, UTF-16LE
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

' Corpus: <id> U+0001 <escaped text>, UTF-16LE.
' The text uses the same escapes as the source matrix (\n, \r, \t, \\) so the
' data file stays one record per line.
Function ReadCases(ByVal path)
    Dim map, lines, k, line, bar
    Set map = CreateObject("Scripting.Dictionary")
    lines = Split(ReadUtf16(path), vbCrLf)
    For k = 0 To UBound(lines)
        line = lines(k)
        If Len(line) > 0 Then
            bar = InStr(1, line, SEP_FIELD, vbBinaryCompare)
            If bar > 0 Then map(Left(line, bar - 1)) = Unescape(Mid(line, bar + 1))
        End If
    Next
    Set ReadCases = map
End Function

' Inverse of the C# Escape helper.
Function Unescape(ByVal value)
    Dim i, ch, out
    i = 1
    out = ""
    Do While i <= Len(value)
        ch = Mid(value, i, 1)
        If ch = "\" And i + 1 <= Len(value) Then
            ch = Mid(value, i + 1, 1)
            If ch = "n" Then
                out = out & vbLf
                i = i + 2
            ElseIf ch = "r" Then
                out = out & vbCr
                i = i + 2
            ElseIf ch = "t" Then
                out = out & vbTab
                i = i + 2
            ElseIf ch = "\" Then
                out = out & "\"
                i = i + 2
            Else
                out = out & "\"
                i = i + 1
            End If
        Else
            out = out & ch
            i = i + 1
        End If
    Loop
    Unescape = out
End Function

' Expectations: S <id> TAB <count> TAB number|name ... / R <id> TAB number|name|count ...
Function ReadExpectations(ByVal path)
    Dim map, lines, k, line, parts, id, j, item, lastBar
    Set map = CreateObject("Scripting.Dictionary")
    lines = Split(ReadUtf8(path), vbLf)
    For k = 0 To UBound(lines)
        line = Replace(lines(k), vbCr, "")
        If Len(line) > 0 And Left(line, 1) <> "#" Then
            parts = Split(line, vbTab)
            If UBound(parts) >= 1 Then
                If Left(parts(0), 2) = "S " Then
                    id = Trim(Mid(parts(0), 3))
                    EnsureExpectation map, id
                    map(id & ".hits") = CLng(Trim(parts(1)))
                    For j = 2 To UBound(parts)
                        If map(id & ".items") = "" Then
                            map(id & ".items") = parts(j)
                        Else
                            map(id & ".items") = map(id & ".items") & Chr(2) & parts(j)
                        End If
                    Next
                ElseIf Left(parts(0), 2) = "R " Then
                    id = Trim(Mid(parts(0), 3))
                    EnsureExpectation map, id
                    For j = 1 To UBound(parts)
                        item = parts(j)
                        lastBar = InStrRev(item, "|", -1, vbBinaryCompare)
                        If lastBar > 0 Then
                            If map(id & ".raw") = "" Then
                                map(id & ".raw") = item
                            Else
                                map(id & ".raw") = map(id & ".raw") & Chr(2) & item
                            End If
                        End If
                    Next
                End If
            End If
        End If
    Next
    Set ReadExpectations = map
End Function

' VBScript cannot store objects in a Dictionary value, so each expectation is
' kept as three plain entries: <id>.hits, <id>.items, <id>.raw.
'   items = "number|name" joined with Chr(2), in order
'   raw   = "number|name=count" joined with Chr(2)
Function EnsureExpectation(ByVal map, ByVal id)
    If map.Exists(id & ".hits") Then Exit Function
    map(id & ".hits") = -1
    map(id & ".items") = ""
    map(id & ".raw") = ""
End Function

' ======================================================================
' Text readers
' ======================================================================
Function ReadTextCp936(ByVal path)
    Dim stream
    Set stream = CreateObject("ADODB.Stream")
    stream.Type = 2
    stream.Charset = "gb2312"
    stream.Open
    stream.LoadFromFile path
    ReadTextCp936 = stream.ReadText(-1)
    stream.Close
End Function

Function ReadUtf8(ByVal path)
    Dim stream
    Set stream = CreateObject("ADODB.Stream")
    stream.Type = 2
    stream.Charset = "utf-8"
    stream.Open
    stream.LoadFromFile path
    ReadUtf8 = stream.ReadText(-1)
    stream.Close
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

' ======================================================================
' Growable list
'
' VBScript has no "New Collection" (that is VBA only), so a list is a
' Scripting.Dictionary holding items under 0..n-1 plus its own length.
' Items are Variants, so a hit array can be stored directly.
' ======================================================================
Function ListNew()
    Dim lst
    Set lst = CreateObject("Scripting.Dictionary")
    lst("n") = 0
    Set ListNew = lst
End Function

Sub ListAdd(ByVal lst, ByVal item)
    lst(lst("n")) = item
    lst("n") = lst("n") + 1
End Sub

Function ListCount(ByVal lst)
    ListCount = lst("n")
End Function

Function ListItem(ByVal lst, ByVal i)
    ListItem = lst(i)
End Function

' ======================================================================
' Result reporting
' ======================================================================
Sub Pass(ByVal selector)
    passed = passed + 1
    WScript.Echo "PASS|" & selector
End Sub

Sub Fail(ByVal selector)
    failed = failed + 1
    WScript.Echo "FAIL|" & selector
End Sub

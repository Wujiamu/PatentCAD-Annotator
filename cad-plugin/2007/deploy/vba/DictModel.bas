Attribute VB_Name = "DictModel"
Option Explicit

Public Function BuildModel(ByVal text As String, ByVal sourceName As String, _
                           ByVal extractedAt As String) As Object

    Dim warnings As Collection: Set warnings = New Collection

    Dim sectionText As String
    sectionText = ExtractMarkingSection(text)

    If sectionText = text Then
        warnings.Add ChrW(&H672A) & ChrW(&H627E) & ChrW(&H5230) & ChrW(&H9644) & ChrW(&H56FE) & ChrW(&H8BF4) & ChrW(&H660E) & ChrW(&H6216) & ChrW(&H6807) & ChrW(&H8BB0) & ChrW(&H7AE0) & ChrW(&H8282) & ChrW(&HFF0C) & ChrW(&H5DF2) & ChrW(&H5BF9) & ChrW(&H6587) & ChrW(&H6863) & ChrW(&H5168) & ChrW(&H6587) & ChrW(&H8FDB) & ChrW(&H884C) & ChrW(&H626B) & ChrW(&H63CF) & ChrW(&H3002)
    End If

    Dim rawHits As Variant
    rawHits = Patterns.ExtractAll(sectionText)

    Dim entries As Object
    Set entries = CreateObject("Scripting.Dictionary")

    Dim i As Long, hit As Variant, number As String, name As String
    For i = LBound(rawHits) To UBound(rawHits)
        On Error GoTo NextHit
        hit = rawHits(i)
        number = hit(0)
        name = hit(1)

        If Not entries.Exists(number) Then
            Dim newEntry As Object
            Set newEntry = CreateObject("Scripting.Dictionary")
            newEntry("number") = number
            Set newEntry("nameCounts") = CreateObject("Scripting.Dictionary")
            newEntry("occurrences") = 0
            Set entries(number) = newEntry
        End If

        Dim entry As Object
        Set entry = entries(number)
        entry("occurrences") = entry("occurrences") + 1
        Dim nc As Object
        Set nc = entry("nameCounts")
        If nc.Exists(name) Then
            nc(name) = nc(name) + 1
        Else
            nc(name) = 1
        End If
NextHit:
        On Error GoTo 0
    Next

    Dim outEntries As Collection: Set outEntries = New Collection

    Dim key As Variant
    For Each key In entries.Keys
        Dim e As Object: Set e = entries(key)
        Dim nameCounts As Object: Set nameCounts = e("nameCounts")

        Dim conflicts As Collection: Set conflicts = New Collection
        If nameCounts.Count > 1 Then
            Dim candidates As Collection: Set candidates = New Collection
            Dim nk As Variant
            For Each nk In nameCounts.Keys
                candidates.Add CStr(nk)
            Next
            conflicts.Add MakeConflict(CStr(key), candidates)
            warnings.Add ChrW(&H6807) & ChrW(&H53F7) & " " & key & " " & ChrW(&H5B58) & ChrW(&H5728) & ChrW(&H591A) & ChrW(&H4E2A) & ChrW(&H540D) & ChrW(&H79F0) & ChrW(&HFF1A) & Join(ToStrArray(candidates), " vs ")
        End If

        Dim outEntry As Object: Set outEntry = CreateObject("Scripting.Dictionary")
        outEntry("number") = CStr(key)
        outEntry("name") = MostFrequent(nameCounts)
        outEntry("occurrences") = e("occurrences")
        outEntry("conflicts") = ToObjArray(conflicts)
        outEntries.Add outEntry
    Next

    Dim metadata As Object: Set metadata = CreateObject("Scripting.Dictionary")
    metadata("source_file") = sourceName
    metadata("extracted_at") = extractedAt
    metadata("version") = "1.0"

    Dim root As Object: Set root = CreateObject("Scripting.Dictionary")
    Set root("metadata") = metadata
    root("entries") = ToObjArray(outEntries)
    root("warnings") = ToStrArray(warnings)
    Set BuildModel = root
End Function

Private Function ExtractMarkingSection(ByVal text As String) As String
    Dim re As Object
    Set re = CreateObject("VBScript.RegExp")
    re.IgnoreCase = False

    Dim fujian As String: fujian = ChrW(&H9644) & ChrW(&H56FE)
    Dim shuoming As String: shuoming = ChrW(&H8BF4) & ChrW(&H660E)
    Dim biaoji As String: biaoji = ChrW(&H6807) & ChrW(&H8BB0)
    Dim tumian As String: tumian = ChrW(&H56FE) & ChrW(&H9762)
    Dim lingbujian As String: lingbujian = ChrW(&H96F6) & ChrW(&H90E8) & ChrW(&H4EF6)
    Dim cankao As String: cankao = ChrW(&H53C2) & ChrW(&H8003)
    Dim xia As String: xia = ChrW(&H4E0B)
    Dim ru As String: ru = ChrW(&H5982)
    Dim maoHao As String: maoHao = ChrW(&HFF1A)

    Dim patterns As Variant
    patterns = Array( _
        fujian & biaoji & shuoming & ru & xia & "[" & maoHao & ":\n\r]*", _
        fujian & shuoming & ru & xia & "[" & maoHao & ":\n\r]*", _
        fujian & shuoming & "[" & maoHao & ":]\s*", _
        fujian & "[" & maoHao & ":]\s*", _
        biaoji & shuoming & ru & xia & "[" & maoHao & ":\n\r]*", _
        biaoji & shuoming & "[" & maoHao & ":]\s*", _
        biaoji & "[" & maoHao & ":]\s*", _
        tumian & shuoming & ru & xia & "[" & maoHao & ":\n\r]*", _
        tumian & shuoming & "[" & maoHao & ":]\s*", _
        lingbujian & shuoming & "[" & maoHao & ":]\s*", _
        cankao & biaoji & shuoming & "[" & maoHao & ":]\s*" _
    )

    Dim p As Variant, m As Object, found As Boolean
    found = False
    For Each p In patterns
        re.pattern = CStr(p)
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

    Dim startPos As Long
    startPos = m(0).FirstIndex + m(0).Length
    Dim after As String
    after = Mid$(text, startPos + 1)

    Dim cutPos As Long
    cutPos = FindSectionEnd(after)
    If cutPos > 0 Then
        ExtractMarkingSection = Left$(after, cutPos - 1)
    Else
        ExtractMarkingSection = after
    End If
End Function

Private Function FindSectionEnd(ByVal s As String) As Long
    Dim pos As Long
    pos = InStr(1, s, vbCrLf & vbCrLf)
    If pos > 0 Then
        FindSectionEnd = pos
        Exit Function
    End If
    pos = InStr(1, s, vbLf & vbLf)
    If pos > 0 Then
        FindSectionEnd = pos
        Exit Function
    End If
    pos = InStr(1, s, vbCr & vbCr)
    If pos > 0 Then
        FindSectionEnd = pos
        Exit Function
    End If
    FindSectionEnd = 0
End Function

Private Function MostFrequent(ByVal nameCounts As Object) As String
    Dim best As String, bestCount As Long: bestCount = -1
    Dim k As Variant
    For Each k In nameCounts.Keys
        If nameCounts(k) > bestCount Then
            best = CStr(k): bestCount = nameCounts(k)
        End If
    Next
    MostFrequent = best
End Function

Private Function MakeConflict(ByVal number As String, ByVal candidates As Collection) As Object
    Dim c As Object: Set c = CreateObject("Scripting.Dictionary")
    c("number") = number
    c("candidates") = ToStrArray(candidates)
    Set MakeConflict = c
End Function

Private Function ToObjArray(ByVal col As Collection) As Variant
    If col.Count = 0 Then ToObjArray = Array(): Exit Function
    Dim arr() As Variant: ReDim arr(0 To col.Count - 1)
    Dim i As Long
    For i = 0 To col.Count - 1
        Set arr(i) = col(i + 1)
    Next
    ToObjArray = arr
End Function

Private Function ToStrArray(ByVal col As Collection) As Variant
    If col.Count = 0 Then ToStrArray = Array(): Exit Function
    Dim arr() As String: ReDim arr(0 To col.Count - 1)
    Dim i As Long
    For i = 0 To col.Count - 1
        arr(i) = CStr(col(i + 1))
    Next
    ToStrArray = arr
End Function

Attribute VB_Name = "DictModel"
Option Explicit

' 纯函数：给定全文，返回可直接 JsonWriter.Serialize 的根字典。
' 不依赖 Word 对象，便于单测。
'
' v1.0 变更：不再全文扫描。先找到「附图标记说明如下：」段落，
'           仅提取该段落内以分号终止的标号行，根除全文误匹配。
Public Function BuildModel(ByVal text As String, ByVal sourceName As String, _
                           ByVal extractedAt As String) As Object

    ' === 初始化 warnings 集合（必须在第一次使用前完成） ===
    Dim warnings As Collection: Set warnings = New Collection

    ' === 第一步：定位并截取「附图标记说明」段落 ===
    Dim sectionText As String
    sectionText = ExtractMarkingSection(text)

    ' 如果未找到标记头（回退到全文），添加警告
    If sectionText = text Then
        warnings.Add "未找到「附图标记说明」段落，已回退到全文扫描。请确认说明书包含「附图标记说明如下：」段落。"
    End If

    ' === 第二步：从段落中提取所有标号 ===
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

    ' === 第三步：组装输出 ===
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
            warnings.Add "编号 " & key & " 存在歧义：" & Join(ToStrArray(candidates), " vs ")
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

' ======================================================================
' 定位并截取「附图标记说明」段落文本
'
' 匹配以下常见写法的标记头：
'   附图标记说明如下：
'   附图标记说明：
'   标记说明如下：
'   标号说明如下：
'
' 截取从标记头之后、到下一个段落空行或下一个章节标题之前的内容。
' 若未找到任何标记头，返回全文（兼容旧文档）。
' ======================================================================
Private Function ExtractMarkingSection(ByVal text As String) As String
    Dim re As Object
    Set re = CreateObject("VBScript.RegExp")
    re.IgnoreCase = False

    ' 按优先级尝试多种标记头
    Dim patterns As Variant
    patterns = Array( _
        "附图标记说明如下[：:\n\r]*", _
        "附图标记说明[：:]\s*", _
        "附图标记[：:]\s*", _
        "标记说明如下[：:\n\r]*", _
        "标记说明[：:]\s*", _
        "标号说明[：:]\s*" _
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
        ' 未找到标记头，回退到全文扫描
        ExtractMarkingSection = text
        Exit Function
    End If

    ' 截取标记头之后的内容
    Dim startPos As Long
    startPos = m(0).FirstIndex + m(0).Length
    Dim after As String
    after = Mid$(text, startPos + 1)

    ' 截断到下一个段落空行（两连换行）或文本末尾
    re.pattern = "[\s\S]*?(\r?\n\s*\r?\n|\Z)"
    Set m = re.Execute(after)
    If m.Count > 0 Then
        ExtractMarkingSection = m(0).Value
    Else
        ExtractMarkingSection = after
    End If
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

Attribute VB_Name = "Patterns"
Option Explicit

' 附图标记匹配引擎 — v3.0
'
' 支持格式（中国专利附图标注常见写法）：
'   [旧格式] 编号在前 + 名称在后   1底座， 2支架； 10外壳A1，
'   [新格式] 名称在前 + 编号在后   箱体结构10、 第一空间S1、 隔板131a、
'   [括号变体] 名称(编号) / 编号(名称)   加热器(1)、 1(底座)、
'   [裸列表] 每行一条，名称 编号   箱体结构 10<换行>箱壁 1
'
' 编号形式：纯数字(10)、字母前缀(S1)、字母后缀(131a)、连字符子编号(10-1)
' 分隔符  ：中文逗号/顿号/分号/句号、英文逗号/分号
'
' 防误匹配策略：
'   1. 旧格式(模式1/括号B)优先收集，新格式(模式2/括号A)若与旧格式命中区间重叠则丢弃
'   2. 裸列表按行匹配(^...$)，与任何标点模式命中重叠的行跳过
'   3. 最终按 (number, name) 去重，保持首次出现顺序

Public Type Hit
    Number As String
    Name As String
    Position As Long       ' 文档中的字符位置（用于光标定位）
End Type

Public Function ExtractAll(ByVal text As String) As Variant
    Dim allHits As Collection
    Set allHits = New Collection
    Dim keepRanges As Collection
    Set keepRanges = New Collection

    ' === 预处理 ===
    ' HTML 换行标签 → 空格
    text = Replace(text, "<br/>", " ")
    text = Replace(text, "<br />", " ")
    text = Replace(text, "<br>", " ")
    text = Replace(text, "<BR/>", " ")
    ' 统一换行符（裸列表按行匹配依赖 \n）
    text = Replace(text, vbCr, vbLf)

    ' === 第一梯队：旧格式（编号在前），命中区间用于过滤 ===
    ' 模式 1：编号 + 名称 + 分隔符(，;,、。.)
    '   1底座， 2支架； 10外壳A1， 1底座。 2支架、
    CollectHits allHits, keepRanges, text, _
        "(\d{1,5})\s*([\u4e00-\u9fa5A-Za-z0-9]*[\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9]*)\s*[，;,、。.]", False
    ' 模式 B：编号 + 括号名称 + 分隔符
    '   1(底座)、 10（泵体）；
    CollectHits allHits, keepRanges, text, _
        "(\d{1,5})\s*[（(]([\u4e00-\u9fa5A-Za-z0-9]*[\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9]*)[）)]\s*[，;；,、。.]", False

    ' === 第二梯队：新格式（名称在前），与第一梯队重叠则丢弃 ===
    Dim candHits As Collection
    Set candHits = New Collection
    Dim candRanges As Collection
    Set candRanges = New Collection
    ' 模式 2：名称 + 编号 + 分隔符(、；。，;,)
    '   箱体结构10、 第一空间S1、 第一开口131a、 液体3000。
    '   外壳 主体 10、 子板10-1、
    CollectHits candHits, candRanges, text, _
        "([\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9 ]*?)\s*([A-Z]?\d{1,5}(?:-[A-Z]?\d{1,5})?[a-z]?)\s*[、；。，;,.]", True
    ' 模式 A：名称 + 括号编号 + 分隔符
    '   加热器(1)、 泵体（2）；
    CollectHits candHits, candRanges, text, _
        "([\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9 ]*?)\s*[（(]([A-Z]?\d{1,5}(?:-[A-Z]?\d{1,5})?[a-z]?)[）)]\s*[、；。，;,.]", True

    Dim ci As Long, ch As Variant
    For ci = 1 To candHits.Count
        ch = candHits(ci)
        If Not Overlaps(ch, keepRanges) Then
            allHits.Add ch
            keepRanges.Add Array(ch(2), ch(3))
        End If
    Next

    ' === 第三梯队：裸列表（每行 名称 编号，行内无标点） ===
    Dim re3 As Object
    Set re3 = CreateObject("VBScript.RegExp")
    re3.Global = True
    re3.Multiline = True
    re3.IgnoreCase = False
    re3.pattern = "^\s*([\u4e00-\u9fa5A-Za-z ]+?)\s*([A-Z]?\d{1,5}(?:-[A-Z]?\d{1,5})?[a-z]?)\s*$"
    Dim m3 As Object, h3 As Hit
    For Each m3 In re3.Execute(text)
        h3.Name = m3.SubMatches(0)
        h3.Number = m3.SubMatches(1)
        h3.Position = m3.FirstIndex
        Dim cand3 As Variant
        cand3 = Array(h3.Number, h3.Name, h3.Position, m3.Length)
        If Not Overlaps(cand3, keepRanges) Then
            allHits.Add cand3
        End If
    Next

    ' === 去重（number|name），保持首次出现顺序 ===
    ExtractAll = Dedupe(allHits)
End Function

' 收集命中：hits 保存 [number, name, position, length]，ranges 保存 [start, end]
Private Sub CollectHits(ByRef hits As Collection, ByRef ranges As Collection, _
                        ByVal text As String, ByVal pattern As String, _
                        ByVal nameFirst As Boolean)
    Dim re As Object
    Set re = CreateObject("VBScript.RegExp")
    re.Global = True
    re.IgnoreCase = False
    re.pattern = pattern

    Dim m As Object, h As Hit
    For Each m In re.Execute(text)
        If nameFirst Then
            h.Name = m.SubMatches(0)
            h.Number = m.SubMatches(1)
        Else
            h.Number = m.SubMatches(0)
            h.Name = m.SubMatches(1)
        End If
        h.Position = m.FirstIndex
        hits.Add Array(h.Number, h.Name, h.Position, m.Length)
        ranges.Add Array(m.FirstIndex, m.FirstIndex + m.Length - 1)
    Next
End Sub

' 判断命中区间是否与已有区间重叠
Private Function Overlaps(ByVal hit As Variant, ByVal ranges As Collection) As Boolean
    Dim startPos As Long, endPos As Long
    startPos = hit(2)
    endPos = startPos + hit(3) - 1
    Dim i As Long, r As Variant
    For i = 1 To ranges.Count
        r = ranges(i)
        If startPos <= r(1) And endPos >= r(0) Then
            Overlaps = True
            Exit Function
        End If
    Next
    Overlaps = False
End Function

Private Function Dedupe(ByVal hits As Collection) As Variant
    Dim dict As Object
    Set dict = CreateObject("Scripting.Dictionary")
    Dim out As Collection
    Set out = New Collection
    Dim i As Long, h As Variant, key As String
    For i = 1 To hits.Count
        h = hits(i)
        key = CStr(h(0)) & "|" & CStr(h(1))
        If Not dict.Exists(key) Then
            dict(key) = True
            out.Add h
        End If
    Next
    Dedupe = CollectionToArray(out)
End Function

Private Function CollectionToArray(col As Collection) As Variant
    If col.Count = 0 Then
        CollectionToArray = Array()
        Exit Function
    End If
    Dim arr() As Variant
    ReDim arr(0 To col.Count - 1)
    Dim i As Long
    For i = 0 To col.Count - 1
        arr(i) = col(i + 1)
    Next
    CollectionToArray = arr
End Function
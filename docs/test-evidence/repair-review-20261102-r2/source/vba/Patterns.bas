Attribute VB_Name = "Patterns"
Option Explicit

' 附图标记匹配引擎 — v4.0
'
' 支持格式（中国专利附图标注常见写法）：
'   [旧格式] 编号在前 + 名称在后   1底座， 2支架； 10外壳A1， S1底座， 10-1支架；
'   [新格式] 名称在前 + 编号在后   箱体结构10、 第一空间S1、 隔板131a、
'   [括号变体] 名称(编号) / 编号(名称)   加热器(1)、 1(底座)、 S1(底座)、
'   [裸列表] 每行一条，名称 编号   箱体结构 10<换行>箱壁 1
'
' 编号形式：纯数字(10)、字母前缀(S1)、字母后缀(131a/123A1)、连字符子编号(10-1)
' 分隔符  ：中文逗号/顿号/分号/句号、英文逗号/分号
'
' 防误匹配策略：
'   1. 旧格式(模式1/括号B)优先收集，新格式(模式2/括号A)若与旧格式命中区间重叠则丢弃
'   2. 裸列表按行匹配(^...$)，与任何标点模式命中重叠的行跳过
'   3. 结果按原文位置稳定排序（同位置保持收集顺序，即旧格式优先）
'   4. 去重视图按 (number, name) 去重并保持首次出现顺序；统计请用未去重视图
'
' v4.0 变更（G02 / R-016、R-017、R-018、R-021、R-032、R-033）：
'   * 编号词法改为完整词法，字母前缀/连字符子编号/字母后缀都不再被截断；
'   * 新增 ExtractAllRaw 提供未去重命中，ExtractAll 保持原有去重契约；
'   * 结果按原文位置稳定排序，混合格式不再按梯队顺序输出；
'   * 空白改用与 .NET \s 等价的显式字符类，裸列表空白只在本行内；
'   * 编号左边界改由 NumberStartOk 判定（VBScript.RegExp 不支持后顾断言）。
'
' 跨引擎约束（与 cad-plugin/Shared/IO/MarkingTextParser.cs 共用同一份模式文本）：
'   * VBScript.RegExp 的 \d 只匹配 0-9，.NET 还匹配全角/阿拉伯数字 → 统一写 [0-9]；
'   * VBScript 的 \s 只有 6 个成员，.NET 有 25 个 → 统一写显式字符类 G02_WS；
'   * 不用 \Z（VBScript 视其为字面量 Z，.NET 停在末尾换行之前）→ 文末用 (?![\s\S])；
'   * VBScript 不支持后顾断言 → 编号左边界由 NumberStartOk 在两侧用同一算法判定。

Public Type Hit
    Number As String
    Name As String
    Position As Long       ' 文档中的字符位置（用于光标定位）
End Type

' ================================================================
' 共享模式常量：与 MarkingTextParser.cs 中同名 G02_* 常量逐字一致
'
' 每一段都以单个字符串字面量写在 Const 中，两侧文本完全相同
' （tools/test-g02-parser.vbs 直接从本文件解析 G02_* 常量，与 C# 生产代码
' 导出的同一份文本逐字比较，再用真实 VBScript.RegExp 跑同一份语料）。
' 改动任何一段都必须两侧同步，否则对照探针失败。
' ================================================================

' 完整编号词法（名称在前 / 表格B / 裸列表）：字母前缀 + 1~5 位数字 +
' 可选连字符子编号 + 可选字母后缀（字母后缀后可以没有数字，例如 131a）
Public Const G02_NUM As String = _
    "[A-Z]?[0-9]{1,5}(?:-[A-Z]?[0-9]{1,5})?(?:[A-Za-z][0-9]*)?"


' 与 .NET \s 完全等价的显式空白类（25 个码位）
Public Const G02_WS As String = _
    "[ \t\r\n\f\v\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]"

' 行内空白类：只允许空格与 Tab，不含任何换行
Public Const G02_INLINE As String = "[ \t]"

' 名称体（编号在前）：允许中英数字，但必须含至少一个汉字
Public Const G02_NAME As String = _
    "[\u4e00-\u9fa5A-Za-z0-9]*[\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9]*"

' 名称体（名称在前）：以汉字开头，可含汉字/字母/数字/空格
Public Const G02_NAMEFIRST As String = "[\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9 ]*?"

' 名称体（裸列表行）：以汉字或字母开头且不含数字
Public Const G02_NAMEBARE As String = "[\u4e00-\u9fa5A-Za-z ]+?"

' 分隔符类
Public Const G02_SEP As String = "[，；;,、。.]"
Public Const G02_SEPN As String = "[、；。，;,.]"

' 表格单元格左边界：文首、段落标记、换行或上一单元格结束符(Ctrl-G)
Public Const G02_CELLSTART As String = "(^|[\r\n\u0007])"

' 各模式（编号在前 / 名称在前 / 裸列表），两侧引擎使用完全相同的文本
Public Const G02_P1 As String = _
    "(" & G02_NUM & ")(?![0-9])" & G02_WS & "*(" & G02_NAME & ")" & G02_WS & "*" & G02_SEP
Public Const G02_PB As String = _
    "(" & G02_NUM & ")(?![0-9])" & G02_WS & "*[（(](" & G02_NAME & ")[）)]" & G02_WS & "*" & G02_SEP
Public Const G02_P2 As String = _
    "(" & G02_NAMEFIRST & ")" & G02_WS & "*(" & G02_NUM & ")(?![0-9])" & G02_WS & "*" & G02_SEPN
Public Const G02_PA As String = _
    "(" & G02_NAMEFIRST & ")" & G02_WS & "*[（(](" & G02_NUM & ")(?![0-9])[）)]" & G02_WS & "*" & G02_SEPN
Public Const G02_P3 As String = _
    "^" & G02_INLINE & "*(" & G02_NAMEBARE & ")" & G02_INLINE & "*(" & G02_NUM & ")(?![0-9])" & G02_INLINE & "*$"

' 标记头模式（按优先级），对应 DictModel.bas 的 patterns 数组
Public Const G02_HDR0 As String = "附图标记说明如下[：:\n\r]*"
Public Const G02_HDR1 As String = "附图标记说明[：:]" & G02_WS & "*"
Public Const G02_HDR2 As String = "附图标记[：:]" & G02_WS & "*"
Public Const G02_HDR3 As String = "标记说明如下[：:\n\r]*"
Public Const G02_HDR4 As String = "标记说明[：:]" & G02_WS & "*"
Public Const G02_HDR5 As String = "标号说明[：:]" & G02_WS & "*"

' 段落结束边界：优先空段落（两连换行），否则文末。
' 文末不用 \Z（VBScript 视其为字面量 Z，.NET 会停在末尾换行之前，这是 R-032 的成因），
' 统一用 (?![\s\S]) 表示真正的文本末尾。
Public Const G02_SECTIONEND As String = _
    "[\s\S]*?(\r\n" & G02_WS & "*\r\n|\n" & G02_WS & "*\n|\r" & G02_WS & "*\r|(?![\s\S]))"

' 表格流模式（Word 单元格文本 + vbCr + Chr(7)）
Public Const G02_TABLEA As String = _
    G02_CELLSTART & "(" & G02_NUM & ")\r\u0007(" & G02_NAME & ")\r\u0007"
Public Const G02_TABLEB As String = _
    "(" & G02_NAME & ")\r\u0007(" & G02_NUM & ")\r\u0007"

' ================================================================
' 公开入口
' ================================================================

' 去重视图：按 (number|name) 去重，保持首次出现顺序。
' 对外契约不变，供面板/粘贴预览使用；统计请用 ExtractAllRaw。
Public Function ExtractAll(ByVal text As String) As Variant
    ExtractAll = Dedupe(ExtractAllRaw(text))
End Function

' 未去重视图：按原文位置稳定排序，保留每一次真实命中，供频率统计使用。
Public Function ExtractAllRaw(ByVal text As String) As Variant
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
    CollectHits allHits, keepRanges, text, G02_P1, False
    CollectHits allHits, keepRanges, text, G02_PB, False

    ' === 第二梯队：新格式（名称在前），与第一梯队重叠则丢弃 ===
    Dim candHits As Collection
    Set candHits = New Collection
    Dim candRanges As Collection
    Set candRanges = New Collection
    CollectHits candHits, candRanges, text, G02_P2, True
    CollectHits candHits, candRanges, text, G02_PA, True

    Dim ci As Long, ch As Variant
    For ci = 1 To candHits.Count
        ch = candHits(ci)
        If Not Overlaps(ch, keepRanges) Then
            allHits.Add ch
            keepRanges.Add Array(ch(2), ch(2) + ch(3) - 1)
        End If
    Next

    ' === 第三梯队：裸列表（每行 名称 编号，行内无标点） ===
    Dim re3 As Object, m3 As Object
    Set re3 = CreateObject("VBScript.RegExp")
    re3.Global = True
    re3.Multiline = True
    re3.IgnoreCase = False
    re3.pattern = G02_P3
    Dim cand3 As Variant
    For Each m3 In re3.Execute(text)
        cand3 = Array(CStr(m3.SubMatches(1)), CStr(m3.SubMatches(0)), _
            CLng(m3.FirstIndex), CLng(m3.Length))
        If Not Overlaps(cand3, keepRanges) Then
            allHits.Add cand3
        End If
    Next

    ' === 按原文位置稳定排序（同位置保持收集顺序） ===
    ExtractAllRaw = SortByPosition(CollectionToArray(allHits))
End Function

' ================================================================
' 内部逻辑
' ================================================================

' 收集命中：hits 保存 [number, name, position, length]，ranges 保存 [start, end]
Private Sub CollectHits(ByRef hits As Collection, ByRef ranges As Collection, _
                        ByVal text As String, ByVal pattern As String, _
                        ByVal nameFirst As Boolean)
    Dim re As Object
    Set re = CreateObject("VBScript.RegExp")
    re.Global = True
    re.IgnoreCase = False
    re.Multiline = False
    re.pattern = pattern

    Dim m As Object, number As String, name As String
    For Each m In re.Execute(text)
        If nameFirst Then
            name = CStr(m.SubMatches(0))
            number = CStr(m.SubMatches(1))
            ' VBScript 没有捕获组 Index，须在匹配片段内自行定位编号起点。
            ' 起点只能在本次名称捕获组之后：名称体允许数字，名称本身可能含
            ' 与编号相同的文本（壳体X123 支架 123），从整个 m.Value 取第一个
            ' 同文子串会命中名称里的那个更早位置，把合法条目录杀。
            ' SubMatches(0) 是未 Trim 的原始捕获文本，其长度即捕获组长度；
            ' 名称之后还有 G02_WS* 空白，因此从该偏移起在 m.Value 内继续找编号。
            Dim numberOffset As Long
            numberOffset = InStr(Len(CStr(m.SubMatches(0))) + 1, CStr(m.Value), number, vbBinaryCompare)
            If numberOffset <= 0 Then GoTo NextMatch
            If Not NumberStartOk(text, CLng(m.FirstIndex) + numberOffset - 1) Then GoTo NextMatch
        Else
            number = CStr(m.SubMatches(0))
            name = CStr(m.SubMatches(1))
            ' 编号在前的模式从编号开始匹配，须拒绝从更长编号中间起配
            If Not NumberStartOk(text, CLng(m.FirstIndex)) Then GoTo NextMatch
        End If
        hits.Add Array(number, name, CLng(m.FirstIndex), CLng(m.Length))
        ranges.Add Array(CLng(m.FirstIndex), CLng(m.FirstIndex) + CLng(m.Length) - 1)
NextMatch:
    Next
End Sub

' 判断编号起点是否合法：前一字符不能是数字、ASCII 字母或连字符，
' 否则说明这是从更长编号中间截取的片段（例如 123456底座 里的 23456）。
' 与 C# MarkingTextParser.NumberStartOk 完全一致（VBScript 不支持后顾断言）。
Private Function NumberStartOk(ByVal text As String, ByVal startPos As Long) As Boolean
    If startPos <= 0 Then
        NumberStartOk = True
        Exit Function
    End If
    Dim code As Long
    code = AscW(Mid$(text, startPos, 1))
    If code < 0 Then code = code + 65536
    If (code >= 48 And code <= 57) Or (code >= 65 And code <= 90) Or _
       (code >= 97 And code <= 122) Or code = 45 Then
        NumberStartOk = False
    Else
        NumberStartOk = True
    End If
End Function

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

' 按 position（下标 2）做稳定插入排序：等值不交换，保持收集顺序（旧格式优先）
Private Function SortByPosition(ByVal items As Variant) As Variant
    If UBound(items) < LBound(items) Then
        SortByPosition = Array()
        Exit Function
    End If
    Dim n As Long, off As Long
    off = LBound(items)
    n = UBound(items) - off + 1
    If n < 2 Then
        SortByPosition = items
        Exit Function
    End If
    Dim sorted As Variant, k As Long, j As Long, cur As Variant
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

' 按 (number|name) 去重，保持首次出现顺序（供展示/粘贴使用）
Private Function Dedupe(ByVal hits As Variant) As Variant
    If UBound(hits) < LBound(hits) Then
        Dedupe = Array()
        Exit Function
    End If
    Dim dict As Object
    Set dict = CreateObject("Scripting.Dictionary")
    Dim out As Collection
    Set out = New Collection
    Dim i As Long, h As Variant, key As String
    For i = LBound(hits) To UBound(hits)
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

Attribute VB_Name = "Patterns"
Option Explicit

' 标号匹配正则规则集 — v1.0 重写
'
' 设计原则：仅提取「附图标记说明」段落的标号，避免全文误匹配。
' 格式假设（用户确认的中国专利附图标记标准写法）：
'   ① 段落前有标记头「附图标记说明如下：」（含变体）
'   ② 每行格式：数字 + 中文名 + 可选后缀 + 分号（中/英）
'   ③ 例：1底座； 2支架； 10传动轴A1； 123外壳B；
'
' 后缀（如 A1、B 等）合并进名称，不单独处理。
' 数字范围：1~5 位（支持 4-5 位子编号）。
'
' 不再使用旧 P1-P4 全文扫描模式，以根除「图1」「步骤1」等误命中。

Public Type Hit
    Number As String
    Name As String
    Position As Long       ' 文档中的字符位置，用于排序和定位
End Type

' 调用方：传入全文（BuildModel 会先提取标记段落后再调用此函数）。
' 返回所有匹配的 (number, name) 对。
Public Function ExtractAll(ByVal text As String) As Variant
    Dim allHits As Collection
    Set allHits = New Collection

    ' 核心模式：数字 + 名称(中文/英文/数字混合) + 分号
    ' 例：1底座； → (1, 底座)
    '     10传动轴A1； → (10, 传动轴A1)
    '     2211连接段； → (2211, 连接段)  ← 支持4-5位子编号
    ' 名称可含中文、英文、数字任意组合，以分号终止。
    AddHits allHits, text, _
        "(\d{1,5})\s*([\u4e00-\u9fa5A-Za-z0-9]+)\s*[；;]", False

    ExtractAll = CollectionToArray(allHits)
End Function

Private Sub AddHits(ByRef hits As Collection, ByVal text As String, _
                    ByVal pattern As String, ByVal nameFirst As Boolean)
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
        hits.Add Array(h.Number, h.Name, h.Position)
    Next
End Sub

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

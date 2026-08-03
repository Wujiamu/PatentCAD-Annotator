Attribute VB_Name = "Patterns"
Option Explicit

' 附图标记匹配引擎 — v2.0 重写
'
' 设计原则：仅提取「附图标记说明」段落内的标号，不做全文匹配。
' 格式假设（用户确认的中国专利附图标注标准写法）：
'   ① 编号在前 + 名称在后（旧格式）：  1底座， 2支架； 10箱体A1， 123外壳B；
'   ② 名称在前 + 编号在后（新格式）：  箱体结构10、 箱壁1、 第一空间S1、 隔板131a、
'
' 编号形式：
'   - 纯数字：1~5 位（支持 4-5 位子编号）
'   - 字母前缀：S1、A2（大写字母 + 数字）
'   - 字母后缀：131a、171a（数字 + 小写字母）
'
' 分隔符：
'   - 旧格式：，（中文逗号）或 ;（英文分号）
'   - 新格式：、（顿号）或 ；（中文分号）或 。（句号）
'   - <br/> 标签在调用前已被替换为空格

Public Type Hit
    Number As String
    Name As String
    Position As Long       ' 文档中的字符位置（用于光标定位）
End Type

' 主方法：扫描全文（BuildModel 会先截取标记段落再调用此函数）。
' 返回所有匹配到的 (number, name) 对。
Public Function ExtractAll(ByVal text As String) As Variant
    Dim allHits As Collection
    Set allHits = New Collection

    ' 预处理：将 <br/> 系列标签替换为空格，避免干扰正则匹配
    text = Replace(text, "<br/>", " ")
    text = Replace(text, "<br />", " ")
    text = Replace(text, "<br>", " ")
    text = Replace(text, "<BR/>", " ")

    ' 模式 1（旧格式）：编号 + 名称 + 分隔符(，或;)
    ' 示例：1底座， → (1, 底座)
    '       10箱体结构A1， → (10, 箱体结构A1)
    '       2211子段， → (2211, 子段)  ← 支持4-5位子编号
    AddHits allHits, text, _
        "(\d{1,5})\s*([\u4e00-\u9fa5A-Za-z0-9]+)\s*[，;]", False

    ' 模式 2（新格式）：名称 + 编号 + 分隔符(、或；或。)
    ' 编号支持：纯数字(10)、字母前缀(S1)、字母后缀(131a)
    ' 示例：箱体结构10、 → (箱体结构, 10)
    '       第一空间S1、 → (第一空间, S1)
    '       第一开口131a、 → (第一开口, 131a)
    '       液体3000。 → (液体, 3000)
    AddHits allHits, text, _
        "([\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9]*?)\s*([A-Z]?\d{1,5}[a-z]?)\s*[、；。]", True

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
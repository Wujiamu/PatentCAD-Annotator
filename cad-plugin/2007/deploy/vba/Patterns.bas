Attribute VB_Name = "Patterns"
Option Explicit

Public Type Hit
    Number As String
    Name As String
    Position As Long
End Type

Public Function ExtractAll(ByVal text As String) As Variant
    Dim allHits As Collection
    Set allHits = New Collection

    Dim re As Object
    Set re = CreateObject("VBScript.RegExp")
    re.Global = True
    re.IgnoreCase = False

    Dim sepLight As String
    sepLight = ChrW(&H3001) & ChrW(&HFF0C) & ",;" & ChrW(&HFF1B) & "/\s-"

    Dim sepHeavy As String
    sepHeavy = ChrW(&H3001) & ChrW(&HFF1A) & ":" & ChrW(&HFF0E) & "." & ChrW(&HFF1B) & ";" & ChrW(&HFF0C) & "," & ChrW(&HFF0D) & ChrW(&H2014) & ChrW(&H2013) & "\s/-"

    Dim cjk As String
    cjk = ChrW(&H4E00) & "-" & ChrW(&H9FA5)

    Dim nameChars As String
    nameChars = cjk & "A-Za-z0-9" & ChrW(&HFF08) & ChrW(&HFF09) & "()"

    re.pattern = "(\d{1,5}[A-Fa-f]?(?:[" & sepLight & "]*\d{1,5}[A-Fa-f]?)*)(?:[" & sepHeavy & "]*)([" & cjk & "A-Za-z][" & nameChars & "]*)"

    Dim m As Object, match As Object
    Set m = re.Execute(text)

    Dim numbersPart As String
    Dim name As String
    Dim nums As Collection
    Dim j As Long

    For Each match In m
        numbersPart = match.SubMatches(0)
        name = match.SubMatches(1)

        Set nums = SplitNumbers(numbersPart)

        For j = 1 To nums.Count
            allHits.Add Array(CStr(nums(j)), name, match.FirstIndex)
        Next
    Next

    Dim re2 As Object
    Set re2 = CreateObject("VBScript.RegExp")
    re2.Global = True
    re2.IgnoreCase = False

    Dim cnNums As String
    cnNums = ChrW(&H4E8C) & ChrW(&H5341) & "|" & _
             ChrW(&H5341) & ChrW(&H4E5D) & "|" & _
             ChrW(&H5341) & ChrW(&H516B) & "|" & _
             ChrW(&H5341) & ChrW(&H4E03) & "|" & _
             ChrW(&H5341) & ChrW(&H516D) & "|" & _
             ChrW(&H5341) & ChrW(&H4E94) & "|" & _
             ChrW(&H5341) & ChrW(&H56DB) & "|" & _
             ChrW(&H5341) & ChrW(&H4E09) & "|" & _
             ChrW(&H5341) & ChrW(&H4E8C) & "|" & _
             ChrW(&H5341) & ChrW(&H4E00) & "|" & _
             ChrW(&H5341) & "|" & _
             ChrW(&H4E5D) & "|" & _
             ChrW(&H516B) & "|" & _
             ChrW(&H4E03) & "|" & _
             ChrW(&H516D) & "|" & _
             ChrW(&H4E94) & "|" & _
             ChrW(&H56DB) & "|" & _
             ChrW(&H4E09) & "|" & _
             ChrW(&H4E8C) & "|" & _
             ChrW(&H4E00)

    Dim cnSep As String
    cnSep = ChrW(&H3001) & ChrW(&HFF1A) & ":"

    Dim cnStop As String
    cnStop = "\n\r" & ChrW(&HFF0C) & "," & ChrW(&HFF1B) & ";" & ChrW(&H3002) & ChrW(&H3001)

    re2.pattern = "(" & cnNums & ")[" & cnSep & "]\s*([" & "^" & cnStop & "]+)"

    Dim m2 As Object, match2 As Object
    Set m2 = re2.Execute(text)

    For Each match2 In m2
        allHits.Add Array(match2.SubMatches(0), Trim(match2.SubMatches(1)), match2.FirstIndex)
    Next

    ExtractAll = CollectionToArray(allHits)
End Function

Private Function SplitNumbers(ByVal numbersPart As String) As Collection
    Dim result As Collection
    Set result = New Collection

    Dim re As Object
    Set re = CreateObject("VBScript.RegExp")
    re.Global = True
    re.IgnoreCase = False
    re.pattern = "\d{1,5}[A-Fa-f]?"

    Dim m As Object, match As Object
    Set m = re.Execute(numbersPart)
    For Each match In m
        result.Add match.Value
    Next

    Set SplitNumbers = result
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

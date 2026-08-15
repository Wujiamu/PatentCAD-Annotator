Attribute VB_Name = "BoundaryHarness"
Option Explicit

Public Sub RunBoundary()
    Dim tracePath As String
    tracePath = Environ$("TEMP") & "\PatentMarkerBoundary.trace.txt"
    On Error GoTo failed
    TraceLine tracePath, "start"

    Dim rawHits As Variant
    rawHits = Patterns.ExtractAll(BuildBoundaryText())
    TraceLine tracePath, "patterns-built"

    Dim root As Object
    Set root = DictModel.BuildModel(BuildBoundaryText(), "boundary.docm", "2026-08-05T00:00:00")
    TraceLine tracePath, "model-built"

    Dim json As String
    json = JsonWriter.Serialize(root)
    TraceLine tracePath, "json-built:" & Len(json)
    JsonWriter.WriteToFile Environ$("TEMP") & "\PatentMarkerBoundary.dict.json", json
    TraceLine tracePath, "json-written"
    Exit Sub

failed:
    TraceLine tracePath, "ERROR:" & Err.Number & ":" & Err.Description
End Sub

Private Function BuildBoundaryText() As String
    Dim header As String, bodyHeading As String
    header = ChrW(&H9644) & ChrW(&H56FE) & ChrW(&H6807) & ChrW(&H8BB0) & ChrW(&H8BF4) & ChrW(&H660E) & ChrW(&H5982) & ChrW(&H4E0B) & ChrW(&HFF1A)
    bodyHeading = ChrW(&H5177) & ChrW(&H4F53) & ChrW(&H5B9E) & ChrW(&H65BD) & ChrW(&H65B9) & ChrW(&H5F0F)
    BuildBoundaryText = ChrW(&H6B63) & ChrW(&H6587) & vbCr & header & vbCr & _
        "100" & ChrW(&H677F) & ChrW(&H5F0F) & ChrW(&H6362) & ChrW(&H70ED) & ChrW(&H5668) & ChrW(&HFF1B) & _
        "110" & ChrW(&H677F) & ChrW(&H7247) & ChrW(&H3002) & vbCr & bodyHeading & vbCr & _
        "200" & ChrW(&H63A7) & ChrW(&H5236) & ChrW(&H5668) & ChrW(&H3002)
End Function

Private Sub TraceLine(ByVal path As String, ByVal message As String)
    Dim n As Integer
    n = FreeFile
    Open path For Append As #n
    Print #n, message
    Close #n
End Sub

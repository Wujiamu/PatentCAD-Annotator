VERSION 1.0 CLASS
BEGIN
  MultiUse = -1
END
Attribute VB_Name = "clsSaveHook"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private WithEvents App As Word.Application

Private Sub Class_Initialize()
    Set App = Word.Application
End Sub

Private Sub Class_Terminate()
    Set App = Nothing
End Sub

Private Sub App_DocumentBeforeSave(ByVal Doc As Document, SaveAsUI As Boolean, Cancel As Boolean)
    Dim exportOk As Boolean
    exportOk = AutoExport.ExportDict(Doc)
    If exportOk Then Exit Sub

    ' A Save As establishes the final document path only after this event;
    ' allow it through and ask the user to save again once the path exists.
    Dim docPath As String
    On Error Resume Next
    docPath = Doc.Path
    On Error GoTo 0
    If Not SaveAsUI And Len(docPath) > 0 Then
        Cancel = True
        SetStatus "PatentMarker: dictionary export failed; save cancelled. See autoexport-error.txt."
    Else
        SetStatus "PatentMarker: dictionary export failed; save again after choosing a path. See autoexport-error.txt."
    End If
End Sub

Private Sub SetStatus(ByVal message As String)
    On Error Resume Next
    App.StatusBar = message
    On Error GoTo 0
End Sub

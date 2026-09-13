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
    AutoExport.TraceHookLifecycle "hook.class_initialize", "PASS", "application_bound=true"
End Sub

Private Sub Class_Terminate()
    AutoExport.TraceHookLifecycle "hook.class_terminate", "INFO", "application_bound=false"
    Set App = Nothing
End Sub

Private Sub App_DocumentBeforeSave(ByVal Doc As Document, SaveAsUI As Boolean, Cancel As Boolean)
    AutoExport.TraceSaveEvent "enter", Doc, SaveAsUI, Cancel
    If AutoExport.ShouldSkipAutomaticExport(Doc) Then
        AutoExport.TraceSaveEvent "skip", Doc, SaveAsUI, Cancel, "reason=template_document"
        Exit Sub
    End If

    Dim exportOk As Boolean
    exportOk = AutoExport.ExportDict(Doc)
    If exportOk Then
        AutoExport.TraceSaveEvent "exit", Doc, SaveAsUI, Cancel, "export_ok=true"
        Exit Sub
    End If

    ' A Save As establishes the final document path only after this event;
    ' allow it through and ask the user to save again once the path exists.
    Dim docPath As String
    On Error Resume Next
    docPath = Doc.Path
    On Error GoTo 0
    If Not SaveAsUI And Len(docPath) > 0 Then
        Cancel = True
        SetStatus "PatentMarker: dictionary export failed; save cancelled. See diagnostics: " & AutoExport.GetDiagnosticLogPath()
    Else
        SetStatus "PatentMarker: dictionary export failed; save again after choosing a path. Diagnostics: " & AutoExport.GetDiagnosticLogPath()
    End If
    AutoExport.TraceSaveEvent "exit", Doc, SaveAsUI, Cancel, "export_ok=false"
End Sub

Private Sub SetStatus(ByVal message As String)
    On Error Resume Next
    App.StatusBar = message
    On Error GoTo 0
End Sub

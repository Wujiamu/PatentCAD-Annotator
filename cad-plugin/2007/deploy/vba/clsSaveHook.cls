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
    AutoExport.ExportDict Doc
End Sub

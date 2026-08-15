VERSION 5.00
Begin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} PatentDictPanel 
   Caption         =   "UserForm1"
   ClientHeight    =   1560
   ClientLeft      =   120
   ClientTop       =   465
   ClientWidth    =   3240
   OleObjectBlob   =   "PatentDictPanel.frx":0000
   StartUpPosition =   1  '所有者中心
End
Attribute VB_Name = "PatentDictPanel"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Private Sub cmdExport_Click()
    AutoExport.ExportDict
End Sub

Private Sub chkAutoExport_Click()
    AutoExport.IsAutoExportEnabled = (chkAutoExport.Value = 1)
End Sub

Private Sub UserForm_Initialize()
    chkAutoExport.Value = IIf(AutoExport.IsAutoExportEnabled, 1, 0)
End Sub

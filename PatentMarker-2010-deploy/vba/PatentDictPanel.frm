VERSION 5.00
Begin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} PatentDictPanel 
   Caption         =   "专利标注字典工具"
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
    ' MSForms coordinates are in POINTS, but the generated .frx design blob
    ' stored twip-scale numbers (e.g. Width=3000, Top=720), which pushed every
    ' control far outside the visible client area - the panel showed up as an
    ' empty little frame. Set the whole layout explicitly in points instead.
    ' Fix (1.0.0): explicit point-based layout, independent of .frx design data.
    Me.Width = 174
    Me.Height = 107
    With cmdExport
        .Left = 6
        .Top = 8
        .Width = 150
        .Height = 26
    End With
    With chkAutoExport
        .Left = 6
        .Top = 44
        .Width = 150
        .Height = 18
    End With
    chkAutoExport.Value = IIf(AutoExport.IsAutoExportEnabled, 1, 0)
End Sub

VERSION 5.00
Begin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} PatentDictPanel 
   Caption         =   "UserForm1"
   ClientHeight    =   3015
   ClientLeft      =   120
   ClientTop       =   465
   ClientWidth     =   4560
   OleObjectBlob   =   "PatentDictPanel.frx":0000
   StartUpPosition =   1  '所有者中心
End
Attribute VB_Name = "PatentDictPanel"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Private WithEvents m_chkJsonVisible As MSForms.CheckBox
Private m_syncingJsonVisibility As Boolean

Private Sub cmdExport_Click()
    ' 1.0.1：导出后立即在底部状态行反馈结果（此前点击后无任何提示）。
    If AutoExport.ExportDictManual() Then
        lblStatus.Caption = "√ 已导出 " & Format(Now, "hh:nn:ss")
        lblStatus.ForeColor = RGB(0, 128, 0)
        UpdateJsonVisibilityControl
    Else
        lblStatus.Caption = "× 导出失败（请检查目标图纸或文档路径）"
        lblStatus.ForeColor = RGB(200, 0, 0)
    End If
End Sub

Private Sub chkAutoExport_Click()
    AutoExport.IsAutoExportEnabled = (chkAutoExport.Value = 1)
End Sub

Private Sub m_chkJsonVisible_Click()
    If m_syncingJsonVisibility Then Exit Sub
    If AutoExport.SetCurrentDictVisibility(m_chkJsonVisible.Value = 1) Then
        If m_chkJsonVisible.Value = 1 Then
            lblStatus.Caption = "JSON 已显示，可手动编辑。"
        Else
            lblStatus.Caption = "JSON 已隐藏。"
        End If
    Else
        lblStatus.Caption = "JSON 可见性修改失败，请先导出字典。"
    End If
    UpdateJsonVisibilityControl
End Sub

Private Sub UserForm_Initialize()
    ' 1.0.1 界面优化：整体放大窗体与字号、微软雅黑、蓝色主按钮、底部状态行。
    ' .frx 仅保存控件骨架，实际布局一律以磅为单位在此显式设置，
    ' 避免 twip 量级坐标导致控件画出窗体外（"小空框"问题）。
    Me.Caption = "专利标注字典工具"
    Me.Width = 300
    Me.Height = 210
    Me.Font.Name = "微软雅黑"
    Me.Font.Size = 10

    Dim x As Single, iw As Single, ih As Single
    On Error Resume Next
    iw = Me.InsideWidth
    ih = Me.InsideHeight
    On Error GoTo 0
    If iw < 100 Then iw = 292
    If ih < 100 Then ih = 150
    x = 16

    With cmdExport
        .Left = x
        .Top = 14
        .Width = iw - 2 * x
        .Height = 50
        .Caption = "手动导出字典"
        .BackColor = RGB(0, 120, 215)
        .ForeColor = RGB(255, 255, 255)
        .Font.Name = "微软雅黑"
        .Font.Size = 14
        .Font.Bold = True
    End With

    With chkAutoExport
        .Left = x - 4
        .Top = 76
        .Width = iw - 2 * x + 8
        .Height = 28
        .Caption = "保存 Word 时自动导出"
        .Font.Name = "微软雅黑"
        .Font.Size = 11
    End With

    Set m_chkJsonVisible = Me.Controls.Add("Forms.CheckBox.1", "chkJsonVisible", True)
    With m_chkJsonVisible
        .Left = x - 4
        .Top = 108
        .Width = iw - 2 * x + 8
        .Height = 28
        .Caption = "显示 JSON（允许手动编辑）"
        .Font.Name = "微软雅黑"
        .Font.Size = 11
    End With

    ' 状态行：锚定在底部
    With lblStatus
        .Left = x
        .Top = ih - 34
        .Width = iw - 2 * x
        .Height = 20
        .Caption = "导出的字典在文档所在文件夹（隐藏文件）"
        .ForeColor = RGB(120, 120, 120)
        .TextAlign = 2
        .Font.Name = "微软雅黑"
        .Font.Size = 10
    End With

    chkAutoExport.Value = IIf(AutoExport.IsAutoExportEnabled, 1, 0)
    UpdateJsonVisibilityControl
End Sub

Private Sub UpdateJsonVisibilityControl()
    On Error Resume Next
    m_syncingJsonVisibility = True
    Dim path As String
    Dim fso As Object
    path = AutoExport.GetCurrentDictPath()
    Set fso = CreateObject("Scripting.FileSystemObject")
    If path = "" Or Not fso.FileExists(path) Then
        m_chkJsonVisible.Enabled = False
        m_chkJsonVisible.Value = 0
        m_syncingJsonVisibility = False
        Exit Sub
    End If
    m_chkJsonVisible.Enabled = True
    m_chkJsonVisible.Value = IIf(AutoExport.IsCurrentDictVisible(), 1, 0)
    m_syncingJsonVisibility = False
End Sub

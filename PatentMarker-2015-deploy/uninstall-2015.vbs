' PatentMarker 2015 Uninstaller (VBScript)
' Removes registry entries for AutoCAD 2015-2024 (R20.0-R24.x, plus R25.0
' covered by install-2015.vbs detection list)

Option Explicit

Const HKCU = &H80000001
Const HKLM = &H80000002

Dim fso, reg, output
Set fso = CreateObject("Scripting.FileSystemObject")
Set reg = GetObject("winmgmts:{impersonationLevel=impersonate}!\\.\root\default:StdRegProv")
output = ""

' === Internationalization (i18n) ===
Function GetSysLang()
    On Error Resume Next
    Dim r
    Set r = CreateObject("WScript.Shell")
    Dim lid
    lid = r.RegRead("HKLM\SYSTEM\CurrentControlSet\Control\Nls\Language\InstallLanguage")
    If Err.Number <> 0 Then
        lid = r.RegRead("HKLM\SYSTEM\CurrentControlSet\Control\Nls\Language\Default")
    End If
    If Err.Number <> 0 Then lid = "0804"
    On Error GoTo 0
    Select Case lid
        Case "0804", "0404", "0C04", "1404", "7C04"
            GetSysLang = "zh"
        Case Else
            GetSysLang = "en"
    End Select
End Function

Function L(t)
    Dim z
    z = (GetSysLang() = "zh")
    If Not z Then
        L = t
        Exit Function
    End If
    t = Replace(t, "PatentMarker 2015 Uninstaller" & vbCrLf & "========================================", _
                           "PatentMarker 2015 卸载程序" & vbCrLf & "========================================")
    t = Replace(t, "Done. Removed ", "完成。已移除 ")
    t = Replace(t, " registry entries.", " 个注册表条目。")
    t = Replace(t, "Done. No PatentMarker entries found.", "完成。未找到 PatentMarker 条目。")
    t = Replace(t, ">>> Restart AutoCAD to finish uninstall.", ">>> 请重启 AutoCAD 完成卸载。")
    L = t
End Function
' === End i18n ===

output = output & "========================================" & vbCrLf
output = output & "PatentMarker 2015 Uninstaller" & vbCrLf
output = output & "========================================" & vbCrLf

' Same release candidates as install-2015.vbs (AutoCAD 2015-2024 detection).
Dim versionCandidates(9)
versionCandidates(0) = "R24.0"
versionCandidates(1) = "R23.1"
versionCandidates(2) = "R23.0"
versionCandidates(3) = "R22.0"
versionCandidates(4) = "R21.0"
versionCandidates(5) = "R20.1"
versionCandidates(6) = "R20.0"
versionCandidates(7) = "R24.1"
versionCandidates(8) = "R24.2"
versionCandidates(9) = "R25.0"

Dim vc, removed
removed = 0

For vc = 0 To 9
    Dim acadBaseKey, subKeys
    acadBaseKey = "Software\Autodesk\AutoCAD\" & versionCandidates(vc)

    reg.EnumKey HKCU, acadBaseKey, subKeys
    If IsNull(subKeys) Then
        reg.EnumKey HKLM, acadBaseKey, subKeys
    End If
    If Not IsNull(subKeys) Then
        Dim i
        For i = 0 To UBound(subKeys)
            If Left(subKeys(i), 5) = "ACAD-" Then
                Dim appKey
                appKey = acadBaseKey & "\" & subKeys(i) & "\Applications\PatentMarker"
                On Error Resume Next
                reg.DeleteKey HKCU, appKey
                If Err.Number = 0 Then
                    output = output & "  Removed HKCU: " & appKey & vbCrLf
                    removed = removed + 1
                End If
                reg.DeleteKey HKLM, appKey
                If Err.Number = 0 Then
                    output = output & "  Removed HKLM: " & appKey & vbCrLf
                    removed = removed + 1
                End If
                On Error GoTo 0
            End If
        Next
    End If
Next

output = output & vbCrLf
If removed > 0 Then
    output = output & "Done. Removed " & removed & " registry entries." & vbCrLf
    output = output & ">>> Restart AutoCAD to finish uninstall." & vbCrLf
Else
    output = output & "Done. No PatentMarker entries found." & vbCrLf
End If
output = output & "========================================" & vbCrLf

WScript.Echo L(output)

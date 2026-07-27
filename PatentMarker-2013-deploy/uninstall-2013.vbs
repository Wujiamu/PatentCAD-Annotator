' PatentMarker 2013 Uninstaller (VBScript)
' Removes registry entries for AutoCAD 2013/2014

Option Explicit

Const HKCU = &H80000001
Const HKLM = &H80000002

Dim fso, reg, output
Set fso = CreateObject("Scripting.FileSystemObject")
Set reg = GetObject("winmgmts:{impersonationLevel=impersonate}!\\.\root\default:StdRegProv")
output = ""

output = output & "========================================" & vbCrLf
output = output & "PatentMarker 2013 Uninstaller" & vbCrLf
output = output & "========================================" & vbCrLf

Dim versionCandidates(1)
versionCandidates(0) = "R19.0"
versionCandidates(1) = "R19.1"

Dim vc, removed
removed = 0

For vc = 0 To 1
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
Else
    output = output & "Done. No PatentMarker entries found." & vbCrLf
End If
output = output & "========================================" & vbCrLf

WScript.Echo output

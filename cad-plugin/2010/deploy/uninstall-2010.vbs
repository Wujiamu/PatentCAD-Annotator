' PatentMarker 2010 Uninstaller (VBScript)
' Removes registry entries and LSP autoload for AutoCAD 2010/2011/2012

Option Explicit

Const HKCU = &H80000001
Const HKLM = &H80000002

Dim fso, reg, output
Set fso = CreateObject("Scripting.FileSystemObject")
Set reg = GetObject("winmgmts:{impersonationLevel=impersonate}!\\.\root\default:StdRegProv")
output = ""

output = output & "========================================" & vbCrLf
output = output & "PatentMarker 2010 Uninstaller" & vbCrLf
output = output & "========================================" & vbCrLf

Dim versionCandidates(2)
versionCandidates(0) = "R18.0"
versionCandidates(1) = "R18.1"
versionCandidates(2) = "R18.2"

Dim vc, removed
removed = 0

For vc = 0 To 2
    Dim acadBaseKey, subKeys
    acadBaseKey = "Software\Autodesk\AutoCAD\" & versionCandidates(vc)

    reg.EnumKey HKCU, acadBaseKey, subKeys
    If IsNull(subKeys) Then
        reg.EnumKey HKLM, acadBaseKey, subKeys
    End If
    If IsNull(subKeys) Then
        ' not installed for this version
    Else
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

' Remove manual LSP
Dim scriptDir
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
Dim lspPath
lspPath = scriptDir & "\load-patent-marker.lsp"
If fso.FileExists(lspPath) Then
    fso.DeleteFile lspPath
    output = output & "  Removed: " & lspPath & vbCrLf
End If

output = output & vbCrLf
If removed > 0 Then
    output = output & "Done. Removed " & removed & " registry entries." & vbCrLf
Else
    output = output & "Done. No PatentMarker entries found." & vbCrLf
End If
output = output & "========================================" & vbCrLf

WScript.Echo output

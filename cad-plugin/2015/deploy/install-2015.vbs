' PatentMarker 2015 Installer (VBScript)
' Target: AutoCAD 2015-2024 (R20.0-R24.x)
' Strategy: HKCU registry auto-load

Option Explicit

Const HKCU = &H80000001
Const HKLM = &H80000002

Dim fso, shell, reg
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
Set reg = GetObject("winmgmts:{impersonationLevel=impersonate}!\\.\root\default:StdRegProv")

Dim scriptDir, output
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
output = ""

output = output & "========================================" & vbCrLf
output = output & "PatentMarker 2015 Installer" & vbCrLf
output = output & "(AutoCAD 2015-2024)" & vbCrLf
output = output & "========================================" & vbCrLf

' --- 1. Locate DLL ---
Dim dllPath
dllPath = scriptDir & "\PatentMarker.dll"
If Not fso.FileExists(dllPath) Then
    output = output & "ERROR: PatentMarker.dll not found in " & scriptDir & vbCrLf
    WScript.Echo output
    WScript.Quit(1)
End If
output = output & "DLL: " & dllPath & vbCrLf

Dim jsonDll
jsonDll = scriptDir & "\Newtonsoft.Json.dll"
If Not fso.FileExists(jsonDll) Then
    output = output & "WARNING: Newtonsoft.Json.dll not found." & vbCrLf
End If

' --- 2. Scan registry for ACAD 2015-2024 (R20.x - R24.x) ---
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

Dim vc, subKeys, acadBaseKey, foundVersion
acadBaseKey = ""
foundVersion = ""
For vc = 0 To 9
    Dim tryKey
    tryKey = "Software\Autodesk\AutoCAD\" & versionCandidates(vc)
    reg.EnumKey HKCU, tryKey, subKeys
    If Not IsNull(subKeys) Then
        acadBaseKey = tryKey
        foundVersion = versionCandidates(vc)
        Exit For
    End If
    reg.EnumKey HKLM, tryKey, subKeys
    If Not IsNull(subKeys) Then
        acadBaseKey = tryKey
        foundVersion = versionCandidates(vc)
        Exit For
    End If
Next

If acadBaseKey = "" Then
    output = output & "ERROR: AutoCAD 2015-2024 not found in registry" & vbCrLf
    WScript.Echo output
    WScript.Quit(1)
End If
output = output & "Found: " & foundVersion & vbCrLf

' --- 3. Write HKCU registry ---
Dim i, installed
installed = 0

For i = 0 To UBound(subKeys)
    If Left(subKeys(i), 5) = "ACAD-" Then
        Dim appKey
        appKey = acadBaseKey & "\" & subKeys(i) & "\Applications\PatentMarker"
        reg.CreateKey HKCU, appKey
        reg.SetStringValue HKCU, appKey, "DESCRIPTION", "PatentMarker - Patent Drawing Annotation Plugin"
        reg.SetDWORDValue HKCU, appKey, "LOADCTRLS", 14
        reg.SetDWORDValue HKCU, appKey, "MANAGED", 1
        reg.SetStringValue HKCU, appKey, "LOADER", dllPath

        Dim verifyVal
        verifyVal = ""
        reg.GetStringValue HKCU, appKey, "LOADER", verifyVal
        If verifyVal = dllPath Then
            output = output & "  " & subKeys(i) & ": Registry OK" & vbCrLf
            installed = installed + 1
        End If
    End If
Next

' --- 4. Summary ---
output = output & vbCrLf
output = output & "=== Summary ===" & vbCrLf
output = output & "Registry entries: " & installed & vbCrLf
output = output & vbCrLf
If installed > 0 Then
    output = output & ">>> Restart AutoCAD." & vbCrLf
    output = output & ">>> PatentMarker will auto-load." & vbCrLf
    output = output & ">>> Type BZ to open the palette." & vbCrLf
Else
    output = output & ">>> Registry failed. Use NETLOAD manually:" & vbCrLf
    output = output & ">>> " & dllPath & vbCrLf
End If
output = output & vbCrLf
output = output & "Commands: BZ BZM BZC BZA BZS" & vbCrLf
output = output & "========================================" & vbCrLf

WScript.Echo output

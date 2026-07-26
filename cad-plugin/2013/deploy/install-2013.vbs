' PatentMarker 2013 Installer (VBScript)
' Target: AutoCAD 2013/2014 (R19.0-R19.1)
' Strategy: HKCU registry + ApplicationPlugins bundle

Option Explicit

Const HKCU = &H80000001
Const HKLM = &H80000002
Const ForWriting = 2
Const ForAppending = 8

Dim fso, shell, reg
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
Set reg = GetObject("winmgmts:{impersonationLevel=impersonate}!\\.\root\default:StdRegProv")

Dim scriptDir, output
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
output = ""

output = output & "========================================" & vbCrLf
output = output & "PatentMarker 2013 Installer" & vbCrLf
output = output & "(AutoCAD 2013/2014)" & vbCrLf
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

' Also check Newtonsoft.Json.dll
Dim jsonDll
jsonDll = scriptDir & "\Newtonsoft.Json.dll"
If Not fso.FileExists(jsonDll) Then
    output = output & "WARNING: Newtonsoft.Json.dll not found. Copy it from packages folder." & vbCrLf
End If

' --- 2. Scan registry for ACAD 2013/2014 ---
Dim versionCandidates(1)
versionCandidates(0) = "R19.0"
versionCandidates(1) = "R19.1"

Dim vc, subKeys, acadBaseKey
acadBaseKey = ""
For vc = 0 To 1
    Dim tryKey
    tryKey = "Software\Autodesk\AutoCAD\" & versionCandidates(vc)
    reg.EnumKey HKCU, tryKey, subKeys
    If Not IsNull(subKeys) Then
        acadBaseKey = tryKey
        Exit For
    End If
    reg.EnumKey HKLM, tryKey, subKeys
    If Not IsNull(subKeys) Then
        acadBaseKey = tryKey
        Exit For
    End If
Next

If acadBaseKey = "" Then
    output = output & "ERROR: AutoCAD 2013/2014 (R19.x) not found in registry" & vbCrLf
    WScript.Echo output
    WScript.Quit(1)
End If
output = output & "Found: " & acadBaseKey & vbCrLf

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
    output = output & ">>> Restart AutoCAD 2013/2014." & vbCrLf
    output = output & ">>> PatentMarker will auto-load." & vbCrLf
    output = output & ">>> Type BZ to open the palette." & vbCrLf
Else
    output = output & ">>> Registry failed. Use NETLOAD manually:" & vbCrLf
    output = output & ">>> " & dllPath & vbCrLf
End If
output = output & vbCrLf
output = output & "Commands:" & vbCrLf
output = output & "  BZ   (PATPALETTE)    Palette" & vbCrLf
output = output & "  BZM  (PATMARK)       Annotate" & vbCrLf
output = output & "  BZC  (PATCHECK)      Check" & vbCrLf
output = output & "  BZA  (PATALIGN)      Align" & vbCrLf
output = output & "  BZS  (PATSELECTALL)  Select All" & vbCrLf
output = output & "========================================" & vbCrLf

WScript.Echo output

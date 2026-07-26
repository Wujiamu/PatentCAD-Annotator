' PatentMarker 2010 Installer (VBScript)
' Encoding: ASCII (compatible with all Windows locales)
'
' Three-layer auto-load strategy:
'   Layer 1: HKCU Applications registry key (LOADCTRLS=14)
'   Layer 2: acad.lsp deployed to ACAD support path
'   Layer 3: Manual LSP file for APPLOAD fallback
'
' Target: AutoCAD 2010/2011/2012 (R18.0-R18.2)

Option Explicit

Const HKCU = &H80000001
Const HKLM = &H80000002
Const ForAppending = 8
Const ForWriting = 2
Const ForReading = 1

Dim fso, shell, reg
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
Set reg = GetObject("winmgmts:{impersonationLevel=impersonate}!\\.\root\default:StdRegProv")

Dim scriptDir, logPath, logFile, output
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
logPath = scriptDir & "\install-2010.log"
Set logFile = fso.OpenTextFile(logPath, ForAppending, True)
output = ""

Sub LogMsg(msg)
    logFile.WriteLine "[" & Now & "] " & msg
    output = output & msg & vbCrLf
End Sub

Sub QuitWithMsg(msg)
    LogMsg msg
    WScript.Echo output
    logFile.Close
    WScript.Quit(1)
End Sub

Function IsAdmin()
    On Error Resume Next
    Dim testKey
    testKey = "SOFTWARE\PMTest" & Int(Timer * 1000)
    reg.CreateKey HKLM, testKey
    If Err.Number = 0 Then
        reg.DeleteKey HKLM, testKey
        IsAdmin = True
    Else
        IsAdmin = False
    End If
    On Error GoTo 0
End Function

Function IIf(cond, trueVal, falseVal)
    If cond Then
        IIf = trueVal
    Else
        IIf = falseVal
    End If
End Function

Function IsDirWritable(dPath)
    On Error Resume Next
    Dim tf
    Set tf = fso.CreateTextFile(dPath & "\~pmtest.tmp", True)
    If Err.Number = 0 Then
        tf.Close
        fso.DeleteFile dPath & "\~pmtest.tmp"
        IsDirWritable = True
    Else
        IsDirWritable = False
    End If
    On Error GoTo 0
End Function

output = output & "========================================" & vbCrLf
output = output & "PatentMarker 2010 Installer" & vbCrLf
output = output & "(AutoCAD 2010/2011/2012)" & vbCrLf
output = output & "========================================" & vbCrLf

' --- 0. System info ---
LogMsg "--- System Info ---"
LogMsg "  OS: " & shell.ExpandEnvironmentStrings("%OS%")
LogMsg "  User: " & shell.ExpandEnvironmentStrings("%USERNAME%")
LogMsg "  Computer: " & shell.ExpandEnvironmentStrings("%COMPUTERNAME%")
LogMsg "  Script Dir: " & scriptDir

' --- 1. Locate DLL ---
Dim dllPath
dllPath = scriptDir & "\PatentMarker.dll"
If Not fso.FileExists(dllPath) Then
    QuitWithMsg "ERROR: PatentMarker.dll not found" & vbCrLf & "Path: " & scriptDir
End If
LogMsg "DLL: " & dllPath & " (" & fso.GetFile(dllPath).Size & " bytes)"

' --- 2. Privilege check ---
Dim adminOk
adminOk = IsAdmin()
LogMsg "Privilege: " & IIf(adminOk, "Admin", "Non-admin (HKLM skipped)")

' --- 3. Scan registry for ACAD 2010/2011/2012 (R18.x) ---
Dim acadBaseKey, acadVersionLabel
acadVersionLabel = ""

' Try R18.0 (2010), R18.1 (2011), R18.2 (2012)
Dim versionCandidates(2)
versionCandidates(0) = "R18.0"
versionCandidates(1) = "R18.1"
versionCandidates(2) = "R18.2"

Dim vc, subKeys
acadBaseKey = ""
For vc = 0 To 2
    Dim tryKey
    tryKey = "Software\Autodesk\AutoCAD\" & versionCandidates(vc)
    reg.EnumKey HKCU, tryKey, subKeys
    If Not IsNull(subKeys) Then
        acadBaseKey = tryKey
        acadVersionLabel = versionCandidates(vc)
        Exit For
    End If
    reg.EnumKey HKLM, tryKey, subKeys
    If Not IsNull(subKeys) Then
        acadBaseKey = tryKey
        acadVersionLabel = versionCandidates(vc)
        Exit For
    End If
Next

If acadBaseKey = "" Then
    QuitWithMsg "ERROR: AutoCAD 2010/2011/2012 (R18.x) not found in registry"
End If
LogMsg "Found: " & acadVersionLabel

Dim productCodes()
ReDim productCodes(0)
Dim productCount
productCount = 0

Dim i, key
For i = 0 To UBound(subKeys)
    key = subKeys(i)
    If Left(key, 5) = "ACAD-" Then
        ReDim Preserve productCodes(productCount)
        productCodes(productCount) = key
        productCount = productCount + 1
    End If
Next

If productCount = 0 Then
    QuitWithMsg "ERROR: No ACAD- product code found"
End If
LogMsg "Products found: " & productCount

' --- 4. Read ACAD paths ---
Dim acadPaths()
ReDim acadPaths(productCount - 1)

For i = 0 To productCount - 1
    Dim prodKey, acadLocation
    prodKey = acadBaseKey & "\" & productCodes(i)
    acadLocation = ""
    reg.GetStringValue HKCU, prodKey, "AcadLocation", acadLocation
    If IsNull(acadLocation) Or acadLocation = "" Then
        reg.GetStringValue HKLM, prodKey, "AcadLocation", acadLocation
    End If
    If IsNull(acadLocation) Then acadLocation = ""
    acadPaths(i) = acadLocation
    LogMsg "  " & productCodes(i) & " -> " & acadLocation
Next

' --- 5. Write HKCU registry (Layer 1) ---
LogMsg ""
LogMsg "--- Layer 1: Write HKCU Registry ---"

Dim j, appKey, installed
installed = 0

For j = 0 To productCount - 1
    appKey = acadBaseKey & "\" & productCodes(j) & "\Applications\PatentMarker"
    reg.CreateKey HKCU, appKey

    Dim verifyVal, verifyDword
    reg.SetStringValue HKCU, appKey, "DESCRIPTION", "PatentMarker - Patent Drawing Annotation Plugin"
    reg.SetDWORDValue HKCU, appKey, "LOADCTRLS", 14
    reg.SetDWORDValue HKCU, appKey, "MANAGED", 1
    reg.SetStringValue HKCU, appKey, "LOADER", dllPath

    verifyVal = ""
    reg.GetStringValue HKCU, appKey, "LOADER", verifyVal
    verifyDword = -1
    reg.GetDWORDValue HKCU, appKey, "LOADCTRLS", verifyDword

    If verifyVal = dllPath And verifyDword = 14 Then
        LogMsg "  " & productCodes(j) & ": OK"
        installed = installed + 1
    Else
        LogMsg "  " & productCodes(j) & ": FAILED"
    End If
Next

' --- 6. Try HKLM (admin only) ---
Dim hklmOk
hklmOk = False

If adminOk Then
    LogMsg ""
    LogMsg "--- Write HKLM ---"
    For j = 0 To productCount - 1
        appKey = acadBaseKey & "\" & productCodes(j) & "\Applications\PatentMarker"
        On Error Resume Next
        reg.CreateKey HKLM, appKey
        If Err.Number = 0 Then
            reg.SetStringValue HKLM, appKey, "DESCRIPTION", "PatentMarker - Patent Drawing Annotation Plugin"
            reg.SetDWORDValue HKLM, appKey, "LOADCTRLS", 14
            reg.SetDWORDValue HKLM, appKey, "MANAGED", 1
            reg.SetStringValue HKLM, appKey, "LOADER", dllPath
            verifyVal = ""
            reg.GetStringValue HKLM, appKey, "LOADER", verifyVal
            If verifyVal = dllPath Then
                LogMsg "  " & productCodes(j) & ": HKLM OK"
                hklmOk = True
            End If
        Else
            LogMsg "  " & productCodes(j) & ": HKLM failed - " & Err.Description
            On Error GoTo 0
            Exit For
        End If
        On Error GoTo 0
    Next
Else
    LogMsg ""
    LogMsg "--- HKLM skipped (non-admin) ---"
End If

' --- 7. Deploy LSP (Layer 2) ---
LogMsg ""
LogMsg "--- Layer 2: Deploy LSP ---"

Dim lspDeployed
lspDeployed = False

Dim lspDllPath
lspDllPath = Replace(dllPath, "\", "/")
Dim lspLoadCmd
lspLoadCmd = "(command ""NETLOAD"" """ & lspDllPath & """)"
Dim lspPrinc
lspPrinc = "(princ ""\nPatentMarker loaded. Type BZ for palette.\n"")(princ)"

For j = 0 To productCount - 1
    If lspDeployed Then Exit For

    Dim productCode
    productCode = productCodes(j)

    ' Try AcadLocation\Support
    If acadPaths(j) <> "" Then
        Dim acadSupport
        acadSupport = acadPaths(j) & "\Support"
        If fso.FolderExists(acadSupport) And IsDirWritable(acadSupport) Then
            Dim newLsp
            newLsp = acadSupport & "\acad.lsp"
            If fso.FileExists(newLsp) Then
                Dim lf, lspContent
                Set lf = fso.OpenTextFile(newLsp, ForReading, False)
                lspContent = lf.ReadAll
                lf.Close
                If InStr(lspContent, "PatentMarker") = 0 Then
                    lspContent = lspContent & vbCrLf & "; --- PatentMarker autoload ---" & vbCrLf & lspLoadCmd & vbCrLf & lspPrinc & vbCrLf
                    Set lf = fso.OpenTextFile(newLsp, ForWriting, False)
                    lf.Write lspContent
                    lf.Close
                    LogMsg "  Appended to: " & newLsp
                Else
                    LogMsg "  Already in: " & newLsp
                End If
            Else
                Dim nf
                Set nf = fso.CreateTextFile(newLsp, True)
                nf.Write "; --- PatentMarker autoload ---" & vbCrLf & lspLoadCmd & vbCrLf & lspPrinc & vbCrLf
                nf.Close
                LogMsg "  Created: " & newLsp
            End If
            lspDeployed = True
        End If
    End If
Next

' --- 8. Generate manual LSP fallback (Layer 3) ---
LogMsg ""
LogMsg "--- Layer 3: Manual LSP Fallback ---"
Dim manualLspPath
manualLspPath = scriptDir & "\load-patent-marker.lsp"
Dim mlf
Set mlf = fso.OpenTextFile(manualLspPath, ForWriting, True)
mlf.Write "; PatentMarker manual load script" & vbCrLf & _
    "; Usage: APPLOAD this file in AutoCAD" & vbCrLf & _
    lspLoadCmd & vbCrLf & _
    lspPrinc & vbCrLf
mlf.Close
LogMsg "Manual LSP: " & manualLspPath

' --- 9. Summary ---
LogMsg ""
LogMsg "=== Summary ==="
LogMsg "DLL: " & dllPath
LogMsg "Layer 1 HKCU: " & installed & "/" & productCount & IIf(installed > 0, " OK", " FAILED")
LogMsg "Layer 1 HKLM: " & IIf(Not adminOk, "Skipped", IIf(hklmOk, "OK", "Failed"))
LogMsg "Layer 2 LSP:  " & IIf(lspDeployed, "OK", "Failed")
LogMsg "Layer 3 LSP:  " & manualLspPath

LogMsg ""
If lspDeployed Or installed > 0 Then
    LogMsg ">>> Restart AutoCAD 2010/2011/2012."
    LogMsg ">>> PatentMarker will auto-load."
    LogMsg ">>> Type BZ to open the palette."
Else
    LogMsg ">>> Auto-deploy failed. Manual steps:"
    LogMsg "  A: APPLOAD -> " & manualLspPath
    LogMsg "  B: NETLOAD -> " & dllPath
End If

LogMsg ""
LogMsg "Commands:"
LogMsg "  BZ   (PATPALETTE)    Palette"
LogMsg "  BZM  (PATMARK)       Annotate"
LogMsg "  BZC  (PATCHECK)      Check"
LogMsg "  BZA  (PATALIGN)      Align"
LogMsg "  BZS  (PATSELECTALL)  Select All"

LogMsg ""
LogMsg "========================================"
LogMsg "Done"
LogMsg "========================================"

WScript.Echo output
logFile.Close

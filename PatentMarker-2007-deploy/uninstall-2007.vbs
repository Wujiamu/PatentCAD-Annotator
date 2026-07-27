' PatentMarker 2007 Uninstaller (VBScript)
' Encoding: ASCII
'
' Cleans up:
'   - HKCU/HKLM Applications registry keys
'   - acad.lsp PatentMarker block in ACAD support paths
'   - acad.lsp in install dir
'   - load-patent-marker.lsp
'   - Install dir from ACAD support path

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
logPath = scriptDir & "\uninstall-2007.log"
Set logFile = fso.OpenTextFile(logPath, ForAppending, True)
output = ""

Sub LogMsg(msg)
    logFile.WriteLine "[" & Now & "] " & msg
    output = output & msg & vbCrLf
End Sub

output = output & "========================================" & vbCrLf
output = output & "PatentMarker 2007 Uninstaller" & vbCrLf
output = output & "========================================" & vbCrLf

Dim acadBaseKey
acadBaseKey = "Software\Autodesk\AutoCAD\R17.0"

' --- 1. Scan for ACAD products ---
Dim subKeys
reg.EnumKey HKCU, acadBaseKey, subKeys

If IsNull(subKeys) Then
    output = output & "AutoCAD R17.0 not found, nothing to uninstall." & vbCrLf
    WScript.Echo output
    logFile.Close
    WScript.Quit(0)
End If

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
    output = output & "No ACAD- product code found." & vbCrLf
    WScript.Echo output
    logFile.Close
    WScript.Quit(0)
End If

' --- 2. Delete HKCU registry keys ---
LogMsg "--- Delete HKCU Registry ---"
Dim j, appKey, deleted
deleted = 0

For j = 0 To productCount - 1
    appKey = acadBaseKey & "\" & productCodes(j) & "\Applications\PatentMarker"
    Dim dummy
    reg.GetStringValue HKCU, appKey, "LOADER", dummy
    If Not IsNull(dummy) Then
        reg.DeleteKey HKCU, appKey
        LogMsg "  Deleted: HKCU\" & appKey
        deleted = deleted + 1
    End If
Next

' --- 3. Try delete HKLM registry keys ---
LogMsg ""
LogMsg "--- Delete HKLM Registry ---"
Dim hklmDeleted
hklmDeleted = 0

For j = 0 To productCount - 1
    appKey = acadBaseKey & "\" & productCodes(j) & "\Applications\PatentMarker"
    On Error Resume Next
    reg.DeleteKey HKLM, appKey
    If Err.Number = 0 Then
        LogMsg "  Deleted: HKLM\" & appKey
        hklmDeleted = hklmDeleted + 1
    End If
    On Error GoTo 0
Next

' --- 4. Clean acad.lsp ---
LogMsg ""
LogMsg "--- Clean acad.lsp ---"

Dim lspCleaned
lspCleaned = 0

For j = 0 To productCount - 1
    Dim productCode
    productCode = productCodes(j)

    ' Collect candidate directories (same logic as installer)
    Dim candidateDirs()
    ReDim candidateDirs(0)
    Dim candCount
    candCount = 0

    ' Source A: ACAD support path
    Dim supportKey, supportPath
    supportKey = acadBaseKey & "\" & productCode & "\Fixed Profile\General\ACAD"
    supportPath = ""
    reg.GetStringValue HKCU, supportKey, "ACAD", supportPath

    If Not IsNull(supportPath) And supportPath <> "" Then
        Dim arrPaths, p, dirPath
        arrPaths = Split(supportPath, ";")
        For p = 0 To UBound(arrPaths)
            dirPath = Trim(shell.ExpandEnvironmentStrings(arrPaths(p)))
            If dirPath <> "" And fso.FolderExists(dirPath) Then
                ReDim Preserve candidateDirs(candCount)
                candidateDirs(candCount) = dirPath
                candCount = candCount + 1
            End If
        Next
    End If

    ' Source B: AcadLocation\Support
    Dim prodKey2, acadLocation
    prodKey2 = acadBaseKey & "\" & productCode
    acadLocation = ""
    reg.GetStringValue HKCU, prodKey2, "AcadLocation", acadLocation
    If IsNull(acadLocation) Or acadLocation = "" Then
        reg.GetStringValue HKLM, prodKey2, "AcadLocation", acadLocation
    End If
    If Not IsNull(acadLocation) And acadLocation <> "" Then
        If fso.FolderExists(acadLocation & "\Support") Then
            ReDim Preserve candidateDirs(candCount)
            candidateDirs(candCount) = acadLocation & "\Support"
            candCount = candCount + 1
        End If
    End If

    ' Source C: AppData support
    Dim langCode, langId
    langCode = "enu"
    If InStr(productCode, ":") > 0 Then
        langId = Mid(productCode, InStr(productCode, ":") + 1)
        Select Case langId
            Case "804"
                langCode = "chs"
            Case "404"
                langCode = "cht"
            Case "409"
                langCode = "enu"
            Case Else
                langCode = "enu"
        End Select
    End If

    Dim appDataSupport
    appDataSupport = shell.ExpandEnvironmentStrings("%APPDATA%") & "\Autodesk\AutoCAD\R17.0\" & productCode & "\" & langCode & "\Support"
    If fso.FolderExists(appDataSupport) Then
        ReDim Preserve candidateDirs(candCount)
        candidateDirs(candCount) = appDataSupport
        candCount = candCount + 1
    End If

    ' Source D: Install dir
    ReDim Preserve candidateDirs(candCount)
    candidateDirs(candCount) = scriptDir
    candCount = candCount + 1

    ' Clean acad.lsp in each directory
    For p = 0 To candCount - 1
        dirPath = candidateDirs(p)
        Dim lspPath
        lspPath = dirPath & "\acad.lsp"
        If fso.FileExists(lspPath) Then
            Dim lspContent, lf
            Set lf = fso.OpenTextFile(lspPath, ForReading, False)
            lspContent = lf.ReadAll
            lf.Close

            If InStr(lspContent, "PatentMarker") > 0 Then
                Dim pmStart
                pmStart = InStr(lspContent, "; --- PatentMarker autoload ---")
                If pmStart > 0 Then
                    Dim before
                    before = Left(lspContent, pmStart - 1)

                    ' Trim trailing newlines
                    Do While Right(before, 2) = vbCrLf
                        before = Left(before, Len(before) - 2)
                    Loop

                    If Trim(before) = "" Then
                        fso.DeleteFile lspPath
                        LogMsg "  Deleted: " & lspPath
                    Else
                        Set lf = fso.OpenTextFile(lspPath, ForWriting, False)
                        lf.Write before & vbCrLf
                        lf.Close
                        LogMsg "  Cleaned: " & lspPath
                    End If
                    lspCleaned = lspCleaned + 1
                End If
            End If
        End If
    Next

    ' Remove install dir from support path
    If Not IsNull(supportPath) And supportPath <> "" Then
        If InStr(LCase(supportPath), LCase(scriptDir)) > 0 Then
            Dim newPath
            newPath = supportPath
            newPath = Replace(newPath, scriptDir & ";", "")
            newPath = Replace(newPath, ";" & scriptDir, "")
            If LCase(Trim(newPath)) = LCase(scriptDir) Then newPath = ""
            reg.SetStringValue HKCU, supportKey, "ACAD", newPath
            LogMsg "  Removed install dir from support path"
        End If
    End If
Next

' --- 5. Delete manual LSP ---
LogMsg ""
LogMsg "--- Delete Manual LSP ---"
Dim manualLsp
manualLsp = scriptDir & "\load-patent-marker.lsp"
If fso.FileExists(manualLsp) Then
    fso.DeleteFile manualLsp
    LogMsg "  Deleted: " & manualLsp
End If

' --- 6. Summary ---
LogMsg ""
LogMsg "=== Summary ==="
LogMsg "HKCU keys deleted: " & deleted
LogMsg "HKLM keys deleted: " & hklmDeleted
LogMsg "LSP files cleaned: " & lspCleaned
LogMsg "DLL NOT deleted (remove manually if needed)"

LogMsg ""
LogMsg "========================================"
LogMsg "Uninstall Complete"
LogMsg "========================================"

WScript.Echo output
logFile.Close

' PatentMarker 2007 §Ø???? (VBScript)
' ????: GBK (Win7 ??????????)

Option Explicit

Const HKCU = &H80000001
Const ForAppending = 8
Const CreateFlag = True

Dim fso, reg
Set fso = CreateObject("Scripting.FileSystemObject")
Set reg = GetObject("winmgmts:{impersonationLevel=impersonate}!\\.\root\default:StdRegProv")

Dim scriptDir, logPath, logFile
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
logPath = scriptDir & "\uninstall-2007.log"
Set logFile = fso.OpenTextFile(logPath, ForAppending, CreateFlag)

Dim output
output = ""

Sub LogMsg(msg)
    Dim ts
    ts = Now
    logFile.WriteLine "[" & ts & "] " & msg
    output = output & msg & vbCrLf
End Sub

output = output & "========================================" & vbCrLf
output = output & "PatentMarker 2007 §Ø?????" & vbCrLf
output = output & "========================================" & vbCrLf

Dim acadBaseKey
acadBaseKey = "Software\Autodesk\AutoCAD\R17.0"

Dim subKeys
reg.EnumKey HKCU, acadBaseKey, subKeys

If IsNull(subKeys) Then
    output = output & "??????¦Ä??? AutoCAD R17.0, ????§Ø??" & vbCrLf
    WScript.Echo output
    logFile.Close
    WScript.Quit(0)
End If

Dim i, key, appKey, deleted
deleted = 0

For i = 0 To UBound(subKeys)
    key = subKeys(i)
    If Left(key, 5) = "ACAD-" Then
        appKey = acadBaseKey & "\" & key & "\Applications\PatentMarker"

        Dim dummy
        reg.GetStringValue HKCU, appKey, "LOADER", dummy
        If Not IsNull(dummy) Then
            reg.DeleteKey HKCU, appKey
            LogMsg "?????: HKCU\" & appKey
            deleted = deleted + 1
        End If
    End If
Next

If deleted = 0 Then
    LogMsg "¦Ä??? PatentMarker ??????, ????§Ø??"
Else
    LogMsg ""
    LogMsg "=== §Ø????? ==="
    LogMsg "????? " & deleted & " ????????"
    LogMsg "DLL ???¦Ä???, ????????????????"
End If

LogMsg ""
LogMsg "========================================"
LogMsg "§Ø?????"
LogMsg "========================================"

WScript.Echo output
logFile.Close

' PatentMarker 2010 Uninstaller (VBScript)
' Removes registry entries and LSP autoload for AutoCAD 2010/2011/2012

Option Explicit

Const HKCU = &H80000001
Const HKLM = &H80000002
Const ForWriting = 2
Const ForReading = 1

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
    t = Replace(t, "PatentMarker 2010 Uninstaller" & vbCrLf & "========================================", _
                           "PatentMarker 2010 卸载程序" & vbCrLf & "========================================")
    t = Replace(t, "Done. Removed ", "完成。已移除 ")
    t = Replace(t, " registry entries.", " 个注册表条目。")
    t = Replace(t, "Done. No PatentMarker entries found.", "完成。未找到 PatentMarker 条目。")
    L = t
End Function
' === End i18n ===

output = output & "========================================" & vbCrLf
output = output & "PatentMarker 2010 Uninstaller" & vbCrLf
output = output & "========================================" & vbCrLf

Dim versionCandidates(2)
versionCandidates(0) = "R18.0"
versionCandidates(1) = "R18.1"
versionCandidates(2) = "R18.2"

Dim vc, removed, hive, hiveName, profileId, seenProfiles
removed = 0
Set seenProfiles = CreateObject("Scripting.Dictionary")
seenProfiles.CompareMode = 1

For vc = 0 To 2
    Dim acadBaseKey, subKeys
    acadBaseKey = "Software\Autodesk\AutoCAD\" & versionCandidates(vc)

    For Each hive In Array(HKCU, HKLM)
        subKeys = Null
        reg.EnumKey hive, acadBaseKey, subKeys
        If Not IsNull(subKeys) Then
            If hive = HKCU Then
                hiveName = "HKCU"
            Else
                hiveName = "HKLM"
            End If
            output = output & "Found: " & versionCandidates(vc) & " (" & hiveName & ")" & vbCrLf
            Dim i
            For i = 0 To UBound(subKeys)
                If Left(subKeys(i), 5) = "ACAD-" Then
                    profileId = LCase(acadBaseKey & "\" & subKeys(i))
                    If Not seenProfiles.Exists(profileId) Then
                        seenProfiles.Add profileId, True
                        Dim appKey
                        appKey = acadBaseKey & "\" & subKeys(i) & "\Applications\PatentMarker"
                        On Error Resume Next
                        Err.Clear
                        reg.DeleteKey HKCU, appKey
                        If Err.Number = 0 Then
                            output = output & "  Removed HKCU: " & appKey & vbCrLf
                            removed = removed + 1
                        End If
                        Err.Clear
                        reg.DeleteKey HKLM, appKey
                        If Err.Number = 0 Then
                            output = output & "  Removed HKLM: " & appKey & vbCrLf
                            removed = removed + 1
                        End If
                        On Error GoTo 0
                    End If
                End If
            Next
        End If
    Next
Next

' Remove generated acad.lsp blocks from every detected profile
Dim scriptDir
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
Dim lspCleaned, lspVc, lspHive, lspBaseKey, lspSubKeys, lspProfile, lspLocation, lspPath
lspCleaned = 0

For lspVc = 0 To 2
    lspBaseKey = "Software\Autodesk\AutoCAD\" & versionCandidates(lspVc)
    For Each lspHive In Array(HKCU, HKLM)
        lspSubKeys = Null
        reg.EnumKey lspHive, lspBaseKey, lspSubKeys
        If Not IsNull(lspSubKeys) Then
            For i = 0 To UBound(lspSubKeys)
                lspProfile = lspSubKeys(i)
                If Left(lspProfile, 5) = "ACAD-" Then
                    lspLocation = ""
                    reg.GetStringValue HKCU, lspBaseKey & "\" & lspProfile, "AcadLocation", lspLocation
                    If IsNull(lspLocation) Or lspLocation = "" Then
                        reg.GetStringValue HKLM, lspBaseKey & "\" & lspProfile, "AcadLocation", lspLocation
                    End If
                    If Not IsNull(lspLocation) And lspLocation <> "" Then
                        lspPath = lspLocation & "\Support\acad.lsp"
                        If fso.FileExists(lspPath) Then
                            Dim lspContent, lf, pmStart, before
                            Set lf = fso.OpenTextFile(lspPath, ForReading, False)
                            lspContent = lf.ReadAll
                            lf.Close
                            pmStart = InStr(lspContent, "; --- PatentMarker autoload ---")
                            If pmStart > 0 Then
                                before = Left(lspContent, pmStart - 1)
                                Do While Right(before, 2) = vbCrLf
                                    before = Left(before, Len(before) - 2)
                                Loop
                                If Trim(before) = "" Then
                                    fso.DeleteFile lspPath
                                    output = output & "  Removed: " & lspPath & vbCrLf
                                Else
                                    Set lf = fso.OpenTextFile(lspPath, ForWriting, False)
                                    lf.Write before & vbCrLf
                                    lf.Close
                                    output = output & "  Cleaned: " & lspPath & vbCrLf
                                End If
                                lspCleaned = lspCleaned + 1
                            End If
                        End If
                    End If
                End If
            Next
        End If
    Next
Next

' Remove manual LSP
Dim lspPathManual
lspPathManual = scriptDir & "\load-patent-marker.lsp"
If fso.FileExists(lspPathManual) Then
    fso.DeleteFile lspPathManual
    output = output & "  Removed: " & lspPathManual & vbCrLf
End If

output = output & vbCrLf
output = output & "LSP files cleaned: " & lspCleaned & vbCrLf
If removed > 0 Then
    output = output & "Done. Removed " & removed & " registry entries." & vbCrLf
Else
    output = output & "Done. No PatentMarker entries found." & vbCrLf
End If
output = output & "========================================" & vbCrLf

WScript.Echo L(output)

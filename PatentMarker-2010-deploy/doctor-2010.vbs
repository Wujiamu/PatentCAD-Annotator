' PatentMarker Doctor - offline + online escalation (VBScript)
' The four deploy copies (doctor-2007/2010/2013/2015.vbs) are byte-identical;
' the edition is auto-detected from this file's name.
'
' Tier 1 (always runs, no AutoCAD needed): deployment DLL presence,
'   demand-load registry entries and their LOADER targets, required .NET
'   runtime, PatentMarker.log tail. Catches the "DLL never loaded" class
'   that the in-CAD PATDOCTOR command cannot see.
' Tier 2 (optional): when an AutoCAD inside this edition's supported range
'   is installed, start it in /b batch mode, NETLOAD the deployment DLL and
'   run PATDOCTOR, so an in-CAD report is produced even when demand-load
'   registration is broken and the BZD command never got registered.
'
' Usage:  cscript doctor-<year>.vbs [offline]     ('offline' skips tier 2)
' Output: PatentMarker-doctor-offline-report.txt next to this script
'         PatentMarker-doctor-report.txt          (written by PATDOCTOR)

Option Explicit

Const HKCU = &H80000001
Const HKLM = &H80000002
Const ONLINE_TIMEOUT_SEC = 300

Dim fso, shell, reg
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
Set reg = GetObject("winmgmts:{impersonationLevel=impersonate}!\\.\root\default:StdRegProv")

Dim scriptDir, scriptName, ver
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
scriptName = WScript.ScriptName

Dim reName
Set reName = CreateObject("VBScript.RegExp")
reName.Pattern = "doctor-(\d{4})"
reName.IgnoreCase = True
If reName.Test(scriptName) Then
    ver = reName.Execute(scriptName)(0).SubMatches(0)
Else
    ver = ""
End If

' Note: this script is intentionally pure ASCII (same convention as the
' install-*.vbs scripts) so no codepage can corrupt it. Console and report
' output are English-only.

' Edition tables: scanList mirrors the installer's registry candidates;
' onlineList is the AutoCAD range this DLL can actually load into.
Dim scanList, onlineList, netMode, netLabel
Select Case ver
    Case "2007"
        scanList = Array("R17.0", "R17.1", "R17.2")
        onlineList = scanList
        netMode = "net20"
        netLabel = ".NET Framework 2.0"
    Case "2010"
        scanList = Array("R18.0", "R18.1", "R18.2")
        onlineList = scanList
        netMode = "net35"
        netLabel = ".NET Framework 3.5"
    Case "2013"
        scanList = Array("R19.0", "R19.1")
        onlineList = scanList
        netMode = "net40"
        netLabel = ".NET Framework 4.0"
    Case "2015"
        scanList = Array("R24.0", "R23.1", "R23.0", "R22.0", "R21.0", "R20.1", "R20.0", "R24.1", "R24.2", "R25.0")
        onlineList = Array("R24.0", "R23.1", "R23.0", "R22.0", "R21.0", "R20.1", "R20.0", "R24.1", "R24.2")
        netMode = "net45"
        netLabel = ".NET Framework 4.5+"
    Case Else
        WScript.Echo "ERROR: unsupported script name '" & scriptName & "'. Use doctor-2007/2010/2013/2015.vbs."
        WScript.Quit 1
End Select

' ---- small helpers ----
Dim rptLines
rptLines = Array()

Sub Rpt(ByVal s)
    If IsArray(rptLines) Then
        ReDim Preserve rptLines(UBound(rptLines) + 1)
    Else
        ReDim rptLines(0)
    End If
    rptLines(UBound(rptLines)) = s
End Sub

Dim passC, failC, warnC
passC = 0 : failC = 0 : warnC = 0

Sub RptResult(ByVal label, ByVal status, ByVal detail)
    Rpt "  [" & status & "] " & label & ": " & detail
    If status = "PASS" Then
        passC = passC + 1
    ElseIf status = "FAIL" Then
        failC = failC + 1
    ElseIf status = "WARN" Then
        warnC = warnC + 1
    End If
End Sub

Function HiveName(ByVal hive)
    If hive = HKCU Then HiveName = "HKCU" Else HiveName = "HKLM"
End Function

' Enumerate ACAD-* profile subkeys under a hive/base path; Null when missing.
Sub EnumProfiles(ByVal hive, ByVal basePath, ByRef outKeys)
    outKeys = Null
    On Error Resume Next
    Dim ks
    reg.EnumKey hive, basePath, ks
    On Error GoTo 0
    If IsArray(ks) Then outKeys = ks
End Sub

Function ReadRegString(ByVal hive, ByVal keyPath, ByVal valueName)
    ReadRegString = Null
    On Error Resume Next
    Dim v
    v = Null
    reg.GetStringValue hive, keyPath, valueName, v
    On Error GoTo 0
    If VarType(v) = vbString Then ReadRegString = v
End Function

Function ReadRegDword(ByVal hive, ByVal keyPath, ByVal valueName)
    ReadRegDword = -1
    On Error Resume Next
    Dim v
    v = -1
    reg.GetDWORDValue hive, keyPath, valueName, v
    On Error GoTo 0
    If IsNumeric(v) Then ReadRegDword = CLng(v)
End Function

' ---- .NET runtime check (reads both native and Wow6432Node views) ----
Function CheckNetRuntime(ByVal mode)
    Dim base, rel, v
    base = "SOFTWARE\Microsoft\NET Framework Setup\NDP"
    Dim paths(1)
    paths(0) = base
    paths(1) = "SOFTWARE\Wow6432Node\Microsoft\NET Framework Setup\NDP"

    Dim found
    found = False
    Dim detail
    detail = ""

    If mode = "net20" Then
        rel = "\v2.0.50727"
        Dim i20
        For i20 = 0 To 1
            v = ReadRegDword(HKLM, paths(i20) & rel, "Install")
            If v = 1 Then found = True : detail = paths(i20) & rel & " Install=1"
        Next
    ElseIf mode = "net35" Then
        rel = "\v3.5"
        Dim i35
        For i35 = 0 To 1
            v = ReadRegDword(HKLM, paths(i35) & rel, "Install")
            If v = 1 Then found = True : detail = paths(i35) & rel & " Install=1"
        Next
    ElseIf mode = "net40" Then
        Dim i40, fullV, clientV
        For i40 = 0 To 1
            fullV = ReadRegDword(HKLM, paths(i40) & "\v4\Full", "Install")
            clientV = ReadRegDword(HKLM, paths(i40) & "\v4\Client", "Install")
            If fullV = 1 Then found = True : detail = paths(i40) & "\v4\Full Install=1"
            If clientV = 1 Then found = True : detail = paths(i40) & "\v4\Client Install=1 (Full preferred)"
        Next
    ElseIf mode = "net45" Then
        Dim i45, rel45
        rel45 = ReadRegDword(HKLM, base & "\v4\Full", "Release")
        If rel45 < 0 Then rel45 = ReadRegDword(HKLM, paths(1) & "\v4\Full", "Release")
        If rel45 >= 379893 Then
            found = True
            detail = "v4\Full Release=" & rel45 & " (4.5 or newer)"
        End If
    End If

    If found Then
        CheckNetRuntime = "PASS|" & detail
    Else
        CheckNetRuntime = "FAIL|not detected in registry"
    End If
End Function

' ---- locate acad.exe for the supported range ----
Dim foundAcadExe, foundAcadR
foundAcadExe = ""

Sub FindAcad(ByVal rlist)
    Dim r, hiveIdx, hive, basePath, profiles, i, dir
    For r = 0 To UBound(rlist)
        For hiveIdx = 0 To 2
            If hiveIdx = 0 Then
                hive = HKCU
                basePath = "Software\Autodesk\AutoCAD\" & rlist(r)
            Else
                hive = HKLM
                If hiveIdx = 1 Then
                    basePath = "SOFTWARE\Autodesk\AutoCAD\" & rlist(r)
                Else
                    basePath = "SOFTWARE\Wow6432Node\Autodesk\AutoCAD\" & rlist(r)
                End If
            End If

            EnumProfiles hive, basePath, profiles
            If Not IsNull(profiles) Then
                For i = 0 To UBound(profiles)
                    If Left(profiles(i), 5) = "ACAD-" Then
                        dir = ReadRegString(hive, basePath & "\" & profiles(i), "ProductDir")
                        If IsNull(dir) Or dir = "" Then
                            dir = ReadRegString(hive, basePath & "\" & profiles(i), "AcadLocation")
                        End If
                        If Not IsNull(dir) And dir <> "" Then
                            If fso.FileExists(fso.BuildPath(dir, "acad.exe")) Then
                                foundAcadExe = fso.BuildPath(dir, "acad.exe")
                                foundAcadR = rlist(r)
                                Exit Sub
                            End If
                        End If
                    End If
                Next
            End If
        Next
    Next
End Sub

' ---- log tail ----
Sub AppendLogTail(ByVal logPath)
    If fso.FileExists(logPath) Then
        Dim ts, all, arr, first, i
        Set ts = fso.OpenTextFile(logPath, 1)
        all = ts.ReadAll
        ts.Close
        arr = Split(all, vbCrLf)
        If UBound(arr) < 0 Then arr = Split(all, vbLf)
        If UBound(arr) > 11 Then first = UBound(arr) - 11 Else first = 0
        Rpt "  Log: " & logPath & " (last lines)"
        For i = first To UBound(arr)
            If Trim(arr(i)) <> "" Then Rpt "    " & arr(i)
        Next
    Else
        Rpt "  Log not found: " & logPath
    End If
End Sub

' ================= main =================
Dim offlineOnly
offlineOnly = False
If WScript.Arguments.Count > 0 Then
    If LCase(WScript.Arguments(0)) = "offline" Then offlineOnly = True
End If

Dim dllPath
dllPath = fso.BuildPath(scriptDir, "PatentMarker.dll")

Rpt "PatentMarker Offline Doctor Report (edition " & ver & ")"
Rpt "Generated : " & FormatDateTime(Now, vbGeneralDate)
Rpt "Script dir: " & scriptDir
Rpt ""

' [1] DLL
Rpt "[1] Deployment DLL"
If fso.FileExists(dllPath) Then
    Dim fl
    Set fl = fso.GetFile(dllPath)
    RptResult "DLL present", "PASS", fl.Size & " bytes, modified " & fl.DateLastModified
Else
    RptResult "DLL present", "FAIL", dllPath & " not found"
End If
Rpt ""

' [2] demand-load registry
Rpt "[2] Demand-load registry entries (Applications\PatentMarker)"
Dim entryCount, loaderBroken
entryCount = 0 : loaderBroken = 0
Dim r2, hiveIdx2, hive2, base2, profiles2, i2, appKey, loader, ctrls
For r2 = 0 To UBound(scanList)
    For hiveIdx2 = 0 To 2
        If hiveIdx2 = 0 Then
            hive2 = HKCU
            base2 = "Software\Autodesk\AutoCAD\" & scanList(r2)
        Else
            hive2 = HKLM
            If hiveIdx2 = 1 Then
                base2 = "SOFTWARE\Autodesk\AutoCAD\" & scanList(r2)
            Else
                base2 = "SOFTWARE\Wow6432Node\Autodesk\AutoCAD\" & scanList(r2)
            End If
        End If

        EnumProfiles hive2, base2, profiles2
        If Not IsNull(profiles2) Then
            For i2 = 0 To UBound(profiles2)
                If Left(profiles2(i2), 5) = "ACAD-" Then
                    appKey = base2 & "\" & profiles2(i2) & "\Applications\PatentMarker"
                    loader = ReadRegString(hive2, appKey, "LOADER")
                    If Not IsNull(loader) Then
                        entryCount = entryCount + 1
                        ctrls = ReadRegDword(hive2, appKey, "LOADCTRLS")
                        If fso.FileExists(loader) Then
                            Rpt "  [OK] " & HiveName(hive2) & "\" & appKey & " (LOADCTRLS=" & ctrls & ")"
                        Else
                            loaderBroken = loaderBroken + 1
                            Rpt "  [BROKEN] " & HiveName(hive2) & "\" & appKey
                            Rpt "           LOADER points to a missing file: " & loader
                        End If
                    End If
                End If
            Next
        End If
    Next
Next
If entryCount = 0 Then
    RptResult "Demand-load entries", "WARN", "none found - AutoCAD will not auto-load this DLL (rerun install-" & ver & ")"
ElseIf loaderBroken > 0 Then
    RptResult "Demand-load entries", "FAIL", loaderBroken & " of " & entryCount & " LOADER targets missing"
Else
    RptResult "Demand-load entries", "PASS", entryCount & " found, all LOADER targets exist"
End If
Rpt ""

' [3] .NET runtime
Rpt "[3] Required .NET runtime for this edition"
Dim netRes, netParts
netRes = CheckNetRuntime(netMode)
netParts = Split(netRes, "|")
RptResult netLabel, netParts(0), netParts(1)
Rpt ""

' [4] log tail
Rpt "[4] PatentMarker.log tail"
AppendLogTail fso.BuildPath(scriptDir, "PatentMarker.log")
Rpt ""

' [5] AutoCAD host in supported range
Rpt "[5] AutoCAD host in supported range"
FindAcad onlineList
If foundAcadExe <> "" Then
    RptResult "AutoCAD found", "PASS", foundAcadR & " -> " & foundAcadExe
Else
    RptResult "AutoCAD found", "WARN", "no AutoCAD for this edition's range installed (tier 2 skipped)"
End If
Rpt ""

' [6] tier 2 online PATDOCTOR
Rpt "[6] Tier 2 - in-CAD PATDOCTOR via /b batch"
Dim cadReport
cadReport = fso.BuildPath(scriptDir, "PatentMarker-doctor-report.txt")
If offlineOnly Then
    Rpt "  [SKIP] 'offline' argument given"
ElseIf foundAcadExe = "" Then
    Rpt "  [SKIP] no supported AutoCAD host installed"
ElseIf Not fso.FileExists(dllPath) Then
    Rpt "  [SKIP] deployment DLL missing, NETLOAD would fail"
Else
    ' A leftover acad.exe holds the profile lock and makes the batch host
    ' hang silently; refuse to launch alongside it (never kill user data).
    Dim wmi, acadProcs, pidList
    Set wmi = GetObject("winmgmts:{impersonationLevel=impersonate}!\\.\root\cimv2")
    Set acadProcs = wmi.ExecQuery("SELECT ProcessId FROM Win32_Process WHERE Name = 'acad.exe'")
    pidList = ""
    If acadProcs.Count > 0 Then
        Dim pIter
        For Each pIter In acadProcs
            If pidList <> "" Then pidList = pidList & ","
            pidList = pidList & pIter.ProcessId
        Next
    End If
    If pidList <> "" Then
        RptResult "PATDOCTOR report", "WARN", "skipped: AutoCAD is already running (PID " & pidList & "); close it and rerun for tier 2"
    Else
    If fso.FileExists(cadReport) Then fso.DeleteFile cadReport

    Dim scrPath, tsScr
    scrPath = fso.BuildPath(fso.GetSpecialFolder(2).Path, "patmarker-doctor.scr")
    Set tsScr = fso.CreateTextFile(scrPath, True)
    tsScr.WriteLine "_.FILEDIA 0"
    tsScr.WriteLine "_.CMDDIA 0"
    tsScr.WriteLine "_.SECURELOAD 0"
    tsScr.WriteLine "_.NETLOAD """ & dllPath & """"
    tsScr.WriteLine "_.PATDOCTOR"
    tsScr.WriteLine "_.QUIT"
    tsScr.WriteLine "_N"
    tsScr.Close

    Rpt "  Launching: " & foundAcadExe & " /b <scr> (timeout " & ONLINE_TIMEOUT_SEC & "s)"
    Dim exec, deadline
    Set exec = shell.Exec("""" & foundAcadExe & """ /b """ & scrPath & """")
    deadline = DateAdd("s", ONLINE_TIMEOUT_SEC, Now)
    On Error Resume Next
    Do While Now < deadline
        If exec.Status = 1 Then Exit Do
        If fso.FileExists(cadReport) Then Exit Do
        WScript.Sleep 2000
    Loop
    On Error GoTo 0

    If fso.FileExists(cadReport) Then
        WScript.Sleep 3000 ' let the report writer finish flushing
        RptResult "PATDOCTOR report", "PASS", cadReport
    Else
        RptResult "PATDOCTOR report", "FAIL", "not generated within " & ONLINE_TIMEOUT_SEC & "s (NETLOAD may have failed; see tier 1 findings)"
    End If
    If exec.Status = 0 Then
        On Error Resume Next
        exec.Terminate
        On Error GoTo 0
        Rpt "  Note: AutoCAD was still running after the wait and has been terminated."
    End If
    End If
End If
Rpt ""

' ---- overall ----
Rpt "OVERALL: PASS=" & passC & " FAIL=" & failC & " WARN=" & warnC

Dim reportPath
reportPath = fso.BuildPath(scriptDir, "PatentMarker-doctor-offline-report.txt")
Dim tsRpt
Set tsRpt = fso.CreateTextFile(reportPath, True)
tsRpt.Write Join(rptLines, vbCrLf) & vbCrLf
tsRpt.Close

WScript.Echo "========================================"
WScript.Echo "PatentMarker Doctor (edition " & ver & ")"
WScript.Echo "PASS=" & passC & " FAIL=" & failC & " WARN=" & warnC
If failC > 0 Then
    WScript.Echo ">>> Problems found. See:"
Else
    WScript.Echo ">>> Report written:"
End If
WScript.Echo "    " & reportPath
If fso.FileExists(cadReport) Then
    WScript.Echo ">>> In-CAD report:"
    WScript.Echo "    " & cadReport
End If
WScript.Echo "========================================"

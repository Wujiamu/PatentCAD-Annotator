' PatentMarker 2007 Installer (VBScript)
' Encoding: GBK (compatible with all Windows locales)
'
' Three-layer auto-load strategy:
'   Layer 1: HKCU Applications registry key (LOADCTRLS=14)
'   Layer 2: acad.lsp deployed to ACAD support path
'   Layer 3: Manual LSP file for APPLOAD fallback

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
logPath = scriptDir & "\install-2007.log"
Set logFile = fso.OpenTextFile(logPath, ForAppending, True)
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
    t = Replace(t, "PatentMarker 2007 Installer" & vbCrLf & "========================================", _
                           "PatentMarker 2007 安装程序" & vbCrLf & "========================================")
    t = Replace(t, "ERROR: PatentMarker.dll not found" & vbCrLf & "Path: ", _
                           "错误：找不到 PatentMarker.dll" & vbCrLf & "路径：")
    t = Replace(t, "ERROR: AutoCAD R17.0 not found in registry", _
                           "错误：注册表中未找到 AutoCAD R17.0")
    t = Replace(t, "ERROR: No ACAD- product code found", "错误：未找到 ACAD- 产品代码")
    t = Replace(t, ">>> Restart AutoCAD 2007.", ">>> 请重启 AutoCAD 2007。")
    t = Replace(t, ">>> PatentMarker will auto-load via acad.lsp.", ">>> PatentMarker 将通过 acad.lsp 自动加载。")
    t = Replace(t, ">>> Type BZ to open the palette.", ">>> 输入 BZ 打开面板。")
    t = Replace(t, ">>> Auto-deploy failed. Manual steps:", ">>> 自动部署失败，请手动操作：")
    t = Replace(t, "  A: APPLOAD -> ", "  A：APPLOAD -> ")
    t = Replace(t, "  B: NETLOAD -> ", "  B：NETLOAD -> ")
    t = Replace(t, "Commands:" & vbCrLf & _
        "  BZ   (PATPALETTE)    Palette" & vbCrLf & _
        "  BZM  (PATMARK)       Annotate" & vbCrLf & _
        "  BZC  (PATCHECK)      Check" & vbCrLf & _
        "  BZA  (PATALIGN)      Align" & vbCrLf & _
        "  BZS  (PATSELECTALL)  Select All", _
        "命令：" & vbCrLf & _
        "  BZ   (PATPALETTE)    标注面板" & vbCrLf & _
        "  BZM  (PATMARK)       标注" & vbCrLf & _
        "  BZC  (PATCHECK)      检查" & vbCrLf & _
        "  BZA  (PATALIGN)      对齐" & vbCrLf & _
        "  BZS  (PATSELECTALL)  全选")
    t = Replace(t, vbCrLf & "Done" & vbCrLf, vbCrLf & "完成" & vbCrLf)
    L = t
End Function
' === End i18n ===

Sub LogMsg(msg)
    logFile.WriteLine "[" & Now & "] " & msg
    output = output & msg & vbCrLf
End Sub

Sub QuitWithMsg(msg)
    LogMsg msg
    WScript.Echo L(output)
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
output = output & "PatentMarker 2007 Installer" & vbCrLf
output = output & "========================================" & vbCrLf

' --- 0. System info ---
LogMsg "--- System Info ---"
LogMsg "  OS: " & shell.ExpandEnvironmentStrings("%OS%")
LogMsg "  User: " & shell.ExpandEnvironmentStrings("%USERNAME%")
LogMsg "  Computer: " & shell.ExpandEnvironmentStrings("%COMPUTERNAME%")
LogMsg "  Script Dir: " & scriptDir
LogMsg "  Log: " & logPath

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

' --- 3. Scan registry for ACAD 2007 ---
Dim acadBaseKey
acadBaseKey = "Software\Autodesk\AutoCAD\R17.0"
LogMsg ""
LogMsg "--- Scanning Registry ---"

Dim subKeys
reg.EnumKey HKCU, acadBaseKey, subKeys
If IsNull(subKeys) Then
    reg.EnumKey HKLM, acadBaseKey, subKeys
    If IsNull(subKeys) Then
        QuitWithMsg "ERROR: AutoCAD R17.0 not found in registry"
    End If
    LogMsg "  Found in HKLM"
Else
    LogMsg "  Found in HKCU"
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

    Dim writeOk, verifyVal, verifyDword
    writeOk = True

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
        writeOk = False
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

' --- 7. Deploy LSP to ACAD support path (Layer 2) ---
LogMsg ""
LogMsg "--- Layer 2: Deploy LSP to ACAD Support Path ---"

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

    ' Collect candidate support directories
    Dim candidateDirs()
    ReDim candidateDirs(0)
    Dim candCount
    candCount = 0

    ' Source A: ACAD support path from registry
    Dim supportKey, supportPath
    supportKey = acadBaseKey & "\" & productCode & "\Fixed Profile\General\ACAD"
    supportPath = ""
    reg.GetStringValue HKCU, supportKey, "ACAD", supportPath

    If Not IsNull(supportPath) And supportPath <> "" Then
        LogMsg "  Support path found for " & productCode
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
    If acadPaths(j) <> "" Then
        Dim acadSupport
        acadSupport = acadPaths(j) & "\Support"
        If fso.FolderExists(acadSupport) Then
            ReDim Preserve candidateDirs(candCount)
            candidateDirs(candCount) = acadSupport
            candCount = candCount + 1
        End If
    End If

    ' Source C: %APPDATA%\Autodesk\AutoCAD\R17.0\<product>\<lang>\Support
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

    ' Find first writable dir and first writable acad.lsp
    Dim firstWritable, writableLsp
    firstWritable = ""
    writableLsp = ""

    For p = 0 To candCount - 1
        dirPath = candidateDirs(p)
        If IsDirWritable(dirPath) Then
            If firstWritable = "" Then
                firstWritable = dirPath
                LogMsg "  Writable dir: " & dirPath
            End If
            Dim lspCheck
            lspCheck = dirPath & "\acad.lsp"
            If fso.FileExists(lspCheck) And writableLsp = "" Then
                writableLsp = lspCheck
            End If
        End If
    Next

    If writableLsp <> "" Then
        ' Append to existing acad.lsp
        Dim lspContent, lf
        Set lf = fso.OpenTextFile(writableLsp, ForReading, False)
        lspContent = lf.ReadAll
        lf.Close

        If InStr(lspContent, "PatentMarker") > 0 Then
            LogMsg "  acad.lsp already has PatentMarker: " & writableLsp
        Else
            lspContent = lspContent & vbCrLf & _
                "; --- PatentMarker autoload ---" & vbCrLf & _
                lspLoadCmd & vbCrLf & _
                lspPrinc & vbCrLf
            Set lf = fso.OpenTextFile(writableLsp, ForWriting, False)
            lf.Write lspContent
            lf.Close
            LogMsg "  Appended to acad.lsp: " & writableLsp
        End If
        lspDeployed = True
    ElseIf firstWritable <> "" Then
        ' Create new acad.lsp in first writable support dir
        Dim newLsp
        newLsp = firstWritable & "\acad.lsp"
        Dim nf
        Set nf = fso.CreateTextFile(newLsp, True)
        nf.Write "; --- PatentMarker autoload ---" & vbCrLf & _
            lspLoadCmd & vbCrLf & _
            lspPrinc & vbCrLf
        nf.Close
        LogMsg "  Created acad.lsp: " & newLsp
        lspDeployed = True
    Else
        ' No writable support dir: create acad.lsp in install dir,
        ' prepend install dir to support path
        LogMsg "  No writable support dir, using install dir"

        Dim installLsp
        installLsp = scriptDir & "\acad.lsp"
        Dim ilf
        Set ilf = fso.CreateTextFile(installLsp, True)
        ilf.Write "; --- PatentMarker autoload ---" & vbCrLf & _
            lspLoadCmd & vbCrLf & _
            lspPrinc & vbCrLf
        ilf.Close

        If Not IsNull(supportPath) And supportPath <> "" Then
            If InStr(LCase(supportPath), LCase(scriptDir)) = 0 Then
                reg.SetStringValue HKCU, supportKey, "ACAD", scriptDir & ";" & supportPath
                LogMsg "  Added install dir to support path"
            End If
        End If
        LogMsg "  Created acad.lsp: " & installLsp
        lspDeployed = True
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
If lspDeployed Then
    LogMsg ">>> Restart AutoCAD 2007."
    LogMsg ">>> PatentMarker will auto-load via acad.lsp."
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

WScript.Echo L(output)
logFile.Close

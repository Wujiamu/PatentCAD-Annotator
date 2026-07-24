' PatentMarker 2007 安装脚本 (VBScript)
' 编码: GBK (Win7 中文系统兼容)
'
' 功能:
'   - 自动检测 AutoCAD 2007 产品代码
'   - 写入 HKCU 注册表 (并尝试 HKLM)
'   - 每步写入后立即读回验证
'   - 记录 HKLM 状态、系统信息、ACAD 路径
'   - 生成 load-patent-marker.lsp 辅助加载文件
'   - 详细日志输出到 install-2007.log
'   - 所有消息累积, 最后一次性显示

Option Explicit

Const HKCU = &H80000001
Const HKLM = &H80000002
Const ForAppending = 8
Const CreateFlag = True
Const ForWriting = 2

Dim fso, shell, reg
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
Set reg = GetObject("winmgmts:{impersonationLevel=impersonate}!\\.\root\default:StdRegProv")

Dim scriptDir, logPath, logFile
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
logPath = scriptDir & "\install-2007.log"
Set logFile = fso.OpenTextFile(logPath, ForAppending, CreateFlag)

Dim output
output = ""

Sub LogMsg(msg)
    Dim ts
    ts = Now
    logFile.WriteLine "[" & ts & "] " & msg
    output = output & msg & vbCrLf
End Sub

Sub QuitWithMsg(msg)
    LogMsg msg
    WScript.Echo output
    logFile.Close
    WScript.Quit(1)
End Sub

output = output & "========================================" & vbCrLf
output = output & "PatentMarker 2007 安装程序" & vbCrLf
output = output & "========================================" & vbCrLf

' --- 0. 系统信息 ---
LogMsg "--- 系统信息 ---"
LogMsg "  OS: " & shell.ExpandEnvironmentStrings("%OS%")
LogMsg "  用户名: " & shell.ExpandEnvironmentStrings("%USERNAME%")
LogMsg "  计算机名: " & shell.ExpandEnvironmentStrings("%COMPUTERNAME%")
LogMsg "  脚本目录: " & scriptDir
LogMsg "  日志文件: " & logPath

' --- 1. 定位 DLL ---
Dim dllPath
dllPath = scriptDir & "\PatentMarker.dll"

If Not fso.FileExists(dllPath) Then
    QuitWithMsg "错误: 未找到 PatentMarker.dll" & vbCrLf & "路径: " & scriptDir & vbCrLf & "请把此脚本和 PatentMarker.dll 放在同一文件夹"
End If

LogMsg "已找到 DLL: " & dllPath
LogMsg "DLL 大小: " & fso.GetFile(dllPath).Size & " 字节"

' --- 2. 检测 AutoCAD 2007 产品代码 ---
Dim acadBaseKey
acadBaseKey = "Software\Autodesk\AutoCAD\R17.0"
LogMsg ""
LogMsg "--- 扫描注册表 ---"
LogMsg "HKCU\" & acadBaseKey

Dim subKeys
reg.EnumKey HKCU, acadBaseKey, subKeys

If IsNull(subKeys) Then
    LogMsg "  HKCU 下无 R17.0 子键"
    ' 也检查 HKLM
    reg.EnumKey HKLM, acadBaseKey, subKeys
    If IsNull(subKeys) Then
        QuitWithMsg "错误: HKCU 和 HKLM 中均未找到 AutoCAD R17.0" & vbCrLf & "请至少启动一次 AutoCAD 2007 后再安装"
    End If
    LogMsg "  HKLM 下找到 " & (UBound(subKeys)+1) & " 个子键"
End If

Dim productCodes()
ReDim productCodes(0)
Dim productCount
productCount = 0

Dim i, key
For i = 0 To UBound(subKeys)
    key = subKeys(i)
    LogMsg "  子键: " & key
    If Left(key, 5) = "ACAD-" Then
        ReDim Preserve productCodes(productCount)
        productCodes(productCount) = key
        productCount = productCount + 1
    End If
Next

If productCount = 0 Then
    QuitWithMsg "错误: 未找到 AutoCAD 2007 产品代码"
End If

LogMsg "检测到 " & productCount & " 个 AutoCAD 产品"

' --- 3. 检测 ACAD 安装路径 ---
Dim productName, acadCurVer
acadCurVer = ""
reg.GetStringValue HKCU, acadBaseKey, "CurVC", acadCurVer
If IsNull(acadCurVer) Then acadCurVer = "(无)"
LogMsg "ACAD CurVC: " & acadCurVer

For i = 0 To productCount - 1
    Dim prodKey
    prodKey = acadBaseKey & "\" & productCodes(i)
    Dim acadLocation
    acadLocation = ""
    reg.GetStringValue HKCU, prodKey, "AcadLocation", acadLocation
    If IsNull(acadLocation) Or acadLocation = "" Then
        reg.GetStringValue HKLM, prodKey, "AcadLocation", acadLocation
    End If
    If IsNull(acadLocation) Or acadLocation = "" Then acadLocation = "(未找到)"
    LogMsg "  " & productCodes(i) & " 安装路径: " & acadLocation

    Dim productName2
    productName2 = ""
    reg.GetStringValue HKLM, prodKey, "ProductName", productName2
    If IsNull(productName2) Then productName2 = "(无)"
    LogMsg "  " & productCodes(i) & " 产品名: " & productName2

    ' 列出该产品下的所有子键
    Dim prodSubKeys
    reg.EnumKey HKCU, prodKey, prodSubKeys
    If Not IsNull(prodSubKeys) Then
        LogMsg "  HKCU 下子键列表:"
        Dim k
        For k = 0 To UBound(prodSubKeys)
            LogMsg "    " & prodSubKeys(k)
        Next
    End If
Next

' --- 4. 写入 HKCU 注册表并验证 ---
LogMsg ""
LogMsg "--- 写入 HKCU 注册表 ---"

Dim j, productCode, appKey, installed
installed = 0

For j = 0 To productCount - 1
    productCode = productCodes(j)
    appKey = acadBaseKey & "\" & productCode & "\Applications\PatentMarker"

    LogMsg "目标: HKCU\" & appKey

    reg.CreateKey HKCU, appKey

    Dim writeOk
    writeOk = True

    reg.SetStringValue HKCU, appKey, "DESCRIPTION", "PatentMarker - Patent Drawing Annotation Plugin"
    Dim verifyVal
    verifyVal = ""
    reg.GetStringValue HKCU, appKey, "DESCRIPTION", verifyVal
    If verifyVal = "PatentMarker - Patent Drawing Annotation Plugin" Then
        LogMsg "  DESCRIPTION: 成功"
    Else
        LogMsg "  DESCRIPTION: 失败 (读回: '" & verifyVal & "')"
        writeOk = False
    End If

    reg.SetDWORDValue HKCU, appKey, "LOADCTRLS", 14
    Dim verifyDword
    verifyDword = -1
    reg.GetDWORDValue HKCU, appKey, "LOADCTRLS", verifyDword
    If verifyDword = 14 Then
        LogMsg "  LOADCTRLS=14: 成功"
    Else
        LogMsg "  LOADCTRLS=14: 失败 (读回: " & verifyDword & ")"
        writeOk = False
    End If

    reg.SetDWORDValue HKCU, appKey, "MANAGED", 1
    verifyDword = -1
    reg.GetDWORDValue HKCU, appKey, "MANAGED", verifyDword
    If verifyDword = 1 Then
        LogMsg "  MANAGED=1: 成功"
    Else
        LogMsg "  MANAGED=1: 失败 (读回: " & verifyDword & ")"
        writeOk = False
    End If

    reg.SetStringValue HKCU, appKey, "LOADER", dllPath
    verifyVal = ""
    reg.GetStringValue HKCU, appKey, "LOADER", verifyVal
    If verifyVal = dllPath Then
        LogMsg "  LOADER: 成功"
    Else
        LogMsg "  LOADER: 失败 (读回: '" & verifyVal & "')"
        writeOk = False
    End If

    If writeOk Then
        LogMsg "  >>> HKCU 写入验证通过"
        installed = installed + 1
    End If
Next

' --- 5. 尝试写入 HKLM (可能需要管理员权限) ---
LogMsg ""
LogMsg "--- 尝试写入 HKLM ---"

Dim hklmOk
hklmOk = False
For j = 0 To productCount - 1
    productCode = productCodes(j)
    appKey = acadBaseKey & "\" & productCode & "\Applications\PatentMarker"

    On Error Resume Next
    reg.CreateKey HKLM, appKey
    If Err.Number <> 0 Then
        LogMsg "  HKLM CreateKey 失败: " & Err.Description & " (0x" & Hex(Err.Number) & ")"
        LogMsg "  >>> HKLM 不可写 (需要管理员权限)"
        On Error GoTo 0
        Exit For
    End If
    On Error GoTo 0

    reg.SetStringValue HKLM, appKey, "DESCRIPTION", "PatentMarker - Patent Drawing Annotation Plugin"
    reg.SetDWORDValue HKLM, appKey, "LOADCTRLS", 14
    reg.SetDWORDValue HKLM, appKey, "MANAGED", 1
    reg.SetStringValue HKLM, appKey, "LOADER", dllPath

    ' 验证 HKLM 写入
    verifyVal = ""
    reg.GetStringValue HKLM, appKey, "LOADER", verifyVal
    If verifyVal = dllPath Then
        LogMsg "  HKLM LOADER: 成功"
        hklmOk = True
    Else
        LogMsg "  HKLM LOADER: 失败 (读回: '" & verifyVal & "')"
    End If
Next

' --- 6. 生成辅助 LSP 文件 ---
LogMsg ""
LogMsg "--- 生成辅助加载文件 ---"
Dim lspPath
lspPath = scriptDir & "\load-patent-marker.lsp"
Dim lspContent
' LSP 中路径需要用正斜杠或双反斜杠
Dim lspDllPath
lspDllPath = Replace(dllPath, "\", "/")
lspContent = "; PatentMarker 自动加载脚本" & vbCrLf
lspContent = lspContent & "; 用法: 在 AutoCAD 中用 APPLOAD 命令加载此文件" & vbCrLf
lspContent = lspContent & "; 或把它加入启动套件 (Startup Suite) 实现自动加载" & vbCrLf
lspContent = lspContent & "(command ""NETLOAD"" """ & lspDllPath & """)" & vbCrLf
lspContent = lspContent & "(princ ""\nPatentMarker 已加载。输入 BZ 打开面板。\n"")" & vbCrLf
lspContent = lspContent & "(princ)" & vbCrLf

Dim lspFile
Set lspFile = fso.OpenTextFile(lspPath, ForWriting, True)
lspFile.Write lspContent
lspFile.Close
LogMsg "已生成: " & lspPath

' --- 7. 总结 ---
LogMsg ""
LogMsg "=== 安装结果 ==="
LogMsg "DLL 路径: " & dllPath
LogMsg "HKCU 写入: " & installed & " / " & productCount & " 成功"
LogMsg "HKLM 写入: " & IIf(hklmOk, "成功", "失败 (权限不足)")
LogMsg "辅助文件: " & lspPath
LogMsg "日志文件: " & logPath

If hklmOk Then
    LogMsg ""
    LogMsg "HKLM 写入成功, 重启 AutoCAD 2007 后插件将自动加载"
Else
    LogMsg ""
    LogMsg "警告: HKLM 不可写, ACAD 2007 可能只读 HKLM"
    LogMsg "HKCU 注册表已写入, 但可能不生效 (ACAD 2007 已知问题)"
    LogMsg ""
    LogMsg ">>> 推荐方案: 用 APPLOAD 加载 LSP <<<"
    LogMsg "  1. 打开 AutoCAD 2007"
    LogMsg "  2. 输入 APPLOAD 命令"
    LogMsg "  3. 点击 '启动套件' 下的 '内容'"
    LogMsg "  4. 添加: " & lspPath
    LogMsg "  5. 关闭对话框, 重启 AutoCAD"
    LogMsg ""
    LogMsg "或直接用 NETLOAD:"
    LogMsg "  输入 NETLOAD, 选择: " & dllPath
End If

LogMsg ""
LogMsg "可用命令 (括号内为拼音别名):"
LogMsg "  PATPALETTE (BIAOZHU / BZ)  - 打开字典面板"
LogMsg "  PATMARK    (BZM)           - 创建引线标注"
LogMsg "  PATCHECK   (BZC)           - 检查一致性"
LogMsg "  PATALIGN   (BZA)           - 对齐引线"
LogMsg "  PATSELECTALL (BZS)         - 全选标注实体"

LogMsg ""
LogMsg "========================================"
LogMsg "安装结束"
LogMsg "========================================"

WScript.Echo output
logFile.Close

' VBScript 没有 IIf 函数, 自己实现
Function IIf(cond, trueVal, falseVal)
    If cond Then
        IIf = trueVal
    Else
        IIf = falseVal
    End If
End Function

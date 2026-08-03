# PatentMarker 2025 Installer (PowerShell)
# Target: AutoCAD 2025/2026+ (R25.0+)
# Requires: Windows 10+, PowerShell 5.1+
# Encoding: UTF-8 with BOM (readable by both PS 5.1 and PS 7)

param(
    [string]$DllPath = ""
)

$ErrorActionPreference = "Stop"

# === Internationalization (i18n) ===
function Get-SysLang {
    try {
        $lid = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Nls\Language" -Name InstallLanguage -ErrorAction SilentlyContinue).InstallLanguage
        if (-not $lid) { $lid = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Nls\Language" -Name Default -ErrorAction SilentlyContinue).Default }
        if (-not $lid) { $lid = "0804" }
    } catch { $lid = "0804" }
    if ($lid -in "0804","0404","0C04","1404","7C04") { return "zh" }
    return "en"
}
$script:Lang = Get-SysLang

function L {
    param([string]$en, [string]$zh)
    if ($script:Lang -eq "zh") { return $zh }
    return $en
}
# === End i18n ===

Write-Host "========================================"
Write-Host (L "PatentMarker 2025 Installer" "PatentMarker 2025 安装程序")
Write-Host (L "(AutoCAD 2025/2026+)" "（AutoCAD 2025/2026+）")
Write-Host "========================================"

# 1. Locate DLL
if ([string]::IsNullOrEmpty($DllPath)) {
    $DllPath = Join-Path $PSScriptRoot "PatentMarker.dll"
}
if (-not (Test-Path $DllPath)) {
    Write-Host (L "ERROR: PatentMarker.dll not found at: $DllPath" "错误：找不到 PatentMarker.dll，路径：$DllPath") -ForegroundColor Red
    exit 1
}
Write-Host "DLL: $DllPath"

# 2. Find AutoCAD 2025+ in registry
$acadVersions = @("R25.0", "R25.1", "R26.0")
$acadBaseKey = $null
$foundVersion = ""

foreach ($ver in $acadVersions) {
    $hkcuPath = "HKCU:\Software\Autodesk\AutoCAD\$ver"
    $hklmPath = "HKLM:\Software\Autodesk\AutoCAD\$ver"
    if (Test-Path $hkcuPath) {
        $acadBaseKey = $hkcuPath
        $foundVersion = $ver
        break
    }
    if (Test-Path $hklmPath) {
        $acadBaseKey = $hklmPath
        $foundVersion = $ver
        break
    }
}

if ($null -eq $acadBaseKey) {
    Write-Host (L "ERROR: AutoCAD 2025+ not found in registry" "错误：注册表中未找到 AutoCAD 2025+") -ForegroundColor Red
    exit 1
}
Write-Host (L "Found: $foundVersion" "找到：$foundVersion")

# 3. Write registry for each product
$installed = 0
$subKeys = Get-ChildItem $acadBaseKey | Where-Object { $_.PSChildName -like "ACAD-*" }

foreach ($sk in $subKeys) {
    $appKey = Join-Path $sk.PSPath "Applications\PatentMarker"
    try {
        if (-not (Test-Path $appKey)) {
            New-Item -Path $appKey -Force | Out-Null
        }
        Set-ItemProperty -Path $appKey -Name "DESCRIPTION" -Value "PatentMarker - Patent Drawing Annotation Plugin"
        Set-ItemProperty -Path $appKey -Name "LOADCTRLS" -Value 14 -Type DWord
        Set-ItemProperty -Path $appKey -Name "MANAGED" -Value 1 -Type DWord
        Set-ItemProperty -Path $appKey -Name "LOADER" -Value $DllPath

        $verify = Get-ItemProperty -Path $appKey -Name "LOADER" -ErrorAction SilentlyContinue
        if ($verify.LOADER -eq $DllPath) {
            Write-Host "  $($sk.PSChildName): $(L 'Registry OK' '注册表 OK')"
            $installed++
        }
    }
    catch {
        Write-Host "  $($sk.PSChildName): $(L 'FAILED' '失败') - $_" -ForegroundColor Yellow
    }
}

# 4. Summary
Write-Host ""
Write-Host (L "=== Summary ===" "=== 摘要 ===")
Write-Host (L "Registry entries: $installed" "注册表条目：$installed")
Write-Host ""
if ($installed -gt 0) {
    Write-Host (L ">>> Restart AutoCAD 2025+." ">>> 请重启 AutoCAD 2025+。") -ForegroundColor Green
    Write-Host (L ">>> PatentMarker will auto-load." ">>> PatentMarker 将自动加载。")
    Write-Host (L ">>> Type BZ to open the palette." ">>> 输入 BZ 打开面板。")
} else {
    Write-Host (L ">>> Registry failed. Use NETLOAD manually:" ">>> 注册表写入失败，请手动 NETLOAD：") -ForegroundColor Yellow
    Write-Host ">>> $DllPath"
}
Write-Host ""
Write-Host (L "Commands: BZ BZM BZC BZA BZS" "命令：BZ(面板) BZM(标注) BZC(检查) BZA(对齐) BZS(全选)")
Write-Host "========================================"

# PatentMarker 2025 Uninstaller (PowerShell)
# Target: AutoCAD 2025/2026+ (R25.0+)
# Requires: Windows 10+, PowerShell 5.1+
# Encoding: UTF-8 with BOM (readable by both PS 5.1 and PS 7)
# Removes: HKCU/HKLM auto-load registry entries written by install-2025.ps1
#          (HKLM cleanup is defensive: an older installer revision wrote HKLM)
#          and the generated LSP fallback files.

[CmdletBinding()]
param(
    [switch]$NoPause,
    [switch]$KeepLsp
)

$ErrorActionPreference = "Stop"
$script:ExitCode = 0
$script:Removed = 0
$script:ScriptDir = $PSScriptRoot
$script:LogPath = Join-Path $script:ScriptDir "uninstall-2025.log"

function Initialize-UninstallerLog {
    $line = "$(Get-Date -Format s) START"
    try {
        Add-Content -LiteralPath $script:LogPath -Value $line -Encoding UTF8 -ErrorAction Stop
    }
    catch {
        $script:LogPath = Join-Path $env:TEMP "PatentMarker-2025-uninstall.log"
        try { Add-Content -LiteralPath $script:LogPath -Value $line -Encoding UTF8 -ErrorAction SilentlyContinue } catch { }
    }
}

function Write-UninstallerLog {
    param([string]$Message)
    try { Add-Content -LiteralPath $script:LogPath -Value "$(Get-Date -Format s) $Message" -Encoding UTF8 -ErrorAction SilentlyContinue } catch { }
}

function Write-UninstallerLine {
    param(
        [string]$Message,
        [ConsoleColor]$Color
    )
    if ($PSBoundParameters.ContainsKey("Color")) {
        Write-Host $Message -ForegroundColor $Color
    }
    else {
        Write-Host $Message
    }
    Write-UninstallerLog $Message
}

# === Internationalization (i18n) ===
function Get-SysLang {
    try {
        $lid = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Nls\Language" -Name InstallLanguage -ErrorAction SilentlyContinue).InstallLanguage
        if (-not $lid) { $lid = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Nls\Language" -Name Default -ErrorAction SilentlyContinue).Default }
        if (-not $lid) { $lid = "0804" }
    }
    catch { $lid = "0804" }
    if ($lid -in "0804", "0404", "0C04", "1404", "7C04") { return "zh" }
    return "en"
}
$script:Lang = Get-SysLang

function L {
    param([string]$en, [string]$zh)
    if ($script:Lang -eq "zh") { return $zh }
    return $en
}
# === End i18n ===

function Remove-RegistryEntries {
    $acadVersions = @("R25.0", "R25.1", "R26.0")
    foreach ($ver in $acadVersions) {
        foreach ($hive in "HKCU", "HKLM") {
            $baseKey = "${hive}:\Software\Autodesk\AutoCAD\$ver"
            if (-not (Test-Path -LiteralPath $baseKey)) { continue }
            $profiles = @(Get-ChildItem -LiteralPath $baseKey -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -like "ACAD-*" })
            foreach ($profile in $profiles) {
                $appKey = Join-Path $profile.PSPath "Applications\PatentMarker"
                if (Test-Path -LiteralPath $appKey) {
                    try {
                        Remove-Item -LiteralPath $appKey -Recurse -Force -ErrorAction Stop
                        Write-UninstallerLine "  $(L "Removed ${hive}: Software\Autodesk\AutoCAD\$ver\$($profile.PSChildName)\Applications\PatentMarker" "已移除 ${hive}: Software\Autodesk\AutoCAD\$ver\$($profile.PSChildName)\Applications\PatentMarker")" -Color Green
                        $script:Removed++
                    }
                    catch {
                        Write-UninstallerLine "  $(L "FAILED on $appKey : $($_.Exception.Message)" "删除失败 $appKey : $($_.Exception.Message)")" -Color Yellow
                    }
                }
            }
        }
    }
}

function Remove-LspFallback {
    $candidates = @(
        (Join-Path $script:ScriptDir "load-patent-marker.lsp"),
        (Join-Path (Join-Path $env:LOCALAPPDATA "PatentMarker") "load-patent-marker.lsp")
    )
    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            try {
                Remove-Item -LiteralPath $path -Force -ErrorAction Stop
                Write-UninstallerLine "  $(L "Removed LSP fallback: $path" "已删除 LSP 兜底文件：$path")" -Color Green
            }
            catch {
                Write-UninstallerLine "  $(L "Could not remove LSP fallback at $path : $($_.Exception.Message)" "无法删除 LSP 兜底文件 $path : $($_.Exception.Message)")" -Color Yellow
            }
        }
    }
    # Remove the LOCALAPPDATA folder itself when nothing else lives there.
    $lspDir = Join-Path $env:LOCALAPPDATA "PatentMarker"
    if (Test-Path -LiteralPath $lspDir -PathType Container) {
        $remaining = @(Get-ChildItem -LiteralPath $lspDir -Force -ErrorAction SilentlyContinue)
        if ($remaining.Count -eq 0) {
            try {
                Remove-Item -LiteralPath $lspDir -Force -ErrorAction Stop
                Write-UninstallerLine "  $(L "Removed empty folder: $lspDir" "已删除空文件夹：$lspDir")" -Color Green
            } catch { }
        }
    }
}

Initialize-UninstallerLog

try {
    Write-UninstallerLine "========================================"
    Write-UninstallerLine (L "PatentMarker 2025 Uninstaller" "PatentMarker 2025 卸载程序")
    Write-UninstallerLine (L "(AutoCAD 2025/2026+)" "（AutoCAD 2025/2026+）")
    Write-UninstallerLine "========================================"
    Write-UninstallerLine (L "Log: $script:LogPath" "日志：$script:LogPath")

    # 1. Registry auto-load entries
    Write-UninstallerLine (L "Removing registry auto-load entries..." "正在移除注册表自动加载条目...")
    Remove-RegistryEntries

    # 2. Generated LSP fallback files
    if ($KeepLsp) {
        Write-UninstallerLine (L "Skipping LSP fallback removal (-KeepLsp)." "按 -KeepLsp 跳过 LSP 兜底文件删除。")
    }
    else {
        Write-UninstallerLine (L "Removing generated LSP fallback files..." "正在删除生成的 LSP 兜底文件...")
        Remove-LspFallback
    }

    # 3. Summary
    Write-UninstallerLine ""
    Write-UninstallerLine (L "=== Summary ===" "=== 摘要 ===")
    Write-UninstallerLine (L "Registry entries removed: $script:Removed" "已移除注册表条目：$script:Removed")
    Write-UninstallerLine ""
    if ($script:Removed -gt 0) {
        Write-UninstallerLine (L ">>> Restart AutoCAD to finish uninstall." ">>> 请重启 AutoCAD 完成卸载。") -Color Green
    }
    else {
        Write-UninstallerLine (L ">>> No PatentMarker auto-load entries were found." ">>> 未找到 PatentMarker 自动加载条目。")
    }
    Write-UninstallerLine (L ">>> The deployment folder is kept; delete it manually if desired." ">>> 部署目录本身保留；如需彻底删除请手动处理。")
    Write-UninstallerLine "========================================"
}
catch {
    $script:ExitCode = 1
    $message = $_.Exception.Message
    Write-UninstallerLine (L "ERROR: $message" "错误：$message") -Color Red
    Write-UninstallerLine (L "The uninstaller stopped. See the log: $script:LogPath" "卸载程序已停止。请查看日志：$script:LogPath") -Color Red
}
finally {
    if (-not $NoPause -and $Host.Name -eq "ConsoleHost") {
        Write-Host ""
        Read-Host (L "Press Enter to close" "按 Enter 键关闭") | Out-Null
    }
}

if ($script:ExitCode -ne 0) { exit $script:ExitCode }

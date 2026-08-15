# PatentMarker Doctor 2025 - offline + online escalation (PowerShell)
# Target: AutoCAD 2025/2026+ (R25.0+)
#
# Tier 1 (always runs, no AutoCAD needed): deployment DLL presence,
#   demand-load registry entries and their LOADER targets, .NET 8 runtime,
#   PatentMarker.log tail. Catches the "DLL never loaded" class that the
#   in-CAD PATDOCTOR command cannot see.
# Tier 2 (optional): start AutoCAD 2025+ in /b batch mode, NETLOAD the
#   deployment DLL and run PATDOCTOR, so an in-CAD report is produced even
#   when demand-load registration is broken and BZD never got registered.
#
# Usage:  powershell -ExecutionPolicy Bypass -File .\doctor-2025.ps1 [-OfflineOnly] [-NoPause]
# Output: PatentMarker-doctor-offline-report.txt next to this script
#         PatentMarker-doctor-report.txt          (written by PATDOCTOR)

[CmdletBinding()]
param(
    [switch]$OfflineOnly,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
$script:ScriptDir = $PSScriptRoot
$script:DllPath = Join-Path $script:ScriptDir "PatentMarker.dll"
$script:CadReport = Join-Path $script:ScriptDir "PatentMarker-doctor-report.txt"
$script:OfflineReport = Join-Path $script:ScriptDir "PatentMarker-doctor-offline-report.txt"
$script:Rpt = New-Object System.Collections.Generic.List[string]
$script:Pass = 0; $script:Fail = 0; $script:Warn = 0

# === Internationalization (i18n, console only; report file stays ASCII English) ===
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

function Add-Rpt {
    param([string]$Line)
    $script:Rpt.Add($Line)
}

function Add-RptResult {
    param([string]$Label, [string]$Status, [string]$Detail)
    Add-Rpt "  [$Status] ${Label}: $Detail"
    switch ($Status) {
        "PASS" { $script:Pass++ }
        "FAIL" { $script:Fail++ }
        "WARN" { $script:Warn++ }
    }
}

# AutoCAD 2025+ release keys: scanList mirrors install-2025.ps1 candidates;
# onlineList is the range this DLL can actually load into (same list here).
$scanList = @("R25.0", "R25.1", "R26.0")

function Get-AcadProfiles {
    # Returns @(hiveLabel, basePath, profileChildName) triples for every
    # existing ACAD-* profile under the scan list. AutoCAD 2025+ is 64-bit
    # only, so the native view is sufficient.
    $found = @()
    foreach ($ver in $scanList) {
        foreach ($hive in "HKCU", "HKLM") {
            $base = "${hive}:\SOFTWARE\Autodesk\AutoCAD\$ver"
            if (-not (Test-Path -LiteralPath $base)) { continue }
            $profiles = @(Get-ChildItem -LiteralPath $base -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -like "ACAD-*" })
            foreach ($p in $profiles) {
                $found += ,@($hive, $ver, $p.PSChildName, $p.PSPath)
            }
        }
    }
    return ,$found
}

function Test-DotNet8 {
    $dotnetShared = Join-Path $env:ProgramFiles "dotnet\shared\Microsoft.NETCore.App"
    if (Test-Path -LiteralPath $dotnetShared) {
        $v8 = @(Get-ChildItem -LiteralPath $dotnetShared -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "8.*" })
        if ($v8.Count -gt 0) {
            return "PASS|found " + ($v8[-1].Name)
        }
    }
    # AutoCAD 2025+ setup ships/relies on .NET 8; absence from the standard
    # location is not always fatal (CAD may carry its own copy), so WARN.
    return "WARN|.NET 8 not found under $dotnetShared (AutoCAD 2025+ setup may provide its own)"
}

function Get-AcadExe {
    foreach ($triple in (Get-AcadProfiles)) {
        $hive = $triple[0]; $ver = $triple[1]; $profile = $triple[2]; $psPath = $triple[3]
        foreach ($valueName in "ProductDir", "AcadLocation") {
            $dir = (Get-ItemProperty -LiteralPath $psPath -Name $valueName -ErrorAction SilentlyContinue).$valueName
            if ($dir) {
                $exe = Join-Path $dir "acad.exe"
                if (Test-Path -LiteralPath $exe -PathType Leaf) {
                    return ,@($exe, $ver)
                }
            }
        }
    }
    return $null
}

try {
    Write-Host "========================================"
    Write-Host (L "PatentMarker Doctor 2025" "PatentMarker 2025 诊断")
    Write-Host (L "(AutoCAD 2025/2026+)" "（AutoCAD 2025/2026+）")
    Write-Host "========================================"

    Add-Rpt "PatentMarker Offline Doctor Report (edition 2025)"
    Add-Rpt "Generated : $(Get-Date -Format s)"
    Add-Rpt "Script dir: $script:ScriptDir"
    Add-Rpt ""

    # [1] DLL
    Add-Rpt "[1] Deployment DLL"
    if (Test-Path -LiteralPath $script:DllPath -PathType Leaf) {
        $f = Get-Item -LiteralPath $script:DllPath
        Add-RptResult "DLL present" "PASS" "$($f.Length) bytes, modified $($f.LastWriteTime)"
    }
    else {
        Add-RptResult "DLL present" "FAIL" "$($script:DllPath) not found"
    }
    Add-Rpt ""

    # [2] demand-load registry
    Add-Rpt "[2] Demand-load registry entries (Applications\PatentMarker)"
    $entryCount = 0; $loaderBroken = 0
    foreach ($triple in (Get-AcadProfiles)) {
        $hive = $triple[0]; $ver = $triple[1]; $profile = $triple[2]
        $appKey = "${hive}:\SOFTWARE\Autodesk\AutoCAD\$ver\$profile\Applications\PatentMarker"
        if (Test-Path -LiteralPath $appKey) {
            $props = Get-ItemProperty -LiteralPath $appKey -ErrorAction SilentlyContinue
            $loader = $props.LOADER
            if ($loader) {
                $entryCount++
                if (Test-Path -LiteralPath $loader -PathType Leaf) {
                    Add-Rpt "  [OK] $hive\SOFTWARE\Autodesk\AutoCAD\$ver\$profile\Applications\PatentMarker (LOADCTRLS=$($props.LOADCTRLS))"
                }
                else {
                    $loaderBroken++
                    Add-Rpt "  [BROKEN] $hive\SOFTWARE\Autodesk\AutoCAD\$ver\$profile\Applications\PatentMarker"
                    Add-Rpt "           LOADER points to a missing file: $loader"
                }
            }
        }
    }
    if ($entryCount -eq 0) {
        Add-RptResult "Demand-load entries" "WARN" "none found - AutoCAD will not auto-load this DLL (rerun install-2025.ps1)"
    }
    elseif ($loaderBroken -gt 0) {
        Add-RptResult "Demand-load entries" "FAIL" "$loaderBroken of $entryCount LOADER targets missing"
    }
    else {
        Add-RptResult "Demand-load entries" "PASS" "$entryCount found, all LOADER targets exist"
    }
    Add-Rpt ""

    # [3] .NET 8 runtime
    Add-Rpt "[3] Required .NET runtime for this edition"
    $net = Test-DotNet8
    $netStatus, $netDetail = $net.Split("|", 2)
    Add-RptResult ".NET 8" $netStatus $netDetail
    Add-Rpt ""

    # [4] log tail
    Add-Rpt "[4] PatentMarker.log tail"
    $logPath = Join-Path $script:ScriptDir "PatentMarker.log"
    if (Test-Path -LiteralPath $logPath -PathType Leaf) {
        Add-Rpt "  Log: $logPath (last lines)"
        Get-Content -LiteralPath $logPath -Tail 12 | ForEach-Object {
            if ($_.Trim()) { Add-Rpt "    $_" }
        }
    }
    else {
        Add-Rpt "  Log not found: $logPath"
    }
    Add-Rpt ""

    # [5] AutoCAD host
    Add-Rpt "[5] AutoCAD host in supported range"
    $acad = Get-AcadExe
    if ($null -ne $acad) {
        Add-RptResult "AutoCAD found" "PASS" "$($acad[1]) -> $($acad[0])"
    }
    else {
        Add-RptResult "AutoCAD found" "WARN" "no AutoCAD 2025+ installed (tier 2 skipped)"
    }
    Add-Rpt ""

    # [6] tier 2 online PATDOCTOR
    Add-Rpt "[6] Tier 2 - in-CAD PATDOCTOR via /b batch"
    if ($OfflineOnly) {
        Add-Rpt "  [SKIP] -OfflineOnly given"
    }
    elseif ($null -eq $acad) {
        Add-Rpt "  [SKIP] no supported AutoCAD host installed"
    }
    elseif (-not (Test-Path -LiteralPath $script:DllPath -PathType Leaf)) {
        Add-Rpt "  [SKIP] deployment DLL missing, NETLOAD would fail"
    }
    else {
        # A leftover acad.exe holds the profile lock and makes the batch host
        # hang silently; refuse to launch alongside it (never kill user data).
        $leftover = @(Get-Process acad -ErrorAction SilentlyContinue)
        if ($leftover.Count -gt 0) {
            Add-RptResult "PATDOCTOR report" "WARN" ("skipped: AutoCAD is already running (PID " + ($leftover.Id -join ',') + '); close it and rerun for tier 2')
        }
        else {
        if (Test-Path -LiteralPath $script:CadReport) { Remove-Item -LiteralPath $script:CadReport -Force }
        $scr = Join-Path ([IO.Path]::GetTempPath()) "patmarker-doctor-2025.scr"
        @(
            '_.FILEDIA 0'
            '_.CMDDIA 0'
            '_.SECURELOAD 0'
            ('_.NETLOAD "' + $script:DllPath + '"')
            '_.PATDOCTOR'
            '_.QUIT'
            '_N'
        ) | Set-Content -LiteralPath $scr -Encoding ASCII

        Add-Rpt "  Launching: $($acad[0]) /b <scr> (timeout 300s)"
        Write-Host (L "Launching AutoCAD batch mode for PATDOCTOR..." "正在启动 AutoCAD 批处理模式运行 PATDOCTOR...") -Color Cyan
        $p = Start-Process -FilePath $acad[0] -ArgumentList ('/b "' + $scr + '"') -PassThru

        $deadline = (Get-Date).AddSeconds(300)
        while ((Get-Date) -lt $deadline -and -not (Test-Path -LiteralPath $script:CadReport) -and -not $p.HasExited) {
            Start-Sleep -Seconds 2
        }

        if (Test-Path -LiteralPath $script:CadReport) {
            Start-Sleep -Seconds 3   # let the report writer finish flushing
            Add-RptResult "PATDOCTOR report" "PASS" $script:CadReport
        }
        else {
            Add-RptResult "PATDOCTOR report" "FAIL" "not generated within 300s (NETLOAD may have failed; see tier 1 findings)"
        }
        if (-not $p.HasExited) {
            $p.Kill(); $p.WaitForExit()
            Add-Rpt "  Note: AutoCAD was still running after the wait and has been terminated."
        }
        }
    }
    Add-Rpt ""

    Add-Rpt "OVERALL: PASS=$script:Pass FAIL=$script:Fail WARN=$script:Warn"

    [IO.File]::WriteAllText($script:OfflineReport, ($script:Rpt -join "`r`n") + "`r`n", (New-Object Text.UTF8Encoding($false)))

    Write-Host (L "PASS=$script:Pass FAIL=$script:Fail WARN=$script:Warn" "通过=$script:Pass 失败=$script:Fail 警告=$script:Warn")
    if ($script:Fail -gt 0) {
        Write-Host (L ">>> Problems found. See:" ">>> 发现问题，详见：") -Color Yellow
    }
    else {
        Write-Host (L ">>> Report written:" ">>> 报告已生成：") -Color Green
    }
    Write-Host "    $script:OfflineReport"
    if (Test-Path -LiteralPath $script:CadReport) {
        Write-Host (L ">>> In-CAD report:" ">>> CAD 内诊断报告：")
        Write-Host "    $script:CadReport"
    }
    Write-Host "========================================"
}
catch {
    Write-Host (L "ERROR: $($_.Exception.Message)" "错误：$($_.Exception.Message)") -Color Red
    if (-not $NoPause -and $Host.Name -eq "ConsoleHost") {
        Read-Host (L "Press Enter to close" "按 Enter 键关闭") | Out-Null
    }
    exit 1
}
finally {
    if (-not $NoPause -and $Host.Name -eq "ConsoleHost") {
        Write-Host ""
        Read-Host (L "Press Enter to close" "按 Enter 键关闭") | Out-Null
    }
}

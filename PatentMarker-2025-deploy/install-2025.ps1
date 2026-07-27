# PatentMarker 2025 Installer (PowerShell)
# Target: AutoCAD 2025/2026+ (R25.0+)
# Requires: Windows 10+, PowerShell 5.1+

param(
    [string]$DllPath = ""
)

$ErrorActionPreference = "Stop"

Write-Host "========================================"
Write-Host "PatentMarker 2025 Installer"
Write-Host "(AutoCAD 2025/2026+)"
Write-Host "========================================"

# 1. Locate DLL
if ([string]::IsNullOrEmpty($DllPath)) {
    $DllPath = Join-Path $PSScriptRoot "PatentMarker.dll"
}
if (-not (Test-Path $DllPath)) {
    Write-Host "ERROR: PatentMarker.dll not found at: $DllPath" -ForegroundColor Red
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
    Write-Host "ERROR: AutoCAD 2025+ not found in registry" -ForegroundColor Red
    exit 1
}
Write-Host "Found: $foundVersion"

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
            Write-Host "  $($sk.PSChildName): Registry OK"
            $installed++
        }
    }
    catch {
        Write-Host "  $($sk.PSChildName): FAILED - $_" -ForegroundColor Yellow
    }
}

# 4. Summary
Write-Host ""
Write-Host "=== Summary ==="
Write-Host "Registry entries: $installed"
Write-Host ""
if ($installed -gt 0) {
    Write-Host ">>> Restart AutoCAD 2025+." -ForegroundColor Green
    Write-Host ">>> PatentMarker will auto-load."
    Write-Host ">>> Type BZ to open the palette."
} else {
    Write-Host ">>> Registry failed. Use NETLOAD manually:" -ForegroundColor Yellow
    Write-Host ">>> $DllPath"
}
Write-Host ""
Write-Host "Commands: BZ BZM BZC BZA BZS"
Write-Host "========================================"

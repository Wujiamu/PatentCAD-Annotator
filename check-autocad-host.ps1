# Read-only AutoCAD host inventory.
#
# This script never starts AutoCAD, changes registry/services, changes trusted
# paths, or attempts to acquire/bypass a license. It only reports whether the
# local host has the files, licensing service, and COM registration needed for
# a later manual/interactive validation.

param(
    [string[]]$Version = @("2025", "2026"),
    [switch]$RequireInstalled
)

$ErrorActionPreference = "Stop"

function Write-Status([string]$label, [string]$message, [ConsoleColor]$color = [ConsoleColor]::Gray) {
    Write-Host ("[{0}] {1}" -f $label, $message) -ForegroundColor $color
}

$knownInstallRoots = @(
    "C:\Program Files\Autodesk",
    "C:\Program Files\Autodesk\AutoCAD 2025",
    "C:\Program Files\Autodesk\AutoCAD 2026"
) | Select-Object -Unique

$acadExecutables = @()
$coreExecutables = @()
foreach ($root in $knownInstallRoots) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    if ((Get-Item -LiteralPath $root -ErrorAction SilentlyContinue).PSIsContainer) {
        $acadExecutables += Get-ChildItem -LiteralPath $root -Filter "acad.exe" -File -Recurse -ErrorAction SilentlyContinue
        $coreExecutables += Get-ChildItem -LiteralPath $root -Filter "accoreconsole.exe" -File -Recurse -ErrorAction SilentlyContinue
    }
}
$acadExecutables = @($acadExecutables | Sort-Object FullName -Unique)
$coreExecutables = @($coreExecutables | Sort-Object FullName -Unique)

if ($acadExecutables.Count -eq 0) {
    Write-Status "BLOCKED" "No AutoCAD desktop executable found under the known Autodesk install roots." Yellow
} else {
    foreach ($file in $acadExecutables) {
        Write-Status "FOUND" "AutoCAD: $($file.FullName)" Green
    }
}

if ($coreExecutables.Count -eq 0) {
    Write-Status "INFO" "No AutoCAD Core Console executable found under the known Autodesk install roots." DarkYellow
} else {
    foreach ($file in $coreExecutables) {
        Write-Status "FOUND" "Core Console: $($file.FullName)" Green
    }
}

$licensingService = Get-Service -Name "AdskLicensingService" -ErrorAction SilentlyContinue
if ($null -eq $licensingService) {
    Write-Status "BLOCKED" "Autodesk Desktop Licensing Service is not registered." Yellow
} elseif ($licensingService.Status -eq "Running") {
    Write-Status "FOUND" "Autodesk Desktop Licensing Service is running." Green
} else {
    Write-Status "WARN" "Autodesk Desktop Licensing Service exists but is $($licensingService.Status)." Yellow
}

$progIds = @("AutoCAD.Application", "AutoCAD.Application.25", "AutoCAD.Application.25.1")
$registeredProgIds = @()
foreach ($progId in $progIds) {
    $progKey = "Registry::HKEY_CLASSES_ROOT\$progId"
    $clsidKey = Join-Path $progKey "CLSID"
    if (-not (Test-Path -LiteralPath $clsidKey)) {
        Write-Status "INFO" "COM ProgID not registered: $progId" DarkYellow
        continue
    }

    $clsid = (Get-ItemProperty -LiteralPath $clsidKey -ErrorAction SilentlyContinue).'(default)'
    $server = $null
    if ($clsid) {
        $serverKey = "Registry::HKEY_CLASSES_ROOT\CLSID\$clsid\LocalServer32"
        $server = (Get-ItemProperty -LiteralPath $serverKey -ErrorAction SilentlyContinue).'(default)'
    }

    $registeredProgIds += $progId
    Write-Status "FOUND" "COM $progId -> CLSID $clsid -> $server" Green
}

if ($registeredProgIds.Count -eq 0) {
    Write-Status "BLOCKED" "No AutoCAD COM ProgID is registered." Yellow
}

Write-Status "INFO" "COM registration and a running licensing service do not prove that an Autodesk account or entitlement is valid." Cyan
Write-Status "INFO" "This check does not launch AutoCAD and does not change licensing, registry, services, or security settings." Cyan

$hostReady = ($acadExecutables.Count -gt 0 -and $licensingService -and $licensingService.Status -eq "Running" -and $registeredProgIds.Count -gt 0)
if ($hostReady) {
    Write-Status "READY" "Host prerequisites are present; interactive launch/licensing still requires a valid Autodesk entitlement." Green
    exit 0
}

if ($RequireInstalled) {
    Write-Status "FAIL" "Required host prerequisites are not present." Red
    exit 1
}

Write-Status "PENDING" "Host prerequisites are incomplete; continue with simulated/API checks until AutoCAD is available." Yellow
exit 0

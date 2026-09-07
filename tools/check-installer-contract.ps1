param()

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$contracts = @(
    @{
        Name = "2007"
        Path = "PatentMarker-2007-deploy\install-2007.vbs"
        Candidates = @("R17.0")
        Required = @(
            "For Each hive In Array(HKCU, HKLM)"
            "seenProfiles"
            "Err.Clear"
        )
    }
    @{
        Name = "2010"
        Path = "PatentMarker-2010-deploy\install-2010.vbs"
        Candidates = @("R18.0", "R18.1", "R18.2")
        Required = @(
            "productBases"
            "productVersions"
            "productBases(j)"
            "acadPaths(j)"
            "For Each hive In Array(HKCU, HKLM)"
            "seenProfiles"
            "Err.Clear"
        )
    }
    @{
        Name = "2013"
        Path = "PatentMarker-2013-deploy\install-2013.vbs"
        Candidates = @("R19.0", "R19.1")
        Required = @(
            "foundAny"
            "appKey = tryKey"
            "For Each hive In Array(HKCU, HKLM)"
            "seenProfiles"
        )
    }
    @{
        Name = "2015"
        Path = "PatentMarker-2015-deploy\install-2015.vbs"
        Candidates = @("R20.0", "R20.1", "R21.0", "R22.0", "R23.0", "R23.1", "R24.0", "R24.1", "R24.2")
        Required = @(
            "foundAny"
            "appKey = tryKey"
            "For Each hive In Array(HKCU, HKLM)"
            "seenProfiles"
        )
    }
    @{
        Name = "2025"
        Path = "PatentMarker-2025-deploy\install-2025.ps1"
        Candidates = @("R25.0", "R25.1", "R26.0")
        Required = @(
            'foreach ($ver in $acadVersions)'
            'foreach ($candidate in $candidates)'
            '$profiles = @{}'
            '$profiles.ContainsKey'
            'Profiles = @($profiles.Values)'
            "detectedTargets"
        )
    }
    @{
        Name = "2007-uninstall"
        Path = "PatentMarker-2007-deploy\uninstall-2007.vbs"
        Candidates = @("R17.0")
        Required = @(
            "For Each hive In Array(HKCU, HKLM)"
            "seenProfiles"
            "Dim candidateDirs()"
            "reg.GetStringValue HKLM, supportKey"
            "reg.DeleteKey HKLM"
        )
    }
    @{
        Name = "2010-uninstall"
        Path = "PatentMarker-2010-deploy\uninstall-2010.vbs"
        Candidates = @("R18.0", "R18.1", "R18.2")
        Required = @(
            "For Each hive In Array(HKCU, HKLM)"
            "seenProfiles"
            "reg.DeleteKey HKLM"
            "lspCleaned"
            "reg.GetStringValue HKLM, lspBaseKey"
        )
    }
    @{
        Name = "2013-uninstall"
        Path = "PatentMarker-2013-deploy\uninstall-2013.vbs"
        Candidates = @("R19.0", "R19.1")
        Required = @(
            "For Each hive In Array(HKCU, HKLM)"
            "seenProfiles"
            "reg.DeleteKey HKLM"
        )
    }
    @{
        Name = "2015-uninstall"
        Path = "PatentMarker-2015-deploy\uninstall-2015.vbs"
        Candidates = @("R20.0", "R20.1", "R21.0", "R22.0", "R23.0", "R23.1", "R24.0", "R24.1", "R24.2")
        Required = @(
            "For Each hive In Array(HKCU, HKLM)"
            "seenProfiles"
            "reg.DeleteKey HKLM"
        )
    }
    @{
        Name = "2025-uninstall"
        Path = "PatentMarker-2025-deploy\uninstall-2025.ps1"
        Candidates = @("R25.0", "R25.1", "R26.0")
        Required = @(
            'foreach ($ver in $acadVersions)'
            'foreach ($hive in "HKCU", "HKLM")'
            "Remove-RegistryEntries"
        )
    }
)

$failCount = 0
Write-Output "Installer profile enumeration check"
foreach ($contract in $contracts) {
    $path = Join-Path $root $contract.Path
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Output "[FAIL] $($contract.Name): missing $($contract.Path)"
        $failCount++
        continue
    }

    $text = Get-Content -LiteralPath $path -Raw
    $missing = @()
    foreach ($candidate in $contract.Candidates) {
        if ($text.IndexOf($candidate, [StringComparison]::Ordinal) -lt 0) {
            $missing += $candidate
        }
    }
    foreach ($required in $contract.Required) {
        if ($text.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            $missing += $required
        }
    }

    $isPowerShell = $contract.Path.EndsWith(".ps1", [StringComparison]::OrdinalIgnoreCase)
    if (-not $isPowerShell -and $text -match "(?m)^\s*Exit For\b") {
        $missing += "no Exit For in profile scan"
    }
    if ($isPowerShell -and $text -match "(?m)^\s*break\s*$") {
        $missing += "no break in hive/profile scan"
    }

    if ($missing.Count -eq 0) {
        Write-Output "[OK]   $($contract.Name): all supported profiles are enumerated"
    } else {
        Write-Output "[FAIL] $($contract.Name): missing or unsafe contract: $($missing -join ', ')"
        $failCount++
    }
}

if ($failCount -gt 0) {
    Write-Output "Installer profile enumeration check failed: $failCount edition(s)."
    exit 1
}

Write-Output "Installer profile enumeration check passed."
exit 0

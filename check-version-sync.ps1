#!/usr/bin/env pwsh
# ─────────────────────────────────────────────────────────────
# check-version-sync.ps1 — Five-version source sync checker
# Compare cad-plugin/{2007,2010,2013,2015,2025}/PatentMarker/
# source files (.cs / .csproj / packages.config) and report diffs.
#
# Usage:  .\check-version-sync.ps1 [-Verbose]
#         .\check-version-sync.ps1 -Group Commands
# ─────────────────────────────────────────────────────────────
param(
    [string]$Group = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$pluginRoot = Join-Path $root "cad-plugin"
$sharedRoot = Join-Path $pluginRoot "Shared"

$versions = @("2007", "2010", "2013", "2015", "2025")

# Excluded directories (build output & SDK DLLs)
$excludeDirs = @("bin", "obj", "lib")

# ── Collect source files per version ────────────────────────
function Get-SourceFiles {
    param([string]$Version)
    $srcRoot = Join-Path $pluginRoot "$Version\PatentMarker"
    if (-not (Test-Path $srcRoot)) { return @{} }

    $files = Get-ChildItem -Path $srcRoot -Recurse -File |
        Where-Object {
            $rel = $_.FullName.Substring($srcRoot.Length + 1)
            $topDir = $rel.Split('\')[0]
            ($excludeDirs -notcontains $topDir) -and
            ($_.Extension -match '^\.(cs|csproj|config)$')
        }

    $map = @{}
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($srcRoot.Length + 1)
        if ($Group -and -not $rel.StartsWith("$Group\")) { continue }
        $map[$rel] = $f.FullName
    }
    return $map
}

# ── Hash cache ──────────────────────────────────────────────
$hashCache = @{}
function Get-FileHashCached {
    param([string]$Path)
    if (-not $hashCache.ContainsKey($Path)) {
        $hashCache[$Path] = (Get-FileHash $Path -Algorithm MD5).Hash
    }
    return $hashCache[$Path]
}

# ── Main ────────────────────────────────────────────────────
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " PatentCAD-Annotator Sync Check" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Collect file maps for all versions
$allMaps = @{}
$allRelPaths = [System.Collections.Generic.HashSet[string]]::new()
foreach ($v in $versions) {
    $allMaps[$v] = Get-SourceFiles $v
    foreach ($key in $allMaps[$v].Keys) {
        [void]$allRelPaths.Add($key)
    }
}

# 2. Classify
$missingFiles   = @()
$diffFiles      = @()
$syncedFiles    = @()
$versionOnly    = @()

$sortedPaths = $allRelPaths | Sort-Object

# These files are deliberately C# 2-compatible and are compiled from one
# canonical source tree. They must not be copied back into each edition.
$criticalSyncFailures = 0
$sharedSourceFiles = @(
    "IO\NumberIdentity.cs",
    "IO\PatSettings.cs",
    "IO\DictDiff.cs",
    "IO\DictConflict.cs",
    "IO\MarkingTextParser.cs",
    "I18n\Language.cs",
    "Cad\PatEntityHelper.cs",
    "Palette\DictPaletteCadService.cs",
    "Palette\DictPaletteWorkflow.cs",
    "Palette\DictPaletteSession.cs"
)
Write-Host "-- Canonical shared source layer --" -ForegroundColor Cyan
function Get-SharedSourcePath {
    param([string]$RelativePath)
    return Join-Path $sharedRoot $RelativePath
}

foreach ($shared in $sharedSourceFiles) {
    $sharedPath = Get-SharedSourcePath $shared
    if (-not (Test-Path -LiteralPath $sharedPath)) {
        Write-Host "  [FAIL] missing canonical source: cad-plugin/Shared/$shared" -ForegroundColor Red
        $criticalSyncFailures++
        continue
    }

    $missingRefs = @()
    $relativeInclude = "..\..\Shared\$shared"
    foreach ($v in $versions) {
        $csproj = Join-Path $pluginRoot "$v\PatentMarker\PatentMarker.csproj"
        if (-not (Test-Path -LiteralPath $csproj)) {
            $missingRefs += "$v (csproj missing)"
            continue
        }
        $projectText = Get-Content -LiteralPath $csproj -Raw
        if ($projectText -notmatch [regex]::Escape($relativeInclude)) {
            $missingRefs += $v
        }
        $localPath = Join-Path $pluginRoot "$v\PatentMarker\$shared"
        if (Test-Path -LiteralPath $localPath) {
            Write-Host "  [FAIL] duplicate local source: $v/$shared (use cad-plugin/Shared/$shared)" -ForegroundColor Red
            $criticalSyncFailures++
        }
    }

    if ($missingRefs.Count -gt 0) {
        Write-Host "  [FAIL] $shared is not linked by: $($missingRefs -join ', ')" -ForegroundColor Red
        $criticalSyncFailures++
    } else {
        Write-Host "  [OK]   $shared canonical and linked by all five editions" -ForegroundColor Green
    }
}
Write-Host ""

# Backward-compatible alias used by older output consumers.
$criticalSharedFiles = @("IO\NumberIdentity.cs", "IO\PatSettings.cs")
Write-Host "-- Critical shared contract aliases --" -ForegroundColor Cyan
foreach ($critical in $criticalSharedFiles) {
    if (Test-Path -LiteralPath (Get-SharedSourcePath $critical)) {
        Write-Host "  [OK]   $critical is covered by the canonical shared layer" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] $critical is missing from the canonical shared layer" -ForegroundColor Red
        $criticalSyncFailures++
    }
}
Write-Host ""

foreach ($rel in $sortedPaths) {
    $present = @()
    $absent  = @()
    $hashes  = @{}

    foreach ($v in $versions) {
        if ($allMaps[$v].ContainsKey($rel)) {
            $present += $v
            $h = Get-FileHashCached $allMaps[$v][$rel]
            $hashes[$v] = $h
        } else {
            $absent += $v
        }
    }

    # Only in one version
    if ($present.Count -eq 1) {
        $versionOnly += [PSCustomObject]@{
            File    = $rel
            Version = $present[0]
        }
        continue
    }

    # Missing in some versions
    if ($absent.Count -gt 0) {
        $missingFiles += [PSCustomObject]@{
            File       = $rel
            Present    = $present
            Missing    = $absent
        }
    }

    # Check content diff
    $uniqueHashes = $hashes.Values | Select-Object -Unique
    if ($uniqueHashes.Count -gt 1) {
        $diffFiles += [PSCustomObject]@{
            File   = $rel
            Hashes = $hashes
        }
    } else {
        $syncedFiles += $rel
    }
}

# ── Report ──────────────────────────────────────────────────

$total = $sortedPaths.Count
Write-Host "Total source files: $total" -ForegroundColor White
Write-Host "  Synced: $($syncedFiles.Count)" -ForegroundColor Green
Write-Host "  Content diff: $($diffFiles.Count)" -ForegroundColor Yellow
Write-Host "  Partial missing: $($missingFiles.Count)" -ForegroundColor Red
Write-Host "  Single-version only: $($versionOnly.Count)" -ForegroundColor Magenta
Write-Host ""

# ── Single-version only files ───────────────────────────────
if ($versionOnly.Count -gt 0) {
    Write-Host "-- Single-version only (may need sync to others) --" -ForegroundColor Magenta
    foreach ($item in $versionOnly) {
        Write-Host "  [$($item.Version)] $($item.File)" -ForegroundColor Magenta
    }
    Write-Host ""
}

# ── Partial missing ─────────────────────────────────────────
if ($missingFiles.Count -gt 0) {
    Write-Host "-- Partial missing --" -ForegroundColor Red
    foreach ($item in $missingFiles) {
        $missingStr = ($item.Missing -join ", ")
        $presentStr = ($item.Present -join ", ")
        Write-Host "  $($item.File)" -ForegroundColor Red
        Write-Host "    Present in: $presentStr" -ForegroundColor DarkGray
        Write-Host "    Missing in: $missingStr" -ForegroundColor Red
    }
    Write-Host ""
}

# ── Content diff ────────────────────────────────────────────
if ($diffFiles.Count -gt 0) {
    Write-Host "-- Content diff (same name, different content) --" -ForegroundColor Yellow

    $grouped = @{}
    foreach ($d in $diffFiles) {
        $dir = Split-Path -Parent $d.File
        if (-not $grouped.ContainsKey($dir)) { $grouped[$dir] = @() }
        $grouped[$dir] += $d
    }

    foreach ($dir in ($grouped.Keys | Sort-Object)) {
        Write-Host ""
        Write-Host "  [$dir]" -ForegroundColor DarkCyan
        foreach ($d in $grouped[$dir]) {
            $fname = Split-Path -Leaf $d.File
            $refHash = $d.Hashes[$versions[0]]
            $changed = @()
            foreach ($v in $versions) {
                if ($d.Hashes.ContainsKey($v) -and $d.Hashes[$v] -ne $refHash) {
                    $changed += $v
                }
            }
            if ($changed.Count -eq 0) {
                $presentVersions = $d.Hashes.Keys | Sort-Object
                $changed = $presentVersions
            }
            Write-Host "    $fname  (diff versions: $($changed -join ', '))" -ForegroundColor Yellow
        }
    }
    Write-Host ""
}

# ── Synced files ────────────────────────────────────────────
if ($syncedFiles.Count -le 20) {
    Write-Host "-- Synced files (identical across present versions) --" -ForegroundColor Green
    foreach ($f in $syncedFiles) {
        Write-Host "  $f" -ForegroundColor DarkGray
    }
    Write-Host ""
}

# ── Action summary ──────────────────────────────────────────
$needAction = $versionOnly.Count + $missingFiles.Count + $diffFiles.Count
if ($needAction -eq 0) {
    Write-Host "[OK] All five-version sources are fully synced." -ForegroundColor Green
} else {
    Write-Host "========================================" -ForegroundColor Yellow
    Write-Host " Sync Action Summary" -ForegroundColor Yellow
    Write-Host "========================================" -ForegroundColor Yellow
    Write-Host ""

    $actionItems = @()

    foreach ($item in $versionOnly) {
        $others = $versions | Where-Object { $_ -ne $item.Version }
        $actionItems += [PSCustomObject]@{
            Priority = "HIGH"
            File     = $item.File
            Action   = "Only in $($item.Version), consider sync to $($others -join ', ')"
        }
    }
    foreach ($item in $missingFiles) {
        $actionItems += [PSCustomObject]@{
            Priority = "HIGH"
            File     = $item.File
            Action   = "Missing in $($item.Missing -join ', '), need add or confirm not needed"
        }
    }
    foreach ($item in $diffFiles) {
        $refHash = $null; $refVer = $null
        foreach ($v in $versions) {
            if ($item.Hashes.ContainsKey($v)) { $refHash = $item.Hashes[$v]; $refVer = $v; break }
        }
        $changed = @()
        foreach ($v in $versions) {
            if ($item.Hashes.ContainsKey($v) -and $item.Hashes[$v] -ne $refHash) {
                $changed += $v
            }
        }
        if ($changed.Count -eq 0) {
            $changed = ($item.Hashes.Keys | Sort-Object)
        }
        $actionItems += [PSCustomObject]@{
            Priority = "MEDIUM"
            File     = $item.File
            Action   = "Based on $refVer, versions $($changed -join ', ') differ, confirm sync"
        }
    }

    $i = 1
    foreach ($item in $actionItems) {
        $color = switch ($item.Priority) { "HIGH" { "Red" } "MEDIUM" { "Yellow" } default { "Gray" } }
        Write-Host "  $i. [$($item.Priority)] $($item.File)" -ForegroundColor $color
        Write-Host "     $($item.Action)" -ForegroundColor DarkGray
        $i++
    }
    Write-Host ""
    Write-Host "Total $needAction items need attention." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Check complete." -ForegroundColor Cyan
if ($criticalSyncFailures -gt 0) {
    exit 1
}

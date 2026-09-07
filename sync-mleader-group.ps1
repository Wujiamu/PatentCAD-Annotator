#!/usr/bin/env pwsh
# sync-mleader-group.ps1 - synchronize the byte-identical MLeader command
# group across the 2010/2013/2015/2025 edition forks.
#
# The 2007 edition is deliberately excluded because its SDK has no MLeader
# type and it compiles the Shared Leader+MText commands instead.
#
# Usage:
#   .\sync-mleader-group.ps1                 # use 2010 as the source
#   .\sync-mleader-group.ps1 -SourceVersion 2025
#   .\sync-mleader-group.ps1 -Check           # report drift without writes
param(
    [ValidateSet("2010", "2013", "2015", "2025")]
    [string]$SourceVersion = "2010",
    [switch]$Check
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$pluginRoot = Join-Path $root "cad-plugin"
$editions = @("2010", "2013", "2015", "2025")
$files = @(
    "Commands\PatMarkCommand.cs",
    "Commands\PatMLeaderCreator.cs",
    "Commands\PatMLeaderSetCommand.cs",
    "Commands\PatMLeaderVerifyCommand.cs",
    "Commands\PatCheckCommand.cs",
    "Commands\PatAlignCommand.cs",
    "Commands\PatSelectAllCommand.cs"
)

$drift = 0
foreach ($relativePath in $files) {
    $sourcePath = Join-Path $pluginRoot "$SourceVersion\PatentMarker\$relativePath"
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Missing source MLeader file: $sourcePath"
    }
    $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash

    foreach ($version in $editions) {
        if ($version -eq $SourceVersion) { continue }
        $targetPath = Join-Path $pluginRoot "$version\PatentMarker\$relativePath"
        $needsSync = -not (Test-Path -LiteralPath $targetPath)
        if (-not $needsSync) {
            $targetHash = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
            $needsSync = $targetHash -ne $sourceHash
        }
        if (-not $needsSync) {
            Write-Host "[OK]   $version/$relativePath" -ForegroundColor Green
            continue
        }

        $drift++
        if ($Check) {
            Write-Host "[DRIFT] $version/$relativePath <- $SourceVersion/$relativePath" -ForegroundColor Yellow
        } else {
            $targetDir = Split-Path -Parent $targetPath
            if (-not (Test-Path -LiteralPath $targetDir)) {
                New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
            }
            Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
            Write-Host "[SYNC] $version/$relativePath <- $SourceVersion/$relativePath" -ForegroundColor Cyan
        }
    }
}

if ($Check -and $drift -gt 0) {
    Write-Host "MLeader fork drift detected: $drift file(s)." -ForegroundColor Yellow
    exit 1
}

if ($Check) {
    Write-Host "MLeader fork check passed." -ForegroundColor Green
} else {
    Write-Host "MLeader fork synchronized from $SourceVersion." -ForegroundColor Green
}

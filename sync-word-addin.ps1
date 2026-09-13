#!/usr/bin/env pwsh
# Synchronize the canonical Word Startup add-in assets into all deployments.
# Usage: .\sync-word-addin.ps1
#        .\sync-word-addin.ps1 -Check

param(
    [switch]$Check
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$canonicalDir = Join-Path $root "word-addin"
$versions = @("2007", "2010", "2013", "2015", "2025")
$assets = @("PatentMarker.dotm", "install-vba.vbs", "uninstall-vba.vbs")
$driftCount = 0

foreach ($asset in $assets) {
    $source = Join-Path $canonicalDir $asset
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Missing canonical Word add-in asset: $source"
    }
    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash

    foreach ($version in $versions) {
        $target = Join-Path $root "PatentMarker-$version-deploy\$asset"
        $matches = (Test-Path -LiteralPath $target) -and `
            ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $sourceHash)
        if ($matches) { continue }

        if ($Check) {
            Write-Host "[DRIFT] $version/$asset differs from word-addin/$asset" -ForegroundColor Yellow
            $driftCount++
        } else {
            Copy-Item -LiteralPath $source -Destination $target -Force
            Write-Host "[SYNC] $version/$asset <- word-addin/$asset" -ForegroundColor Green
        }
    }
}

if ($Check) {
    if ($driftCount -gt 0) {
        Write-Host "Word add-in sync check failed: $driftCount drift item(s)." -ForegroundColor Red
        exit 1
    }
    Write-Host "Word add-in sync check passed (no writes)." -ForegroundColor Green
} else {
    Write-Host "Word add-in sync complete: canonical assets pushed to all five deploys." -ForegroundColor Cyan
}

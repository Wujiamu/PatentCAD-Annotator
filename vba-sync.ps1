#!/usr/bin/env pwsh
# vba-sync.ps1 - push the canonical VBA modules (vba/) into all five
# deployment packages.
#
# The root vba/ directory is the single source of truth for Word-side VBA
# modules. package.ps1 refuses to build when a deployment copy drifts from
# the canonical source; run this script after editing vba/ to propagate.
#
# Usage: .\vba-sync.ps1          (push canonical -> all deploys)
#        .\vba-sync.ps1 -Check   (verify only, no writes)
param(
    [switch]$Check
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$canonical = Join-Path $root "vba"
$versions = @("2007", "2010", "2013", "2015", "2025")
$vbaFiles = @("Patterns.bas", "DictModel.bas", "JsonWriter.bas", "PatentExtractor.bas", "AutoExport.bas", "clsSaveHook.cls")

foreach ($file in $vbaFiles) {
    $src = Join-Path $canonical $file
    if (-not (Test-Path -LiteralPath $src)) { throw "Missing canonical VBA module: $src" }
    foreach ($ver in $versions) {
        $dst = Join-Path $root "PatentMarker-$ver-deploy\vba\$file"
        if (-not (Test-Path -LiteralPath $dst)) { throw "Missing deployment VBA module: $dst" }
        $srcHash = (Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash
        $dstHash = (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash
        if ($srcHash -ne $dstHash) {
            if ($Check) {
                Write-Host "[DRIFT] $ver/vba/$file differs from canonical vba/$file" -ForegroundColor Yellow
            } else {
                Copy-Item -LiteralPath $src -Destination $dst -Force
                Write-Host "[SYNC] $ver/vba/$file <- vba/$file" -ForegroundColor Green
            }
        }
    }
}

if ($Check) {
    Write-Host "VBA sync check complete (no writes)." -ForegroundColor Cyan
} else {
    Write-Host "VBA sync complete: canonical vba/ pushed to all five deploys." -ForegroundColor Cyan
}

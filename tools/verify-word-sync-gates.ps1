param(
    [switch]$KeepArtifacts
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = Join-Path $tempBase ("PatentMarker-Sync-Gates-" + [guid]::NewGuid().ToString("N"))
$versions = @("2007", "2010", "2013", "2015", "2025")
$vbaFiles = @(
    "Patterns.bas", "DictModel.bas", "JsonWriter.bas", "PatentExtractor.bas",
    "AutoExport.bas", "PatentMarkerBootstrap.bas", "clsSaveHook.cls",
    "PatentDictPanel.frm", "PatentDictPanel.frx"
)
$wordAssets = @("PatentMarker.dotm", "install-vba.vbs", "uninstall-vba.vbs")
$engine = (Get-Process -Id $PID).Path
$dotmVerifier = Join-Path $repoRoot "tools\verify-dotm-package.ps1"
$passed = $false

function Invoke-Check([string]$ScriptPath) {
    $output = @(& $engine -NoLogo -NoProfile -File $ScriptPath -Check 2>&1 | ForEach-Object { [string]$_ })
    [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = $output
    }
}

function Assert-Exit([object]$Result, [int]$Expected, [string]$Label) {
    if ($Result.ExitCode -ne $Expected) {
        throw "$Label returned $($Result.ExitCode), expected $Expected.`n$($Result.Output -join [Environment]::NewLine)"
    }
}

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "vba-sync.ps1") -Destination $tempRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot "sync-word-addin.ps1") -Destination $tempRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot "vba") -Destination $tempRoot -Recurse
    $canonicalWordDir = Join-Path $tempRoot "word-addin"
    New-Item -ItemType Directory -Path $canonicalWordDir -Force | Out-Null
    foreach ($asset in $wordAssets) {
        Copy-Item -LiteralPath (Join-Path $repoRoot "word-addin\$asset") -Destination $canonicalWordDir
    }

    foreach ($version in $versions) {
        $deployDir = Join-Path $tempRoot "PatentMarker-$version-deploy"
        $deployVbaDir = Join-Path $deployDir "vba"
        New-Item -ItemType Directory -Path $deployVbaDir -Force | Out-Null
        foreach ($file in $vbaFiles) {
            Copy-Item -LiteralPath (Join-Path $repoRoot "vba\$file") -Destination $deployVbaDir
        }
        foreach ($asset in $wordAssets) {
            Copy-Item -LiteralPath (Join-Path $repoRoot "word-addin\$asset") -Destination $deployDir
        }
    }

    $vbaScript = Join-Path $tempRoot "vba-sync.ps1"
    $wordScript = Join-Path $tempRoot "sync-word-addin.ps1"
    Assert-Exit (Invoke-Check $vbaScript) 0 "clean VBA sync check"
    Assert-Exit (Invoke-Check $wordScript) 0 "clean Word-asset sync check"

    $vbaDrift = Join-Path $tempRoot "PatentMarker-2013-deploy\vba\AutoExport.bas"
    [IO.File]::AppendAllText($vbaDrift, "' deliberate sync-gate drift", [Text.Encoding]::ASCII)
    $vbaDriftHash = (Get-FileHash -LiteralPath $vbaDrift -Algorithm SHA256).Hash
    $vbaResult = Invoke-Check $vbaScript
    if ($vbaResult.ExitCode -eq 0) {
        throw "VBA -Check accepted deliberate deployment drift.`n$($vbaResult.Output -join [Environment]::NewLine)"
    }
    if ((Get-FileHash -LiteralPath $vbaDrift -Algorithm SHA256).Hash -ne $vbaDriftHash) {
        throw "VBA -Check modified the drifted file"
    }

    Copy-Item -LiteralPath (Join-Path $repoRoot "vba\AutoExport.bas") -Destination $vbaDrift -Force
    Assert-Exit (Invoke-Check $vbaScript) 0 "restored VBA sync check"

    $assetDrift = Join-Path $tempRoot "PatentMarker-2015-deploy\install-vba.vbs"
    [IO.File]::AppendAllText($assetDrift, "' deliberate sync-gate drift", [Text.Encoding]::ASCII)
    $assetDriftHash = (Get-FileHash -LiteralPath $assetDrift -Algorithm SHA256).Hash
    $assetResult = Invoke-Check $wordScript
    if ($assetResult.ExitCode -eq 0) {
        throw "Word-asset -Check accepted deliberate deployment drift.`n$($assetResult.Output -join [Environment]::NewLine)"
    }
    if ((Get-FileHash -LiteralPath $assetDrift -Algorithm SHA256).Hash -ne $assetDriftHash) {
        throw "Word-asset -Check modified the drifted file"
    }

    $dotmDrift = Join-Path $tempRoot "missing-vba-relationship.dotm"
    Copy-Item -LiteralPath (Join-Path $repoRoot "word-addin\PatentMarker.dotm") -Destination $dotmDrift
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($dotmDrift, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $relationshipEntry = $archive.GetEntry("word/_rels/vbaProject.bin.rels")
        if ($null -eq $relationshipEntry) { throw "Canonical DOTM lacks the relationship selected for fault injection" }
        $relationshipEntry.Delete()
    } finally {
        $archive.Dispose()
    }
    $dotmDriftHash = (Get-FileHash -LiteralPath $dotmDrift -Algorithm SHA256).Hash
    $dotmOutput = @(& $engine -NoLogo -NoProfile -File $dotmVerifier -Path $dotmDrift 2>&1 | ForEach-Object { [string]$_ })
    $dotmExitCode = $LASTEXITCODE
    if ($dotmExitCode -eq 0) {
        throw "DOTM verifier accepted a package with no VBA supplemental-data relationship.`n$($dotmOutput -join [Environment]::NewLine)"
    }
    if ((Get-FileHash -LiteralPath $dotmDrift -Algorithm SHA256).Hash -ne $dotmDriftHash) {
        throw "DOTM verifier modified the fault-injected package"
    }

    $passed = $true
    Write-Output "PASS|WORD_SYNC_GATE_FAILURE_INJECTION|vba_drift_rejected=true|word_asset_drift_rejected=true|dotm_metadata_drift_rejected=true|check_writes=0"
} finally {
    if ($passed -and -not $KeepArtifacts) {
        $resolved = [IO.Path]::GetFullPath($tempRoot)
        $safePrefix = Join-Path $tempBase "PatentMarker-Sync-Gates-"
        if (-not $resolved.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean unexpected path: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    } else {
        Write-Output "ARTIFACTS|$tempRoot"
    }
}

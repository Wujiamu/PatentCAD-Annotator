param([string]$ProjectRoot, [string]$OutputPath, [switch]$UseSealedCatchBodies)
# G18 replay of R-029 with the same business scenario and the same pass/fail
# standard as ../uninstall-recovery-probe.ps1 (the R-029 red record):
#
#   After a failed uninstall, the original add-in registration may only be
#   re-enabled when the complete product file set was restored and verified.
#
# Two modes, one standard:
#   -UseSealedCatchBodies : replays the archived pre-fix catch bodies that the
#                           R-029 review sealed (../uninstall-ppt-catch.ps1,
#                           ../uninstall-visio-catch.ps1). This is the red run
#                           and proves the probe actually catches the defect.
#   (default)             : replays the catch bodies read from the live
#                           production scripts. This is the green run.
#
# What changed versus the R-029 probe: the fixed catch bodies additionally call
# Assert-PatentMarkerOwnedDirectory (complete product set + hashes) and
# Assert-PatentMarkerRegistrySnapshotRestored (read-back confirmation), so those
# two controlled boundaries are stubbed here as well. The scenario, the injected
# failure and the expected_registry_enabled rule are unchanged.
#
# The catch body is read from real production source (or the sealed archive) and
# executed verbatim; nothing here re-implements the rollback order.
$ErrorActionPreference = 'Stop'
$reviewRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    # Deliberately NOT the archived evidence file: re-running the probe must not
    # overwrite red-*/green-uninstall-catch-replay.txt, which carry run headers.
    $OutputPath = Join-Path $reviewRoot $(if ($UseSealedCatchBodies) { 'probe-output-red.txt' } else { 'probe-output-green.txt' })
}

function Read-CatchBody([string]$source) {
    $m = [regex]::Match($source, '(?m)^catch\s*\{(?=\r?\n\s+\$failure)')
    if (-not $m.Success) { throw 'Top-level recovery catch missing' }
    $b = $source.IndexOf('{', $m.Index)
    $d = 0
    for ($n = $b; $n -lt $source.Length; $n++) {
        if ($source[$n] -eq '{') { $d++ }
        if ($source[$n] -eq '}') { $d--; if (-not $d) { return $source.Substring($b + 1, $n - $b - 1) } }
    }
    throw 'Unclosed recovery catch'
}

# --- controlled boundaries -------------------------------------------------
function Remove-PatentMarkerRegistryTree { param($BaseKey, $Path); $state.Trace.Add('registry.remove'); $state.Enabled = $false }
function Remove-VisioRegistryTree { param($BaseKey, $Path); $state.Trace.Add('registry.remove'); $state.Enabled = $false }
function Restore-PatentMarkerRegistrySnapshot {
    param($BaseKey, $Snapshot)
    $state.Trace.Add('registry.restore')
    if ($state.FailRegistryRestore) { throw [IO.IOException]::new('injected registry restore failure') }
    $state.Enabled = $true
}
function Restore-VisioRegistrySnapshot {
    param($BaseKey, $Snapshot)
    $state.Trace.Add('registry.restore')
    if ($state.FailRegistryRestore) { throw [IO.IOException]::new('injected registry restore failure') }
    $state.Enabled = $true
}
function Assert-PatentMarkerRegistrySnapshotRestored {
    param($BaseKey, $Path, $Snapshot)
    $state.Trace.Add('registry.verify')
    if (-not $state.Enabled) { throw 'registry snapshot not restored' }
}
function Assert-VisioRegistrySnapshotRestored {
    param($BaseKey, $Path, $Snapshot)
    $state.Trace.Add('registry.verify')
    if (-not $state.Enabled) { throw 'registry snapshot not restored' }
}
# Completeness check: the product directory must exist and carry the fixture
# marker file. Models "complete product set + hashes" of the real assertion.
function Assert-PatentMarkerOwnedDirectory {
    param($InstallDirectory)
    $state.Trace.Add('files.verify')
    if (-not (Test-Path -LiteralPath (Join-Path $InstallDirectory 'owned-fixture.txt') -PathType Leaf)) {
        throw "产品目录不完整：$InstallDirectory"
    }
    return [pscustomobject]@{ ProductId = 'fixture' }
}
function Assert-VisioOwnedDirectory {
    param($InstallDirectory)
    $state.Trace.Add('files.verify')
    if (-not (Test-Path -LiteralPath (Join-Path $InstallDirectory 'owned-fixture.txt') -PathType Leaf)) {
        throw "产品目录不完整：$InstallDirectory"
    }
    return [pscustomobject]@{ ProductId = 'fixture' }
}
function Restore-PatentMarkerDirectoryFromCopy {
    param($RecoveryCopy, $InstallDirectory, $ExpectedParent)
    $state.Trace.Add('files.restore')
    if ($state.FailFiles) { throw [IO.IOException]::new('injected recovery I/O failure') }
    if (-not (Test-Path -LiteralPath $RecoveryCopy -PathType Container)) { return $false }
    Copy-Item -LiteralPath $RecoveryCopy -Destination $InstallDirectory -Recurse -Force
    return $true
}
function Restore-VisioDirectoryFromCopy {
    param($RecoveryCopy, $InstallDirectory, $ExpectedParent)
    $state.Trace.Add('files.restore')
    if ($state.FailFiles) { throw [IO.IOException]::new('injected recovery I/O failure') }
    if (-not (Test-Path -LiteralPath $RecoveryCopy -PathType Container)) { return $false }
    Copy-Item -LiteralPath $RecoveryCopy -Destination $InstallDirectory -Recurse -Force
    return $true
}

$lines = New-Object Collections.Generic.List[string]
$failed = 0

foreach ($hostName in @('ppt', 'visio')) {
    $relative = if ($hostName -eq 'ppt') { 'office-com-addin/uninstall-office-addin.ps1' } else { 'office-com-addin/uninstall-visio-addin.ps1' }
    $body = if ($UseSealedCatchBodies) {
        # Archived pre-fix catch body sealed by the R-029 review.
        [IO.File]::ReadAllText((Join-Path (Split-Path -Parent $reviewRoot) ('uninstall-' + $hostName + '-catch.ps1')))
    }
    else {
        Read-CatchBody ([IO.File]::ReadAllText((Join-Path $ProjectRoot $relative)))
    }
    $mode = if ($UseSealedCatchBodies) { 'sealed_prefix_source' } else { 'live_production_source' }
    # Archive the exact body that was executed, named by mode so the red run's
    # pre-fix body is never mistaken for the fixed one.
    $bodyFile = if ($UseSealedCatchBodies) { 'executed-' + $hostName + '-catch.prefix.txt' } else { 'executed-' + $hostName + '-catch.fixed.txt' }
    [IO.File]::WriteAllText((Join-Path $reviewRoot $bodyFile), $body, [Text.UTF8Encoding]::new($false))

    foreach ($scenario in @('files_restore_ok', 'files_restore_fails', 'registry_restore_fails')) {
        $failFiles = $scenario -eq 'files_restore_fails'
        $failRegistry = $scenario -eq 'registry_restore_fails'

        $fixture = [IO.Path]::GetFullPath((Join-Path $reviewRoot ('g18-' + $hostName + '-' + [guid]::NewGuid().ToString('N'))))
        [IO.Directory]::CreateDirectory($fixture) | Out-Null
        $parent = $fixture
        $installFull = Join-Path $fixture 'product'
        $backupDirectory = Join-Path $fixture 'staged'
        $recoveryDirectory = Join-Path $fixture 'complete-recovery'
        # The recovery copy is the complete, verified material.
        [IO.Directory]::CreateDirectory($recoveryDirectory) | Out-Null
        [IO.File]::WriteAllText((Join-Path $recoveryDirectory 'owned-fixture.txt'), 'synthetic')
        # Entering the catch the way the real uninstall does: the product
        # directory has already been moved to the staged location.
        [IO.Directory]::CreateDirectory($backupDirectory) | Out-Null
        [IO.File]::WriteAllText((Join-Path $backupDirectory 'owned-fixture.txt'), 'synthetic')

        $registryPaths = @('ProductOnly')
        $registrySnapshots = @('snapshot')
        $snapshots = $registrySnapshots
        # The Visio scripts name these two variables differently; bind both so
        # either catch body sees the same fixture.
        $recovery = $recoveryDirectory
        $backup = $backupDirectory
        $baseKey = $null
        $state = [pscustomobject]@{
            Enabled = $false; FailFiles = $failFiles; FailRegistryRestore = $failRegistry
            Trace = [Collections.Generic.List[string]]::new()
        }
        try { throw 'injected uninstall deletion failure' } catch { try { & ([scriptblock]::Create($body)) } catch { } }

        # Same standard as the R-029 record: the registration may be live only
        # when the files came back complete AND the original registration was
        # restored. A failed registration restore must stay disabled too.
        $expectedEnabled = (-not $failFiles) -and (-not $failRegistry)
        $ok = ($state.Enabled -eq $expectedEnabled)
        $notes = @()
        if (-not $ok) { $notes += 'registry_state_mismatch' }

        # Materials must survive whenever the rollback is not fully confirmed,
        # and must never be deleted as if this had been a normal uninstall.
        $recoveryKept = Test-Path -LiteralPath $recoveryDirectory -PathType Container
        if (-not $expectedEnabled -and -not $recoveryKept) {
            $ok = $false; $notes += 'recovery_material_lost'
        }

        # Reported separately, not folded into the pass/fail standard: the
        # order is the actual R-029 defect, and the trace column makes it
        # visible either way (old bodies restore the registration first).
        $fileStage = @($state.Trace | Select-String -Pattern '^files\.' -SimpleMatch:$false)
        $firstFileIndex = -1
        for ($i = 0; $i -lt $state.Trace.Count; $i++) {
            if ($state.Trace[$i] -like 'files.*') { $firstFileIndex = $i; break }
        }
        $restoreIndex = $state.Trace.IndexOf('registry.restore')
        $orderOk = ($restoreIndex -lt 0) -or ($firstFileIndex -ge 0 -and $firstFileIndex -lt $restoreIndex)
        if (-not $ok) { $failed++ }
        $mark = if ($ok) { 'PASS' } else { 'FAIL' }
        $lines.Add("$mark|uninstall_${hostName}_recovery|source=$mode|scenario=$scenario|expected_registry_enabled=$expectedEnabled|actual=$($state.Enabled)|order_ok=$orderOk|trace=$($state.Trace -join ',')|notes=$($notes -join ',')")
        if (Test-Path -LiteralPath $fixture -PathType Container) {
            if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($fixture)) -ne [IO.Path]::GetFullPath($reviewRoot)) { throw 'Fixture deletion escaped its root' }
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}

$lines.Add('SUMMARY|total=6|failed=' + $failed)
$lines | Set-Content -LiteralPath $OutputPath -Encoding utf8
$lines
if ($failed) { exit 1 }

# G08 / R-007 — Word installer rollback matrix.
#
# Runs one candidate install-vba.vbs against a throwaway Startup folder and drives
# the failure stages that the older tools/verify-vba-installer.ps1 never reached:
# the new add-in is already activated and the new ownership manifest is already
# created when the write, close, read-back or activation fails.
#
# Every case asserts business results, not just an exit code: the old add-in and
# old manifest must be back byte-for-byte with their attributes, non-product
# Startup objects must be untouched, and no staging residue may survive. A case
# whose expected failure does not happen is a FAIL.
#
# This script never reads or writes the real Word Startup folder.
#
#   pwsh -File .\tools\test-g08-rollback.ps1 -InstallerPath <install-vba.vbs>
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\test-g08-rollback.ps1 `
#       -InstallerPath <install-vba.vbs> -AddinPath <PatentMarker.dotm>

param(
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [Parameter(Mandatory = $true)][string]$AddinPath,
    # Restrict to a subset of case names; used to run the frozen baseline, which can
    # only express the earliest fault stages.
    [string]$Select,
    [switch]$KeepArtifacts
)

$ErrorActionPreference = "Stop"
$installerFull = (Resolve-Path -LiteralPath $InstallerPath).Path
$addinFull = (Resolve-Path -LiteralPath $AddinPath).Path

function Get-FileState([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    [pscustomobject]@{
        Hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
        Length = $item.Length
        Protected = ([int]$item.Attributes -band 7)
    }
}

function Get-TreeSnapshot([string]$Root, [string[]]$Exclude) {
    $snapshot = @{}
    foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -Force -File) {
        $relative = $file.FullName.Substring($Root.Length).TrimStart('\')
        $skip = $false
        foreach ($prefix in $Exclude) {
            if ($relative.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { $skip = $true; break }
        }
        if ($skip) { continue }
        $snapshot[$relative] = Get-FileState $file.FullName
    }
    $snapshot
}

function Compare-Snapshot($Expected, $Actual, [string]$Label) {
    $problems = @()
    foreach ($key in $Expected.Keys) {
        if (-not $Actual.ContainsKey($key)) { $problems += "$Label missing: $key"; continue }
        if ($Expected[$key].Hash -ne $Actual[$key].Hash) { $problems += "$Label bytes changed: $key" }
        if ($Expected[$key].Protected -ne $Actual[$key].Protected) { $problems += "$Label attributes changed: $key" }
    }
    foreach ($key in $Actual.Keys) {
        if (-not $Expected.ContainsKey($key)) { $problems += "$Label gained: $key" }
    }
    $problems
}

$productPrefixes = @(
    "PatentMarker.dotm",
    "PatentMarker.addin.install.txt"
)

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = Join-Path $tempBase ("PatentMarker-G08-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

$oldAddinBytes = [Text.Encoding]::ASCII.GetBytes("G08-PREVIOUS-ADDIN-" + [guid]::NewGuid().ToString("N"))
$oldManifestText = "product=PatentMarker`r`nfile=PatentMarker.dotm`r`nsize=0000`r`ninstalled_at=2026/10/30 09:00:00`r`n"

function New-Startup([string]$Name, [switch]$WithPrevious, [switch]$ProtectedPrevious) {
    $startup = Join-Path (Join-Path $tempRoot $Name) "Word Startup with spaces"
    New-Item -ItemType Directory -Path $startup -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $startup "Normal.dotm"), [byte[]](0..255))
    [IO.File]::WriteAllText((Join-Path $startup "UserMacros.dotm"), "G08-USER-MACRO-SENTINEL", [Text.Encoding]::UTF8)
    [IO.File]::WriteAllText((Join-Path $startup "ForeignAddin.dotm"), "G08-FOREIGN-ADDIN-SENTINEL", [Text.Encoding]::UTF8)
    $sub = Join-Path $startup "User Sub Folder"
    New-Item -ItemType Directory -Path $sub -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $sub "notes.txt"), "G08-NON-PRODUCT-SENTINEL", [Text.Encoding]::UTF8)
    $addin = Join-Path $startup "PatentMarker.dotm"
    $manifest = Join-Path $startup "PatentMarker.addin.install.txt"
    if ($WithPrevious) {
        [IO.File]::WriteAllBytes($addin, $oldAddinBytes)
        [IO.File]::WriteAllText($manifest, $oldManifestText, [Text.Encoding]::ASCII)
        if ($ProtectedPrevious) {
            (Get-Item -LiteralPath $addin -Force).Attributes = [IO.FileAttributes]::ReadOnly -bor [IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::System
            (Get-Item -LiteralPath $manifest -Force).Attributes = [IO.FileAttributes]::ReadOnly -bor [IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::System
        }
    }
    $startup
}

function Invoke-Installer([string]$Startup, [string]$LogPath, [string]$InjectFault, [string]$SecondaryFault) {
    $arguments = @("/NoPrompt", "/StartupPath:$Startup", "/LogPath:$LogPath", "/SourcePath:$addinFull")
    if ($InjectFault) { $arguments += "/InjectFault:$InjectFault" }
    if ($SecondaryFault) { $arguments += "/InjectFault2:$SecondaryFault" }
    $lines = @(& cscript.exe //nologo $installerFull @arguments 2>&1 | ForEach-Object { [string]$_ })
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $lines }
}

function Test-Case($Case) {
    $problems = @()
    $startup = New-Startup -Name $Case.Name -WithPrevious:$Case.WithPrevious -ProtectedPrevious:$Case.ProtectedPrevious
    $addin = Join-Path $startup "PatentMarker.dotm"
    $manifest = Join-Path $startup "PatentMarker.addin.install.txt"
    $logPath = Join-Path (Split-Path -Parent $startup) "installer.log"

    $before = Get-TreeSnapshot $startup $productPrefixes
    $oldAddinState = if ($Case.WithPrevious) { Get-FileState $addin } else { $null }
    $oldManifestState = if ($Case.WithPrevious) { Get-FileState $manifest } else { $null }

    $lock = $null
    if ($Case.LockManifest -and (Test-Path -LiteralPath $manifest)) {
        $lock = [IO.File]::Open($manifest, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    }
    try {
        $run = Invoke-Installer -Startup $startup -LogPath $logPath -InjectFault $Case.InjectFault -SecondaryFault $Case.SecondaryFault
    } finally {
        if ($lock) { $lock.Dispose() }
    }

    $outputText = ($run.Output -join "`n")
    $expected = $Case.ExpectExit
    if ($expected -eq 0) {
        if ($run.ExitCode -ne 0) { $problems += "expected exit 0, got $($run.ExitCode)" }
    } else {
        if ($run.ExitCode -eq 0) { $problems += "expected a non-zero exit, got 0 (the failure did not happen)" }
    }

    # Non-product Startup objects must never change, in any case.
    $problems += Compare-Snapshot $before (Get-TreeSnapshot $startup $productPrefixes) "sentinel"

    $restoredClaim = $outputText -match 'previous add-in restored'
    $incompleteClaim = $outputText -match 'ROLLBACK_INCOMPLETE'

    if ($Case.ExpectRestored) {
        # A clean rollback may only claim a restore when a previous add-in actually
        # existed; a first-install rollback must say that it removed its own residue.
        if ($oldAddinState) {
            if (-not $restoredClaim) { $problems += "output does not claim a verified restore" }
        } else {
            if (-not ($outputText -match 'first installation rolled back')) {
                $problems += "output does not report a first-installation rollback"
            }
        }
        if ($incompleteClaim) { $problems += "output reports an incomplete rollback" }
        if ($oldAddinState) {
            $now = Get-FileState $addin
            if ($now.Hash -ne $oldAddinState.Hash) { $problems += "previous add-in bytes were not restored" }
            if ($now.Protected -ne $oldAddinState.Protected) { $problems += "previous add-in attributes were not restored" }
            $nowManifest = Get-FileState $manifest
            if ($nowManifest.Hash -ne $oldManifestState.Hash) { $problems += "previous ownership manifest bytes were not restored" }
            if ($nowManifest.Protected -ne $oldManifestState.Protected) { $problems += "previous ownership manifest attributes were not restored" }
        } else {
            if (Test-Path -LiteralPath $addin) { $problems += "first-install rollback left the product add-in behind" }
            if (Test-Path -LiteralPath $manifest) { $problems += "first-install rollback left the ownership manifest behind" }
        }
    }

    if ($Case.ExpectIncomplete) {
        if (-not $incompleteClaim) { $problems += "a failed restore was not reported as ROLLBACK_INCOMPLETE" }
        if ($restoredClaim) { $problems += "output claims a restore even though it failed" }
        if ($outputText -notmatch 'recovery=') { $problems += "output does not name the recovery file" }
    }

    if ($Case.ExpectSuccess) {
        $now = Get-FileState $addin
        if ($now.Hash -ne (Get-FileState $addinFull).Hash) { $problems += "installed add-in is not the candidate bytes" }
        if (-not (Test-Path -LiteralPath $manifest)) {
            $problems += "ownership manifest missing"
        } else {
            $manifestText = [IO.File]::ReadAllText($manifest)
            if ($manifestText -notmatch "product=PatentMarker") { $problems += "manifest lacks the product marker" }
            if ($manifestText -notmatch "size=$($now.Length)") { $problems += "manifest size does not match the installed add-in" }
        }
        if ($Case.WithPrevious) {
            $backups = @(Get-ChildItem -LiteralPath $startup -Filter "PatentMarker.dotm.pm-backup-*" -Force)
            if ($backups.Count -lt 1) {
                $problems += "previous add-in was not preserved as a backup"
            } elseif ((Get-FileState $backups[0].FullName).Hash -ne $oldAddinState.Hash) {
                $problems += "preserved previous add-in does not match the original bytes"
            }
            if ((Get-FileState $backups[0].FullName).Protected -ne $oldAddinState.Protected) {
                $problems += "preserved previous add-in lost its attributes"
            }
            $manifestBackups = @(Get-ChildItem -LiteralPath $startup -Filter "PatentMarker.addin.install.txt.pm-backup-*" -Force)
            if ($manifestBackups.Count -lt 1) { $problems += "previous ownership manifest was not preserved" }
        }
    }

    # No staging residue may survive a run, whatever the outcome. A rollback that
    # could not finish is the one case allowed to keep its .pm-old reference copy,
    # because that copy is the recovery file the installer reports to the user.
    $residuePatterns = @("*.pm-new-*")
    if (-not $Case.ExpectIncomplete) { $residuePatterns += "*.pm-old-*" }
    foreach ($pattern in $residuePatterns) {
        $left = @(Get-ChildItem -LiteralPath $startup -Filter $pattern -Force)
        if ($left.Count -gt 0) { $problems += "staging residue left behind: $(($left | ForEach-Object Name) -join ', ')" }
    }

    if ($Case.ExpectIncomplete) {
        # The recovery copy must still hold the real previous bytes.
        if ($oldAddinState) {
            $recovery = @(Get-ChildItem -LiteralPath $startup -Filter "PatentMarker.dotm.pm-old-*" -Force)
            if ($recovery.Count -ne 1) {
                $problems += "expected exactly one add-in recovery copy, found $($recovery.Count)"
            } elseif ((Get-FileState $recovery[0].FullName).Hash -ne $oldAddinState.Hash) {
                $problems += "add-in recovery copy does not hold the previous bytes"
            }
        }
    }

    if (-not $Case.ExpectSuccess -and $Case.WithPrevious -and -not $Case.ExpectIncomplete) {
        # A clean rollback must not leave a half-written product manifest around.
        $manifestText = [IO.File]::ReadAllText($manifest)
        if ($manifestText -notmatch 'installed_at=2026/10/30 09:00:00') {
            $problems += "product manifest is not the previous one"
        }
    }

    [pscustomobject]@{
        Name = $Case.Name
        Status = if ($problems.Count -eq 0) { "PASS" } else { "FAIL" }
        Problems = $problems
        ExitCode = $run.ExitCode
        Output = $run.Output
        LogPath = $logPath
        Startup = $startup
    }
}

$cases = @(
    @{ Name = "first-install"; WithPrevious = $false; ExpectExit = 0; ExpectSuccess = $true }
    @{ Name = "upgrade"; WithPrevious = $true; ExpectExit = 0; ExpectSuccess = $true }
    @{ Name = "upgrade-protected-attributes"; WithPrevious = $true; ProtectedPrevious = $true; ExpectExit = 0; ExpectSuccess = $true }
    @{ Name = "upgrade-locked-manifest"; WithPrevious = $true; LockManifest = $true; ExpectExit = 1; ExpectRestored = $true }
    @{ Name = "fail-addin-activate"; WithPrevious = $true; InjectFault = "addin-activate"; ExpectExit = 1; ExpectRestored = $true }
    @{ Name = "fail-manifest-write"; WithPrevious = $true; InjectFault = "manifest-write"; ExpectExit = 1; ExpectRestored = $true }
    @{ Name = "fail-manifest-close"; WithPrevious = $true; InjectFault = "manifest-close"; ExpectExit = 1; ExpectRestored = $true }
    @{ Name = "fail-manifest-content"; WithPrevious = $true; InjectFault = "manifest-content"; ExpectExit = 1; ExpectRestored = $true }
    @{ Name = "fail-manifest-activate"; WithPrevious = $true; InjectFault = "manifest-activate"; ExpectExit = 1; ExpectRestored = $true }
    @{ Name = "fail-manifest-write-protected"; WithPrevious = $true; ProtectedPrevious = $true; InjectFault = "manifest-write"; ExpectExit = 1; ExpectRestored = $true }
    @{ Name = "first-install-manifest-write"; WithPrevious = $false; InjectFault = "manifest-write"; ExpectExit = 1; ExpectRestored = $true }
    # The next four pair a primary fault with a rollback fault, so a restore that
    # cannot complete must be reported as such instead of as a clean recovery.
    @{ Name = "fail-addin-restore"; WithPrevious = $true; InjectFault = "manifest-write"; SecondaryFault = "addin-restore"; ExpectExit = 1; ExpectIncomplete = $true }
    @{ Name = "fail-manifest-restore"; WithPrevious = $true; InjectFault = "manifest-write"; SecondaryFault = "manifest-restore"; ExpectExit = 1; ExpectIncomplete = $true }
    @{ Name = "fail-addin-restore-corrupt"; WithPrevious = $true; InjectFault = "manifest-write"; SecondaryFault = "addin-restore-corrupt"; ExpectExit = 1; ExpectIncomplete = $true }
    @{ Name = "fail-manifest-restore-corrupt"; WithPrevious = $true; InjectFault = "manifest-write"; SecondaryFault = "manifest-restore-corrupt"; ExpectExit = 1; ExpectIncomplete = $true }
)

if ($Select) {
    $wanted = @($Select.Split(",") | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $cases = @($cases | Where-Object { $wanted -contains $_.Name })
    if ($cases.Count -eq 0) { throw "No case matched: $($wanted -join ', ')" }
}

$results = @()
foreach ($case in $cases) {
    $results += Test-Case $case
}

$failed = @($results | Where-Object { $_.Status -ne "PASS" })
Write-Output "INSTALLER|$installerFull"
Write-Output "ADDIN|$addinFull"
foreach ($result in $results) {
    Write-Output ("CASE|{0}|{1}|exit={2}" -f $result.Status, $result.Name, $result.ExitCode)
    if ($result.Status -ne "PASS") {
        foreach ($problem in $result.Problems) { Write-Output ("  - {0}" -f $problem) }
        foreach ($line in $result.Output) { Write-Output ("  > {0}" -f $line) }
    }
}
if ($failed.Count -eq 0) {
    Write-Output ("PASS|G08_INSTALLER_ROLLBACK|cases={0}|failed=0" -f $results.Count)
} else {
    Write-Output ("FAIL|G08_INSTALLER_ROLLBACK|cases={0}|failed={1}|{2}" -f $results.Count, $failed.Count, (($failed | ForEach-Object Name) -join ','))
}

if ($KeepArtifacts) {
    Write-Output "ARTIFACTS|$tempRoot"
} elseif ($failed.Count -eq 0) {
    $resolved = [IO.Path]::GetFullPath($tempRoot)
    $safePrefix = Join-Path $tempBase "PatentMarker-G08-"
    if ($resolved.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
} else {
    Write-Output "ARTIFACTS|$tempRoot"
}

if ($failed.Count -gt 0) { exit 1 }
exit 0

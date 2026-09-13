param(
    [string]$DeployDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "PatentMarker-2025-deploy"),
    [switch]$KeepArtifacts,
    [switch]$ExtendedMatrix
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$deployPath = (Resolve-Path -LiteralPath $DeployDir).Path
$installer = Join-Path $deployPath "install-vba.vbs"
$sourceAddin = Join-Path $deployPath "PatentMarker.dotm"
$environmentProbe = Join-Path $PSScriptRoot "query-word-environment.vbs"
$runtimeProbe = Join-Path $PSScriptRoot "verify-vba-installed-runtime.vbs"
$matrixProbe = Join-Path $PSScriptRoot "verify-vba-installed-matrix.vbs"
$requiredAssets = @($installer, $sourceAddin, $environmentProbe, $runtimeProbe)
if ($ExtendedMatrix) { $requiredAssets += $matrixProbe }
foreach ($required in $requiredAssets) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing installed-E2E asset: $required" }
}

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = Join-Path $tempBase ("PatentMarker-Installed-E2E-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
$installerLog = Join-Path $tempRoot "installer.log"
$testDocPath = Join-Path $tempRoot "fresh-word-save.docx"
$dictPath = Join-Path $tempRoot "fresh-word-save.dict.json"
$previousAddinCopy = Join-Path $tempRoot "preexisting-PatentMarker.dotm"
$previousManifestCopy = Join-Path $tempRoot "preexisting-manifest.txt"
$failedNewAddin = Join-Path $tempRoot "failed-new-PatentMarker.dotm"
$failedNewManifest = Join-Path $tempRoot "failed-new-manifest.txt"

$word = $null
$doc = $null
$startupPath = ""
$normalPath = ""
$targetAddin = ""
$manifestPath = ""
$normalHashBefore = ""
$unrelatedBefore = $null
$hadPreviousAddin = $false
$hadPreviousManifest = $false
$installStarted = $false
$passed = $false

function Get-HashValue([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Get-StableHash([string]$Path, [int]$Seconds = 20) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    $lastError = ""
    do {
        try {
            $first = Get-HashValue $Path
            Start-Sleep -Milliseconds 200
            $second = Get-HashValue $Path
            if ($first -eq $second) { return $first }
            $lastError = "hash changed between consecutive reads"
        } catch {
            $lastError = $_.Exception.Message
        }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "File did not become stably readable within $Seconds seconds: $Path ($lastError)"
}

function Invoke-Vbs([string]$Script, [string[]]$ArgumentList) {
    $output = @(& cscript.exe //nologo $Script @ArgumentList 2>&1 | ForEach-Object { [string]$_ })
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "VBScript failed (exit=$exitCode): $Script`n$($output -join [Environment]::NewLine)"
    }
    $output
}

function Get-TaggedValue([string[]]$Lines, [string]$Tag) {
    $prefix = $Tag + "|"
    $line = $Lines | Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) } | Select-Object -Last 1
    if ($null -eq $line) { throw "Probe output lacks $Tag" }
    $line.Substring($prefix.Length)
}

function Release-ComObject($Object) {
    if ($null -ne $Object -and [Runtime.InteropServices.Marshal]::IsComObject($Object)) {
        try { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($Object) } catch {}
    }
}

function Wait-ForNoWord([int]$Seconds = 20) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        if (-not (Get-Process -Name WINWORD -ErrorAction SilentlyContinue)) { return }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "WINWORD.EXE did not exit within $Seconds seconds; no process was killed"
}

function Assert-NoWord {
    $existing = @(Get-Process -Name WINWORD -ErrorAction SilentlyContinue)
    if ($existing.Count -gt 0) {
        throw "Word is already running (PID: $($existing.Id -join ',')); close it before L4 validation"
    }
}

function Get-WordEnvironment {
    Assert-NoWord
    $app = $null
    $normalTemplate = $null
    try {
        $app = New-Object -ComObject Word.Application
        $app.Visible = $false
        $app.DisplayAlerts = 0
        $normalTemplate = $app.NormalTemplate
        [pscustomobject]@{
            StartupPath = [string]$app.StartupPath
            NormalPath = [string]$normalTemplate.FullName
            WordVersion = [string]$app.Version
        }
    } finally {
        Release-ComObject $normalTemplate
        if ($null -ne $app) {
            try { $app.Quit(0) } catch {}
        }
        Release-ComObject $app
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
        Wait-ForNoWord
    }
}

function Get-UnrelatedStartupSnapshot([string]$Directory) {
    $snapshot = @{}
    if (-not (Test-Path -LiteralPath $Directory)) { return $snapshot }
    foreach ($file in Get-ChildItem -LiteralPath $Directory -File -Force) {
        if ($file.Name -like '~$*') { continue }
        if ($file.Name -like "PatentMarker.dotm*" -or $file.Name -like "PatentMarker.addin.install.txt*") { continue }
        $snapshot[$file.Name] = Get-HashValue $file.FullName
    }
    $snapshot
}

function Assert-SnapshotEqual($Expected, $Actual, [string]$Label) {
    $expectedKeys = @($Expected.Keys | Sort-Object)
    $actualKeys = @($Actual.Keys | Sort-Object)
    if (($expectedKeys -join "`n") -ne ($actualKeys -join "`n")) {
        throw "$Label file inventory changed"
    }
    foreach ($key in $expectedKeys) {
        if ($Expected[$key] -ne $Actual[$key]) { throw "$Label bytes changed: $key" }
    }
}

function Close-OwnedWord {
    if ($null -ne $doc) {
        try { $doc.Close(0) } catch {}
        Release-ComObject $doc
        $script:doc = $null
    }
    if ($null -ne $word) {
        try { $word.Quit(0) } catch {}
        Release-ComObject $word
        $script:word = $null
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}

function Restore-PreexistingProduct {
    if (-not $installStarted -or $startupPath -eq "") { return }
    Assert-NoWord
    if (Test-Path -LiteralPath $targetAddin) {
        Move-Item -LiteralPath $targetAddin -Destination $failedNewAddin -Force
    }
    if ($hadPreviousAddin) {
        Copy-Item -LiteralPath $previousAddinCopy -Destination $targetAddin -Force
    }
    if (Test-Path -LiteralPath $manifestPath) {
        Move-Item -LiteralPath $manifestPath -Destination $failedNewManifest -Force
    }
    if ($hadPreviousManifest) {
        Copy-Item -LiteralPath $previousManifestCopy -Destination $manifestPath -Force
    }
}

try {
    $environmentOutput = @(Invoke-Vbs $environmentProbe @())
    $startupPath = Get-TaggedValue $environmentOutput "STARTUP_PATH"
    $normalPath = Get-TaggedValue $environmentOutput "NORMAL_PATH"
    $wordVersion = Get-TaggedValue $environmentOutput "WORD_VERSION"
    $wordExe = Get-TaggedValue $environmentOutput "WORD_EXE"
    if ($startupPath -eq "") { throw "Word returned an empty Startup path" }
    if (-not (Test-Path -LiteralPath $normalPath)) { throw "Normal template does not exist: $normalPath" }
    $targetAddin = Join-Path $startupPath "PatentMarker.dotm"
    $manifestPath = Join-Path $startupPath "PatentMarker.addin.install.txt"
    $normalHashBefore = Get-StableHash $normalPath
    $unrelatedBefore = Get-UnrelatedStartupSnapshot $startupPath

    $hadPreviousAddin = Test-Path -LiteralPath $targetAddin
    $hadPreviousManifest = Test-Path -LiteralPath $manifestPath
    if ($hadPreviousAddin) { Copy-Item -LiteralPath $targetAddin -Destination $previousAddinCopy -Force }
    if ($hadPreviousManifest) { Copy-Item -LiteralPath $manifestPath -Destination $previousManifestCopy -Force }

    Assert-NoWord
    $installStarted = $true
    $installOutput = @(& cscript.exe //nologo $installer "/NoPrompt" "/LogPath:$installerLog" 2>&1 | ForEach-Object { [string]$_ })
    $installExit = $LASTEXITCODE
    if ($installExit -ne 0) { throw "Installer failed (exit=$installExit): $($installOutput -join ' | ')" }
    if (-not (Test-Path -LiteralPath $targetAddin)) { throw "Installer reported success but Startup add-in is missing" }
    if ((Get-HashValue $targetAddin) -ne (Get-HashValue $sourceAddin)) { throw "Installed Startup add-in differs from deployment source" }
    Wait-ForNoWord
    if ((Get-StableHash $normalPath) -ne $normalHashBefore) { throw "Normal.dotm changed during installation" }
    Assert-SnapshotEqual $unrelatedBefore (Get-UnrelatedStartupSnapshot $startupPath) "Unrelated Startup templates after install"

    Assert-NoWord
    $runtimeOutput = @(Invoke-Vbs $runtimeProbe @($targetAddin, $tempRoot, $wordExe))
    $runId = Get-TaggedValue $runtimeOutput "RUN_ID"
    $logPath = Get-TaggedValue $runtimeOutput "LOG_PATH"
    $runtimeWordVersion = Get-TaggedValue $runtimeOutput "WORD_VERSION"
    if (-not ($runtimeOutput -contains "PASS|L4_RUNTIME|startup_loaded=true|autoexec=true|ordinary_save_export=true")) {
        throw "Runtime probe did not emit its L4 PASS contract"
    }
    if (-not (Test-Path -LiteralPath $dictPath)) { throw "Runtime probe reported success but JSON artifact is missing" }
    if (-not (Test-Path -LiteralPath $logPath)) { throw "Runtime diagnostic log is missing: $logPath" }
    $bitnessLine = Get-Content -LiteralPath $logPath -Encoding Unicode |
        Where-Object { $_.Contains("`t$runId`t") -and $_.Contains("office_bitness=") } |
        Select-Object -Last 1
    if ($null -eq $bitnessLine -or $bitnessLine -notmatch 'office_bitness=(32|64)') {
        throw "Runtime log has no Office bitness for run_id=$runId"
    }
    $officeBitness = $Matches[1]

    if ((Get-StableHash $normalPath) -ne $normalHashBefore) { throw "Normal.dotm changed during fresh-process runtime validation" }
    Assert-SnapshotEqual $unrelatedBefore (Get-UnrelatedStartupSnapshot $startupPath) "Unrelated Startup templates after runtime"

    $matrixRunId = ""
    $saveAsImmediateExport = "not_run"
    if ($ExtendedMatrix) {
        $matrixRoot = Join-Path $tempRoot "extended-matrix"
        New-Item -ItemType Directory -Path $matrixRoot -Force | Out-Null
        $matrixOutput = @(Invoke-Vbs $matrixProbe @($targetAddin, $matrixRoot, $wordExe, $installer))
        if (-not ($matrixOutput -contains "PASS|L4_EXTENDED_MATRIX|multi_document_isolation=true|save_as_followup_export=true|locked_target_cancel=true|locked_target_recovery=true|readonly_target_cancel=true|readonly_target_recovery=true|installer_running_word_rejected=true|uninstaller_running_word_rejected=true")) {
            throw "Extended runtime probe did not emit its PASS contract"
        }
        $matrixRunId = Get-TaggedValue $matrixOutput "RUN_ID"
        $saveAsImmediateExport = Get-TaggedValue $matrixOutput "SAVE_AS_IMMEDIATE_EXPORT"
        if ($matrixRunId -eq $runId) { throw "Two fresh Word runs reused the same diagnostic run ID" }
        if ((Get-StableHash $normalPath) -ne $normalHashBefore) { throw "Normal.dotm changed during extended matrix" }
        Assert-SnapshotEqual $unrelatedBefore (Get-UnrelatedStartupSnapshot $startupPath) "Unrelated Startup templates after extended matrix"
    }

    $passed = $true
    $summary = "PASS|L4_FRESH_WORD|word=" + $runtimeWordVersion + "|office_bitness=" + $officeBitness +
        "|run_id=" + $runId + "|addin_path=" + $targetAddin + "|addin_sha256=" + (Get-HashValue $targetAddin) +
        "|normal_sha256=" + $normalHashBefore + "|normal_unchanged=true|auto_export=true|log=" + $logPath
    if ($ExtendedMatrix) {
        $summary += "|extended_matrix=true|matrix_run_id=" + $matrixRunId + "|save_as_immediate_export=" + $saveAsImmediateExport
    }
    if ($KeepArtifacts) { $summary += "|artifacts=" + $tempRoot }
    Write-Output $summary
} catch {
    $failureMessage = $_.Exception.Message
    Close-OwnedWord
    try { Wait-ForNoWord } catch { Write-Output "CLEANUP_WARNING|$($_.Exception.Message)" }
    try { Restore-PreexistingProduct } catch { Write-Output "RESTORE_WARNING|$($_.Exception.Message)" }
    Write-Output "FAIL|L4_FRESH_WORD|$failureMessage"
    throw $failureMessage
} finally {
    Close-OwnedWord
    if ($passed -and -not $KeepArtifacts) {
        $resolved = [IO.Path]::GetFullPath($tempRoot)
        $safePrefix = Join-Path $tempBase "PatentMarker-Installed-E2E-"
        if (-not $resolved.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean unexpected path: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    } else {
        Write-Output "ARTIFACTS|$tempRoot"
    }
}

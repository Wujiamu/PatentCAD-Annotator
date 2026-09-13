param(
    [string]$DeployDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "PatentMarker-2025-deploy"),
    [switch]$KeepArtifacts
)

$ErrorActionPreference = "Stop"
$deployPath = (Resolve-Path -LiteralPath $DeployDir).Path
$installer = Join-Path $deployPath "install-vba.vbs"
$uninstaller = Join-Path $deployPath "uninstall-vba.vbs"
$sourceAddin = Join-Path $deployPath "PatentMarker.dotm"
foreach ($required in @($installer, $uninstaller, $sourceAddin)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing installer asset: $required" }
}

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = Join-Path $tempBase ("PatentMarker-Installer-Isolation-" + [guid]::NewGuid().ToString("N"))
$startupPath = Join-Path $tempRoot "Word Startup with spaces"
New-Item -ItemType Directory -Path $startupPath -Force | Out-Null

function Get-HashValue([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Invoke-Vbs([string]$Script, [string[]]$ArgumentList) {
    $result = Invoke-VbsRaw $Script $ArgumentList
    if ($result.ExitCode -ne 0) {
        throw "VBScript failed (exit=$($result.ExitCode)): $Script`n$($result.Output -join [Environment]::NewLine)"
    }
    $result.Output
}

function Invoke-VbsRaw([string]$Script, [string[]]$ArgumentList) {
    $output = @(& cscript.exe //nologo $Script @ArgumentList 2>&1 | ForEach-Object { [string]$_ })
    [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = $output
    }
}

$normalSentinel = Join-Path $startupPath "Normal.dotm"
$userMacroSentinel = Join-Path $startupPath "UserMacros.dotm"
[IO.File]::WriteAllBytes($normalSentinel, [byte[]](0..255))
[IO.File]::WriteAllText($userMacroSentinel, "USER-MACRO-SENTINEL-" + [guid]::NewGuid(), [Text.Encoding]::UTF8)
$normalHash = Get-HashValue $normalSentinel
$userMacroHash = Get-HashValue $userMacroSentinel
$logPath = Join-Path $tempRoot "installer.log"
$installedPath = Join-Path $startupPath "PatentMarker.dotm"
$manifestPath = Join-Path $startupPath "PatentMarker.addin.install.txt"
$passed = $false

try {
    $installArgs = @("/NoPrompt", "/StartupPath:$startupPath", "/LogPath:$logPath")

    $foreignBytes = [Text.Encoding]::ASCII.GetBytes("FOREIGN-UNOWNED-ADDIN-" + [guid]::NewGuid())
    [IO.File]::WriteAllBytes($installedPath, $foreignBytes)
    $foreignHash = Get-HashValue $installedPath
    $missingManifestUninstall = Invoke-VbsRaw $uninstaller @("/StartupPath:$startupPath")
    if ($missingManifestUninstall.ExitCode -eq 0) { throw "Uninstaller removed an unowned add-in without a manifest" }
    if (-not (Test-Path -LiteralPath $installedPath)) { throw "Uninstaller deleted an unowned add-in without a manifest" }
    if ((Get-HashValue $installedPath) -ne $foreignHash) { throw "Uninstaller changed an unowned add-in without a manifest" }
    Remove-Item -LiteralPath $installedPath -Force

    $firstOutput = Invoke-Vbs $installer $installArgs
    if (-not (Test-Path -LiteralPath $installedPath)) { throw "First install did not create PatentMarker.dotm" }
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "First install did not create ownership manifest" }
    if ((Get-HashValue $installedPath) -ne (Get-HashValue $sourceAddin)) { throw "Installed add-in differs from deployment source" }
    if ((Get-HashValue $normalSentinel) -ne $normalHash) { throw "Normal.dotm sentinel changed on first install" }
    if ((Get-HashValue $userMacroSentinel) -ne $userMacroHash) { throw "User macro sentinel changed on first install" }

    $syntheticPrevious = [Text.Encoding]::ASCII.GetBytes("SYNTHETIC-PREVIOUS-ADDIN-" + [guid]::NewGuid())
    [IO.File]::WriteAllBytes($installedPath, $syntheticPrevious)
    $syntheticPreviousHash = Get-HashValue $installedPath
    $manifestLock = [IO.File]::Open($manifestPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    try {
        $failedInstall = Invoke-VbsRaw $installer $installArgs
    } finally {
        $manifestLock.Dispose()
    }
    if ($failedInstall.ExitCode -eq 0) { throw "Installer unexpectedly succeeded while the ownership manifest was locked" }
    if ((Get-HashValue $installedPath) -ne $syntheticPreviousHash) { throw "Installer did not roll back the previous add-in after manifest failure" }
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Installer lost the existing manifest after rollback" }

    $secondOutput = Invoke-Vbs $installer $installArgs
    if ((Get-HashValue $installedPath) -ne (Get-HashValue $sourceAddin)) { throw "Repeated install produced different add-in bytes" }
    if ((Get-HashValue $normalSentinel) -ne $normalHash) { throw "Normal.dotm sentinel changed on repeated install" }
    if ((Get-HashValue $userMacroSentinel) -ne $userMacroHash) { throw "User macro sentinel changed on repeated install" }
    if (@(Get-ChildItem -LiteralPath $startupPath -Filter "PatentMarker.dotm.pm-backup-*").Count -lt 1) {
        throw "Repeated install did not preserve the previous add-in"
    }

    $manifestLock = [IO.File]::Open($manifestPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    try {
        $failedUninstall = Invoke-VbsRaw $uninstaller @("/StartupPath:$startupPath")
    } finally {
        $manifestLock.Dispose()
    }
    if ($failedUninstall.ExitCode -eq 0) { throw "Uninstaller unexpectedly succeeded while the ownership manifest was locked" }
    if (-not (Test-Path -LiteralPath $installedPath)) { throw "Uninstaller did not restore the active add-in after manifest failure" }
    if ((Get-HashValue $installedPath) -ne (Get-HashValue $sourceAddin)) { throw "Uninstaller rollback restored incorrect add-in bytes" }

    $uninstallOutput = Invoke-Vbs $uninstaller @("/StartupPath:$startupPath")
    if (Test-Path -LiteralPath $installedPath) { throw "Uninstaller left the active product add-in behind" }
    if ((Get-HashValue $normalSentinel) -ne $normalHash) { throw "Normal.dotm sentinel changed on uninstall" }
    if ((Get-HashValue $userMacroSentinel) -ne $userMacroHash) { throw "User macro sentinel changed on uninstall" }
    if (@(Get-ChildItem -LiteralPath $startupPath -Filter "PatentMarker.dotm.uninstalled-*.bak").Count -ne 1) {
        throw "Uninstaller did not create exactly one recoverable product backup"
    }

    $passed = $true
    Write-Output "PASS|L2_INSTALLER_ISOLATION|normal_unchanged=true|user_macros_unchanged=true|missing_manifest_refused=true|repeat_install=true|install_rollback=true|uninstall_rollback=true|recoverable_uninstall=true"
} finally {
    if ($passed -and -not $KeepArtifacts) {
        $resolved = [IO.Path]::GetFullPath($tempRoot)
        $safePrefix = Join-Path $tempBase "PatentMarker-Installer-Isolation-"
        if (-not $resolved.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean unexpected path: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    } else {
        Write-Output "ARTIFACTS|$tempRoot"
    }
}

[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'PatentMarker\OfficeAddin\PowerPoint'),
    [ValidateSet('Auto', '32', '64')][string]$OfficeBitness = 'Auto',
    [string]$TestRegistryRoot,
    # Test-only: simulate Remove-Item failing after part of the directory was
    # already deleted. Locking a file cannot do this, because Windows refuses
    # to rename a directory whose child is open, so the delete stage would
    # never be reached.
    # Test-only injection points for the rollback order matrix (G18). They only
    # act inside the recovery catch, so a normal uninstall is unaffected.
    [ValidateSet('None', 'AfterPartialDelete', 'PartialDeleteThenRestoreFails', 'RegistryRestoreFails', 'CleanupFails')]
    [string]$FaultInjectionPoint = 'None'
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'OfficeAddin.Common.psm1') -Force

$installFull = [IO.Path]::GetFullPath($InstallDirectory)
$productionInstall = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'PatentMarker\OfficeAddin\PowerPoint'))
if ((Get-Process -Name POWERPNT -ErrorAction SilentlyContinue) -and
    ([string]::IsNullOrWhiteSpace($TestRegistryRoot) -or
     [string]::Equals($installFull, $productionInstall, [StringComparison]::OrdinalIgnoreCase))) {
    throw '请先关闭 PowerPoint，再卸载加载项。'
}
if (-not (Test-Path -LiteralPath $installFull -PathType Container)) {
    Write-Output '未找到产品安装目录，没有执行卸载。'
    return
}
$manifest = Assert-PatentMarkerOwnedDirectory -InstallDirectory $installFull
if ($OfficeBitness -eq 'Auto') { $OfficeBitness = [string]$manifest.OfficeBitness }
$registryPaths = @(Get-PatentMarkerRegistryPaths -TestRegistryRoot $TestRegistryRoot)
$baseKey = Open-PatentMarkerCurrentUserRegistry -OfficeBitness $OfficeBitness
$registrySnapshots = @()
foreach ($path in $registryPaths) {
    $registrySnapshots += ,(Get-PatentMarkerRegistrySnapshot -BaseKey $baseKey -Path $path)
}

$parent = Split-Path -Parent $installFull
$leaf = Split-Path -Leaf $installFull
$parent = [IO.Path]::GetFullPath($parent)
$backupDirectory = Join-Path $parent ('.' + $leaf + '.uninstall-' + [Guid]::NewGuid().ToString('N'))
$backupDirectory = Assert-PatentMarkerSiblingDirectory -Candidate $backupDirectory -ExpectedParent $parent
$recoveryDirectory = $null
$moved = $false

# The directory we delete can end up half-deleted, so it cannot be its own
# recovery point. Build and verify a separate copy first; if that fails, stop
# before anything destructive happens.
$recoveryDirectory = New-PatentMarkerRecoveryCopy -InstallDirectory $installFull -ExpectedParent $parent

try {
    Move-Item -LiteralPath $installFull -Destination $backupDirectory
    $moved = $true
    foreach ($path in $registryPaths) {
        Remove-PatentMarkerRegistryTree -BaseKey $baseKey -Path $path
    }
    if ($FaultInjectionPoint -ne 'None') {
        # Remove one staged file first: this is the state a failing recursive
        # delete leaves behind, and the reason the staged directory cannot be
        # renamed back as a "restore". Every injection point starts from this
        # real half-deleted staging state; the differences are which recovery
        # stage then fails, so no stage is faked.
        $victim = Join-Path $backupDirectory 'Newtonsoft.Json.dll'
        if (Test-Path -LiteralPath $victim) { Remove-Item -LiteralPath $victim -Force }
        throw '测试故障注入：递归删除中途失败。'
    }
    Remove-PatentMarkerSiblingDirectory -Candidate $backupDirectory -ExpectedParent $parent
    if ($recoveryDirectory -and (Test-Path -LiteralPath $recoveryDirectory -PathType Container)) {
        try { Remove-Item -LiteralPath $recoveryDirectory -Recurse -Force }
        catch { Write-Warning "清理恢复副本失败：$recoveryDirectory" }
    }
    Write-Output "已卸载 PatentOffice.PowerPointAddIn，并移除产品专属文件和 HKCU 注册项。"
}
catch {
    $failure = $_
    foreach ($path in $registryPaths) {
        try { Remove-PatentMarkerRegistryTree -BaseKey $baseKey -Path $path } catch { Write-Warning "rollback failed: $path" }
    }
    for ($index = 0; $index -lt $registryPaths.Count; $index++) {
        try { Restore-PatentMarkerRegistrySnapshot -BaseKey $baseKey -Snapshot $registrySnapshots[$index] } catch { Write-Warning "restore failed" }
    }
    $filesRestored = $false
    if ($recoveryDirectory) {
        try { $filesRestored = Restore-PatentMarkerDirectoryFromCopy -RecoveryCopy $recoveryDirectory -InstallDirectory $installFull -ExpectedParent $parent }
        catch { Write-Warning "file restore failed" }
    }
    if ($filesRestored) {
        if (Test-Path -LiteralPath $backupDirectory -PathType Container) { try { Remove-Item -LiteralPath $backupDirectory -Recurse -Force } catch { } }
        try { Remove-Item -LiteralPath $recoveryDirectory -Recurse -Force } catch { }
    }
    throw $failure
}
finally { $baseKey.Dispose() }
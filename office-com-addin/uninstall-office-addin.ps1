[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'PatentMarker\OfficeAddin\PowerPoint'),
    [ValidateSet('Auto', '32', '64')][string]$OfficeBitness = 'Auto',
    [string]$TestRegistryRoot,
    # Test-only injection points for the rollback order matrix (G18). They all
    # start from the real half-deleted staging state a failing recursive delete
    # leaves behind (locking a file cannot reach that stage, because Windows
    # refuses to rename a directory whose child is open); what differs is which
    # recovery stage then fails. A normal uninstall passes None and is
    # unaffected.
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
$recoveryDirectory
# The directory we delete can end up half-deleted, so it cannot be its own
# recovery point. Build and verify a separate copy first; if that fails, stop
# before anything destructive happens.
$recoveryDirectory = New-PatentMarkerRecoveryCopy -InstallDirectory $installFull -ExpectedParent $parent

try {
    Move-Item -LiteralPath $installFull -Destination $backupDirectory
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
    # Rollback order is the point of this block: the add-in registration may
    # only be re-enabled after the product files are proven complete, because a
    # registration pointing at a half-deleted directory is worse than a
    # disabled add-in. Each step below is therefore gated on the previous one.
    $registryCleared = $true
    foreach ($path in $registryPaths) {
        try { Remove-PatentMarkerRegistryTree -BaseKey $baseKey -Path $path }
        catch { $registryCleared = $false; Write-Warning "回滚注册表项失败：$path" }
    }
    if (-not $registryCleared) {
        # A tree we could not remove may still be live; restoring on top of it
        # would hide the leftover instead of proving the original state. The
        # read-back assertion below is what decides, not this flag.
        Write-Warning '有注册表项未能清除，原注册项恢复结果以回读核对为准。'
    }

    # 1) Is the target already the complete product file set? Never delete a
    #    directory that is already whole, and never trust a leftover partial
    #    directory just because it exists.
    $filesComplete = $false
    try {
        [void](Assert-PatentMarkerOwnedDirectory -InstallDirectory $installFull)
        $filesComplete = $true
    }
    catch { $filesComplete = $false }

    # 2) Restore from the verified copy when the target is not complete yet.
    if (-not $filesComplete -and $recoveryDirectory) {
        if ($FaultInjectionPoint -eq 'PartialDeleteThenRestoreFails') {
            Write-Warning '测试故障注入：从恢复副本还原产品文件失败。'
        }
        else {
            try {
                $filesComplete = Restore-PatentMarkerDirectoryFromCopy -RecoveryCopy $recoveryDirectory `
                    -InstallDirectory $installFull -ExpectedParent $parent
            }
            catch { Write-Warning "从恢复副本还原产品文件失败：$($_.Exception.Message)" }
        }
    }

    # 3) Verify the full product set and hashes again before touching the
    #    registration; a "restored" flag alone is not evidence.
    if ($filesComplete) {
        try {
            [void](Assert-PatentMarkerOwnedDirectory -InstallDirectory $installFull)
            $filesComplete = $true
        }
        catch {
            $filesComplete = $false
            Write-Warning "还原后的产品目录未通过完整性与哈希核对：$($_.Exception.Message)"
        }
    }

    # 4) Only now restore the original registration, and confirm what was
    #    actually written. A failed restore stays failed and keeps the material.
    $registryRestored = $false
    if ($filesComplete) {
        $registryRestored = $true
        for ($index = 0; $index -lt $registryPaths.Count; $index++) {
            if (-not $registryRestored) { break }
            try {
                if ($FaultInjectionPoint -eq 'RegistryRestoreFails' -and $index -eq 0) {
                    throw '测试故障注入：注册表恢复单步失败。'
                }
                Restore-PatentMarkerRegistrySnapshot -BaseKey $baseKey -Snapshot $registrySnapshots[$index]
                Assert-PatentMarkerRegistrySnapshotRestored -BaseKey $baseKey -Path $registryPaths[$index] `
                    -Snapshot $registrySnapshots[$index]
            }
            catch {
                $registryRestored = $false
                Write-Warning "恢复原注册表项失败：$($registryPaths[$index])：$($_.Exception.Message)"
            }
        }
    }

    # 5) Clean up the recovery material last, and only when both the files and
    #    the registration are confirmed back in place.
    if ($filesComplete -and $registryRestored) {
        if ($FaultInjectionPoint -eq 'CleanupFails') {
            Write-Warning ("测试故障注入：清理恢复材料失败。恢复副本保留于：{0}；卸载暂存目录：{1}" -f `
                $recoveryDirectory, $backupDirectory)
        }
        else {
            if (Test-Path -LiteralPath $backupDirectory -PathType Container) {
                try { Remove-Item -LiteralPath $backupDirectory -Recurse -Force }
                catch { Write-Warning "清理卸载暂存目录失败：$backupDirectory" }
            }
            if ($recoveryDirectory -and (Test-Path -LiteralPath $recoveryDirectory -PathType Container)) {
                try { Remove-Item -LiteralPath $recoveryDirectory -Recurse -Force }
                catch { Write-Warning "清理恢复副本失败：$recoveryDirectory" }
            }
        }
        Write-Warning "卸载未完成，已从恢复副本还原完整产品文件并恢复原注册项：$installFull"
    }
    elseif ($filesComplete) {
        # Files are whole but the registration could not be confirmed. Keep the
        # recovery material and do not claim a rollback.
        Write-Warning ("卸载失败：产品文件已还原，但原注册项未能恢复（加载项保持停用）。恢复副本保留于：{0}；卸载暂存目录：{1}" -f `
            $recoveryDirectory, $backupDirectory)
    }
    else {
        # Never claim a rollback we could not verify, and never point a live
        # registration at an incomplete directory. Keep both copies and say
        # exactly where the recoverable data is.
        Write-Warning ("卸载失败且未能确认完整还原，加载项保持停用。恢复副本保留于：{0}；卸载暂存目录：{1}" -f `
            $recoveryDirectory, $backupDirectory)
    }
    throw $failure
}
finally { $baseKey.Dispose() }

[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'PatentMarker\OfficeAddin\Visio'),
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
Import-Module (Join-Path $PSScriptRoot 'VisioAddin.Common.psm1') -Force
$identity = Get-VisioAddinIdentity

if ([string]::IsNullOrWhiteSpace($TestRegistryRoot) -and (Get-Process -Name VISIO -ErrorAction SilentlyContinue)) {
    throw '请先关闭 Visio，再卸载加载项。'
}

$installFull = [IO.Path]::GetFullPath($InstallDirectory)
if (-not (Test-Path -LiteralPath $installFull -PathType Container)) {
    throw "Visio 产品安装目录不存在，拒绝按名称推测其他资产：$installFull"
}
$manifest = Assert-VisioOwnedDirectory -InstallDirectory $installFull
$bitness = if ($OfficeBitness -eq 'Auto') { [string]$manifest.OfficeBitness } else { $OfficeBitness }
$registryPaths = @(Get-VisioAddinRegistryPaths -TestRegistryRoot $TestRegistryRoot)
$baseKey = Open-VisioAddinRegistry -OfficeBitness $bitness
$snapshots = @()
foreach ($path in $registryPaths) {
    $snapshots += ,(Get-VisioRegistrySnapshot -BaseKey $baseKey -Path $path)
}
$parent = [IO.Path]::GetFullPath((Split-Path -Parent $installFull))
$backup = Assert-VisioSiblingDirectory -Candidate (Join-Path $parent ('.' + (Split-Path -Leaf $installFull) + '.uninstall-' + [Guid]::NewGuid().ToString('N'))) -ExpectedParent $parent
$recovery = $null

# The directory we delete can end up half-deleted, so it cannot be its own
# recovery point. Build and verify a separate copy first; if that fails, stop
# before anything destructive happens.
$recovery = New-VisioRecoveryCopy -InstallDirectory $installFull -ExpectedParent $parent

try {
    foreach ($path in $registryPaths) {
        Remove-VisioRegistryTree -BaseKey $baseKey -Path $path
    }
    Move-Item -LiteralPath $installFull -Destination $backup
    if ($FaultInjectionPoint -ne 'None') {
        # Remove one staged file first: this is the state a failing recursive
        # delete leaves behind, and the reason the staged directory cannot be
        # renamed back as a "restore". Every injection point starts from this
        # real half-deleted staging state; the differences are which recovery
        # stage then fails, so no stage is faked.
        $victim = Join-Path $backup 'Newtonsoft.Json.dll'
        if (Test-Path -LiteralPath $victim) { Remove-Item -LiteralPath $victim -Force }
        throw '测试故障注入：递归删除中途失败。'
    }
    try { Remove-VisioSiblingDirectory -Candidate $backup -ExpectedParent $parent }
    catch
    {
        # The delete may have removed part of the staged directory; restore
        # from the verified copy rather than renaming the leftover back.
        if (Restore-VisioDirectoryFromCopy -RecoveryCopy $recovery -InstallDirectory $installFull -ExpectedParent $parent) {
            if (Test-Path -LiteralPath $backup -PathType Container) {
                try { Remove-Item -LiteralPath $backup -Recurse -Force } catch { Write-Warning "清理卸载暂存目录失败：$backup" }
            }
        }
        throw
    }
    if ($recovery -and (Test-Path -LiteralPath $recovery -PathType Container)) {
        try { Remove-Item -LiteralPath $recovery -Recurse -Force }
        catch { Write-Warning "清理恢复副本失败：$recovery" }
    }
    Write-Output "已卸载 $($identity.ProgId)。"
}
catch {
    $failure = $_
    # Rollback order is the point of this block: the add-in registration may
    # only be re-enabled after the product files are proven complete, because a
    # registration pointing at a half-deleted directory is worse than a
    # disabled add-in. Each step below is therefore gated on the previous one.
    $registryCleared = $true
    foreach ($path in $registryPaths) {
        try { Remove-VisioRegistryTree -BaseKey $baseKey -Path $path }
        catch { $registryCleared = $false; Write-Warning "清理 Visio 注册项失败：$path" }
    }

    # 1) Is the target already the complete product file set? Never delete a
    #    directory that is already whole, and never trust a leftover partial
    #    directory just because it exists.
    $filesComplete = $false
    try {
        [void](Assert-VisioOwnedDirectory -InstallDirectory $installFull)
        $filesComplete = $true
    }
    catch { $filesComplete = $false }

    # 2) Restore from the verified copy when the target is not complete yet.
    if (-not $filesComplete -and $recovery) {
        if ($FaultInjectionPoint -eq 'PartialDeleteThenRestoreFails') {
            Write-Warning '测试故障注入：从恢复副本还原 Visio 产品文件失败。'
        }
        else {
            try {
                $filesComplete = Restore-VisioDirectoryFromCopy -RecoveryCopy $recovery `
                    -InstallDirectory $installFull -ExpectedParent $parent
            }
            catch { Write-Warning "从恢复副本还原 Visio 产品文件失败：$($_.Exception.Message)" }
        }
    }

    # 3) Verify the full product set and hashes again before touching the
    #    registration; a "restored" flag alone is not evidence.
    if ($filesComplete) {
        try {
            [void](Assert-VisioOwnedDirectory -InstallDirectory $installFull)
            $filesComplete = $true
        }
        catch {
            $filesComplete = $false
            Write-Warning "还原后的 Visio 产品目录未通过完整性与哈希核对：$($_.Exception.Message)"
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
                Restore-VisioRegistrySnapshot -BaseKey $baseKey -Snapshot $snapshots[$index]
                Assert-VisioRegistrySnapshotRestored -BaseKey $baseKey -Path $registryPaths[$index] `
                    -Snapshot $snapshots[$index]
            }
            catch {
                $registryRestored = $false
                Write-Warning "恢复 Visio 注册项失败：$($registryPaths[$index])：$($_.Exception.Message)"
            }
        }
    }

    # 5) Clean up the recovery material last, and only when both the files and
    #    the registration are confirmed back in place.
    if ($filesComplete -and $registryRestored) {
        if ($FaultInjectionPoint -eq 'CleanupFails') {
            Write-Warning ("测试故障注入：清理恢复材料失败。恢复副本保留于：{0}；卸载暂存目录：{1}" -f `
                $recovery, $backup)
        }
        else {
            if (Test-Path -LiteralPath $backup -PathType Container) {
                try { Remove-Item -LiteralPath $backup -Recurse -Force }
                catch { Write-Warning "清理卸载暂存目录失败：$backup" }
            }
            if ($recovery -and (Test-Path -LiteralPath $recovery -PathType Container)) {
                try { Remove-Item -LiteralPath $recovery -Recurse -Force }
                catch { Write-Warning "清理恢复副本失败：$recovery" }
            }
        }
        Write-Warning "卸载未完成，已从恢复副本还原完整 Visio 产品文件并恢复原注册项：$installFull"
    }
    elseif ($filesComplete) {
        # Files are whole but the registration could not be confirmed. Keep the
        # recovery material and do not claim a rollback.
        Write-Warning ("卸载失败：Visio 产品文件已还原，但原注册项未能恢复（加载项保持停用）。恢复副本保留于：{0}；卸载暂存目录：{1}" -f `
            $recovery, $backup)
    }
    else {
        # Never claim a rollback we could not verify, and never point a live
        # registration at an incomplete directory. Keep both copies and say
        # exactly where the recoverable data is.
        Write-Warning ("卸载失败且未能确认完整还原，Visio 加载项保持停用。恢复副本保留于：{0}；卸载暂存目录：{1}" -f `
            $recovery, $backup)
    }
    throw $failure
}
finally { $baseKey.Dispose() }

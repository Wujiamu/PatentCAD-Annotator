
    $failure = $_
    foreach ($path in $registryPaths) {
        try { Remove-PatentMarkerRegistryTree -BaseKey $baseKey -Path $path }
        catch { Write-Warning "回滚注册表项失败：$path" }
    }
    for ($index = 0; $index -lt $registryPaths.Count; $index++) {
        try { Restore-PatentMarkerRegistrySnapshot -BaseKey $baseKey -Snapshot $registrySnapshots[$index] }
        catch { Write-Warning "恢复原注册表项失败：$($registryPaths[$index])" }
    }
    # Restore the complete file set from the verified copy, not the possibly
    # half-deleted working directory.
    $filesRestored = $false
    if ($recoveryDirectory) {
        try { $filesRestored = Restore-PatentMarkerDirectoryFromCopy -RecoveryCopy $recoveryDirectory -InstallDirectory $installFull -ExpectedParent $parent }
        catch { Write-Warning "从恢复副本还原产品文件失败：$($_.Exception.Message)" }
    }
    if ($filesRestored) {
        if (Test-Path -LiteralPath $backupDirectory -PathType Container) {
            try { Remove-Item -LiteralPath $backupDirectory -Recurse -Force }
            catch { Write-Warning "清理卸载暂存目录失败：$backupDirectory" }
        }
        try { Remove-Item -LiteralPath $recoveryDirectory -Recurse -Force }
        catch { Write-Warning "清理恢复副本失败：$recoveryDirectory" }
        Write-Warning "卸载未完成，已从恢复副本还原完整产品文件：$installFull"
    }
    else {
        # Never claim a rollback we could not verify. Keep both copies and say
        # exactly where the recoverable data is.
        Write-Warning ("卸载失败且未能确认完整还原。恢复副本保留于：{0}；卸载暂存目录：{1}" -f `
            $recoveryDirectory, $backupDirectory)
    }
    throw $failure

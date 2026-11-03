
    $failure = $_
    foreach ($path in $registryPaths) {
        try { Remove-VisioRegistryTree -BaseKey $baseKey -Path $path }
        catch { Write-Warning "清理 Visio 注册项失败：$path" }
    }
    for ($index = 0; $index -lt $registryPaths.Count; $index++) {
        try { Restore-VisioRegistrySnapshot -BaseKey $baseKey -Snapshot $snapshots[$index] }
        catch { Write-Warning "恢复 Visio 注册项失败：$($registryPaths[$index])" }
    }
    # Restore the complete file set from the verified copy, not the possibly
    # half-deleted working directory.
    $filesRestored = $false
    if ($recovery) {
        try { $filesRestored = Restore-VisioDirectoryFromCopy -RecoveryCopy $recovery -InstallDirectory $installFull -ExpectedParent $parent }
        catch { Write-Warning "从恢复副本还原 Visio 产品文件失败：$($_.Exception.Message)" }
    }
    if ($filesRestored) {
        if (Test-Path -LiteralPath $backup -PathType Container) {
            try { Remove-Item -LiteralPath $backup -Recurse -Force }
            catch { Write-Warning "清理卸载暂存目录失败：$backup" }
        }
        try { Remove-Item -LiteralPath $recovery -Recurse -Force }
        catch { Write-Warning "清理恢复副本失败：$recovery" }
        Write-Warning "卸载未完成，已从恢复副本还原完整产品文件：$installFull"
    }
    else {
        # Never claim a rollback we could not verify. Keep both copies and say
        # exactly where the recoverable data is.
        Write-Warning ("卸载失败且未能确认完整还原。恢复副本保留于：{0}；卸载暂存目录：{1}" -f `
            $recovery, $backup)
    }
    throw $failure

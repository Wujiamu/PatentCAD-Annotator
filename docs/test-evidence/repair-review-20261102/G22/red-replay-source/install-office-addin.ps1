[CmdletBinding()]
param(
    [string]$SourceDirectory,
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'PatentMarker\OfficeAddin\PowerPoint'),
    [ValidateSet('Auto', '32', '64')][string]$OfficeBitness = 'Auto',
    [string]$TestRegistryRoot,
    # Test-only injection points for the rollback order matrix (G18). They only
    # act inside the recovery catch, so a normal install is unaffected.
    [ValidateSet('None', 'AfterFiles', 'AfterRegistry', 'RollbackPartialDelete', 'RollbackRemovalBlocked', 'RegistryRestoreFails', 'CleanupFails', 'StagedCopyCorrupted', 'NewMoveFails')]
    [string]$FaultInjectionPoint = 'None'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Join-Path $PSScriptRoot 'dist'
}
Import-Module (Join-Path $PSScriptRoot 'OfficeAddin.Common.psm1') -Force

$productId = 'PatentMarker.WordPpt.ReadOnly'
$classId = '{4E9B0E7A-4D92-47AF-A9D7-7E769CB367D8}'
$progId = 'PatentOffice.PowerPointAddIn'
$className = 'PatentOffice.PowerPoint.PowerPointAddIn'
$manifestName = 'install-manifest.json'

$sourceFull = [IO.Path]::GetFullPath($SourceDirectory)
$installFull = [IO.Path]::GetFullPath($InstallDirectory)
$productionInstall = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'PatentMarker\OfficeAddin\PowerPoint'))
if ((Get-Process -Name POWERPNT -ErrorAction SilentlyContinue) -and
    ([string]::IsNullOrWhiteSpace($TestRegistryRoot) -or
     [string]::Equals($installFull, $productionInstall, [StringComparison]::OrdinalIgnoreCase))) {
    throw '请先关闭 PowerPoint，再安装或升级加载项。'
}
$installParent = Split-Path -Parent $installFull
$installLeaf = Split-Path -Leaf $installFull
if ([string]::IsNullOrWhiteSpace($installLeaf)) { throw '产品安装目录无效。' }
if (-not (Test-Path -LiteralPath $sourceFull -PathType Container)) { throw "未找到构建文件目录：$sourceFull" }

$sourceFiles = @('PatentOffice.PowerPoint.dll', 'Newtonsoft.Json.dll')
foreach ($fileName in $sourceFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceFull $fileName) -PathType Leaf)) {
        throw "构建文件不完整，缺少 $fileName。请先运行 build.ps1。"
    }
}

$bitness = Get-PatentMarkerOfficeBitness -Requested $OfficeBitness
$registryPaths = @(Get-PatentMarkerRegistryPaths -TestRegistryRoot $TestRegistryRoot)
$baseKey = Open-PatentMarkerCurrentUserRegistry -OfficeBitness $bitness
$registrySnapshots = @()
foreach ($path in $registryPaths) {
    $registrySnapshots += ,(Get-PatentMarkerRegistrySnapshot -BaseKey $baseKey -Path $path)
}

$manifest = $null
if (Test-Path -LiteralPath $installFull) {
    $manifest = Assert-PatentMarkerOwnedDirectory -InstallDirectory $installFull
}
elseif ($registrySnapshots | Where-Object { $null -ne $_ } | Select-Object -First 1) {
    $baseKey.Dispose()
    throw '发现同名加载项注册项但没有产品所有权清单。为避免覆盖其他加载项，安装已停止。'
}

$parentExists = Test-Path -LiteralPath $installParent -PathType Container
if (-not $parentExists) { New-Item -ItemType Directory -Path $installParent -Force | Out-Null }
$installParent = [IO.Path]::GetFullPath($installParent)
$stageDirectory = Join-Path $installParent ('.' + $installLeaf + '.stage-' + [Guid]::NewGuid().ToString('N'))
$backupDirectory = Join-Path $installParent ('.' + $installLeaf + '.rollback-' + [Guid]::NewGuid().ToString('N'))
$stageDirectory = Assert-PatentMarkerSiblingDirectory -Candidate $stageDirectory -ExpectedParent $installParent
$backupDirectory = Assert-PatentMarkerSiblingDirectory -Candidate $backupDirectory -ExpectedParent $installParent
$targetMovedToBackup = $false
$newTargetInstalled = $false

try {
    New-Item -ItemType Directory -Path $stageDirectory | Out-Null
    $fileHashes = [ordered]@{}
    foreach ($fileName in $sourceFiles) {
        $sourcePath = Join-Path $sourceFull $fileName
        $destinationPath = Join-Path $stageDirectory $fileName
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath
        if ($FaultInjectionPoint -eq 'StagedCopyCorrupted' -and $fileName -eq $sourceFiles[0]) {
            [IO.File]::WriteAllText($destinationPath, 'corrupted-by-test-injection')
        }
        $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
        $copiedHash = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash
        if ($sourceHash -ne $copiedHash) { throw "复制校验失败：$fileName" }
        $fileHashes[$fileName] = $copiedHash
    }

    $assemblyInfo = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $stageDirectory 'PatentOffice.PowerPoint.dll'))
    $manifestData = [ordered]@{
        ProductId = $productId
        ClassId = $classId
        ProgId = $progId
        ProductVersion = $assemblyInfo.Version.ToString()
        OfficeBitness = $bitness
        InstalledAt = [DateTime]::UtcNow.ToString('o')
        Files = $fileHashes
    }
    $manifestData | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stageDirectory $manifestName) -Encoding UTF8

    if (Test-Path -LiteralPath $installFull) {
        $resolvedTarget = (Resolve-Path -LiteralPath $installFull).Path
        if (-not [string]::Equals($resolvedTarget, $installFull, [StringComparison]::OrdinalIgnoreCase)) {
            throw "安装目录解析到不同位置，已拒绝覆盖：$resolvedTarget"
        }
        Move-Item -LiteralPath $installFull -Destination $backupDirectory
        $targetMovedToBackup = $true
    }
    if ($FaultInjectionPoint -eq 'NewMoveFails') {
        $script:g22StageHandle = [IO.File]::Open(
            (Join-Path $stageDirectory $sourceFiles[0]),
            [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        Write-Warning '测试故障注入：新安装目录无法移动到产品路径。'
    }
    Move-Item -LiteralPath $stageDirectory -Destination $installFull
    $newTargetInstalled = $true

    if ($FaultInjectionPoint -eq 'AfterFiles') { throw '测试故障注入：文件部署后。' }

    $classesRoot = if ([string]::IsNullOrWhiteSpace($TestRegistryRoot)) { 'Software\Classes' } else { $TestRegistryRoot.Trim('\') + '\Classes' }
    $classPath = $registryPaths[0]
    $progPath = $registryPaths[1]
    $addinPath = $registryPaths[2]
    $inprocPath = $classPath + '\InprocServer32'
    $classText = $className
    $assemblyText = $assemblyInfo.FullName
    $codeBase = ([Uri](Join-Path $installFull 'PatentOffice.PowerPoint.dll')).AbsoluteUri

    Set-PatentMarkerRegistryValues -BaseKey $baseKey -Path $classPath -Values @{
        '' = $classText
    }
    Set-PatentMarkerRegistryValues -BaseKey $baseKey -Path $progPath -Values @{ '' = $classText }
    Set-PatentMarkerRegistryValues -BaseKey $baseKey -Path $inprocPath -Values @{
        '' = 'mscoree.dll'
        'ThreadingModel' = 'Both'
        'Class' = $className
        'Assembly' = $assemblyText
        'RuntimeVersion' = 'v4.0.30319'
        'CodeBase' = $codeBase
    }
    Set-PatentMarkerRegistryValues -BaseKey $baseKey -Path ($progPath + '\CLSID') -Values @{ '' = $classId }
    Set-PatentMarkerRegistryValues -BaseKey $baseKey -Path ($progPath + '\CurVer') -Values @{ '' = $progId }
    Set-PatentMarkerRegistryValues -BaseKey $baseKey -Path ($classesRoot + '\CLSID\' + $classId + '\ProgID') -Values @{ '' = $progId }
    Set-PatentMarkerRegistryValues -BaseKey $baseKey -Path ($classesRoot + '\CLSID\' + $classId + '\VersionIndependentProgID') -Values @{ '' = $progId }
    Set-PatentMarkerRegistryValues -BaseKey $baseKey -Path $addinPath -Values @{
        'FriendlyName' = 'PatentMarker Word to PowerPoint'
        'Description' = 'Read-only Word dictionary binding and PowerPoint picture annotations'
        'LoadBehavior' = @{ Kind = [Microsoft.Win32.RegistryValueKind]::DWord; Value = 3 }
    }

    if ($FaultInjectionPoint -in @('AfterRegistry', 'RollbackPartialDelete', 'RollbackRemovalBlocked', 'RegistryRestoreFails', 'CleanupFails')) {
        # Every one of these fails at the same real point (after the new files
        # and the new registration are in place); what differs is which stage of
        # the recovery catch then fails, so no recovery stage is faked away.
        throw '测试故障注入：注册表写入后。'
    }

    $installedManifest = Get-PatentMarkerOwnershipManifest -InstallDirectory $installFull
    foreach ($fileName in $sourceFiles) {
        $installedHash = (Get-FileHash -LiteralPath (Join-Path $installFull $fileName) -Algorithm SHA256).Hash
        if ($installedHash -ne $installedManifest.Files.$fileName) { throw "安装后哈希核对失败：$fileName" }
    }
    $addinKey = $baseKey.OpenSubKey($addinPath)
    try {
        if (-not $addinKey -or [int]$addinKey.GetValue('LoadBehavior', 0) -ne 3) {
            throw '加载项注册检查失败：LoadBehavior 不是 3。'
        }
    }
    finally { if ($addinKey) { $addinKey.Dispose() } }

    if ($targetMovedToBackup) {
        try { Remove-PatentMarkerSiblingDirectory -Candidate $backupDirectory -ExpectedParent $installParent }
        catch { Write-Warning "安装成功，但旧版备份未能清理：$backupDirectory" }
    }
    Write-Output "已安装 $progId（Office $bitness 位）至 $installFull"
    Write-Output "下次正常启动 PowerPoint 会自动打开：专利标注字典工具 · PowerPoint。"
    Write-Output "面板关闭或最小化后，从 Windows 任务栏或 Alt+Tab 恢复。入口与加载项检查见本包 README.md。"
}
catch {
    $failure = $_
    # Rollback order is the point of this block (G18): the add-in registration
    # may only be re-enabled after a complete product directory is proven to be
    # back in place. A registration pointing at a half-deleted directory is
    # worse than a disabled add-in.
    $registryCleared = $true
    foreach ($path in $registryPaths) {
        try { Remove-PatentMarkerRegistryTree -BaseKey $baseKey -Path $path }
        catch { $registryCleared = $false; Write-Warning "回滚注册表项失败：$path" }
    }

    # 1) Isolate this run's new directory first. It is a fragment of the failed
    #    upgrade, so it must never be mistaken for the old installation just
    #    because the original install path happens to exist.
    $quarantineDirectory = Assert-PatentMarkerSiblingDirectory -Candidate (
        Join-Path $installParent ('.' + $installLeaf + '.failed-' + [Guid]::NewGuid().ToString('N'))) `
        -ExpectedParent $installParent
    if ($newTargetInstalled -and (Test-Path -LiteralPath $installFull -PathType Container)) {
        if ($FaultInjectionPoint -in @('RollbackPartialDelete', 'RollbackRemovalBlocked')) {
            # Reproduce a failing recursive delete: the new directory keeps only
            # part of its files and is still present when the rollback starts.
            # One file stays present so the blocked case can hold it open.
            $victim = Join-Path $installFull 'PatentOffice.PowerPoint.dll'
            if (Test-Path -LiteralPath $victim) { Remove-Item -LiteralPath $victim -Force }
            Write-Warning '测试故障注入：新安装目录半删除。'
        }
        if ($FaultInjectionPoint -eq 'RollbackRemovalBlocked') {
            # The half-deleted new directory cannot be removed or renamed away.
            # A real open handle is used instead of skipping the call, so the
            # isolation and the removal both genuinely fail the way a locked or
            # ACL-protected fragment does.
            $script:g18BlockedHandle = [IO.File]::Open(
                (Join-Path $installFull 'Newtonsoft.Json.dll'),
                [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
            Write-Warning '测试故障注入：新安装残片被占用，无法隔离或移除。'
        }
        else {
            try { Move-Item -LiteralPath $installFull -Destination $quarantineDirectory }
            catch {
                Write-Warning "隔离本次新安装残片失败：$($_.Exception.Message)"
                try { Remove-PatentMarkerSiblingDirectory -Candidate $installFull -ExpectedParent $installParent }
                catch { Write-Warning "移除本次新安装残片失败：$installFull" }
            }
        }
    }

    # 2) Restore the old complete directory, then 3) verify its full product
    #    set and hashes before the registration is allowed back.
    $filesComplete = $false
    if ($targetMovedToBackup) {
        if (Test-Path -LiteralPath $installFull -PathType Container) {
            try { Remove-PatentMarkerSiblingDirectory -Candidate $installFull -ExpectedParent $installParent }
            catch { Write-Warning "清理阻挡旧目录还原的残片失败：$installFull" }
        }
        if (-not (Test-Path -LiteralPath $installFull)) {
            try { Move-Item -LiteralPath $backupDirectory -Destination $installFull }
            catch { Write-Warning "恢复旧安装目录失败：$installFull" }
        }
        if (Test-Path -LiteralPath $installFull -PathType Container) {
            try {
                [void](Assert-PatentMarkerOwnedDirectory -InstallDirectory $installFull)
                $filesComplete = $true
            }
            catch { Write-Warning "还原的旧产品目录未通过完整性与哈希核对：$($_.Exception.Message)" }
        }
    }
    else {
        # No previous installation existed: "complete" means nothing is left
        # behind for a registration to point at.
        $filesComplete = -not (Test-Path -LiteralPath $installFull)
    }

    # 4) Only now restore the original registration, and confirm the result.
    $registryRestored = $false
    if ($filesComplete) {
        $registryRestored = $registryCleared
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

    # 5) Clean up last, and only after both halves are confirmed back.
    if ($filesComplete -and $registryRestored) {
        if ($FaultInjectionPoint -eq 'CleanupFails') {
            Write-Warning ("测试故障注入：清理恢复材料失败。旧目录备份保留于：{0}；新目录残片：{1}" -f `
                $backupDirectory, $quarantineDirectory)
        }
        else {
            foreach ($leftover in @($quarantineDirectory, $backupDirectory)) {
                if ($leftover -and (Test-Path -LiteralPath $leftover -PathType Container)) {
                    try { Remove-PatentMarkerSiblingDirectory -Candidate $leftover -ExpectedParent $installParent }
                    catch { Write-Warning "清理回滚材料失败：$leftover" }
                }
            }
        }
        Write-Warning "安装未完成，已恢复旧版产品文件与原注册项：$installFull"
    }
    elseif ($filesComplete) {
        Write-Warning ("安装失败：旧版产品文件已还原，但原注册项未能恢复（加载项保持停用）。旧目录备份保留于：{0}" -f `
            $backupDirectory)
    }
    else {
        # Never claim a rollback we could not verify, and never point a live
        # registration at an incomplete directory.
        Write-Warning ("安装失败且未能确认旧版完整还原，加载项保持停用。旧目录备份保留于：{0}；新目录残片：{1}" -f `
            $backupDirectory, $quarantineDirectory)
    }
    throw $failure
}
finally {
    if ($script:g18BlockedHandle) {
        try { $script:g18BlockedHandle.Dispose() } catch { }
        $script:g18BlockedHandle = $null
    }
    if ($script:g22StageHandle) {
        try { $script:g22StageHandle.Dispose() } catch { }
        $script:g22StageHandle = $null
    }
    if (Test-Path -LiteralPath $stageDirectory -PathType Container) {
        try { Remove-PatentMarkerSiblingDirectory -Candidate $stageDirectory -ExpectedParent $installParent }
        catch { Write-Warning "暂存目录未能清理：$stageDirectory" }
    }
    $baseKey.Dispose()
}

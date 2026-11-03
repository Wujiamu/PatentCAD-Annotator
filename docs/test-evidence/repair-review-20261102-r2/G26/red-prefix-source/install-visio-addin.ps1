[CmdletBinding()]
param(
    [string]$SourceDirectory,
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'PatentMarker\OfficeAddin\Visio'),
    [ValidateSet('Auto', '32', '64')][string]$OfficeBitness = 'Auto',
    [string]$TestRegistryRoot,
    # Test-only injection points for the rollback order matrix (G18). They only
    # act inside the recovery catch, so a normal install is unaffected.
    [ValidateSet('None', 'AfterFiles', 'AfterRegistry', 'RollbackPartialDelete', 'RollbackRemovalBlocked', 'RegistryRestoreFails', 'CleanupFails', 'StagedCopyCorrupted', 'NewMoveFails')]
    [string]$FaultInjectionPoint = 'None'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Join-Path $PSScriptRoot 'dist\Visio'
}
Import-Module (Join-Path $PSScriptRoot 'VisioAddin.Common.psm1') -Force
$identity = Get-VisioAddinIdentity

if ([string]::IsNullOrWhiteSpace($TestRegistryRoot) -and (Get-Process -Name VISIO -ErrorAction SilentlyContinue)) {
    throw '请先关闭 Visio，再安装或升级加载项。'
}

$sourceFull = [IO.Path]::GetFullPath($SourceDirectory)
$installFull = [IO.Path]::GetFullPath($InstallDirectory)
$installParent = Split-Path -Parent $installFull
$installLeaf = Split-Path -Leaf $installFull
if ([string]::IsNullOrWhiteSpace($installLeaf)) { throw 'Visio 加载项安装目录无效。' }
if (-not (Test-Path -LiteralPath $sourceFull -PathType Container)) { throw "未找到构建文件目录：$sourceFull" }

$sourceNames = @('PatentOffice.Visio.dll', 'Newtonsoft.Json.dll')
foreach ($name in $sourceNames) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceFull $name) -PathType Leaf)) {
        throw "构建文件不完整，缺少 $name。请先运行 build-visio.ps1。"
    }
}

$bitness = if ($OfficeBitness -eq 'Auto') { Get-VisioOfficeBitness } else { $OfficeBitness }
$registryPaths = @(Get-VisioAddinRegistryPaths -TestRegistryRoot $TestRegistryRoot)
$registryRoot = if ([string]::IsNullOrWhiteSpace($TestRegistryRoot)) { 'Software' } else { $TestRegistryRoot.Trim('\') }
$classesRoot = $registryRoot + '\Classes'
$baseKey = Open-VisioAddinRegistry -OfficeBitness $bitness
$registrySnapshots = @()
foreach ($path in $registryPaths) {
    $registrySnapshots += ,(Get-VisioRegistrySnapshot -BaseKey $baseKey -Path $path)
}

# G22/R-029: whether a complete old Visio installation exists is decided here,
# before anything is staged or moved. The rollback must not re-derive it from
# "the old directory was moved": a staging failure leaves that installation in
# place and still installed.
$hadExistingInstall = $false
if (Test-Path -LiteralPath $installFull) {
    [void](Assert-VisioOwnedDirectory -InstallDirectory $installFull)
    $hadExistingInstall = $true
}
elseif ($registrySnapshots | Where-Object { $null -ne $_ } | Select-Object -First 1) {
    $baseKey.Dispose()
    throw '发现同名 Visio 注册项但没有产品文件所有权清单；为保护其他加载项，已停止安装。'
}

if (-not (Test-Path -LiteralPath $installParent -PathType Container)) {
    New-Item -ItemType Directory -Path $installParent -Force | Out-Null
}
$installParent = [IO.Path]::GetFullPath($installParent)
$stageDirectory = Join-Path $installParent ('.' + $installLeaf + '.stage-' + [Guid]::NewGuid().ToString('N'))
$backupDirectory = Join-Path $installParent ('.' + $installLeaf + '.rollback-' + [Guid]::NewGuid().ToString('N'))
$stageDirectory = Assert-VisioSiblingDirectory -Candidate $stageDirectory -ExpectedParent $installParent
$backupDirectory = Assert-VisioSiblingDirectory -Candidate $backupDirectory -ExpectedParent $installParent
$oldMoved = $false
$newInstalled = $false
$registrationTouched = $false

try {
    New-Item -ItemType Directory -Path $stageDirectory | Out-Null
    $hashes = [ordered]@{}
    foreach ($name in $sourceNames) {
        $source = Join-Path $sourceFull $name
        $staged = Join-Path $stageDirectory $name
        Copy-Item -LiteralPath $source -Destination $staged
        if ($FaultInjectionPoint -eq 'StagedCopyCorrupted' -and $name -eq $sourceNames[0]) {
            # Corrupt the staged copy so the production hash comparison below is
            # the code that fails, instead of the test skipping that check.
            [IO.File]::WriteAllText($staged, 'corrupted-by-test-injection')
        }
        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $stagedHash = (Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash
        if ($sourceHash -ne $stagedHash) { throw "暂存校验失败：$name" }
        $hashes[$name] = $stagedHash
    }

    $assembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $stageDirectory 'PatentOffice.Visio.dll'))
    $manifest = [ordered]@{
        ProductId = $identity.ProductId
        ClassId = $identity.ClassId
        ProgId = $identity.ProgId
        ProductVersion = $assembly.Version.ToString()
        OfficeBitness = $bitness
        InstalledAt = [DateTime]::UtcNow.ToString('o')
        Files = $hashes
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stageDirectory $identity.ManifestName) -Encoding UTF8

    if (Test-Path -LiteralPath $installFull) {
        $resolved = (Resolve-Path -LiteralPath $installFull).Path
        if (-not [string]::Equals($resolved, $installFull, [StringComparison]::OrdinalIgnoreCase)) {
            throw "安装目录解析到不同路径，已拒绝覆盖：$resolved"
        }
        Move-Item -LiteralPath $installFull -Destination $backupDirectory
        $oldMoved = $true
    }
    if ($FaultInjectionPoint -eq 'NewMoveFails') {
        # The old directory is already gone; hold a real handle on a staged file
        # so the rename below fails the way an in-use or virus-scanned file does.
        $script:g22StageHandle = [IO.File]::Open(
            (Join-Path $stageDirectory $sourceNames[0]),
            [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        Write-Warning '测试故障注入：新 Visio 安装目录无法移动到产品路径。'
    }
    Move-Item -LiteralPath $stageDirectory -Destination $installFull
    $newInstalled = $true
    if ($FaultInjectionPoint -eq 'AfterFiles') { throw '测试故障注入：Visio 产品文件部署后。' }

    $classPath = $registryPaths[0]
    $progPath = $registryPaths[1]
    $addinPath = $registryPaths[2]
    $inprocPath = $classPath + '\InprocServer32'
    $codeBase = ([Uri](Join-Path $installFull 'PatentOffice.Visio.dll')).AbsoluteUri

    # From here on the original Visio registration may be partly overwritten, so
    # the rollback has to clear and restore it instead of trusting it in place.
    $registrationTouched = $true
    Set-VisioRegistryValues -BaseKey $baseKey -Path $classPath -Values @{ '' = $identity.ClassName }
    Set-VisioRegistryValues -BaseKey $baseKey -Path $progPath -Values @{ '' = $identity.ClassName }
    Set-VisioRegistryValues -BaseKey $baseKey -Path $inprocPath -Values @{
        '' = 'mscoree.dll'
        ThreadingModel = 'Both'
        Class = $identity.ClassName
        Assembly = $assembly.FullName
        RuntimeVersion = 'v4.0.30319'
        CodeBase = $codeBase
    }
    Set-VisioRegistryValues -BaseKey $baseKey -Path ($progPath + '\CLSID') -Values @{ '' = $identity.ClassId }
    Set-VisioRegistryValues -BaseKey $baseKey -Path ($progPath + '\CurVer') -Values @{ '' = $identity.ProgId }
    Set-VisioRegistryValues -BaseKey $baseKey -Path ($classesRoot + '\CLSID\' + $identity.ClassId + '\ProgID') -Values @{ '' = $identity.ProgId }
    Set-VisioRegistryValues -BaseKey $baseKey -Path ($classesRoot + '\CLSID\' + $identity.ClassId + '\VersionIndependentProgID') -Values @{ '' = $identity.ProgId }
    Set-VisioRegistryValues -BaseKey $baseKey -Path $addinPath -Values @{
        FriendlyName = 'PatentMarker Word to Visio'
        Description = 'Read-only Word dictionary binding and Visio line annotations'
        LoadBehavior = @{ Kind = [Microsoft.Win32.RegistryValueKind]::DWord; Value = 3 }
    }
    if ($FaultInjectionPoint -in @('AfterRegistry', 'RollbackPartialDelete', 'RollbackRemovalBlocked', 'RegistryRestoreFails', 'CleanupFails')) {
        # Every one of these fails at the same real point (after the new files
        # and the new registration are in place); what differs is which stage of
        # the recovery catch then fails, so no recovery stage is faked away.
        throw '测试故障注入：Visio 注册表写入后。'
    }

    $installedManifest = Assert-VisioOwnedDirectory -InstallDirectory $installFull
    $addinKey = $baseKey.OpenSubKey($addinPath)
    try {
        if (-not $addinKey -or [int]$addinKey.GetValue('LoadBehavior', 0) -ne 3) {
            throw 'Visio 加载项注册检查失败：LoadBehavior 不是 3。'
        }
    }
    finally { if ($addinKey) { $addinKey.Dispose() } }

    if ($oldMoved) {
        try { Remove-VisioSiblingDirectory -Candidate $backupDirectory -ExpectedParent $installParent }
        catch { Write-Warning "安装成功，但旧版产品备份未能清理：$backupDirectory" }
    }
    Write-Output "已安装 $($identity.ProgId)（Visio $bitness 位）至 $installFull"
    Write-Output '下次正常启动 Visio 会自动打开：专利标注字典工具 · Visio。'
    Write-Output '面板关闭或最小化后，从 Windows 任务栏或 Alt+Tab 恢复。入口与加载项检查见本包 README.md。'
}
catch {
    $failure = $_
    # Rollback order is the point of this block (G18): the add-in registration
    # may only be re-enabled after a complete product directory is proven to be
    # back in place. A registration pointing at a half-deleted directory is
    # worse than a disabled add-in.
    # G22/R-029: a failure before the first registry write leaves the original
    # Visio registration untouched, and deleting it here is what used to make a
    # complete old installation unloadable.
    $registryCleared = $true
    if ($registrationTouched) {
        foreach ($path in $registryPaths) {
            try { Remove-VisioRegistryTree -BaseKey $baseKey -Path $path }
            catch { $registryCleared = $false; Write-Warning "回滚 Visio 注册项失败：$path" }
        }
    }

    # 1) Isolate this run's new directory first. It is a fragment of the failed
    #    upgrade, so it must never be mistaken for the old installation just
    #    because the original install path happens to exist.
    $quarantineDirectory = Assert-VisioSiblingDirectory -Candidate (
        Join-Path $installParent ('.' + $installLeaf + '.failed-' + [Guid]::NewGuid().ToString('N'))) `
        -ExpectedParent $installParent
    if ($newInstalled -and (Test-Path -LiteralPath $installFull -PathType Container)) {
        if ($FaultInjectionPoint -in @('RollbackPartialDelete', 'RollbackRemovalBlocked')) {
            # Reproduce a failing recursive delete: the new directory keeps only
            # part of its files and is still present when the rollback starts.
            # One file stays present so the blocked case can hold it open.
            $victim = Join-Path $installFull 'PatentOffice.Visio.dll'
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
            Write-Warning '测试故障注入：新 Visio 安装残片被占用，无法隔离或移除。'
        }
        else {
            try { Move-Item -LiteralPath $installFull -Destination $quarantineDirectory }
            catch {
                Write-Warning "隔离本次新 Visio 安装残片失败：$($_.Exception.Message)"
                try { Remove-VisioSiblingDirectory -Candidate $installFull -ExpectedParent $installParent }
                catch { Write-Warning "移除本次新 Visio 安装残片失败：$installFull" }
            }
        }
    }

    # 2) Restore the old complete directory, then 3) verify its full product
    #    set and hashes before the registration is allowed back.
    $filesComplete = $false
    if ($oldMoved) {
        if (Test-Path -LiteralPath $installFull -PathType Container) {
            try { Remove-VisioSiblingDirectory -Candidate $installFull -ExpectedParent $installParent }
            catch { Write-Warning "清理阻挡旧 Visio 目录还原的残片失败：$installFull" }
        }
        if (-not (Test-Path -LiteralPath $installFull)) {
            try { Move-Item -LiteralPath $backupDirectory -Destination $installFull }
            catch { Write-Warning "恢复旧 Visio 安装目录失败：$installFull" }
        }
        if (Test-Path -LiteralPath $installFull -PathType Container) {
            try {
                [void](Assert-VisioOwnedDirectory -InstallDirectory $installFull)
                $filesComplete = $true
            }
            catch { Write-Warning "还原的旧 Visio 产品目录未通过完整性与哈希核对：$($_.Exception.Message)" }
        }
    }
    elseif ($hadExistingInstall) {
        # A complete old Visio installation existed and was never moved: staging
        # failed before anything replaced it. Verify it where it stands and never
        # delete it; "the old directory was not moved" does not mean "nothing was
        # installed".
        if (Test-Path -LiteralPath $installFull -PathType Container) {
            try {
                [void](Assert-VisioOwnedDirectory -InstallDirectory $installFull)
                $filesComplete = $true
            }
            catch { Write-Warning "未移动的旧 Visio 产品目录未通过完整性与哈希核对：$($_.Exception.Message)" }
        }
    }
    else {
        # No previous installation existed: "complete" means nothing is left
        # behind for a registration to point at.
        $filesComplete = -not (Test-Path -LiteralPath $installFull)
    }

    # 4) Only now restore the original registration, and confirm the result.
    $registryRestored = $false
    if ($filesComplete -and -not $registrationTouched) {
        # This run never wrote a Visio registration value, so the original keys
        # were never disturbed. Confirm that instead of deleting and rewriting them.
        $registryRestored = $true
        for ($index = 0; $index -lt $registryPaths.Count; $index++) {
            if (-not $registryRestored) { break }
            try {
                Assert-VisioRegistrySnapshotRestored -BaseKey $baseKey -Path $registryPaths[$index] `
                    -Snapshot $registrySnapshots[$index]
            }
            catch {
                $registryRestored = $false
                Write-Warning "原 Visio 注册项已与安装前不一致：$($registryPaths[$index])：$($_.Exception.Message)"
            }
        }
    }
    elseif ($filesComplete) {
        $registryRestored = $registryCleared
        for ($index = 0; $index -lt $registryPaths.Count; $index++) {
            if (-not $registryRestored) { break }
            try {
                if ($FaultInjectionPoint -eq 'RegistryRestoreFails' -and $index -eq 0) {
                    throw '测试故障注入：注册表恢复单步失败。'
                }
                Restore-VisioRegistrySnapshot -BaseKey $baseKey -Snapshot $registrySnapshots[$index]
                Assert-VisioRegistrySnapshotRestored -BaseKey $baseKey -Path $registryPaths[$index] `
                    -Snapshot $registrySnapshots[$index]
            }
            catch {
                $registryRestored = $false
                Write-Warning "恢复原 Visio 注册项失败：$($registryPaths[$index])：$($_.Exception.Message)"
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
                    try { Remove-VisioSiblingDirectory -Candidate $leftover -ExpectedParent $installParent }
                    catch { Write-Warning "清理回滚材料失败：$leftover" }
                }
            }
        }
        Write-Warning "安装未完成，已恢复旧版 Visio 产品文件与原注册项：$installFull"
    }
    elseif ($filesComplete) {
        Write-Warning ("安装失败：旧版 Visio 产品文件已还原，但原注册项未能恢复（加载项保持停用）。旧目录备份保留于：{0}" -f `
            $backupDirectory)
    }
    else {
        # Never claim a rollback we could not verify, and never point a live
        # registration at an incomplete directory.
        Write-Warning ("安装失败且未能确认旧版完整还原，Visio 加载项保持停用。旧目录备份保留于：{0}；新目录残片：{1}" -f `
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
        try { Remove-VisioSiblingDirectory -Candidate $stageDirectory -ExpectedParent $installParent }
        catch { Write-Warning "Visio 暂存目录未能清理：$stageDirectory" }
    }
    $baseKey.Dispose()
}

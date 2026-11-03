[CmdletBinding()]
param(
    [string]$SourceDirectory
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Join-Path $PSScriptRoot 'dist\Visio'
}
Import-Module (Join-Path $PSScriptRoot 'VisioAddin.Common.psm1') -Force
$installScript = Join-Path $PSScriptRoot 'install-visio-addin.ps1'
$uninstallScript = Join-Path $PSScriptRoot 'uninstall-visio-addin.ps1'
$powershellExe = (Get-Command powershell.exe -ErrorAction Stop).Source
$sourceFull = [IO.Path]::GetFullPath($SourceDirectory)
foreach ($name in @('PatentOffice.Visio.dll', 'Newtonsoft.Json.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceFull $name) -PathType Leaf)) {
        throw "缺少 $name；请先构建 Visio 加载项。"
    }
}

$testRoot = 'Software\PatentMarkerTests\Visio-' + [Guid]::NewGuid().ToString('N')
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('PatentMarkerVisioInstaller-' + [Guid]::NewGuid().ToString('N'))
$target = Join-Path $temporaryRoot 'install\Visio'
$candidate = Join-Path $temporaryRoot 'candidate'
$sentinelFile = Join-Path $temporaryRoot 'user-data-sentinel.txt'
$sentinelPath = $testRoot + '\Microsoft\Visio\Addins\UnrelatedAddin'
$registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
    [Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)

function Assert-VisioInstallerCondition([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Invoke-VisioInstallerScript([string]$ScriptPath, [string[]]$Arguments, [bool]$ExpectedFailure) {
    $previousErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $powershellExe -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorAction }
    if ($ExpectedFailure -and $exitCode -eq 0) {
        throw ("预期失败的安装器场景返回成功：" + $ScriptPath + " | " + ($output -join " | "))
    }
    if (-not $ExpectedFailure -and $exitCode -ne 0) {
        throw ("安装器场景失败（退出码 " + $exitCode + "）：" + $ScriptPath + " | " + ($output -join " | "))
    }
}

function Test-VisioRegistryPath([string]$Path) {
    $key = $registry.OpenSubKey($Path)
    if (-not $key) { return $false }
    $key.Dispose()
    return $true
}

try {
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    New-Item -ItemType Directory -Path $candidate -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceFull 'PatentOffice.Visio.dll') -Destination $candidate
    Copy-Item -LiteralPath (Join-Path $sourceFull 'Newtonsoft.Json.dll') -Destination $candidate
    Set-Content -LiteralPath $sentinelFile -Value 'user-data-sentinel' -Encoding UTF8

    $sentinel = $registry.CreateSubKey($sentinelPath)
    try { $sentinel.SetValue('FriendlyName', 'Unrelated Visio Add-in', [Microsoft.Win32.RegistryValueKind]::String) }
    finally { $sentinel.Dispose() }

    $common = @('-SourceDirectory', $sourceFull, '-InstallDirectory', $target,
        '-OfficeBitness', '64', '-TestRegistryRoot', $testRoot)
    Invoke-VisioInstallerScript $installScript ($common + @('-FaultInjectionPoint', 'AfterFiles')) $true
    Assert-VisioInstallerCondition (-not (Test-Path -LiteralPath $target)) '部署文件故障后，临时安装目录没有回滚。'

    $paths = @(Get-VisioAddinRegistryPaths -TestRegistryRoot $testRoot)
    foreach ($path in $paths) {
        Assert-VisioInstallerCondition (-not (Test-VisioRegistryPath $path)) ("首次部署故障后残留产品注册项：" + $path)
    }
    Assert-VisioInstallerCondition (Test-Path -LiteralPath $sentinelFile) '隔离安装删除了旁边的哨兵文件。'
    Assert-VisioInstallerCondition (Test-VisioRegistryPath $sentinelPath) '隔离安装更改了无关 Visio 注册项。'

    Invoke-VisioInstallerScript $installScript ($common + @('-FaultInjectionPoint', 'None')) $false
    $oldHash = (Get-FileHash -LiteralPath (Join-Path $target 'PatentOffice.Visio.dll') -Algorithm SHA256).Hash
    $oldJsonHash = (Get-FileHash -LiteralPath (Join-Path $target 'Newtonsoft.Json.dll') -Algorithm SHA256).Hash
    Assert-VisioInstallerCondition (Test-Path -LiteralPath (Join-Path $target 'install-manifest.json')) '成功安装没有写入所有权清单。'

    Add-Content -LiteralPath (Join-Path $candidate 'Newtonsoft.Json.dll') -Value 'isolated-upgrade-marker'
    $upgrade = @('-SourceDirectory', $candidate, '-InstallDirectory', $target,
        '-OfficeBitness', '64', '-TestRegistryRoot', $testRoot, '-FaultInjectionPoint', 'AfterRegistry')
    Invoke-VisioInstallerScript $installScript $upgrade $true
    $restoredHash = (Get-FileHash -LiteralPath (Join-Path $target 'PatentOffice.Visio.dll') -Algorithm SHA256).Hash
    $restoredJsonHash = (Get-FileHash -LiteralPath (Join-Path $target 'Newtonsoft.Json.dll') -Algorithm SHA256).Hash
    Assert-VisioInstallerCondition ($restoredHash -eq $oldHash) '注册表故障后的升级回滚没有恢复旧版 DLL。'
    Assert-VisioInstallerCondition ($restoredJsonHash -eq $oldJsonHash) '注册表故障后的升级回滚没有恢复旧版 Newtonsoft.Json。'
    Assert-VisioInstallerCondition (Test-VisioRegistryPath $sentinelPath) '升级回滚更改了无关 Visio 注册项。'

    Invoke-VisioInstallerScript $installScript ($common + @('-FaultInjectionPoint', 'None')) $false
    $uninstallArguments = @('-InstallDirectory', $target, '-OfficeBitness', '64', '-TestRegistryRoot', $testRoot)
    Invoke-VisioInstallerScript $uninstallScript $uninstallArguments $false
    Assert-VisioInstallerCondition (-not (Test-Path -LiteralPath $target)) '卸载后产品目录仍存在。'
    foreach ($path in $paths) {
        Assert-VisioInstallerCondition (-not (Test-VisioRegistryPath $path)) ("卸载后残留产品注册项：" + $path)
    }
    Assert-VisioInstallerCondition (Test-VisioRegistryPath $sentinelPath) '卸载移除了无关 Visio 注册项。'
    Assert-VisioInstallerCondition (Test-Path -LiteralPath $sentinelFile) '卸载删除了旁边的哨兵文件。'

    # --- G14 / R-029: partial-delete recovery -------------------------------
    # A recursive delete can remove part of the directory before it throws.
    # Two honest outcomes are allowed: the complete file set is restored from
    # the verified copy, or the copy is kept and named. Renaming a leftover
    # half-deleted directory back into place is never PASS.
    Invoke-VisioInstallerScript $installScript ($common + @('-FaultInjectionPoint', 'None')) $false
    $g14Files = @{}
    foreach ($name in @('PatentOffice.Visio.dll', 'Newtonsoft.Json.dll', 'install-manifest.json')) {
        $g14Files[$name] = (Get-FileHash -LiteralPath (Join-Path $target $name) -Algorithm SHA256).Hash
    }
    $g14Registry = @{}
    foreach ($path in $paths) { $g14Registry[$path] = (Test-VisioRegistryPath $path) }

    # Windows refuses to rename a directory whose child is open, so locking a
    # file never reaches the recursive delete. Inject the failure at the delete
    # stage instead: one staged file is already gone when the delete throws,
    # which is exactly the state that used to be renamed back as "restored".
    $deleteFailed = $false
    try {
        Invoke-VisioInstallerScript $uninstallScript ($uninstallArguments + @('-FaultInjectionPoint', 'AfterPartialDelete')) $false
    }
    catch { $deleteFailed = $true }
    if (-not $deleteFailed) { throw 'G14: 删除阶段故障注入后 Visio 卸载仍然报告成功。' }

    $recoveryCopies = @(Get-ChildItem -LiteralPath (Split-Path -Parent $target) -Force -Directory |
        Where-Object { $_.Name -like '.*.recovery-*' })
    $intact = $true
    foreach ($name in $g14Files.Keys) {
        $file = Join-Path $target $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { $intact = $false; continue }
        try {
            if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $g14Files[$name]) { $intact = $false }
        }
        catch { $intact = $false }
    }
    if ($intact) {
        foreach ($path in $paths) {
            Assert-VisioInstallerCondition ((Test-VisioRegistryPath $path) -eq $g14Registry[$path]) `
                "G14: 文件已还原但 Visio 注册项未恢复到卸载前状态：$path"
        }
        Assert-VisioInstallerCondition ($recoveryCopies.Count -eq 0) 'G14: 完整还原后仍残留恢复副本。'
        Write-Output 'PASS: G14 Visio 部分删除失败后从恢复副本还原完整文件集与注册项。'
    }
    else {
        Assert-VisioInstallerCondition ($recoveryCopies.Count -gt 0) 'G14: Visio 文件未完整还原，却也没有保留恢复副本。'
        foreach ($copy in $recoveryCopies) {
            foreach ($name in $g14Files.Keys) {
                $mirror = Join-Path $copy.FullName $name
                Assert-VisioInstallerCondition ((Test-Path -LiteralPath $mirror -PathType Leaf) -and
                    (Get-FileHash -LiteralPath $mirror -Algorithm SHA256).Hash -eq $g14Files[$name]) `
                    "G14: Visio 恢复副本不完整：$name"
            }
        }
        Write-Output ('PASS: G14 Visio 无法就地还原时保留完整恢复副本并明确报告（{0}）。' -f ($recoveryCopies.Name -join ', '))
        foreach ($copy in $recoveryCopies) { Remove-Item -LiteralPath $copy.FullName -Recurse -Force }
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        foreach ($path in $paths) { if (Test-VisioRegistryPath $path) { Remove-VisioRegistryTree -BaseKey $registry -Path $path } }
        Invoke-VisioInstallerScript $installScript ($common + @('-FaultInjectionPoint', 'None')) $false
    }

    Invoke-VisioInstallerScript $uninstallScript $uninstallArguments $false
    Assert-VisioInstallerCondition (-not (Test-Path -LiteralPath $target)) 'G14: 恢复后再次卸载未移除产品目录。'

    # --- G18 / R-029: file verification must precede registration restore ----
    # Each case breaks one rollback stage and asserts the contract: the original
    # Visio registration is re-enabled only after the complete product set is
    # back and verified; a failed rollback leaves the add-in disabled and keeps
    # the material; non-product sentinels never change.
    $g18 = [ordered]@{}
    function Add-G18Result([string]$Name, [bool]$Passed, [string]$Detail) {
        $script:g18[$Name] = [pscustomobject]@{ Passed = $Passed; Detail = $Detail }
    }
    function Get-G18ProductKeys {
        $present = @()
        foreach ($path in $paths) { if (Test-VisioRegistryPath $path) { $present += $path } }
        return ($present -join '|')
    }
    function Assert-G18Sentinel([string]$Stage) {
        Assert-VisioInstallerCondition (Test-VisioRegistryPath $sentinelPath) "G18($Stage): 无关 Visio 注册项丢失。"
        Assert-VisioInstallerCondition (Test-Path -LiteralPath $sentinelFile) "G18($Stage): 旁边哨兵文件丢失。"
    }
    function Get-G18Leftovers {
        return @(Get-ChildItem -LiteralPath (Split-Path -Parent $target) -Force -Directory |
            Where-Object { $_.Name -like '.*.recovery-*' -or $_.Name -like '.*.uninstall-*' -or $_.Name -like '.*.rollback-*' -or $_.Name -like '.*.failed-*' -or $_.Name -like '.*.stage-*' })
    }
    function Get-G18Files([string[]]$Names) {
        $map = @{}
        foreach ($name in $Names) {
            $map[$name] = (Get-FileHash -LiteralPath (Join-Path $target $name) -Algorithm SHA256).Hash
        }
        return $map
    }
    function Test-G18FilesMatch([hashtable]$Expected) {
        foreach ($name in $Expected.Keys) {
            $file = Join-Path $target $name
            if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { return $false }
            if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $Expected[$name]) { return $false }
        }
        return $true
    }
    $productNames = @('PatentOffice.Visio.dll', 'Newtonsoft.Json.dll', 'install-manifest.json')

    # --- G18-a: uninstall, file restore fails -> registration stays disabled --
    Invoke-VisioInstallerScript $installScript ($common + @('-FaultInjectionPoint', 'None')) $false
    $g18BaseFiles = Get-G18Files $productNames
        # Invoke-VisioInstallerScript throws when the exit code is not the
    # expected one, so this call itself proves the reported failure.
        Invoke-VisioInstallerScript $uninstallScript ($uninstallArguments + @('-FaultInjectionPoint', 'PartialDeleteThenRestoreFails')) $true
    $afterA = Get-G18ProductKeys
    Add-G18Result 'uninstall_file_restore_failed_registration_disabled' ($afterA -eq '') `
        $(if ($afterA -eq '') { '注册项全部移除，加载项保持停用' } else { "仍存在产品注册项：$afterA" })
    $leftoversA = @(Get-G18Leftovers)
    $recoveryA = @($leftoversA | Where-Object { $_.Name -like '.*.recovery-*' })
    if ($recoveryA.Count -eq 0) {
        Add-G18Result 'uninstall_file_restore_failed_material_kept' $false '恢复失败却没有保留恢复副本'
    }
    else {
        $complete = $true
        foreach ($copy in $recoveryA) {
            foreach ($name in $g18BaseFiles.Keys) {
                $mirror = Join-Path $copy.FullName $name
                if (-not (Test-Path -LiteralPath $mirror -PathType Leaf) -or
                    (Get-FileHash -LiteralPath $mirror -Algorithm SHA256).Hash -ne $g18BaseFiles[$name]) { $complete = $false }
            }
        }
        Add-G18Result 'uninstall_file_restore_failed_material_kept' $complete `
            $(if ($complete) { '保留的恢复副本完整' } else { '保留的恢复副本不完整' })
    }
    Assert-G18Sentinel 'uninstall_file_restore_failed'

    foreach ($copy in $leftoversA) { Remove-Item -LiteralPath $copy.FullName -Recurse -Force }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    foreach ($path in $paths) { if (Test-VisioRegistryPath $path) { Remove-VisioRegistryTree -BaseKey $registry -Path $path } }

    # --- G18-b: uninstall, registration restore fails -> disabled + material --
    Invoke-VisioInstallerScript $installScript ($common + @('-FaultInjectionPoint', 'None')) $false
        # Invoke-VisioInstallerScript throws when the exit code is not the
    # expected one, so this call itself proves the reported failure.
        Invoke-VisioInstallerScript $uninstallScript ($uninstallArguments + @('-FaultInjectionPoint', 'RegistryRestoreFails')) $true
    $afterB = Get-G18ProductKeys
    Add-G18Result 'uninstall_registry_restore_failed_disabled' ($afterB -eq '') `
        $(if ($afterB -eq '') { '注册恢复失败后加载项保持停用' } else { "注册项未全部保持移除：$afterB" })
    $leftoversB = @(Get-G18Leftovers)
    Add-G18Result 'uninstall_registry_restore_failed_material_kept' `
        (@($leftoversB | Where-Object { $_.Name -like '.*.recovery-*' }).Count -gt 0) `
        ("残留回滚材料：{0}" -f ($leftoversB.Name -join ', '))
    Assert-G18Sentinel 'uninstall_registry_restore_failed'

    foreach ($copy in $leftoversB) { Remove-Item -LiteralPath $copy.FullName -Recurse -Force }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    foreach ($path in $paths) { if (Test-VisioRegistryPath $path) { Remove-VisioRegistryTree -BaseKey $registry -Path $path } }

    # --- G18-c: uninstall, cleanup fails -> files+registration back, honest ---
    Invoke-VisioInstallerScript $installScript ($common + @('-FaultInjectionPoint', 'None')) $false
    $g18cFiles = Get-G18Files $productNames
    $g18cRegistry = @{}
    foreach ($path in $paths) { $g18cRegistry[$path] = (Test-VisioRegistryPath $path) }
        # Invoke-VisioInstallerScript throws when the exit code is not the
    # expected one, so this call itself proves the reported failure.
        Invoke-VisioInstallerScript $uninstallScript ($uninstallArguments + @('-FaultInjectionPoint', 'CleanupFails')) $true
    Add-G18Result 'uninstall_cleanup_failed_files_complete' (Test-G18FilesMatch $g18cFiles) `
        '清理失败时 Visio 产品文件仍为完整集合'
    $registryBack = $true
    foreach ($path in $paths) {
        if ((Test-VisioRegistryPath $path) -ne $g18cRegistry[$path]) { $registryBack = $false }
    }
    Add-G18Result 'uninstall_cleanup_failed_registration_restored' $registryBack '清理失败时原 Visio 注册项已恢复'
    $leftoversC = @(Get-G18Leftovers)
    Add-G18Result 'uninstall_cleanup_failed_material_kept' ($leftoversC.Count -gt 0) `
        ("残留回滚材料：{0}" -f ($leftoversC.Name -join ', '))
    Assert-G18Sentinel 'uninstall_cleanup_failed'
    foreach ($copy in $leftoversC) { Remove-Item -LiteralPath $copy.FullName -Recurse -Force }

    # --- G18-d: upgrade, new directory half-deleted -> old dir restored ------
    Invoke-VisioInstallerScript $installScript ($common + @('-FaultInjectionPoint', 'None')) $false
    $g18dFiles = Get-G18Files $productNames
    $g18dRegistry = @{}
    foreach ($path in $paths) { $g18dRegistry[$path] = (Test-VisioRegistryPath $path) }
        # Invoke-VisioInstallerScript throws when the exit code is not the
    # expected one, so this call itself proves the reported failure.
        Invoke-VisioInstallerScript $installScript ($common + @('-FaultInjectionPoint', 'RollbackPartialDelete')) $true
    Add-G18Result 'upgrade_new_dir_half_deleted_old_files_restored' (Test-G18FilesMatch $g18dFiles) `
        '升级新目录半删除后旧完整 Visio 目录与哈希已还原'
    $registryBackD = $true
    foreach ($path in $paths) {
        if ((Test-VisioRegistryPath $path) -ne $g18dRegistry[$path]) { $registryBackD = $false }
    }
    Add-G18Result 'upgrade_new_dir_half_deleted_registration_restored' $registryBackD '升级回滚后原 Visio 注册项已恢复'
    $leftoversD = @(Get-G18Leftovers)
    Add-G18Result 'upgrade_new_dir_half_deleted_material_cleaned' ($leftoversD.Count -eq 0) `
        ("残留回滚材料：{0}" -f ($leftoversD.Name -join ', '))
    Assert-G18Sentinel 'upgrade_new_dir_half_deleted'

    # --- G18-e: upgrade, registration restore fails -> disabled, files back --
        # Invoke-VisioInstallerScript throws when the exit code is not the
    # expected one, so this call itself proves the reported failure.
        Invoke-VisioInstallerScript $installScript ($common + @('-FaultInjectionPoint', 'RegistryRestoreFails')) $true
    $afterE = Get-G18ProductKeys
    Add-G18Result 'upgrade_registry_restore_failed_disabled' ($afterE -eq '') `
        $(if ($afterE -eq '') { '注册恢复失败后 Visio 加载项保持停用' } else { "仍存在产品注册项：$afterE" })
    Add-G18Result 'upgrade_registry_restore_failed_old_files_intact' (Test-G18FilesMatch $g18dFiles) `
        'Visio 注册恢复失败时旧版文件仍完整'
    $leftoversE = @(Get-G18Leftovers)
    Add-G18Result 'upgrade_registry_restore_failed_material_kept' ($leftoversE.Count -gt 0) `
        ("残留回滚材料：{0}" -f ($leftoversE.Name -join ', '))
    Assert-G18Sentinel 'upgrade_registry_restore_failed'

    foreach ($copy in $leftoversE) { Remove-Item -LiteralPath $copy.FullName -Recurse -Force }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    foreach ($path in $paths) { if (Test-VisioRegistryPath $path) { Remove-VisioRegistryTree -BaseKey $registry -Path $path } }
    Invoke-VisioInstallerScript $installScript ($common + @('-FaultInjectionPoint', 'None')) $false

    # --- G18-f: upgrade, half-deleted new dir cannot be removed --------------
    # The branch the review flagged: the original install path still exists, so
    # the pre-fix code skipped restoring the old directory. The honest outcome
    # is a disabled add-in plus kept material, never a registration pointing at
    # the fragment.
    Invoke-VisioInstallerScript $installScript ($common + @('-FaultInjectionPoint', 'RollbackRemovalBlocked')) $true
    $afterF = Get-G18ProductKeys
    Add-G18Result 'upgrade_residue_removal_blocked_registration_disabled' ($afterF -eq '') `
        $(if ($afterF -eq '') { '残片无法隔离时 Visio 加载项保持停用' } else { "仍存在指向残片的产品注册项：$afterF" })
    $leftoversF = @(Get-G18Leftovers)
    $keptF = @($leftoversF | Where-Object { $_.Name -like '.*.rollback-*' -or $_.Name -like '.*.failed-*' -or $_.Name -like '.*.recovery-*' })
    Add-G18Result 'upgrade_residue_removal_blocked_material_kept' ($keptF.Count -gt 0) `
        ("保留的回滚材料：{0}" -f ($keptF.Name -join ', '))
    Assert-G18Sentinel 'upgrade_residue_removal_blocked'

    foreach ($copy in $leftoversF) { Remove-Item -LiteralPath $copy.FullName -Recurse -Force }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    foreach ($path in $paths) { if (Test-VisioRegistryPath $path) { Remove-VisioRegistryTree -BaseKey $registry -Path $path } }
    Invoke-VisioInstallerScript $installScript ($common + @('-FaultInjectionPoint', 'None')) $false

    $g18Failed = @($g18.Keys | Where-Object { -not $g18[$_].Passed })
    foreach ($name in $g18.Keys) {
        $mark = if ($g18[$name].Passed) { 'PASS' } else { 'FAIL' }
        Write-Output ("{0}: G18 Visio {1} — {2}" -f $mark, $name, $g18[$name].Detail)
    }
    if ($g18Failed.Count -gt 0) {
        throw ("G18: {0} 条 Visio 断言失败：{1}" -f $g18Failed.Count, ($g18Failed -join ', '))
    }

    # --- G22 / R-029: an old install that exists is not an old install that moved
    # Every cell starts from a complete old Visio installation with its original
    # registration, injects a failure, and then asserts four things that must not
    # change: the old file bytes, the complete product file set plus its ownership
    # manifest, the original registration values, and a non-project sentinel.
    #
    # Two of the failures are produced by the operating system rather than by an
    # installer flag, so "the staging/move step failed" is a genuine environment
    # failure and not a skipped call:
    #   * stage creation -> a Deny/CreateDirectories ACE on the install parent
    #   * old-dir Move   -> a real open handle inside the old product directory
    $g22 = [ordered]@{}
    $g22ProductNames = @('PatentOffice.Visio.dll', 'Newtonsoft.Json.dll', 'install-manifest.json')
    $g22Parent = Split-Path -Parent $target
    $g22SentinelText = 'g22-sentinel-' + [Guid]::NewGuid().ToString('N')
    $g22SentinelFile = Join-Path $temporaryRoot 'g22-unrelated-sentinel.txt'
    [IO.File]::WriteAllText($g22SentinelFile, $g22SentinelText)

    function Add-G22Result([string]$Name, [bool]$Passed, [string]$Detail) {
        $script:g22[$Name] = [pscustomobject]@{ Passed = $Passed; Detail = $Detail }
    }
    function Get-G22Baseline {
        $files = @{}
        foreach ($name in $g22ProductNames) {
            $files[$name] = (Get-FileHash -LiteralPath (Join-Path $target $name) -Algorithm SHA256).Hash
        }
        # Do not name this $registry: that identifier is the script-scope RegistryKey.
        $registryState = @{}
        foreach ($path in $paths) {
            $registryState[$path] = (Get-VisioRegistrySnapshot -BaseKey $registry -Path $path | ConvertTo-Json -Depth 12 -Compress)
        }
        return [pscustomobject]@{
            Files        = $files
            ManifestText = [IO.File]::ReadAllText((Join-Path $target 'install-manifest.json'))
            Registry     = $registryState
        }
    }
    function Test-G22ProductDirectory([psobject]$Baseline) {
        if (-not (Test-Path -LiteralPath $target -PathType Container)) { return $false }
        $actual = @(Get-ChildItem -LiteralPath $target -Force)
        if ($actual.Count -ne $g22ProductNames.Count) { return $false }
        foreach ($entry in $actual) {
            if ($entry.PSIsContainer -or $g22ProductNames -notcontains $entry.Name) { return $false }
        }
        foreach ($name in $g22ProductNames) {
            if ((Get-FileHash -LiteralPath (Join-Path $target $name) -Algorithm SHA256).Hash -ne
                $Baseline.Files[$name]) { return $false }
        }
        return ([IO.File]::ReadAllText((Join-Path $target 'install-manifest.json')) -ceq $Baseline.ManifestText)
    }
    function Test-G22Registration([psobject]$Baseline) {
        foreach ($path in $paths) {
            $current = (Get-VisioRegistrySnapshot -BaseKey $registry -Path $path | ConvertTo-Json -Depth 12 -Compress)
            if ($current -ne $Baseline.Registry[$path]) { return $false }
        }
        return $true
    }
    function Test-G22Sentinel {
        if (-not (Test-Path -LiteralPath $g22SentinelFile -PathType Leaf)) { return $false }
        if ([IO.File]::ReadAllText($g22SentinelFile) -cne $g22SentinelText) { return $false }
        return (Test-VisioRegistryPath $sentinelPath)
    }
    function Reset-G22State {
        foreach ($copy in @(Get-G18Leftovers)) { Remove-Item -LiteralPath $copy.FullName -Recurse -Force }
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        foreach ($path in $paths) { if (Test-VisioRegistryPath $path) { Remove-VisioRegistryTree -BaseKey $registry -Path $path } }
    }
    function Install-G22Baseline {
        Reset-G22State
        Invoke-VisioInstallerScript $installScript ($common + @('-FaultInjectionPoint', 'None')) $false
        return (Get-G22Baseline)
    }
    function New-G22AclRule {
        return (New-Object System.Security.AccessControl.FileSystemAccessRule(
            ([Security.Principal.WindowsIdentity]::GetCurrent().Name),
            [System.Security.AccessControl.FileSystemRights]'CreateDirectories',
            [System.Security.AccessControl.AccessControlType]'Deny'))
    }
    function Disable-G22ParentCreate([string]$Parent) {
        $rule = New-G22AclRule
        $acl = Get-Acl -LiteralPath $Parent
        $acl.AddAccessRule($rule)
        Set-Acl -LiteralPath $Parent -AclObject $acl
        return $rule
    }
    function Enable-G22ParentCreate([string]$Parent, $Rule) {
        if ($null -eq $Rule) { return }
        $acl = Get-Acl -LiteralPath $Parent
        $acl.RemoveAccessRule($Rule) | Out-Null
        Set-Acl -LiteralPath $Parent -AclObject $acl
    }
    function Invoke-G22Installer {
        # Streams every message (warnings included) into a list as it is produced.
        # A pipeline that throws never completes its assignment, so collecting with
        # "$out = @(& $script ...)" silently drops the warnings that identify which
        # stage actually failed.
        param([string[]]$Arguments, [bool]$ExpectedFailure)
        $script:g22LastOutput = New-Object System.Collections.Generic.List[string]
        $previousErrorAction = $ErrorActionPreference
        $exitCode = 0
        try {
            $ErrorActionPreference = 'Continue'
            $raw = @(& $powershellExe -NoProfile -ExecutionPolicy Bypass -File $installScript @Arguments 2>&1)
            $exitCode = $LASTEXITCODE
        }
        finally { $ErrorActionPreference = $previousErrorAction }
        foreach ($line in $raw) { $script:g22LastOutput.Add([string]$line) }
        if ($ExpectedFailure) { return ($exitCode -ne 0) }
        return ($exitCode -eq 0)
    }
    function Invoke-G22Case {
        param(
            [string]$Name,
            [string[]]$Arguments,
            [scriptblock]$Inject,
            [scriptblock]$Release,
            [string]$ExpectOutput
        )
        $baseline = Install-G22Baseline
        $failed = $false
        try {
            if ($Inject) { & $Inject }
            $failed = Invoke-G22Installer $Arguments $true
        }
        finally { if ($Release) { & $Release } }
        $flat = ((@($script:g22LastOutput) -join ' ') -replace '\s+', ' ').Trim()
        if (-not $failed) {
            Add-G22Result ($Name + '_installer_failed') $false '故障注入后 Visio 安装仍然报告成功'
            return
        }
        Add-G22Result ($Name + '_failure_origin') ($flat -match $ExpectOutput) `
            $(if ($flat -match $ExpectOutput) { "失败阶段匹配 '$ExpectOutput'" }
              else { "失败不在预期阶段（应匹配 '$ExpectOutput'）：$flat" })
        $filesOk = Test-G22ProductDirectory $baseline
        Add-G22Result ($Name + '_old_files') $filesOk `
            $(if ($filesOk) { '旧 Visio 文件字节、完整文件集与所有权清单保持不变' } else { '旧 Visio 产品目录被改动' })
        $regOk = Test-G22Registration $baseline
        Add-G22Result ($Name + '_registration') $regOk `
            $(if ($regOk) { '原 Visio 注册值保持不变' } else { "原 Visio 注册值被改动：$(Get-G18ProductKeys)" })
        Add-G22Result ($Name + '_sentinel') (Test-G22Sentinel) '非项目哨兵文件与注册项保持不变'
    }

    # (1) First Visio install: the stage directory cannot be created. Nothing
    #     existed before, so "complete" still means "nothing left behind".
    Reset-G22State
    $g22AclRule = Disable-G22ParentCreate $g22Parent
    $firstFailed = $false
    try { $firstFailed = Invoke-G22Installer ($common + @('-FaultInjectionPoint', 'None')) $true }
    finally { Enable-G22ParentCreate $g22Parent $g22AclRule }
    $firstFlat = ((@($script:g22LastOutput) -join ' ') -replace '\s+', ' ').Trim()
    Add-G22Result 'first_install_stage_failed_installer_failed' $firstFailed `
        $(if ($firstFailed) { '阶段创建失败后 Visio 安装返回失败' } else { '阶段创建失败后 Visio 安装仍然报告成功' })
    Add-G22Result 'first_install_stage_failed_failure_origin' ($firstFlat -match 'stage-') `
        $(if ($firstFlat -match 'stage-') { '失败来自暂存目录创建' } else { "失败来源不符：$firstFlat" })
    Add-G22Result 'first_install_stage_failed_no_product_dir' (-not (Test-Path -LiteralPath $target)) `
        '首次 Visio 安装早期失败后未留下产品目录'
    Add-G22Result 'first_install_stage_failed_registration' ((Get-G18ProductKeys) -eq '') `
        '首次 Visio 安装早期失败后没有产品注册项'
    Add-G22Result 'first_install_stage_failed_sentinel' (Test-G22Sentinel) '非项目哨兵保持不变'

    # (2) Upgrade, stage creation fails. The old installation is complete and was
    #     never moved: it must stay usable and keep its registration.
    Invoke-G22Case -Name 'upgrade_stage_failed' -Arguments ($common + @('-FaultInjectionPoint', 'None')) `
        -Inject { $script:g22CaseRule = Disable-G22ParentCreate $g22Parent } `
        -Release { Enable-G22ParentCreate $g22Parent $script:g22CaseRule; $script:g22CaseRule = $null } `
        -ExpectOutput 'stage-'

    # (3) Upgrade, the staged copy fails its hash check (this round's defect).
    Invoke-G22Case -Name 'upgrade_staged_copy_failed' -Arguments ($common + @('-FaultInjectionPoint', 'StagedCopyCorrupted')) `
        -ExpectOutput '暂存校验失败'

    # (4) Upgrade, moving the old directory fails (this round's defect). The old
    #     directory is complete and still in place when the rollback starts.
    Invoke-G22Case -Name 'upgrade_old_move_failed' -Arguments ($common + @('-FaultInjectionPoint', 'None')) `
        -Inject {
            $script:g22OldHandle = [IO.File]::Open(
                (Join-Path $target 'Newtonsoft.Json.dll'),
                [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        } `
        -Release { if ($script:g22OldHandle) { $script:g22OldHandle.Dispose(); $script:g22OldHandle = $null } } `
        -ExpectOutput 'denied|being used|拒绝访问|另一进程'

    # (5) Upgrade, the old directory was moved away and the new directory cannot
    #     be moved into place: the old one has to come back from the backup.
    Invoke-G22Case -Name 'upgrade_new_move_failed' -Arguments ($common + @('-FaultInjectionPoint', 'NewMoveFails')) `
        -ExpectOutput '新 Visio 安装目录无法移动|\.Visio\.stage-'

    # (6) Upgrade, the new directory is fully deployed and the run then fails
    #     before any registration value is written.
    Invoke-G22Case -Name 'upgrade_after_files_failed' -Arguments ($common + @('-FaultInjectionPoint', 'AfterFiles')) `
        -ExpectOutput 'Visio 产品文件部署后'

    $g22Failed = @($g22.Keys | Where-Object { -not $g22[$_].Passed })
    foreach ($name in $g22.Keys) {
        $mark = if ($g22[$name].Passed) { 'PASS' } else { 'FAIL' }
        Write-Output ("{0}: G22 Visio {1} — {2}" -f $mark, $name, $g22[$name].Detail)
    }
    if ($g22Failed.Count -gt 0) {
        throw ("G22: {0} 条 Visio 断言失败：{1}" -f $g22Failed.Count, ($g22Failed -join ', '))
    }
    if (Test-Path -LiteralPath $g22SentinelFile -PathType Leaf) { Remove-Item -LiteralPath $g22SentinelFile -Force }
    [void](Install-G22Baseline)

    Invoke-VisioInstallerScript $uninstallScript $uninstallArguments $false
    Assert-VisioInstallerCondition (-not (Test-Path -LiteralPath $target)) 'G18: 恢复后再次卸载未移除 Visio 产品目录。'

    Write-Output 'Visio 隔离安装回归 PASS：首次安装故障回滚、成功安装、升级故障回滚、重复安装、卸载及旁边哨兵保护。'
    Write-Output 'PASS: G18 Visio 文件验证先于注册恢复（文件恢复失败、注册恢复失败、清理失败、升级新目录半删除）。'
    Write-Output 'PASS: G22 Visio 区分旧安装存在与旧目录已移动（首次安装早期失败、stage 创建/复制校验/旧目录移动/新目录移动/部署后失败）。'
}
finally {
    $registry.Dispose()
    $rootParent = $testRoot.Substring(0, $testRoot.LastIndexOf('\'))
    $rootLeaf = $testRoot.Substring($testRoot.LastIndexOf('\') + 1)
    $parentKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($rootParent, $true)
    if ($parentKey) {
        try { $parentKey.DeleteSubKeyTree($rootLeaf, $false) }
        finally { $parentKey.Dispose() }
    }
    $temporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $temporaryRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($temporaryRoot) -notlike 'PatentMarkerVisioInstaller-*') {
        throw "拒绝清理不属于本次测试的临时目录：$temporaryRoot"
    }
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

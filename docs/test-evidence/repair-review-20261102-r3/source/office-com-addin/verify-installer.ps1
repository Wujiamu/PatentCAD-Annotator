[CmdletBinding()]
param(
    [string]$SourceDirectory,
    [ValidateSet('32', '64')][string]$OfficeBitness = '64'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Join-Path $PSScriptRoot 'dist'
}
Import-Module (Join-Path $PSScriptRoot 'OfficeAddin.Common.psm1') -Force

$testId = [Guid]::NewGuid().ToString('N')
$testRegistryRoot = 'Software\PatentMarkerOfficeAddinTests\' + $testId
$testParent = Join-Path ([IO.Path]::GetTempPath()) ('PatentMarkerOfficeAddinTest-' + $testId)
$testInstallDirectory = Join-Path $testParent 'PowerPoint'
# The isolated registry fixture does not require PowerPoint to be installed.
# Real installation keeps automatic host detection in install-office-addin.ps1.
$bitness = $OfficeBitness
$baseKey = Open-PatentMarkerCurrentUserRegistry -OfficeBitness $bitness
$sentinelPath = $testRegistryRoot + '\Office\PowerPoint\Addins\Example.VendorAddin'
$sentinelName = 'UnrelatedSentinel'
$sentinelValue = 'preserve-' + $testId
$addinScript = Join-Path $PSScriptRoot 'install-office-addin.ps1'
$uninstallScript = Join-Path $PSScriptRoot 'uninstall-office-addin.ps1'
$registryPaths = @(Get-PatentMarkerRegistryPaths -TestRegistryRoot $testRegistryRoot)

function Invoke-ExpectedFailure {
    param([string]$ScriptPath, [hashtable]$Arguments)
    $failed = $false
    try { & $ScriptPath @Arguments | Out-Null }
    catch { $failed = $true }
    if (-not $failed) { throw "故障注入没有失败：$ScriptPath" }
}

function Get-SnapshotText {
    param([Microsoft.Win32.RegistryKey]$RegistryBase, [string[]]$Paths)
    $items = @()
    foreach ($path in $Paths) {
        $items += ,(Get-PatentMarkerRegistrySnapshot -BaseKey $RegistryBase -Path $path)
    }
    return (ConvertTo-Json -InputObject @($items) -Depth 12 -Compress)
}

function Assert-DefaultRegistryValue {
    param(
        [Microsoft.Win32.RegistryKey]$RegistryBase,
        [string]$Path,
        [string]$Expected
    )
    $key = $RegistryBase.OpenSubKey($Path)
    if (-not $key) { throw "COM 注册子键不存在：$Path" }
    try {
        $actual = [string]$key.GetValue('', '')
        if ($actual -cne $Expected) { throw "COM 注册默认值错误：$Path；实际='$actual'；预期='$Expected'" }
    }
    finally { $key.Dispose() }
}

function Assert-PatentMarkerComRegistration {
    param(
        [Microsoft.Win32.RegistryKey]$RegistryBase,
        [string]$RegistryRoot
    )
    $classId = '{4E9B0E7A-4D92-47AF-A9D7-7E769CB367D8}'
    $progId = 'PatentOffice.PowerPointAddIn'
    $className = 'PatentOffice.PowerPoint.PowerPointAddIn'
    $classPath = $RegistryRoot + '\Classes\CLSID\' + $classId
    $progPath = $RegistryRoot + '\Classes\' + $progId

    Assert-DefaultRegistryValue -RegistryBase $RegistryBase -Path $classPath -Expected $className
    Assert-DefaultRegistryValue -RegistryBase $RegistryBase -Path ($classPath + '\ProgID') -Expected $progId
    Assert-DefaultRegistryValue -RegistryBase $RegistryBase -Path ($classPath + '\VersionIndependentProgID') -Expected $progId
    Assert-DefaultRegistryValue -RegistryBase $RegistryBase -Path $progPath -Expected $className
    Assert-DefaultRegistryValue -RegistryBase $RegistryBase -Path ($progPath + '\CLSID') -Expected $classId
    Assert-DefaultRegistryValue -RegistryBase $RegistryBase -Path ($progPath + '\CurVer') -Expected $progId

    $classRoot = $RegistryBase.OpenSubKey($classPath)
    $progRoot = $RegistryBase.OpenSubKey($progPath)
    try {
        if ($classRoot.GetValueNames() -contains 'ProgID' -or
            $classRoot.GetValueNames() -contains 'VersionIndependentProgID') {
            throw 'ProgID 映射不得写成 CLSID 根键普通值。'
        }
        if ($progRoot.GetValueNames() -contains 'CLSID' -or
            $progRoot.GetValueNames() -contains 'CurVer') {
            throw 'CLSID/CurVer 映射不得写成 ProgID 根键普通值。'
        }
    }
    finally {
        if ($classRoot) { $classRoot.Dispose() }
        if ($progRoot) { $progRoot.Dispose() }
    }
}

try {
    New-Item -ItemType Directory -Path $testParent | Out-Null
    Set-PatentMarkerRegistryValues -BaseKey $baseKey -Path $sentinelPath -Values @{ $sentinelName = $sentinelValue }

    Invoke-ExpectedFailure -ScriptPath $addinScript -Arguments @{
        SourceDirectory = $SourceDirectory
        InstallDirectory = $testInstallDirectory
        OfficeBitness = $bitness
        TestRegistryRoot = $testRegistryRoot
        FaultInjectionPoint = 'AfterRegistry'
    }
    if (Test-Path -LiteralPath $testInstallDirectory) { throw '首次安装故障后仍存在产品目录。' }
    $afterFailedFirstInstall = Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths
    if ($afterFailedFirstInstall -notmatch '^\[null,null,null\]$') { throw '首次安装故障后产品注册项未完整回滚。' }

    & $addinScript -SourceDirectory $SourceDirectory -InstallDirectory $testInstallDirectory -OfficeBitness $bitness -TestRegistryRoot $testRegistryRoot
    Assert-PatentMarkerComRegistration -RegistryBase $baseKey -RegistryRoot $testRegistryRoot
    $firstFiles = @{}
    foreach ($name in @('PatentOffice.PowerPoint.dll', 'Newtonsoft.Json.dll')) {
        $firstFiles[$name] = (Get-FileHash -LiteralPath (Join-Path $testInstallDirectory $name) -Algorithm SHA256).Hash
    }
    $firstRegistry = Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths

    $foreignPath = Join-Path $testInstallDirectory 'unrelated-user-file.txt'
    [IO.File]::WriteAllText($foreignPath, $sentinelValue)
    Invoke-ExpectedFailure -ScriptPath $addinScript -Arguments @{
        SourceDirectory = $SourceDirectory
        InstallDirectory = $testInstallDirectory
        OfficeBitness = $bitness
        TestRegistryRoot = $testRegistryRoot
    }
    Invoke-ExpectedFailure -ScriptPath $uninstallScript -Arguments @{
        InstallDirectory = $testInstallDirectory
        OfficeBitness = $bitness
        TestRegistryRoot = $testRegistryRoot
    }
    if ([IO.File]::ReadAllText($foreignPath) -ne $sentinelValue -or
        (Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths) -ne $firstRegistry) {
        throw '未登记文件哨兵或产品注册状态被改动。'
    }
    Remove-Item -LiteralPath $foreignPath

    & $addinScript -SourceDirectory $SourceDirectory -InstallDirectory $testInstallDirectory -OfficeBitness $bitness -TestRegistryRoot $testRegistryRoot
    if ((Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths) -ne $firstRegistry) {
        throw '重复安装改变了产品注册状态。'
    }

    Invoke-ExpectedFailure -ScriptPath $addinScript -Arguments @{
        SourceDirectory = $SourceDirectory
        InstallDirectory = $testInstallDirectory
        OfficeBitness = $bitness
        TestRegistryRoot = $testRegistryRoot
        FaultInjectionPoint = 'AfterRegistry'
    }
    if ((Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths) -ne $firstRegistry) {
        throw '升级故障后注册表没有恢复到升级前状态。'
    }
    foreach ($name in $firstFiles.Keys) {
        $actual = (Get-FileHash -LiteralPath (Join-Path $testInstallDirectory $name) -Algorithm SHA256).Hash
        if ($actual -ne $firstFiles[$name]) { throw "升级故障后旧版文件未恢复：$name" }
    }

    & $uninstallScript -InstallDirectory $testInstallDirectory -OfficeBitness $bitness -TestRegistryRoot $testRegistryRoot
    if (Test-Path -LiteralPath $testInstallDirectory) { throw '卸载后产品目录仍存在。' }
    foreach ($path in $registryPaths) {
        if (Get-PatentMarkerRegistrySnapshot -BaseKey $baseKey -Path $path) { throw "卸载后产品注册项仍存在：$path" }
    }
    $sentinel = $baseKey.OpenSubKey($sentinelPath)
    if (-not $sentinel) { throw '无关加载项哨兵丢失。' }
    try {
        if ([string]$sentinel.GetValue($sentinelName, '') -ne $sentinelValue) { throw '无关加载项哨兵值改变。' }
    }
    finally { $sentinel.Dispose() }

    # --- G14 / R-029: partial-delete recovery -------------------------------
    # A recursive delete can remove part of the directory before it throws.
    # Two distinct outcomes are allowed, and both must be honest:
    #   (a) files fully restored from the verified recovery copy; or
    #   (b) restore impossible -> the recovery copy is kept and named.
    # A leftover half-deleted directory renamed back into place is never PASS.
    & $addinScript -SourceDirectory $SourceDirectory -InstallDirectory $testInstallDirectory -OfficeBitness $bitness -TestRegistryRoot $testRegistryRoot
    $reinstallFiles = @{}
    foreach ($name in @('PatentOffice.PowerPoint.dll', 'Newtonsoft.Json.dll', 'install-manifest.json')) {
        $reinstallFiles[$name] = (Get-FileHash -LiteralPath (Join-Path $testInstallDirectory $name) -Algorithm SHA256).Hash
    }
    $reinstallRegistry = Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths

    # Windows refuses to rename a directory whose child is open, so locking a
    # file never reaches the recursive delete. Inject the failure at the delete
    # stage instead: one staged file is already gone when the delete throws,
    # which is exactly the state that used to be renamed back as "restored".
    $deleteFailed = $false
    try {
        & $uninstallScript -InstallDirectory $testInstallDirectory -OfficeBitness $bitness `
            -TestRegistryRoot $testRegistryRoot -FaultInjectionPoint 'AfterPartialDelete' | Out-Null
    }
    catch { $deleteFailed = $true }
    if (-not $deleteFailed) { throw 'G14: 删除阶段故障注入后卸载仍然报告成功。' }

    $recoveryCopies = @(Get-ChildItem -LiteralPath $testParent -Force -Directory |
        Where-Object { $_.Name -like '.*.recovery-*' })

    $intact = $true
    foreach ($name in $reinstallFiles.Keys) {
        $path = Join-Path $testInstallDirectory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $intact = $false; continue }
        try {
            if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $reinstallFiles[$name]) { $intact = $false }
        }
        catch { $intact = $false }
    }

    if ($intact) {
        if ((Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths) -ne $reinstallRegistry) {
            throw 'G14: 文件已还原但注册项没有恢复到卸载前状态。'
        }
        if ($recoveryCopies.Count -ne 0) {
            throw ("G14: 完整还原后仍残留恢复副本：{0}" -f ($recoveryCopies.Name -join ', '))
        }
        Write-Output 'PASS: G14 部分删除失败后从恢复副本还原完整文件集与注册项。'
    }
    else {
        # The honest alternative: no restore claim, recovery data kept and named.
        if ($recoveryCopies.Count -eq 0) {
            throw 'G14: 文件未完整还原，却也没有保留恢复副本。'
        }
        $recovered = 0
        foreach ($copy in $recoveryCopies) {
            foreach ($name in $reinstallFiles.Keys) {
                $mirror = Join-Path $copy.FullName $name
                if ((Test-Path -LiteralPath $mirror -PathType Leaf) -and
                    (Get-FileHash -LiteralPath $mirror -Algorithm SHA256).Hash -eq $reinstallFiles[$name]) {
                    $recovered++
                }
            }
        }
        if ($recovered -lt $reinstallFiles.Count) {
            throw 'G14: 恢复副本本身不完整。'
        }
        Write-Output ('PASS: G14 无法就地还原时保留完整恢复副本并明确报告（{0}）。' -f ($recoveryCopies.Name -join ', '))
        # The in-place directory is knowingly incomplete now. Drop the product
        # registry entries the failed uninstall left behind, then put a real
        # installation back so the clean-uninstall assertion below is meaningful.
        foreach ($copy in $recoveryCopies) { Remove-Item -LiteralPath $copy.FullName -Recurse -Force }
        if (Test-Path -LiteralPath $testInstallDirectory -PathType Container) {
            Remove-Item -LiteralPath $testInstallDirectory -Recurse -Force
        }
        foreach ($path in $registryPaths) {
            Remove-PatentMarkerRegistryTree -BaseKey $baseKey -Path $path
        }
        & $addinScript -SourceDirectory $SourceDirectory -InstallDirectory $testInstallDirectory -OfficeBitness $bitness -TestRegistryRoot $testRegistryRoot | Out-Null
    }

    # Non-product sentinels still untouched after the failure path.
    $sentinel = $baseKey.OpenSubKey($sentinelPath)
    if (-not $sentinel) { throw 'G14: 无关加载项哨兵丢失。' }
    try {
        if ([string]$sentinel.GetValue($sentinelName, '') -ne $sentinelValue) { throw 'G14: 无关加载项哨兵值改变。' }
    }
    finally { $sentinel.Dispose() }

    # --- G18 / R-029: file verification must precede registration restore ----
    # Each case below breaks one rollback stage and asserts the contract:
    #   * the original registration is re-enabled only after the complete
    #     product set is back and verified;
    #   * a failed rollback leaves the add-in disabled and keeps the material;
    #   * non-product sentinels never change.
    $g18 = [ordered]@{}
    function Add-G18Result([string]$Name, [bool]$Passed, [string]$Detail) {
        $script:g18[$Name] = [pscustomobject]@{ Passed = $Passed; Detail = $Detail }
    }
    function Get-G18State {
        $present = @()
        foreach ($path in $registryPaths) {
            if (Get-PatentMarkerRegistrySnapshot -BaseKey $baseKey -Path $path) { $present += $path }
        }
        return ($present -join '|')
    }
    function Assert-G18Sentinel([string]$Stage) {
        $key = $baseKey.OpenSubKey($sentinelPath)
        if (-not $key) { throw "G18($Stage): 无关加载项哨兵丢失。" }
        try {
            if ([string]$key.GetValue($sentinelName, '') -ne $sentinelValue) {
                throw "G18($Stage): 无关加载项哨兵值改变。"
            }
        }
        finally { $key.Dispose() }
    }
    function Get-G18Leftovers([string]$Parent) {
        return @(Get-ChildItem -LiteralPath $Parent -Force -Directory |
            Where-Object { $_.Name -like '.*.recovery-*' -or $_.Name -like '.*.uninstall-*' -or $_.Name -like '.*.rollback-*' -or $_.Name -like '.*.failed-*' -or $_.Name -like '.*.stage-*' })
    }

    # --- G18-a: uninstall, file restore fails -> registration stays disabled --
    & $addinScript -SourceDirectory $SourceDirectory -InstallDirectory $testInstallDirectory -OfficeBitness $bitness -TestRegistryRoot $testRegistryRoot | Out-Null
    $g18BaseRegistry = Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths
    $g18BaseFiles = @{}
    foreach ($name in @('PatentOffice.PowerPoint.dll', 'Newtonsoft.Json.dll', 'install-manifest.json')) {
        $g18BaseFiles[$name] = (Get-FileHash -LiteralPath (Join-Path $testInstallDirectory $name) -Algorithm SHA256).Hash
    }
    $failedUninstall = $false
    try {
        & $uninstallScript -InstallDirectory $testInstallDirectory -OfficeBitness $bitness `
            -TestRegistryRoot $testRegistryRoot -FaultInjectionPoint 'PartialDeleteThenRestoreFails' | Out-Null
    }
    catch { $failedUninstall = $true }
    if (-not $failedUninstall) { throw 'G18(a): 文件恢复故障注入后卸载仍然报告成功。' }
    $afterA = Get-G18State
    if ($afterA -ne '') {
        Add-G18Result 'uninstall_file_restore_failed_registration_disabled' $false "仍存在产品注册项：$afterA"
    }
    else {
        Add-G18Result 'uninstall_file_restore_failed_registration_disabled' $true '注册项全部移除，加载项保持停用'
    }
    $leftoversA = @(Get-G18Leftovers $testParent)
    $recoveryKeptA = @($leftoversA | Where-Object { $_.Name -like '.*.recovery-*' })
    if ($recoveryKeptA.Count -eq 0) {
        Add-G18Result 'uninstall_file_restore_failed_material_kept' $false '恢复失败却没有保留恢复副本'
    }
    else {
        # The kept copy must still hold the complete product file set.
        $complete = $true
        foreach ($copy in $recoveryKeptA) {
            foreach ($name in $g18BaseFiles.Keys) {
                $mirror = Join-Path $copy.FullName $name
                if (-not (Test-Path -LiteralPath $mirror -PathType Leaf) -or
                    (Get-FileHash -LiteralPath $mirror -Algorithm SHA256).Hash -ne $g18BaseFiles[$name]) { $complete = $false }
            }
        }
        Add-G18Result 'uninstall_file_restore_failed_material_kept' $complete `
            ($(if ($complete) { '保留的恢复副本完整' } else { '保留的恢复副本不完整' }))
    }
    Assert-G18Sentinel 'uninstall_file_restore_failed'

    # Clean slate for the next case: drop the leftover material and reinstall.
    foreach ($copy in $leftoversA) { Remove-Item -LiteralPath $copy.FullName -Recurse -Force }
    if (Test-Path -LiteralPath $testInstallDirectory -PathType Container) {
        Remove-Item -LiteralPath $testInstallDirectory -Recurse -Force
    }
    foreach ($path in $registryPaths) { Remove-PatentMarkerRegistryTree -BaseKey $baseKey -Path $path }

    # --- G18-b: uninstall, registration restore fails -> disabled + material --
    & $addinScript -SourceDirectory $SourceDirectory -InstallDirectory $testInstallDirectory -OfficeBitness $bitness -TestRegistryRoot $testRegistryRoot | Out-Null
    $failedUninstall = $false
    try {
        & $uninstallScript -InstallDirectory $testInstallDirectory -OfficeBitness $bitness `
            -TestRegistryRoot $testRegistryRoot -FaultInjectionPoint 'RegistryRestoreFails' | Out-Null
    }
    catch { $failedUninstall = $true }
    if (-not $failedUninstall) { throw 'G18(b): 注册恢复故障注入后卸载仍然报告成功。' }
    $afterB = Get-G18State
    if ($afterB -ne '') {
        Add-G18Result 'uninstall_registry_restore_failed_disabled' $false "注册项未全部保持移除：$afterB"
    }
    else {
        Add-G18Result 'uninstall_registry_restore_failed_disabled' $true '注册恢复失败后加载项保持停用'
    }
    $leftoversB = @(Get-G18Leftovers $testParent)
    Add-G18Result 'uninstall_registry_restore_failed_material_kept' (@($leftoversB | Where-Object { $_.Name -like '.*.recovery-*' }).Count -gt 0) `
        ("残留回滚材料：{0}" -f ($leftoversB.Name -join ', '))
    Assert-G18Sentinel 'uninstall_registry_restore_failed'

    foreach ($copy in $leftoversB) { Remove-Item -LiteralPath $copy.FullName -Recurse -Force }
    if (Test-Path -LiteralPath $testInstallDirectory -PathType Container) {
        Remove-Item -LiteralPath $testInstallDirectory -Recurse -Force
    }
    foreach ($path in $registryPaths) { Remove-PatentMarkerRegistryTree -BaseKey $baseKey -Path $path }

    # --- G18-c: uninstall, cleanup fails -> files+registration back, honest ---
    & $addinScript -SourceDirectory $SourceDirectory -InstallDirectory $testInstallDirectory -OfficeBitness $bitness -TestRegistryRoot $testRegistryRoot | Out-Null
    $g18cRegistry = Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths
    $g18cFiles = @{}
    foreach ($name in @('PatentOffice.PowerPoint.dll', 'Newtonsoft.Json.dll', 'install-manifest.json')) {
        $g18cFiles[$name] = (Get-FileHash -LiteralPath (Join-Path $testInstallDirectory $name) -Algorithm SHA256).Hash
    }
    $failedUninstall = $false
    try {
        & $uninstallScript -InstallDirectory $testInstallDirectory -OfficeBitness $bitness `
            -TestRegistryRoot $testRegistryRoot -FaultInjectionPoint 'CleanupFails' | Out-Null
    }
    catch { $failedUninstall = $true }
    if (-not $failedUninstall) { throw 'G18(c): 清理故障注入后卸载仍然报告成功。' }
    $filesBackC = $true
    foreach ($name in $g18cFiles.Keys) {
        $path = Join-Path $testInstallDirectory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $g18cFiles[$name]) { $filesBackC = $false }
    }
    Add-G18Result 'uninstall_cleanup_failed_files_complete' $filesBackC '清理失败时产品文件仍为完整集合'
    Add-G18Result 'uninstall_cleanup_failed_registration_restored' `
        ((Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths) -eq $g18cRegistry) '清理失败时原注册项已恢复'
    $leftoversC = @(Get-G18Leftovers $testParent)
    Add-G18Result 'uninstall_cleanup_failed_material_kept' ($leftoversC.Count -gt 0) `
        ("残留回滚材料：{0}" -f ($leftoversC.Name -join ', '))
    Assert-G18Sentinel 'uninstall_cleanup_failed'

    foreach ($copy in $leftoversC) { Remove-Item -LiteralPath $copy.FullName -Recurse -Force }

    # --- G18-d: upgrade, new directory half-deleted -> old dir restored ------
    # The new directory still exists when the rollback starts, so "the install
    # path exists" must not be read as "the old installation is fine".
    & $addinScript -SourceDirectory $SourceDirectory -InstallDirectory $testInstallDirectory -OfficeBitness $bitness -TestRegistryRoot $testRegistryRoot | Out-Null
    $g18dRegistry = Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths
    $g18dFiles = @{}
    foreach ($name in @('PatentOffice.PowerPoint.dll', 'Newtonsoft.Json.dll', 'install-manifest.json')) {
        $g18dFiles[$name] = (Get-FileHash -LiteralPath (Join-Path $testInstallDirectory $name) -Algorithm SHA256).Hash
    }
    $failedUpgrade = $false
    try {
        & $addinScript -SourceDirectory $SourceDirectory -InstallDirectory $testInstallDirectory -OfficeBitness $bitness `
            -TestRegistryRoot $testRegistryRoot -FaultInjectionPoint 'RollbackPartialDelete' | Out-Null
    }
    catch { $failedUpgrade = $true }
    if (-not $failedUpgrade) { throw 'G18(d): 升级半删除故障注入后安装仍然报告成功。' }
    $filesBackD = $true
    foreach ($name in $g18dFiles.Keys) {
        $path = Join-Path $testInstallDirectory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $g18dFiles[$name]) { $filesBackD = $false }
    }
    Add-G18Result 'upgrade_new_dir_half_deleted_old_files_restored' $filesBackD `
        '升级新目录半删除后旧完整目录与哈希已还原'
    Add-G18Result 'upgrade_new_dir_half_deleted_registration_restored' `
        ((Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths) -eq $g18dRegistry) '升级回滚后原注册项已恢复'
    $leftoversD = @(Get-G18Leftovers $testParent)
    Add-G18Result 'upgrade_new_dir_half_deleted_material_cleaned' ($leftoversD.Count -eq 0) `
        ("残留回滚材料：{0}" -f ($leftoversD.Name -join ', '))
    Assert-G18Sentinel 'upgrade_new_dir_half_deleted'

    # --- G18-e: upgrade, registration restore fails -> disabled, files back --
    $failedUpgrade = $false
    try {
        & $addinScript -SourceDirectory $SourceDirectory -InstallDirectory $testInstallDirectory -OfficeBitness $bitness `
            -TestRegistryRoot $testRegistryRoot -FaultInjectionPoint 'RegistryRestoreFails' | Out-Null
    }
    catch { $failedUpgrade = $true }
    if (-not $failedUpgrade) { throw 'G18(e): 升级注册恢复故障注入后安装仍然报告成功。' }
    $afterE = Get-G18State
    Add-G18Result 'upgrade_registry_restore_failed_disabled' ($afterE -eq '') `
        $(if ($afterE -eq '') { '注册恢复失败后加载项保持停用' } else { "仍存在产品注册项：$afterE" })
    $filesBackE = $true
    foreach ($name in $g18dFiles.Keys) {
        $path = Join-Path $testInstallDirectory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $g18dFiles[$name]) { $filesBackE = $false }
    }
    Add-G18Result 'upgrade_registry_restore_failed_old_files_intact' $filesBackE '注册恢复失败时旧版文件仍完整'
    $leftoversE = @(Get-G18Leftovers $testParent)
    Add-G18Result 'upgrade_registry_restore_failed_material_kept' ($leftoversE.Count -gt 0) `
        ("残留回滚材料：{0}" -f ($leftoversE.Name -join ', '))
    Assert-G18Sentinel 'upgrade_registry_restore_failed'

    foreach ($copy in $leftoversE) { Remove-Item -LiteralPath $copy.FullName -Recurse -Force }
    # Re-establish the product registration the failed upgrade rollback left off.
    if (Test-Path -LiteralPath $testInstallDirectory -PathType Container) {
        Remove-Item -LiteralPath $testInstallDirectory -Recurse -Force
    }
    foreach ($path in $registryPaths) { Remove-PatentMarkerRegistryTree -BaseKey $baseKey -Path $path }
    & $addinScript -SourceDirectory $SourceDirectory -InstallDirectory $testInstallDirectory -OfficeBitness $bitness -TestRegistryRoot $testRegistryRoot | Out-Null

    # --- G18-f: upgrade, half-deleted new dir cannot be removed --------------
    # This is the branch the review flagged: the original install path still
    # exists, so the pre-fix code skipped restoring the old directory. The
    # honest outcome is a disabled add-in plus kept material, never a
    # registration pointing at the fragment.
    $failedUpgrade = $false
    try {
        & $addinScript -SourceDirectory $SourceDirectory -InstallDirectory $testInstallDirectory -OfficeBitness $bitness `
            -TestRegistryRoot $testRegistryRoot -FaultInjectionPoint 'RollbackRemovalBlocked' | Out-Null
    }
    catch { $failedUpgrade = $true }
    if (-not $failedUpgrade) { throw 'G18(f): 残片无法隔离时安装仍然报告成功。' }
    $afterF = Get-G18State
    Add-G18Result 'upgrade_residue_removal_blocked_registration_disabled' ($afterF -eq '') `
        $(if ($afterF -eq '') { '残片无法隔离时加载项保持停用' } else { "仍存在指向残片的产品注册项：$afterF" })
    $leftoversF = @(Get-G18Leftovers $testParent)
    $keptF = @($leftoversF | Where-Object { $_.Name -like '.*.rollback-*' -or $_.Name -like '.*.failed-*' -or $_.Name -like '.*.recovery-*' })
    Add-G18Result 'upgrade_residue_removal_blocked_material_kept' ($keptF.Count -gt 0) `
        ("保留的回滚材料：{0}" -f ($keptF.Name -join ', '))
    Assert-G18Sentinel 'upgrade_residue_removal_blocked'

    foreach ($copy in $leftoversF) { Remove-Item -LiteralPath $copy.FullName -Recurse -Force }
    if (Test-Path -LiteralPath $testInstallDirectory -PathType Container) {
        Remove-Item -LiteralPath $testInstallDirectory -Recurse -Force
    }
    foreach ($path in $registryPaths) { Remove-PatentMarkerRegistryTree -BaseKey $baseKey -Path $path }
    & $addinScript -SourceDirectory $SourceDirectory -InstallDirectory $testInstallDirectory -OfficeBitness $bitness -TestRegistryRoot $testRegistryRoot | Out-Null

    $g18Failed = @($g18.Keys | Where-Object { -not $g18[$_].Passed })
    foreach ($name in $g18.Keys) {
        $mark = if ($g18[$name].Passed) { 'PASS' } else { 'FAIL' }
        Write-Output ("{0}: G18 {1} — {2}" -f $mark, $name, $g18[$name].Detail)
    }
    if ($g18Failed.Count -gt 0) {
        throw ("G18: {0} 条断言失败：{1}" -f $g18Failed.Count, ($g18Failed -join ', '))
    }

    # --- G22 / R-029: an old install that exists is not an old install that moved
    # Every cell starts from a complete old installation with its original
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
    $g22ProductNames = @('PatentOffice.PowerPoint.dll', 'Newtonsoft.Json.dll', 'install-manifest.json')
    $g22SentinelFile = Join-Path $testParent 'g22-unrelated-sentinel.txt'
    $g22SentinelText = 'g22-sentinel-' + $testId
    [IO.File]::WriteAllText($g22SentinelFile, $g22SentinelText)

    function Add-G22Result([string]$Name, [bool]$Passed, [string]$Detail) {
        $script:g22[$Name] = [pscustomobject]@{ Passed = $Passed; Detail = $Detail }
    }
    function Get-G22Baseline {
        $files = @{}
        foreach ($name in $g22ProductNames) {
            $files[$name] = (Get-FileHash -LiteralPath (Join-Path $testInstallDirectory $name) -Algorithm SHA256).Hash
        }
        return [pscustomobject]@{
            Files        = $files
            ManifestText = [IO.File]::ReadAllText((Join-Path $testInstallDirectory 'install-manifest.json'))
            Registry     = (Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths)
        }
    }
    function Test-G22ProductDirectory([psobject]$Baseline) {
        # Complete file set: no missing file, no unregistered entry, hashes and the
        # ownership manifest byte-identical to the pre-failure snapshot.
        if (-not (Test-Path -LiteralPath $testInstallDirectory -PathType Container)) { return $false }
        $actual = @(Get-ChildItem -LiteralPath $testInstallDirectory -Force)
        if ($actual.Count -ne $g22ProductNames.Count) { return $false }
        foreach ($entry in $actual) {
            if ($entry.PSIsContainer -or $g22ProductNames -notcontains $entry.Name) { return $false }
        }
        foreach ($name in $g22ProductNames) {
            if ((Get-FileHash -LiteralPath (Join-Path $testInstallDirectory $name) -Algorithm SHA256).Hash -ne
                $Baseline.Files[$name]) { return $false }
        }
        return ([IO.File]::ReadAllText((Join-Path $testInstallDirectory 'install-manifest.json')) -ceq $Baseline.ManifestText)
    }
    function Test-G22Registration([psobject]$Baseline) {
        return ((Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths) -eq $Baseline.Registry)
    }
    function Test-G22Sentinel {
        if (-not (Test-Path -LiteralPath $g22SentinelFile -PathType Leaf)) { return $false }
        if ([IO.File]::ReadAllText($g22SentinelFile) -cne $g22SentinelText) { return $false }
        $key = $baseKey.OpenSubKey($sentinelPath)
        if (-not $key) { return $false }
        try { return ([string]$key.GetValue($sentinelName, '') -eq $sentinelValue) }
        finally { $key.Dispose() }
    }
    function Reset-G22State {
        foreach ($copy in @(Get-G18Leftovers $testParent)) { Remove-Item -LiteralPath $copy.FullName -Recurse -Force }
        if (Test-Path -LiteralPath $testInstallDirectory -PathType Container) {
            Remove-Item -LiteralPath $testInstallDirectory -Recurse -Force
        }
        foreach ($path in $registryPaths) { Remove-PatentMarkerRegistryTree -BaseKey $baseKey -Path $path }
    }
    function Install-G22Baseline {
        Reset-G22State
        & $addinScript -SourceDirectory $SourceDirectory -InstallDirectory $testInstallDirectory `
            -OfficeBitness $bitness -TestRegistryRoot $testRegistryRoot | Out-Null
        return (Get-G22Baseline)
    }
    function Invoke-G22Installer {
        # Streams every message (warnings included) into a list as it is produced.
        # A pipeline that throws never completes its assignment, so collecting with
        # "$out = @(& $script ...)" silently drops the warnings that identify which
        # stage actually failed.
        param([hashtable]$Arguments)
        $script:g22LastOutput = New-Object System.Collections.Generic.List[string]
        $failed = $false
        try {
            & $addinScript @Arguments *>&1 | ForEach-Object { $script:g22LastOutput.Add([string]$_) }
        }
        catch {
            $failed = $true
            $script:g22LastOutput.Add('THROW: ' + $_.Exception.Message)
        }
        return $failed
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
    function Invoke-G22Case {
        param(
            [string]$Name,
            [hashtable]$Arguments,
            [scriptblock]$Inject,
            [scriptblock]$Release,
            [string]$ExpectOutput
        )
        $baseline = Install-G22Baseline
        $failed = $false
        try {
            if ($Inject) { & $Inject }
            $failed = Invoke-G22Installer $Arguments
        }
        finally { if ($Release) { & $Release } }
        $output = @($script:g22LastOutput)
        if (-not $failed) {
            Add-G22Result ($Name + '_installer_failed') $false '故障注入后安装仍然报告成功'
            return
        }
        $flat = (($output -join ' ') -replace '\s+', ' ').Trim()
        Add-G22Result ($Name + '_failure_origin') ($flat -match $ExpectOutput) `
            $(if ($flat -match $ExpectOutput) { "失败阶段匹配 '$ExpectOutput'" }
              else { "失败不在预期阶段（应匹配 '$ExpectOutput'）：$flat" })
        Add-G22Result ($Name + '_old_files') (Test-G22ProductDirectory $baseline) `
            '旧文件字节、完整文件集与所有权清单保持不变'
        Add-G22Result ($Name + '_registration') (Test-G22Registration $baseline) `
            $(if (Test-G22Registration $baseline) { '原注册值保持不变' } else { "原注册值被改动：$(Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths)" })
        Add-G22Result ($Name + '_sentinel') (Test-G22Sentinel) '非项目哨兵文件与注册值保持不变'
    }

    $g22Base = @{
        SourceDirectory  = $SourceDirectory
        InstallDirectory = $testInstallDirectory
        OfficeBitness    = $bitness
        TestRegistryRoot = $testRegistryRoot
    }
    function New-G22Args([string]$Point) {
        $a = $g22Base.Clone()
        $a['FaultInjectionPoint'] = $Point
        return $a
    }

    # (1) First install: the stage directory cannot be created. Nothing existed
    #     before, so "complete" still means "nothing left behind" and no product
    #     registration may be created.
    Reset-G22State
    $g22AclRule = Disable-G22ParentCreate $testParent
    $firstFailed = $false
    try { $firstFailed = Invoke-G22Installer $g22Base }
    finally { Enable-G22ParentCreate $testParent $g22AclRule }
    $firstOutput = @($script:g22LastOutput)
    $firstFlat = (($firstOutput -join ' ') -replace '\s+', ' ').Trim()
    Add-G22Result 'first_install_stage_failed_installer_failed' $firstFailed `
        $(if ($firstFailed) { '阶段创建失败后安装返回失败' } else { '阶段创建失败后安装仍然报告成功' })
    Add-G22Result 'first_install_stage_failed_failure_origin' ($firstFlat -match 'stage-') `
        $(if ($firstFlat -match 'stage-') { '失败来自暂存目录创建' } else { "失败来源不符：$firstFlat" })
    Add-G22Result 'first_install_stage_failed_no_product_dir' (-not (Test-Path -LiteralPath $testInstallDirectory)) `
        '首次安装早期失败后未留下产品目录'
    Add-G22Result 'first_install_stage_failed_registration' `
        ((Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths) -eq '[null,null,null]') `
        '首次安装早期失败后没有产品注册项'
    Add-G22Result 'first_install_stage_failed_sentinel' (Test-G22Sentinel) '非项目哨兵保持不变'

    # (2) Upgrade, stage creation fails. The old installation is complete and was
    #     never moved: it must stay usable and keep its registration.
    Invoke-G22Case -Name 'upgrade_stage_failed' -Arguments (New-G22Args 'None') `
        -Inject { $script:g22CaseRule = Disable-G22ParentCreate $testParent } `
        -Release { Enable-G22ParentCreate $testParent $script:g22CaseRule; $script:g22CaseRule = $null } `
        -ExpectOutput 'stage-'

    # (3) Upgrade, the staged copy fails its hash check (this round's defect).
    Invoke-G22Case -Name 'upgrade_staged_copy_failed' -Arguments (New-G22Args 'StagedCopyCorrupted') `
        -ExpectOutput '复制校验失败'

    # (4) Upgrade, moving the old directory fails (this round's defect). The old
    #     directory is complete and still in place when the rollback starts.
    Invoke-G22Case -Name 'upgrade_old_move_failed' -Arguments (New-G22Args 'None') `
        -Inject {
            $script:g22OldHandle = [IO.File]::Open(
                (Join-Path $testInstallDirectory 'Newtonsoft.Json.dll'),
                [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        } `
        -Release { if ($script:g22OldHandle) { $script:g22OldHandle.Dispose(); $script:g22OldHandle = $null } } `
        -ExpectOutput 'denied|being used|拒绝访问|另一进程'

    # (5) Upgrade, the old directory was moved away and the new directory cannot
    #     be moved into place: the old one has to come back from the backup.
    Invoke-G22Case -Name 'upgrade_new_move_failed' -Arguments (New-G22Args 'NewMoveFails') `
        -ExpectOutput '新安装目录无法移动|\.PowerPoint\.stage-'

    # (6) Upgrade, the new directory is fully deployed and the run then fails
    #     before any registration value is written.
    Invoke-G22Case -Name 'upgrade_after_files_failed' -Arguments (New-G22Args 'AfterFiles') `
        -ExpectOutput '文件部署后'

    $g22Failed = @($g22.Keys | Where-Object { -not $g22[$_].Passed })
    foreach ($name in $g22.Keys) {
        $mark = if ($g22[$name].Passed) { 'PASS' } else { 'FAIL' }
        Write-Output ("{0}: G22 {1} — {2}" -f $mark, $name, $g22[$name].Detail)
    }
    if ($g22Failed.Count -gt 0) {
        throw ("G22: {0} 条断言失败：{1}" -f $g22Failed.Count, ($g22Failed -join ', '))
    }
    if (Test-Path -LiteralPath $g22SentinelFile -PathType Leaf) { Remove-Item -LiteralPath $g22SentinelFile -Force }
    [void](Install-G22Baseline)

    # --- G26 / R-029: file path changed but no registration write yet --------
    # Every cell starts from a complete old installation with its original
    # registration and asserts, in the real isolated HKCU root and a real
    # temporary install tree, that the two are never allowed to disagree:
    #   * the product directory is either the byte-identical complete old set,
    #     or it is not presented as a complete installation;
    #   * the product registration exists only for a complete directory, and
    #     when it exists its InprocServer32 CodeBase resolves to a real file and
    #     its LoadBehavior is still 3 (a key that exists but does not work is not
    #     "enabled");
    #   * recovery material is kept exactly when the rollback could not confirm
    #     both halves, and is removed when it could;
    #   * the non-product sentinel never changes;
    #   * the warning text states the registration state that is really present.
    # The five phases are this round's five: staging only, old directory already
    # moved away, new files already deployed, registration already written, and
    # a first install, each with the old-directory restore succeeding or failing.
    $g26 = [ordered]@{}
    $g26SentinelFile = Join-Path $testParent 'g26-unrelated-sentinel.txt'
    $g26SentinelText = 'g26-sentinel-' + $testId
    [IO.File]::WriteAllText($g26SentinelFile, $g26SentinelText)
    $g26ProductNames = @('PatentOffice.PowerPoint.dll', 'Newtonsoft.Json.dll', 'install-manifest.json')

    function Add-G26Result([string]$Name, [bool]$Passed, [string]$Detail) {
        $script:g26[$Name] = [pscustomobject]@{ Passed = $Passed; Detail = $Detail }
    }
    function Test-G26Sentinel {
        if (-not (Test-Path -LiteralPath $g26SentinelFile -PathType Leaf)) { return $false }
        if ([IO.File]::ReadAllText($g26SentinelFile) -cne $g26SentinelText) { return $false }
        $key = $baseKey.OpenSubKey($sentinelPath)
        if (-not $key) { return $false }
        try { return ([string]$key.GetValue($sentinelName, '') -eq $sentinelValue) }
        finally { $key.Dispose() }
    }
    function Get-G26RegistrationSnapshot {
        return (Get-SnapshotText -RegistryBase $baseKey -Paths $registryPaths)
    }
    function Test-G26RegistrationUsable {
        # A registration that exists but whose CodeBase is gone, or whose
        # LoadBehavior no longer asks Office to load it, is not usable. Key
        # presence alone is the exact mistake this round is about.
        foreach ($path in $registryPaths) {
            if (-not (Get-PatentMarkerRegistrySnapshot -BaseKey $baseKey -Path $path)) { return $false }
        }
        $inproc = $baseKey.OpenSubKey(($registryPaths[0] + '\InprocServer32'))
        if (-not $inproc) { return $false }
        try {
            $codeBase = [string]$inproc.GetValue('CodeBase', '')
            if ([string]::IsNullOrWhiteSpace($codeBase)) { return $false }
            try { $localPath = ([Uri]$codeBase).LocalPath } catch { return $false }
            if (-not (Test-Path -LiteralPath $localPath -PathType Leaf)) { return $false }
        }
        finally { $inproc.Dispose() }
        $addin = $baseKey.OpenSubKey($registryPaths[2])
        if (-not $addin) { return $false }
        try { return ([int]$addin.GetValue('LoadBehavior', 0) -eq 3) }
        finally { $addin.Dispose() }
    }
    function Get-G26ProductState([psobject]$Baseline) {
        if ($null -eq $Baseline) {
            # No previous installation: "complete" means nothing of ours is left.
            return (-not (Test-Path -LiteralPath $testInstallDirectory))
        }
        return (Test-G22ProductDirectory $Baseline)
    }
    function Test-G26RollbackKept([psobject]$Baseline) {
        if ($null -eq $Baseline) { return $false }
        foreach ($copy in @(Get-ChildItem -LiteralPath $testParent -Force -Directory | Where-Object { $_.Name -like '.*.rollback-*' })) {
            $allThere = $true
            foreach ($name in $g26ProductNames) {
                $file = Join-Path $copy.FullName $name
                if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { $allThere = $false; continue }
                if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $Baseline.Files[$name]) { $allThere = $false }
            }
            if ($allThere) { return $true }
        }
        return $false
    }
    function Get-G26LogState([string]$Flat) {
        if ($Flat -match '已恢复旧版产品文件与原注册项|已恢复旧版产品文件（安装前没有产品注册项）') { return 'restored' }
        if ($Flat -match '未部署产品文件（安装前没有旧版安装）') { return 'nothing-installed' }
        if ($Flat -match '产品注册项仍然存在|产品注册项与原注册项不一致') { return 'inconsistent' }
        if ($Flat -match '原注册项未能恢复（加载项保持停用）|加载项保持停用') { return 'disabled' }
        return 'unknown'
    }
    function Invoke-G26Cell {
        param(
            [string]$Name,
            [hashtable]$Arguments,
            [scriptblock]$Inject,
            [scriptblock]$Release,
            [string]$ExpectOutput,
            [switch]$NoPreviousInstall,
            [bool]$ExpectComplete = $true,
            [bool]$ExpectRegistration = $true,
            [bool]$ExpectRollbackKept = $false,
            [string]$ExpectLog = 'restored'
        )
        $baseline = $null
        if ($NoPreviousInstall) { Reset-G22State } else { $baseline = Install-G22Baseline }
        $failed = $false
        try {
            if ($Inject) { & $Inject }
            $failed = Invoke-G22Installer $Arguments
        }
        finally { if ($Release) { & $Release } }
        $flat = ((@($script:g22LastOutput) -join ' ') -replace '\s+', ' ').Trim()
        if (-not $failed) {
            Add-G26Result ($Name + '_installer_failed') $false '故障注入后安装仍然报告成功'
            return
        }
        Add-G26Result ($Name + '_failure_origin') ($flat -match $ExpectOutput) `
            $(if ($flat -match $ExpectOutput) { '失败阶段匹配' } else { '失败不在预期阶段：' + $flat })

        $complete = Get-G26ProductState $baseline
        Add-G26Result ($Name + '_target_integrity') ($complete -eq $ExpectComplete) `
            ('完整={0}；预期={1}' -f $complete, $ExpectComplete)

        $snapshot = Get-G26RegistrationSnapshot
        $enabled = ($snapshot -ne '[null,null,null]')
        Add-G26Result ($Name + '_registration_state') ($enabled -eq $ExpectRegistration) `
            ('启用={0}；预期={1}' -f $enabled, $ExpectRegistration)

        # The registration must be usable, not merely present, and when the old
        # installation is back it must be the original registration values.
        if ($ExpectRegistration) {
            Add-G26Result ($Name + '_registration_usable') (Test-G26RegistrationUsable) '注册指向真实 CodeBase 且 LoadBehavior=3'
            Add-G26Result ($Name + '_registration_matches_baseline') `
                ($null -ne $baseline -and (Test-G22Registration $baseline)) '注册值等于安装前快照'
        }
        else {
            Add-G26Result ($Name + '_registration_absent') (-not $enabled) '没有任何产品注册项'
        }

        Add-G26Result ($Name + '_rollback_material') ((Test-G26RollbackKept $baseline) -eq $ExpectRollbackKept) `
            ('保留旧目录备份={0}；预期={1}' -f (Test-G26RollbackKept $baseline), $ExpectRollbackKept)

        Add-G26Result ($Name + '_sentinel') (Test-G26Sentinel) '非项目哨兵文件与注册项保持不变'

        $logState = Get-G26LogState $flat
        $logConsistent = ($logState -eq $ExpectLog) -and
            ($ExpectRegistration -eq ($logState -ne 'disabled' -and $logState -ne 'nothing-installed'))
        Add-G26Result ($Name + '_log_matches_state') $logConsistent `
            ('日志={0}；预期={1}；注册实际={2}' -f $logState, $ExpectLog, $enabled)
    }

    $g26Base = $g22Base
    function New-G26Args([string]$Point) {
        $a = $g26Base.Clone()
        $a['FaultInjectionPoint'] = $Point
        return $a
    }

    # (1) staging only: old directory never moved, restore not needed.
    Invoke-G26Cell -Name 'stage_restore_ok' -Arguments (New-G26Args 'None') `
        -Inject { $script:g26Rule = Disable-G22ParentCreate $testParent } `
        -Release { Enable-G22ParentCreate $testParent $script:g26Rule; $script:g26Rule = $null } `
        -ExpectOutput 'stage-' -ExpectComplete $true -ExpectRegistration $true

    # (2) staging only, and the untouched old directory cannot even be verified
    #     any more. The registration may not survive an unconfirmed directory.
    Invoke-G26Cell -Name 'stage_restore_fails' -Arguments (New-G26Args 'StageVerifyBlocked') `
        -ExpectOutput '暂存阶段失败' -ExpectComplete $true -ExpectRegistration $false -ExpectLog 'disabled'

    # (3) old directory already moved away, restore succeeds.
    Invoke-G26Cell -Name 'old_moved_restore_ok' -Arguments (New-G26Args 'NewMoveFails') `
        -ExpectOutput '新安装目录无法移动|\.PowerPoint\.stage-' -ExpectComplete $true -ExpectRegistration $true

    # (4) old directory already moved away, first registration write not yet
    #     done, and restoring the old directory fails (this round's defect).
    Invoke-G26Cell -Name 'old_moved_restore_fails' -Arguments (New-G26Args 'OldMovedRestoreBlocked') `
        -ExpectOutput '旧目录已移走' -ExpectComplete $false -ExpectRegistration $false `
        -ExpectRollbackKept $true -ExpectLog 'disabled'

    # (5) new directory already deployed, restore of the old one succeeds.
    Invoke-G26Cell -Name 'new_files_restore_ok' -Arguments (New-G26Args 'AfterFiles') `
        -ExpectOutput '文件部署后' -ExpectComplete $true -ExpectRegistration $true

    # (6) new directory already deployed, old restore fails (this round's defect).
    Invoke-G26Cell -Name 'new_files_restore_fails' -Arguments (New-G26Args 'AfterFilesOldRestoreBlocked') `
        -ExpectOutput '文件部署后' -ExpectComplete $false -ExpectRegistration $false `
        -ExpectRollbackKept $true -ExpectLog 'disabled'

    # (7) registration already written, old restore succeeds: the original
    #     snapshot is written back, not merely trusted in place.
    Invoke-G26Cell -Name 'new_registry_restore_ok' -Arguments (New-G26Args 'AfterRegistry') `
        -ExpectOutput '注册表写入后' -ExpectComplete $true -ExpectRegistration $true

    # (8) registration already written, old restore fails.
    Invoke-G26Cell -Name 'new_registry_restore_fails' -Arguments (New-G26Args 'AfterRegistryOldRestoreBlocked') `
        -ExpectOutput '注册表写入后' -ExpectComplete $false -ExpectRegistration $false `
        -ExpectRollbackKept $true -ExpectLog 'disabled'

    # (9) first install, new files deployed, rollback removes them.
    Invoke-G26Cell -Name 'first_new_files_restore_ok' -Arguments (New-G26Args 'AfterFiles') -NoPreviousInstall `
        -ExpectOutput '文件部署后' -ExpectComplete $true -ExpectRegistration $false -ExpectLog 'nothing-installed'

    # (10) first install, new files deployed, fragment cannot be removed: no
    #      product registration may exist and the fragment must be kept.
    Invoke-G26Cell -Name 'first_new_files_restore_fails' -Arguments (New-G26Args 'AfterFilesResidueBlocked') -NoPreviousInstall `
        -ExpectOutput '残片无法隔离' -ExpectComplete $false -ExpectRegistration $false -ExpectLog 'disabled'

    # (11) registration untouched by this run, but an outside actor changed one
    #      product value before the first write. Comparing with the snapshot is
    #      what distinguishes "untouched" from "believed untouched".
    Invoke-G26Cell -Name 'registration_diverged_restore_ok' -Arguments (New-G26Args 'AfterFilesRegistrationDiverged') `
        -ExpectOutput '产品注册项已被外部改动' -ExpectComplete $true -ExpectRegistration $true

    $g26Failed = @($g26.Keys | Where-Object { -not $g26[$_].Passed })
    foreach ($name in $g26.Keys) {
        $mark = if ($g26[$name].Passed) { 'PASS' } else { 'FAIL' }
        Write-Output ("{0}: G26 {1} — {2}" -f $mark, $name, $g26[$name].Detail)
    }
    if ($g26Failed.Count -gt 0) {
        throw ("G26: {0} 条断言失败：{1}" -f $g26Failed.Count, ($g26Failed -join ', '))
    }
    if (Test-Path -LiteralPath $g26SentinelFile -PathType Leaf) { Remove-Item -LiteralPath $g26SentinelFile -Force }
    [void](Install-G22Baseline)

    # Whatever the failure left behind, a clean uninstall must still work.
    & $uninstallScript -InstallDirectory $testInstallDirectory -OfficeBitness $bitness -TestRegistryRoot $testRegistryRoot
    if (Test-Path -LiteralPath $testInstallDirectory) { throw 'G14: 恢复后再次卸载未移除产品目录。' }

    Write-Output 'PASS: 首次安装故障回滚、重复安装、升级故障回滚、卸载以及非产品文件和注册项哨兵保护。'
    Write-Output 'PASS: G18 文件验证先于注册恢复（文件恢复失败、注册恢复失败、清理失败、升级新目录半删除）。'
    Write-Output 'PASS: G22 区分旧安装存在与旧目录已移动（首次安装早期失败、stage 创建/复制校验/旧目录移动/新目录移动/部署后失败）。'
    Write-Output 'PASS: G26 注册未写不等于文件路径未改（五阶段×恢复成功/失败，注册实际可用性与日志状态一致）。'
}
finally {
    $baseKey.Dispose()
    if (Test-Path -LiteralPath $testParent -PathType Container) {
        $resolvedTestParent = (Resolve-Path -LiteralPath $testParent).Path
        if ([string]::Equals($resolvedTestParent, [IO.Path]::GetFullPath($testParent), [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedTestParent -Recurse -Force
        }
    }
    $cleanupBase = Open-PatentMarkerCurrentUserRegistry -OfficeBitness $bitness
    try { Remove-PatentMarkerRegistryTree -BaseKey $cleanupBase -Path $testRegistryRoot }
    finally { $cleanupBase.Dispose() }
}

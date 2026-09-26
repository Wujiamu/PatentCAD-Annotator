[CmdletBinding()]
param(
    [string]$SourceDirectory
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
$bitness = Get-PatentMarkerOfficeBitness -Requested 'Auto'
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

    Write-Output 'PASS: 首次安装故障回滚、重复安装、升级故障回滚、卸载以及非产品文件和注册项哨兵保护。'
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

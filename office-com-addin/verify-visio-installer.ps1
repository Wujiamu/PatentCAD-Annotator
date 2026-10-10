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
    Write-Output 'Visio 隔离安装回归 PASS：首次安装故障回滚、成功安装、升级故障回滚、重复安装、卸载及旁边哨兵保护。'
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

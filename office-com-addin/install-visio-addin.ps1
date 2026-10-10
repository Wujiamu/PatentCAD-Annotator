[CmdletBinding()]
param(
    [string]$SourceDirectory,
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'PatentMarker\OfficeAddin\Visio'),
    [ValidateSet('Auto', '32', '64')][string]$OfficeBitness = 'Auto',
    [string]$TestRegistryRoot,
    [ValidateSet('None', 'AfterFiles', 'AfterRegistry')][string]$FaultInjectionPoint = 'None'
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

if (Test-Path -LiteralPath $installFull) {
    [void](Assert-VisioOwnedDirectory -InstallDirectory $installFull)
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

try {
    New-Item -ItemType Directory -Path $stageDirectory | Out-Null
    $hashes = [ordered]@{}
    foreach ($name in $sourceNames) {
        $source = Join-Path $sourceFull $name
        $staged = Join-Path $stageDirectory $name
        Copy-Item -LiteralPath $source -Destination $staged
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
    Move-Item -LiteralPath $stageDirectory -Destination $installFull
    $newInstalled = $true
    if ($FaultInjectionPoint -eq 'AfterFiles') { throw '测试故障注入：Visio 产品文件部署后。' }

    $classPath = $registryPaths[0]
    $progPath = $registryPaths[1]
    $addinPath = $registryPaths[2]
    $inprocPath = $classPath + '\InprocServer32'
    $codeBase = ([Uri](Join-Path $installFull 'PatentOffice.Visio.dll')).AbsoluteUri

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
    if ($FaultInjectionPoint -eq 'AfterRegistry') { throw '测试故障注入：Visio 注册表写入后。' }

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
    foreach ($path in $registryPaths) {
        try { Remove-VisioRegistryTree -BaseKey $baseKey -Path $path }
        catch { Write-Warning "回滚 Visio 注册项失败：$path" }
    }
    for ($index = 0; $index -lt $registryPaths.Count; $index++) {
        try { Restore-VisioRegistrySnapshot -BaseKey $baseKey -Snapshot $registrySnapshots[$index] }
        catch { Write-Warning "恢复原 Visio 注册项失败：$($registryPaths[$index])" }
    }
    if ($newInstalled -and (Test-Path -LiteralPath $installFull -PathType Container)) {
        try { Remove-VisioSiblingDirectory -Candidate $installFull -ExpectedParent $installParent }
        catch { Write-Warning "回滚新 Visio 安装目录失败：$installFull" }
    }
    if ($oldMoved -and (Test-Path -LiteralPath $backupDirectory -PathType Container) -and
        -not (Test-Path -LiteralPath $installFull)) {
        try { Move-Item -LiteralPath $backupDirectory -Destination $installFull }
        catch { Write-Warning "恢复旧 Visio 安装目录失败：$installFull" }
    }
    throw $failure
}
finally {
    if (Test-Path -LiteralPath $stageDirectory -PathType Container) {
        try { Remove-VisioSiblingDirectory -Candidate $stageDirectory -ExpectedParent $installParent }
        catch { Write-Warning "Visio 暂存目录未能清理：$stageDirectory" }
    }
    $baseKey.Dispose()
}

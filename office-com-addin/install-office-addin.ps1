[CmdletBinding()]
param(
    [string]$SourceDirectory,
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'PatentMarker\OfficeAddin\PowerPoint'),
    [ValidateSet('Auto', '32', '64')][string]$OfficeBitness = 'Auto',
    [string]$TestRegistryRoot,
    [ValidateSet('None', 'AfterFiles', 'AfterRegistry')][string]$FaultInjectionPoint = 'None'
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

    if ($FaultInjectionPoint -eq 'AfterRegistry') { throw '测试故障注入：注册表写入后。' }

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
    foreach ($path in $registryPaths) {
        try { Remove-PatentMarkerRegistryTree -BaseKey $baseKey -Path $path }
        catch { Write-Warning "回滚注册表项失败：$path" }
    }
    for ($index = 0; $index -lt $registryPaths.Count; $index++) {
        try { Restore-PatentMarkerRegistrySnapshot -BaseKey $baseKey -Snapshot $registrySnapshots[$index] }
        catch { Write-Warning "恢复原注册表项失败：$($registryPaths[$index])" }
    }

    if ($newTargetInstalled -and (Test-Path -LiteralPath $installFull -PathType Container)) {
        try { Remove-PatentMarkerSiblingDirectory -Candidate $installFull -ExpectedParent $installParent }
        catch { Write-Warning "回滚新安装目录失败：$installFull" }
    }
    if ($targetMovedToBackup -and (Test-Path -LiteralPath $backupDirectory -PathType Container) -and
        -not (Test-Path -LiteralPath $installFull)) {
        try { Move-Item -LiteralPath $backupDirectory -Destination $installFull }
        catch { Write-Warning "恢复旧安装目录失败：$installFull" }
    }
    throw $failure
}
finally {
    if (Test-Path -LiteralPath $stageDirectory -PathType Container) {
        try { Remove-PatentMarkerSiblingDirectory -Candidate $stageDirectory -ExpectedParent $installParent }
        catch { Write-Warning "暂存目录未能清理：$stageDirectory" }
    }
    $baseKey.Dispose()
}

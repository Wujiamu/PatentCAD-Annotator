[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'PatentMarker\OfficeAddin\PowerPoint'),
    [ValidateSet('Auto', '32', '64')][string]$OfficeBitness = 'Auto',
    [string]$TestRegistryRoot
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'OfficeAddin.Common.psm1') -Force

$installFull = [IO.Path]::GetFullPath($InstallDirectory)
$productionInstall = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'PatentMarker\OfficeAddin\PowerPoint'))
if ((Get-Process -Name POWERPNT -ErrorAction SilentlyContinue) -and
    ([string]::IsNullOrWhiteSpace($TestRegistryRoot) -or
     [string]::Equals($installFull, $productionInstall, [StringComparison]::OrdinalIgnoreCase))) {
    throw '请先关闭 PowerPoint，再卸载加载项。'
}
if (-not (Test-Path -LiteralPath $installFull -PathType Container)) {
    Write-Output '未找到产品安装目录，没有执行卸载。'
    return
}
$manifest = Assert-PatentMarkerOwnedDirectory -InstallDirectory $installFull
if ($OfficeBitness -eq 'Auto') { $OfficeBitness = [string]$manifest.OfficeBitness }
$registryPaths = @(Get-PatentMarkerRegistryPaths -TestRegistryRoot $TestRegistryRoot)
$baseKey = Open-PatentMarkerCurrentUserRegistry -OfficeBitness $OfficeBitness
$registrySnapshots = @()
foreach ($path in $registryPaths) {
    $registrySnapshots += ,(Get-PatentMarkerRegistrySnapshot -BaseKey $baseKey -Path $path)
}

$parent = Split-Path -Parent $installFull
$leaf = Split-Path -Leaf $installFull
$parent = [IO.Path]::GetFullPath($parent)
$backupDirectory = Join-Path $parent ('.' + $leaf + '.uninstall-' + [Guid]::NewGuid().ToString('N'))
$backupDirectory = Assert-PatentMarkerSiblingDirectory -Candidate $backupDirectory -ExpectedParent $parent
$moved = $false

try {
    Move-Item -LiteralPath $installFull -Destination $backupDirectory
    $moved = $true
    foreach ($path in $registryPaths) {
        Remove-PatentMarkerRegistryTree -BaseKey $baseKey -Path $path
    }
    Remove-PatentMarkerSiblingDirectory -Candidate $backupDirectory -ExpectedParent $parent
    Write-Output "已卸载 PatentOffice.PowerPointAddIn，并移除产品专属文件和 HKCU 注册项。"
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
    if ($moved -and (Test-Path -LiteralPath $backupDirectory -PathType Container) -and
        -not (Test-Path -LiteralPath $installFull)) {
        try { Move-Item -LiteralPath $backupDirectory -Destination $installFull }
        catch { Write-Warning "恢复产品目录失败：$installFull" }
    }
    throw $failure
}
finally { $baseKey.Dispose() }

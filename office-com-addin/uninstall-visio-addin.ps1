[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'PatentMarker\OfficeAddin\Visio'),
    [ValidateSet('Auto', '32', '64')][string]$OfficeBitness = 'Auto',
    [string]$TestRegistryRoot
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'VisioAddin.Common.psm1') -Force
$identity = Get-VisioAddinIdentity

if ([string]::IsNullOrWhiteSpace($TestRegistryRoot) -and (Get-Process -Name VISIO -ErrorAction SilentlyContinue)) {
    throw '请先关闭 Visio，再卸载加载项。'
}

$installFull = [IO.Path]::GetFullPath($InstallDirectory)
if (-not (Test-Path -LiteralPath $installFull -PathType Container)) {
    throw "Visio 产品安装目录不存在，拒绝按名称推测其他资产：$installFull"
}
$manifest = Assert-VisioOwnedDirectory -InstallDirectory $installFull
$bitness = if ($OfficeBitness -eq 'Auto') { [string]$manifest.OfficeBitness } else { $OfficeBitness }
$registryPaths = @(Get-VisioAddinRegistryPaths -TestRegistryRoot $TestRegistryRoot)
$baseKey = Open-VisioAddinRegistry -OfficeBitness $bitness
$snapshots = @()
foreach ($path in $registryPaths) {
    $snapshots += ,(Get-VisioRegistrySnapshot -BaseKey $baseKey -Path $path)
}
$parent = [IO.Path]::GetFullPath((Split-Path -Parent $installFull))
$backup = Assert-VisioSiblingDirectory -Candidate (Join-Path $parent ('.' + (Split-Path -Leaf $installFull) + '.uninstall-' + [Guid]::NewGuid().ToString('N'))) -ExpectedParent $parent
$moved = $false

try {
    foreach ($path in $registryPaths) {
        Remove-VisioRegistryTree -BaseKey $baseKey -Path $path
    }
    Move-Item -LiteralPath $installFull -Destination $backup
    $moved = $true
    try { Remove-VisioSiblingDirectory -Candidate $backup -ExpectedParent $parent }
    catch
    {
        Move-Item -LiteralPath $backup -Destination $installFull
        $moved = $false
        throw
    }
    Write-Output "已卸载 $($identity.ProgId)。"
}
catch {
    $failure = $_
    foreach ($path in $registryPaths) {
        try { Remove-VisioRegistryTree -BaseKey $baseKey -Path $path }
        catch { Write-Warning "清理 Visio 注册项失败：$path" }
    }
    for ($index = 0; $index -lt $registryPaths.Count; $index++) {
        try { Restore-VisioRegistrySnapshot -BaseKey $baseKey -Snapshot $snapshots[$index] }
        catch { Write-Warning "恢复 Visio 注册项失败：$($registryPaths[$index])" }
    }
    if ($moved -and (Test-Path -LiteralPath $backup -PathType Container) -and
        -not (Test-Path -LiteralPath $installFull)) {
        try { Move-Item -LiteralPath $backup -Destination $installFull }
        catch { Write-Warning "恢复 Visio 产品文件失败：$installFull" }
    }
    throw $failure
}
finally { $baseKey.Dispose() }

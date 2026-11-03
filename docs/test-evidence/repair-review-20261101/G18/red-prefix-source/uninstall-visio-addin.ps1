[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'PatentMarker\OfficeAddin\Visio'),
    [ValidateSet('Auto', '32', '64')][string]$OfficeBitness = 'Auto',
    [string]$TestRegistryRoot,
    # Test-only: simulate Remove-Item failing after part of the directory was
    # already deleted. Locking a file cannot do this, because Windows refuses
    # to rename a directory whose child is open, so the delete stage would
    # never be reached.
    # Test-only injection points for the rollback order matrix (G18). They only
    # act inside the recovery catch, so a normal uninstall is unaffected.
    [ValidateSet('None', 'AfterPartialDelete', 'PartialDeleteThenRestoreFails', 'RegistryRestoreFails', 'CleanupFails')]
    [string]$FaultInjectionPoint = 'None'
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
$recovery = $null
$moved = $false

# The directory we delete can end up half-deleted, so it cannot be its own
# recovery point. Build and verify a separate copy first; if that fails, stop
# before anything destructive happens.
$recovery = New-VisioRecoveryCopy -InstallDirectory $installFull -ExpectedParent $parent

try {
    foreach ($path in $registryPaths) {
        Remove-VisioRegistryTree -BaseKey $baseKey -Path $path
    }
    Move-Item -LiteralPath $installFull -Destination $backup
    $moved = $true
    if ($FaultInjectionPoint -ne 'None') {
        # Remove one staged file first: this is the state a failing recursive
        # delete leaves behind, and the reason the staged directory cannot be
        # renamed back as a "restore". Every injection point starts from this
        # real half-deleted staging state; the differences are which recovery
        # stage then fails, so no stage is faked.
        $victim = Join-Path $backup 'Newtonsoft.Json.dll'
        if (Test-Path -LiteralPath $victim) { Remove-Item -LiteralPath $victim -Force }
        throw '测试故障注入：递归删除中途失败。'
    }
    try { Remove-VisioSiblingDirectory -Candidate $backup -ExpectedParent $parent }
    catch
    {
        # The delete may have removed part of the staged directory; restore
        # from the verified copy rather than renaming the leftover back.
        if (Restore-VisioDirectoryFromCopy -RecoveryCopy $recovery -InstallDirectory $installFull -ExpectedParent $parent) {
            $moved = $false
            if (Test-Path -LiteralPath $backup -PathType Container) {
                try { Remove-Item -LiteralPath $backup -Recurse -Force } catch { Write-Warning "清理卸载暂存目录失败：$backup" }
            }
        }
        throw
    }
    if ($recovery -and (Test-Path -LiteralPath $recovery -PathType Container)) {
        try { Remove-Item -LiteralPath $recovery -Recurse -Force }
        catch { Write-Warning "清理恢复副本失败：$recovery" }
    }
    Write-Output "已卸载 $($identity.ProgId)。"
}
catch {
    $failure = $_
    foreach ($path in $registryPaths) {
        try { Remove-VisioRegistryTree -BaseKey $baseKey -Path $path } catch { }
    }
    for ($index = 0; $index -lt $registryPaths.Count; $index++) {
        try { Restore-VisioRegistrySnapshot -BaseKey $baseKey -Snapshot $snapshots[$index] } catch { }
    }
    $filesRestored = $false
    if ($recovery) {
        try { $filesRestored = Restore-VisioDirectoryFromCopy -RecoveryCopy $recovery -InstallDirectory $installFull -ExpectedParent $parent }
        catch { Write-Warning "file restore failed" }
    }
    if ($filesRestored) {
        if (Test-Path -LiteralPath $backup -PathType Container) { try { Remove-Item -LiteralPath $backup -Recurse -Force } catch { } }
        try { Remove-Item -LiteralPath $recovery -Recurse -Force } catch { }
    }
    throw $failure
}
finally { $baseKey.Dispose() }
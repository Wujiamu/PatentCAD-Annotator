Set-StrictMode -Version Latest

$script:PatentMarkerProductId = 'PatentMarker.WordPpt.ReadOnly'
$script:PatentMarkerClassId = '{4E9B0E7A-4D92-47AF-A9D7-7E769CB367D8}'
$script:PatentMarkerProgId = 'PatentOffice.PowerPointAddIn'
$script:PatentMarkerClassName = 'PatentOffice.PowerPoint.PowerPointAddIn'
$script:PatentMarkerManifestName = 'install-manifest.json'

function Get-PatentMarkerOfficeBitness {
    param([ValidateSet('Auto', '32', '64')][string]$Requested = 'Auto')

    if ($Requested -ne 'Auto') { return $Requested }
    if ([Environment]::Is64BitOperatingSystem) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
            [Microsoft.Win32.RegistryHive]::LocalMachine,
            [Microsoft.Win32.RegistryView]::Registry64)
        try {
            $configuration = $base.OpenSubKey('SOFTWARE\Microsoft\Office\ClickToRun\Configuration')
            if ($configuration) {
                try {
                    $platform = [string]$configuration.GetValue('Platform', '')
                    if ($platform -match '^x64$') { return '64' }
                    if ($platform -match '^x86$') { return '32' }
                }
                finally { $configuration.Dispose() }
            }
        }
        finally { $base.Dispose() }
    }

    $candidates = @()
    foreach ($baseDirectory in @($env:ProgramW6432, $env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ([string]::IsNullOrWhiteSpace($baseDirectory)) { continue }
        foreach ($officePath in @(
            'Microsoft Office\Root\Office16\POWERPNT.EXE',
            'Microsoft Office\Office16\POWERPNT.EXE',
            'Microsoft Office\Office14\POWERPNT.EXE')) {
            $candidates += (Join-Path $baseDirectory $officePath)
        }
    }
    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-Path -LiteralPath $candidate) {
            if ($env:ProgramW6432 -and
                $candidate.StartsWith($env:ProgramW6432.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { return '64' }
            return '32'
        }
    }
    throw '无法确定本机 PowerPoint 位数。请使用 -OfficeBitness 32 或 64 指定。'
}

function Open-PatentMarkerCurrentUserRegistry {
    param([ValidateSet('32', '64')][string]$OfficeBitness)
    $view = if ($OfficeBitness -eq '64') {
        [Microsoft.Win32.RegistryView]::Registry64
    } else {
        [Microsoft.Win32.RegistryView]::Registry32
    }
    return [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::CurrentUser, $view)
}

function Get-PatentMarkerRegistryPaths {
    param([string]$TestRegistryRoot)

    if ([string]::IsNullOrWhiteSpace($TestRegistryRoot)) {
        $root = 'Software'
        return @(
            ($root + '\Classes\CLSID\' + $script:PatentMarkerClassId),
            ($root + '\Classes\' + $script:PatentMarkerProgId),
            ($root + '\Microsoft\Office\PowerPoint\Addins\' + $script:PatentMarkerProgId)
        )
    }

    $root = $TestRegistryRoot.Trim('\')
    return @(
        ($root + '\Classes\CLSID\' + $script:PatentMarkerClassId),
        ($root + '\Classes\' + $script:PatentMarkerProgId),
        ($root + '\Office\PowerPoint\Addins\' + $script:PatentMarkerProgId)
    )
}

function Get-PatentMarkerRegistrySnapshot {
    param(
        [Microsoft.Win32.RegistryKey]$BaseKey,
        [string]$Path
    )

    $key = $BaseKey.OpenSubKey($Path)
    if (-not $key) { return $null }
    try {
        $values = @()
        foreach ($name in $key.GetValueNames()) {
            $kind = $key.GetValueKind($name)
            $value = $key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            $encoding = 'Value'
            if ($kind -eq [Microsoft.Win32.RegistryValueKind]::Binary -or
                $kind -eq [Microsoft.Win32.RegistryValueKind]::None) {
                $encoding = 'Base64'
                $value = [Convert]::ToBase64String([byte[]]$value)
            }
            elseif ($kind -eq [Microsoft.Win32.RegistryValueKind]::MultiString) {
                $encoding = 'Strings'
                $value = [string[]]$value
            }
            $values += [pscustomobject]@{ Name = $name; Kind = $kind.ToString(); Encoding = $encoding; Data = $value }
        }

        $children = @()
        foreach ($childName in $key.GetSubKeyNames()) {
            $child = Get-PatentMarkerRegistrySnapshot -BaseKey $BaseKey -Path ($Path + '\' + $childName)
            if ($null -ne $child) { $children += $child }
        }
        return [pscustomobject]@{ Path = $Path; Values = $values; Children = $children }
    }
    finally { $key.Dispose() }
}

function Remove-PatentMarkerRegistryTree {
    param(
        [Microsoft.Win32.RegistryKey]$BaseKey,
        [string]$Path
    )

    $separator = $Path.LastIndexOf('\')
    if ($separator -lt 1) { throw "拒绝删除不完整的注册表路径：$Path" }
    $parentPath = $Path.Substring(0, $separator)
    $leafName = $Path.Substring($separator + 1)
    $parent = $BaseKey.OpenSubKey($parentPath, $true)
    if (-not $parent) { return }
    try {
        if ($parent.GetSubKeyNames() -contains $leafName) {
            $parent.DeleteSubKeyTree($leafName, $false)
        }
    }
    finally { $parent.Dispose() }
}

function Restore-PatentMarkerRegistrySnapshot {
    param(
        [Microsoft.Win32.RegistryKey]$BaseKey,
        [psobject]$Snapshot
    )

    if ($null -eq $Snapshot) { return }
    $key = $BaseKey.CreateSubKey($Snapshot.Path)
    if (-not $key) { throw "无法恢复注册表路径：$($Snapshot.Path)" }
    try {
        foreach ($entry in $Snapshot.Values) {
            $kind = [Enum]::Parse([Microsoft.Win32.RegistryValueKind], [string]$entry.Kind)
            $value = $entry.Data
            if ($entry.Encoding -eq 'Base64') { $value = [Convert]::FromBase64String([string]$entry.Data) }
            elseif ($entry.Encoding -eq 'Strings') { $value = [string[]]$entry.Data }
            elseif ($kind -eq [Microsoft.Win32.RegistryValueKind]::DWord) { $value = [int]$entry.Data }
            elseif ($kind -eq [Microsoft.Win32.RegistryValueKind]::QWord) { $value = [long]$entry.Data }
            $key.SetValue([string]$entry.Name, $value, $kind)
        }
    }
    finally { $key.Dispose() }

    foreach ($child in $Snapshot.Children) {
        Restore-PatentMarkerRegistrySnapshot -BaseKey $BaseKey -Snapshot $child
    }
}

function Set-PatentMarkerRegistryValues {
    param(
        [Microsoft.Win32.RegistryKey]$BaseKey,
        [string]$Path,
        [hashtable]$Values
    )
    $key = $BaseKey.CreateSubKey($Path)
    if (-not $key) { throw "无法写入注册表路径：$Path" }
    try {
        foreach ($name in $Values.Keys) {
            $value = $Values[$name]
            if ($value -is [hashtable] -and $value.ContainsKey('Kind')) {
                $key.SetValue([string]$name, $value.Value, [Microsoft.Win32.RegistryValueKind]$value.Kind)
            }
            else {
                $key.SetValue([string]$name, $value)
            }
        }
    }
    finally { $key.Dispose() }
}

function Get-PatentMarkerOwnershipManifest {
    param([string]$InstallDirectory)
    $path = Join-Path $InstallDirectory $script:PatentMarkerManifestName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    try { return (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop) }
    catch { throw "产品所有权清单损坏，已停止以保护目录：$path" }
}

function Assert-PatentMarkerOwnedDirectory {
    param([string]$InstallDirectory)
    $directory = Get-Item -LiteralPath $InstallDirectory -Force
    if (-not $directory.PSIsContainer -or
        (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw "产品安装路径不是普通目录，已拒绝覆盖或删除：$InstallDirectory"
    }
    $manifest = Get-PatentMarkerOwnershipManifest -InstallDirectory $InstallDirectory
    if ($null -eq $manifest -or $manifest.ProductId -ne $script:PatentMarkerProductId -or
        $manifest.ClassId -ne $script:PatentMarkerClassId -or $manifest.ProgId -ne $script:PatentMarkerProgId) {
        throw "目标目录缺少匹配的产品所有权清单，已拒绝覆盖或删除：$InstallDirectory"
    }
    $expected = @('PatentOffice.PowerPoint.dll', 'Newtonsoft.Json.dll', $script:PatentMarkerManifestName)
    $actual = @(Get-ChildItem -LiteralPath $InstallDirectory -Force)
    if ($actual.Count -ne $expected.Count -or
        @($actual | Where-Object {
            $_.Name -notin $expected -or $_.PSIsContainer -or
            (($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
        }).Count -ne 0) {
        throw "产品目录中存在未登记资产或链接，已拒绝覆盖或删除：$InstallDirectory"
    }
    foreach ($name in @('PatentOffice.PowerPoint.dll', 'Newtonsoft.Json.dll')) {
        $recorded = [string]$manifest.Files.PSObject.Properties[$name].Value
        if ([string]::IsNullOrWhiteSpace($recorded) -or
            (Get-FileHash -LiteralPath (Join-Path $InstallDirectory $name) -Algorithm SHA256).Hash -ne $recorded) {
            throw "产品文件与所有权清单不一致，已拒绝覆盖或删除：$name"
        }
    }
    return $manifest
}

function Assert-PatentMarkerSiblingDirectory {
    param(
        [string]$Candidate,
        [string]$ExpectedParent
    )
    $candidateFull = [IO.Path]::GetFullPath($Candidate).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $parentFull = [IO.Path]::GetFullPath($ExpectedParent).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if (-not [string]::Equals([IO.Path]::GetDirectoryName($candidateFull), $parentFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝操作不在产品安装目录同级的路径：$candidateFull"
    }
    return $candidateFull
}

function Remove-PatentMarkerSiblingDirectory {
    param(
        [string]$Candidate,
        [string]$ExpectedParent
    )
    $safePath = Assert-PatentMarkerSiblingDirectory -Candidate $Candidate -ExpectedParent $ExpectedParent
    if (Test-Path -LiteralPath $safePath -PathType Container) {
        Remove-Item -LiteralPath $safePath -Recurse -Force
    }
}

Export-ModuleMember -Function Get-PatentMarkerOfficeBitness, Open-PatentMarkerCurrentUserRegistry, Get-PatentMarkerRegistryPaths, Get-PatentMarkerRegistrySnapshot, Remove-PatentMarkerRegistryTree, Restore-PatentMarkerRegistrySnapshot, Set-PatentMarkerRegistryValues, Get-PatentMarkerOwnershipManifest, Assert-PatentMarkerOwnedDirectory, Assert-PatentMarkerSiblingDirectory, Remove-PatentMarkerSiblingDirectory

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

function Test-PatentMarkerRegistryValueEqual {
    param($Expected, $Actual)

    if ($null -eq $Expected -and $null -eq $Actual) { return $true }
    if ($null -eq $Expected -or $null -eq $Actual) { return $false }
    if ($Expected -is [array] -or $Actual -is [array]) {
        $expectedItems = @($Expected)
        $actualItems = @($Actual)
        if ($expectedItems.Count -ne $actualItems.Count) { return $false }
        for ($index = 0; $index -lt $expectedItems.Count; $index++) {
            if ([string]$expectedItems[$index] -cne [string]$actualItems[$index]) { return $false }
        }
        return $true
    }
    return ([string]$Expected -ceq [string]$Actual)
}

function Assert-PatentMarkerRegistrySnapshotRestored {
    # Re-reads what the restore actually wrote. Callers may only re-enable the
    # add-in after this confirmation passes, so a restore that wrote nothing, or
    # wrote the wrong data, must fail here instead of looking successful. A null
    # snapshot means the key did not exist before, so it must not exist now.
    param(
        [Microsoft.Win32.RegistryKey]$BaseKey,
        [string]$Path,
        [psobject]$Snapshot
    )

    if ($null -eq $Snapshot) {
        $existing = $BaseKey.OpenSubKey($Path)
        if ($existing) {
            $existing.Dispose()
            throw "回滚后本应不存在的注册表路径仍然存在：$Path"
        }
        return
    }

    $key = $BaseKey.OpenSubKey($Snapshot.Path)
    if (-not $key) { throw "恢复后注册表路径不存在：$($Snapshot.Path)" }
    try {
        $expectedNames = @($Snapshot.Values | ForEach-Object { [string]$_.Name })
        $actualNames = @($key.GetValueNames())
        $missing = @($expectedNames | Where-Object { $actualNames -notcontains $_ })
        $unexpected = @($actualNames | Where-Object { $expectedNames -notcontains $_ })
        if ($missing.Count -gt 0 -or $unexpected.Count -gt 0) {
            throw ("恢复后注册表值集合不一致：{0}；缺少={1}；多出={2}" -f `
                $Snapshot.Path, ($missing -join ','), ($unexpected -join ','))
        }
        foreach ($entry in $Snapshot.Values) {
            $name = [string]$entry.Name
            $expected = $entry.Data
            if ($entry.Encoding -eq 'Base64') { $expected = [Convert]::FromBase64String([string]$entry.Data) }
            elseif ($entry.Encoding -eq 'Strings') { $expected = [string[]]$entry.Data }
            elseif ([string]$entry.Kind -eq 'DWord') { $expected = [int]$entry.Data }
            elseif ([string]$entry.Kind -eq 'QWord') { $expected = [long]$entry.Data }
            $actual = $key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            if (-not (Test-PatentMarkerRegistryValueEqual -Expected $expected -Actual $actual)) {
                throw "恢复后注册表值不一致：$($Snapshot.Path)::$name"
            }
        }
    }
    finally { $key.Dispose() }
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

function New-PatentMarkerRecoveryCopy {
    # A recursive delete can remove part of a directory before it fails, so the
    # directory being deleted can never double as the recovery point. This
    # copies the product directory to a separate sibling and verifies every
    # file hash before the caller starts deleting anything.
    param(
        [string]$InstallDirectory,
        [string]$ExpectedParent
    )
    $source = Assert-PatentMarkerSiblingDirectory -Candidate $InstallDirectory -ExpectedParent $ExpectedParent
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "恢复源目录不存在：$source"
    }
    $copy = Assert-PatentMarkerSiblingDirectory -Candidate (
        Join-Path $ExpectedParent ('.' + (Split-Path -Leaf $source) + '.recovery-' + [Guid]::NewGuid().ToString('N'))) `
        -ExpectedParent $ExpectedParent
    Copy-Item -LiteralPath $source -Destination $copy -Recurse -Force
    foreach ($file in @(Get-ChildItem -LiteralPath $source -Recurse -Force -File)) {
        $relative = $file.FullName.Substring($source.Length).TrimStart('\')
        $mirror = Join-Path $copy $relative
        if (-not (Test-Path -LiteralPath $mirror -PathType Leaf) -or
            (Get-FileHash -LiteralPath $mirror -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
            throw "恢复副本校验失败：$relative"
        }
    }
    return $copy
}

function Restore-PatentMarkerDirectoryFromCopy {
    # Replaces a partially deleted product directory with the verified copy.
    # Returns $true only when every file matches the recovery copy again.
    param(
        [string]$RecoveryCopy,
        [string]$InstallDirectory,
        [string]$ExpectedParent
    )
    $target = Assert-PatentMarkerSiblingDirectory -Candidate $InstallDirectory -ExpectedParent $ExpectedParent
    $source = Assert-PatentMarkerSiblingDirectory -Candidate $RecoveryCopy -ExpectedParent $ExpectedParent
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { return $false }
    if (Test-Path -LiteralPath $target -PathType Container) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    Copy-Item -LiteralPath $source -Destination $target -Recurse -Force
    foreach ($file in @(Get-ChildItem -LiteralPath $source -Recurse -Force -File)) {
        $relative = $file.FullName.Substring($source.Length).TrimStart('\')
        $mirror = Join-Path $target $relative
        if (-not (Test-Path -LiteralPath $mirror -PathType Leaf) -or
            (Get-FileHash -LiteralPath $mirror -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
            return $false
        }
    }
    return $true
}

Export-ModuleMember -Function Get-PatentMarkerOfficeBitness, Open-PatentMarkerCurrentUserRegistry, Get-PatentMarkerRegistryPaths, Get-PatentMarkerRegistrySnapshot, Remove-PatentMarkerRegistryTree, Restore-PatentMarkerRegistrySnapshot, Set-PatentMarkerRegistryValues, Get-PatentMarkerOwnershipManifest, Assert-PatentMarkerOwnedDirectory, Assert-PatentMarkerSiblingDirectory, Remove-PatentMarkerSiblingDirectory, New-PatentMarkerRecoveryCopy, Restore-PatentMarkerDirectoryFromCopy, Assert-PatentMarkerRegistrySnapshotRestored

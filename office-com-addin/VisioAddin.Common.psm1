Set-StrictMode -Version Latest

$script:VisioProductId = 'PatentMarker.WordVisio.ReadOnly'
$script:VisioClassId = '{D1D78625-AF57-462D-A1AB-5C47587AB30C}'
$script:VisioProgId = 'PatentOffice.VisioAddIn'
$script:VisioClassName = 'PatentOffice.Visio.VisioAddIn'
$script:VisioManifestName = 'install-manifest.json'

function Get-VisioAddinIdentity {
    return [pscustomobject]@{
        ProductId = $script:VisioProductId
        ClassId = $script:VisioClassId
        ProgId = $script:VisioProgId
        ClassName = $script:VisioClassName
        ManifestName = $script:VisioManifestName
    }
}

function Get-VisioOfficeBitness {
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

    $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    $candidates = @()
    if ($env:ProgramW6432) { $candidates += (Join-Path $env:ProgramW6432 'Microsoft Office\root\Office16\VISIO.EXE') }
    if ($env:ProgramFiles) { $candidates += (Join-Path $env:ProgramFiles 'Microsoft Office\root\Office16\VISIO.EXE') }
    if ($programFilesX86) { $candidates += (Join-Path $programFilesX86 'Microsoft Office\root\Office16\VISIO.EXE') }
    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-Path -LiteralPath $candidate) {
            if ($env:ProgramW6432 -and $candidate.StartsWith($env:ProgramW6432, [StringComparison]::OrdinalIgnoreCase)) { return '64' }
            return '32'
        }
    }
    throw '无法确定本机 Visio 位数；请安装 Visio 或检查 Office 安装路径。'
}

function Open-VisioAddinRegistry {
    param([ValidateSet('32', '64')][string]$OfficeBitness)
    $view = if ($OfficeBitness -eq '64') {
        [Microsoft.Win32.RegistryView]::Registry64
    } else {
        [Microsoft.Win32.RegistryView]::Registry32
    }
    return [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::CurrentUser, $view)
}

function Get-VisioAddinRegistryPaths {
    param([string]$TestRegistryRoot)
    $root = if ([string]::IsNullOrWhiteSpace($TestRegistryRoot)) { 'Software' } else { $TestRegistryRoot.Trim('\') }
    return @(
        ($root + '\Classes\CLSID\' + $script:VisioClassId),
        ($root + '\Classes\' + $script:VisioProgId),
        ($root + '\Microsoft\Visio\Addins\' + $script:VisioProgId)
    )
}

function Get-VisioOwnershipManifest {
    param([string]$InstallDirectory)
    $path = Join-Path $InstallDirectory $script:VisioManifestName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    try { return (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop) }
    catch { throw "Visio 加载项所有权清单损坏，已停止覆盖或删除：$path" }
}

function Assert-VisioOwnedDirectory {
    param([string]$InstallDirectory)
    $manifest = Get-VisioOwnershipManifest -InstallDirectory $InstallDirectory
    if ($null -eq $manifest -or $manifest.ProductId -ne $script:VisioProductId -or
        $manifest.ClassId -ne $script:VisioClassId -or $manifest.ProgId -ne $script:VisioProgId) {
        throw "Visio 加载项目录缺少匹配的产品所有权清单：$InstallDirectory"
    }
    $ownedNames = @('PatentOffice.Visio.dll', 'Newtonsoft.Json.dll', $script:VisioManifestName)
    $actualNames = @(Get-ChildItem -LiteralPath $InstallDirectory -Force | ForEach-Object { $_.Name })
    foreach ($name in $actualNames) {
        if ($ownedNames -notcontains $name) { throw "安装目录含有清单外文件，拒绝删除或覆盖：$name" }
    }
    foreach ($name in @('PatentOffice.Visio.dll', 'Newtonsoft.Json.dll')) {
        $file = Join-Path $InstallDirectory $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "产品文件缺失：$name" }
        $expected = [string]$manifest.Files.$name
        $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        if ([string]::IsNullOrWhiteSpace($expected) -or $expected -ne $actual) {
            throw "产品文件哈希与所有权清单不一致，拒绝覆盖或删除：$name"
        }
    }
    return $manifest
}

function Get-VisioRegistrySnapshot {
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
            $values += [pscustomobject]@{
                Name = $name
                Kind = $kind.ToString()
                Encoding = $encoding
                Data = $value
            }
        }
        $children = @()
        foreach ($childName in $key.GetSubKeyNames()) {
            $child = Get-VisioRegistrySnapshot -BaseKey $BaseKey -Path ($Path + '\' + $childName)
            if ($null -ne $child) { $children += $child }
        }
        return [pscustomobject]@{ Path = $Path; Values = $values; Children = $children }
    }
    finally { $key.Dispose() }
}

function Remove-VisioRegistryTree {
    param(
        [Microsoft.Win32.RegistryKey]$BaseKey,
        [string]$Path
    )
    $separator = $Path.LastIndexOf('\')
    if ($separator -lt 1) { throw "拒绝删除不完整的 Visio 注册表路径：$Path" }
    $parent = $BaseKey.OpenSubKey($Path.Substring(0, $separator), $true)
    if (-not $parent) { return }
    try {
        $leaf = $Path.Substring($separator + 1)
        if ($parent.GetSubKeyNames() -contains $leaf) { $parent.DeleteSubKeyTree($leaf, $false) }
    }
    finally { $parent.Dispose() }
}

function Restore-VisioRegistrySnapshot {
    param(
        [Microsoft.Win32.RegistryKey]$BaseKey,
        [psobject]$Snapshot
    )
    if ($null -eq $Snapshot) { return }
    $key = $BaseKey.CreateSubKey($Snapshot.Path)
    if (-not $key) { throw "无法恢复 Visio 注册表路径：$($Snapshot.Path)" }
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
        Restore-VisioRegistrySnapshot -BaseKey $BaseKey -Snapshot $child
    }
}

function Set-VisioRegistryValues {
    param(
        [Microsoft.Win32.RegistryKey]$BaseKey,
        [string]$Path,
        [hashtable]$Values
    )
    $key = $BaseKey.CreateSubKey($Path)
    if (-not $key) { throw "无法写入 Visio 注册表路径：$Path" }
    try {
        foreach ($name in $Values.Keys) {
            $value = $Values[$name]
            if ($value -is [hashtable] -and $value.ContainsKey('Kind')) {
                $key.SetValue([string]$name, $value.Value, [Microsoft.Win32.RegistryValueKind]$value.Kind)
            }
            else { $key.SetValue([string]$name, $value) }
        }
    }
    finally { $key.Dispose() }
}

function Assert-VisioSiblingDirectory {
    param([string]$Candidate, [string]$ExpectedParent)
    $candidateFull = [IO.Path]::GetFullPath($Candidate).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $parentFull = [IO.Path]::GetFullPath($ExpectedParent).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if (-not [string]::Equals([IO.Path]::GetDirectoryName($candidateFull), $parentFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝操作不在 Visio 产品目录同级的路径：$candidateFull"
    }
    return $candidateFull
}

function Remove-VisioSiblingDirectory {
    param([string]$Candidate, [string]$ExpectedParent)
    $safePath = Assert-VisioSiblingDirectory -Candidate $Candidate -ExpectedParent $ExpectedParent
    if (Test-Path -LiteralPath $safePath -PathType Container) { Remove-Item -LiteralPath $safePath -Recurse -Force }
}

Export-ModuleMember -Function Get-VisioAddinIdentity, Get-VisioOfficeBitness, Open-VisioAddinRegistry, Get-VisioAddinRegistryPaths, Get-VisioOwnershipManifest, Assert-VisioOwnedDirectory, Get-VisioRegistrySnapshot, Remove-VisioRegistryTree, Restore-VisioRegistrySnapshot, Set-VisioRegistryValues, Assert-VisioSiblingDirectory, Remove-VisioSiblingDirectory

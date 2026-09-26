[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [string]$SourceRoot = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageFull = [IO.Path]::GetFullPath($PackagePath)
if (-not (Test-Path -LiteralPath $packageFull -PathType Leaf)) { throw "发布包不存在：$packageFull" }

$expectedNames = @(
    'install-office-addin.ps1', 'uninstall-office-addin.ps1', 'OfficeAddin.Common.psm1',
    'README.md', 'LICENSE', 'third-party/Newtonsoft.Json-LICENSE.md',
    'dist/PatentOffice.PowerPoint.dll', 'dist/Newtonsoft.Json.dll'
)
$archive = [IO.Compression.ZipFile]::OpenRead($packageFull)
$sha = [Security.Cryptography.SHA256]::Create()
try {
    $entries = @($archive.Entries)
    $actualNames = @($entries | ForEach-Object { $_.FullName })
    if ($actualNames.Count -ne ($expectedNames.Count + 1) -or
        @($actualNames | Group-Object | Where-Object { $_.Count -ne 1 }).Count -ne 0 -or
        @($actualNames | Where-Object { $_ -notin ($expectedNames + 'package-manifest.json') }).Count -ne 0) {
        throw '发布包文件清单不精确，含缺项、重复项或额外文件。'
    }
    foreach ($name in $expectedNames) {
        if ($actualNames -notcontains $name) { throw "发布包缺少 $name" }
    }

    $reader = [IO.StreamReader]::new($archive.GetEntry('package-manifest.json').Open(), [Text.Encoding]::UTF8)
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json -ErrorAction Stop }
    finally { $reader.Dispose() }
    if ($manifest.Product -ne 'PatentMarker.WordPpt.ReadOnly' -or
        $manifest.Version -notmatch '^\d+\.\d+\.\d+\.\d+$' -or
        $manifest.TargetFramework -ne '.NET Framework 4.0') {
        throw '发布包产品身份、版本或目标框架不匹配。'
    }
    if (-not [string]::IsNullOrWhiteSpace($SourceRoot)) {
        $assemblyPath = Join-Path $SourceRoot 'dist\PatentOffice.PowerPoint.dll'
        $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version.ToString()
        if ($manifest.Version -ne $assemblyVersion) { throw '发布包版本与产品程序集不一致。' }
    }

    $manifestNames = @($manifest.Files.PSObject.Properties | ForEach-Object { $_.Name })
    if ($manifestNames.Count -ne $expectedNames.Count) { throw '发布包哈希清单数量不匹配。' }
    foreach ($name in $expectedNames) {
        if ($manifestNames -notcontains $name) { throw "哈希清单缺少 $name" }
        $stream = $archive.GetEntry($name).Open()
        try { $actualHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose() }
        $expectedHash = [string]$manifest.Files.PSObject.Properties[$name].Value
        if ($actualHash -ne $expectedHash) { throw "发布包内文件哈希不符：$name" }
        if (-not [string]::IsNullOrWhiteSpace($SourceRoot)) {
            $source = Join-Path ([IO.Path]::GetFullPath($SourceRoot)) ($name.Replace('/', '\'))
            if ($name -eq 'README.md') { $source = Join-Path $SourceRoot 'ppt-release-readme.md' }
            if ($name -eq 'LICENSE') { $source = Join-Path $SourceRoot '..\LICENSE' }
            if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $actualHash) {
                throw "发布包与源文件不一致：$name"
            }
        }
    }
}
finally { $sha.Dispose(); $archive.Dispose() }
Write-Output "PASS|PPT_PACKAGE_CONTENT|$packageFull|$($manifest.Version)"

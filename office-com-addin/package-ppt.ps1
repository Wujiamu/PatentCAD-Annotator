[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$SkipBuildAndTests
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot 'release'
}

if (-not $SkipBuildAndTests) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    if (-not $?) { throw 'PowerPoint 构建失败。' }
    & (Join-Path $PSScriptRoot 'verify-installer.ps1')
    if (-not $?) { throw 'PowerPoint 安装器隔离回归失败。' }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$files = [ordered]@{
    'install-office-addin.ps1' = (Join-Path $PSScriptRoot 'install-office-addin.ps1')
    'uninstall-office-addin.ps1' = (Join-Path $PSScriptRoot 'uninstall-office-addin.ps1')
    'OfficeAddin.Common.psm1' = (Join-Path $PSScriptRoot 'OfficeAddin.Common.psm1')
    'README.md' = (Join-Path $PSScriptRoot 'ppt-release-readme.md')
    'LICENSE' = (Join-Path $PSScriptRoot '..\LICENSE')
    'third-party/Newtonsoft.Json-LICENSE.md' = (Join-Path $PSScriptRoot 'third-party\Newtonsoft.Json-LICENSE.md')
    'dist/PatentOffice.PowerPoint.dll' = (Join-Path $PSScriptRoot 'dist\PatentOffice.PowerPoint.dll')
    'dist/Newtonsoft.Json.dll' = (Join-Path $PSScriptRoot 'dist\Newtonsoft.Json.dll')
}
foreach ($source in $files.Values) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "发布包缺少源文件：$source" }
}

$assembly = [Reflection.AssemblyName]::GetAssemblyName($files['dist/PatentOffice.PowerPoint.dll'])
$version = $assembly.Version.ToString()
$destinationDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $destinationDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
}
$packagePath = Join-Path $destinationDirectory ("PatentMarker-WordPowerPoint-$version-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.zip')
if (Test-Path -LiteralPath $packagePath) { throw "拒绝覆盖已有发布包：$packagePath" }

$manifestFiles = [ordered]@{}
foreach ($entryName in $files.Keys) {
    $manifestFiles[$entryName] = (Get-FileHash -LiteralPath $files[$entryName] -Algorithm SHA256).Hash
}
$manifest = [ordered]@{
    Product = 'PatentMarker.WordPpt.ReadOnly'
    Version = $version
    TargetFramework = '.NET Framework 4.0'
    Files = $manifestFiles
}

$archive = [IO.Compression.ZipFile]::Open($packagePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($entryName in $files.Keys) {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $files[$entryName], $entryName, [IO.Compression.CompressionLevel]::Optimal)
    }
    $entry = $archive.CreateEntry('package-manifest.json')
    $stream = $entry.Open()
    try {
        $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
        try { $writer.Write(($manifest | ConvertTo-Json -Depth 6)) }
        finally { $writer.Dispose() }
    }
    finally { $stream.Dispose() }
}
finally { $archive.Dispose() }

& (Join-Path $PSScriptRoot 'verify-ppt-package.ps1') -PackagePath $packagePath -SourceRoot $PSScriptRoot
if (-not $?) { throw 'PowerPoint 发布包复核失败。' }
Write-Output "PASS|PPT_RELEASE_PACKAGE|$packagePath|SHA256=$((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash)"

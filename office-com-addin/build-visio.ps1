[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\PatentOffice.Visio\PatentOffice.Visio.csproj'
$output = Join-Path $PSScriptRoot ('src\PatentOffice.Visio\bin\' + $Configuration + '\net40')
$distribution = Join-Path $PSScriptRoot 'dist\Visio'

dotnet restore $project --ignore-failed-sources -p:NuGetAudit=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'Visio 加载项依赖还原失败。' }
dotnet build $project --configuration $Configuration --nologo -v minimal -p:NuGetAudit=false --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Visio 加载项构建失败。' }

if (-not (Test-Path -LiteralPath $distribution -PathType Container)) {
    New-Item -ItemType Directory -Path $distribution | Out-Null
}
foreach ($name in @('PatentOffice.Visio.dll', 'Newtonsoft.Json.dll')) {
    $source = Join-Path $output $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "构建产物缺失：$name" }
    $destination = Join-Path $distribution $name
    Copy-Item -LiteralPath $source -Destination $destination
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) {
        throw "发布暂存哈希不一致：$name"
    }
}
Write-Output "Visio 加载项构建和发布暂存完成：$distribution"

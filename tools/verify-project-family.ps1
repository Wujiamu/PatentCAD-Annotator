[CmdletBinding()]
param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$canonical = Join-Path $root 'cad-plugin/Shared/IO/NumberIdentity.cs'
if (-not (Test-Path -LiteralPath $canonical -PathType Leaf)) {
    throw 'FAIL|PROJECT_FAMILY|Missing canonical NumberIdentity.cs'
}
$canonical = [IO.Path]::GetFullPath($canonical)
$projects = @('2007', '2010', '2013', '2015', '2025') |
    ForEach-Object { "cad-plugin/$_/PatentMarker/PatentMarker.csproj" }
$projects += @('PowerPoint', 'Visio') |
    ForEach-Object { "office-com-addin/src/PatentOffice.$_/PatentOffice.$_.csproj" }

foreach ($relative in $projects) {
    $project = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "FAIL|PROJECT_FAMILY|Missing project: $relative"
    }
    [xml]$xml = [IO.File]::ReadAllText($project)
    $includes = @($xml.SelectNodes("//*[local-name()='Compile' and @Include]") |
        Where-Object { [IO.Path]::GetFileName($_.Include) -eq 'NumberIdentity.cs' })
    if ($includes.Count -ne 1) {
        throw "FAIL|PROJECT_FAMILY|Expected one NumberIdentity source link: $relative"
    }
    $resolved = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $project) $includes[0].Include))
    if (-not [string]::Equals($resolved, $canonical, [StringComparison]::OrdinalIgnoreCase)) {
        throw "FAIL|PROJECT_FAMILY|Project links a different NumberIdentity source: $relative"
    }
}
$officeSource = Join-Path $root 'office-com-addin/src'
if (@(Get-ChildItem -LiteralPath $officeSource -Recurse -File -Filter 'NumberIdentity.cs').Count -ne 0) {
    throw 'FAIL|PROJECT_FAMILY|Duplicate Office NumberIdentity source'
}
Write-Output 'PASS|PROJECT_FAMILY|7_PRODUCT_PROJECTS|ONE_NUMBER_IDENTITY_SOURCE'

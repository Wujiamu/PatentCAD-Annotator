param(
    [Parameter(Mandatory = $true)]
    [string]$TestProject
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = (Resolve-Path $TestProject).Path
$projectDir = Split-Path -Parent $project
$assemblyName = [IO.Path]::GetFileNameWithoutExtension($project)

dotnet build $project -c Release --nologo -v quiet | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Test project build failed: $project" }

$runnerProject = Join-Path $root "tools\FactRunner\FactRunner.csproj"
dotnet build $runnerProject -c Release --nologo -v quiet | Out-Host
if ($LASTEXITCODE -ne 0) { throw "FactRunner build failed" }

$testDll = Get-ChildItem (Join-Path $projectDir "bin\Release") -Recurse -File -Filter "$assemblyName.dll" |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if ($null -eq $testDll) { throw "Built test assembly not found: $assemblyName.dll" }

$runnerDir = Join-Path $root "tools\FactRunner\bin\Release\net8.0-windows"
$testDir = $testDll.DirectoryName
foreach ($name in @(
    "PatentMarker.FactRunner.dll",
    "PatentMarker.FactRunner.runtimeconfig.json",
    "PatentMarker.FactRunner.deps.json")) {
    Copy-Item (Join-Path $runnerDir $name) $testDir -Force
}

Push-Location $testDir
try {
    dotnet exec (Join-Path $testDir "PatentMarker.FactRunner.dll") $testDll.FullName
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}
exit $code
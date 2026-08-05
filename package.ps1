# package.ps1 - reproducible local release staging for all editions
#
# The default action writes to a new staging directory outside the repository
# and never overwrites an existing deployment package. Use -Apply only after
# reviewing the staged files; it creates a timestamped DLL backup first.

param(
    [ValidateSet("2007", "2010", "2013", "2015", "2025", "all")]
    [string]$Version = "all",
    [string]$OutputRoot = "",
    [switch]$Apply
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$OutputRoot = if ([string]::IsNullOrEmpty($OutputRoot)) {
    Join-Path ([System.IO.Path]::GetTempPath()) ("PatentCAD-Annotator-release-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
} else { $OutputRoot }
$versions = if ($Version -eq "all") { @("2007", "2010", "2013", "2015", "2025") } else { @($Version) }
$vbaFiles = @("Patterns.bas", "DictModel.bas", "JsonWriter.bas", "PatentExtractor.bas", "AutoExport.bas", "clsSaveHook.cls")
$ilrepack = Join-Path $root "tools\ilrepack\tools\ILRepack.exe"

function Fail($message) { throw $message }

function Get-SourceDll {
    param([string]$ver)
    $dir = Join-Path $root "cad-plugin\$ver\PatentMarker\bin\Release"
    if ($ver -eq "2025") { return Join-Path $dir "net8.0-windows\PatentMarker.dll" }
    return Join-Path $dir "PatentMarker.dll"
}

function Get-AssemblyReferenceNames {
    param([string]$path)
    # Reflection-only assemblies cannot be unloaded from one AppDomain and
    # all editions use the same simple assembly name. Inspect each DLL in a
    # short-lived Windows PowerShell child process instead.
    $command = '& { param([string]$p) $a=[System.Reflection.Assembly]::ReflectionOnlyLoadFrom($p); $a.GetReferencedAssemblies() | ForEach-Object { $_.Name } }'
    $output = & powershell.exe -NoProfile -Command $command $path 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { Fail "Unable to read assembly references: $path" }
    return @($output | ForEach-Object { [string]$_ })
}

function Assert-VbaSync {
    foreach ($file in $vbaFiles) {
        $hashes = @()
        foreach ($ver in $versions) {
            $path = Join-Path $root "PatentMarker-$ver-deploy\vba\$file"
            if (-not (Test-Path -LiteralPath $path)) { Fail "Missing VBA module: $path" }
            $hashes += (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        }
        if (@($hashes | Select-Object -Unique).Count -ne 1) {
            Fail "VBA module drift detected: $file"
        }
    }
}

function Invoke-IlRepack {
    param([string]$ver, [string]$stage, [string]$sourceDll)
    if (-not (Test-Path -LiteralPath $ilrepack)) { Fail "ILRepack not found: $ilrepack" }
    $newtonsoftPath = if ($ver -eq "2013") {
        Join-Path $root "cad-plugin\packages\Newtonsoft.Json.13.0.3\lib\net35\Newtonsoft.Json.dll"
    } else {
        Join-Path $root "cad-plugin\packages\Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll"
    }
    if (-not (Test-Path -LiteralPath $newtonsoftPath)) { Fail "Missing Newtonsoft.Json input for ${ver}: $newtonsoftPath" }

    $outDll = Join-Path $stage "PatentMarker.dll"
    $libDir = Join-Path $root "cad-plugin\$ver\PatentMarker\lib"
    $output = & $ilrepack "/out:$outDll" "/target:library" "/internalize" "/lib:$libDir" $sourceDll $newtonsoftPath 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0 -or -not (Test-Path -LiteralPath $outDll)) {
        Fail "$ver ILRepack failed (exit=$exitCode)"
    }
}

function Stage-Version {
    param([string]$ver)
    $template = Join-Path $root "PatentMarker-$ver-deploy"
    $stage = Join-Path $OutputRoot $ver
    $sourceDll = Get-SourceDll $ver
    if (-not (Test-Path -LiteralPath $template)) { Fail "Missing deployment template: $template" }
    if (-not (Test-Path -LiteralPath $sourceDll)) { Fail "Missing built DLL: $sourceDll" }
    if (Test-Path -LiteralPath $stage) { Fail "Stage directory already exists; choose a new OutputRoot: $stage" }

    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $template -Force) {
        if ($item.Name -in @("PatentMarker.dll", "PatentMarker.repacked.dll", "PatentMarker.repacked.pdb")) { continue }
        Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $stage $item.Name) -Recurse -Force
    }

    if ($ver -eq "2013" -or $ver -eq "2015") {
        Invoke-IlRepack $ver $stage $sourceDll
    } else {
        Copy-Item -LiteralPath $sourceDll -Destination (Join-Path $stage "PatentMarker.dll") -Force
    }

    $refs = Get-AssemblyReferenceNames (Join-Path $stage "PatentMarker.dll")
    if ($ver -eq "2013" -or $ver -eq "2015") {
        if ($refs -contains "Newtonsoft.Json") { Fail "$ver merged DLL still references external Newtonsoft.Json" }
    }
    if (Test-Path -LiteralPath (Join-Path $stage "Newtonsoft.Json.dll")) {
        Fail "External Newtonsoft.Json.dll must not be present in $ver staging"
    }
    Write-Host "[OK] $ver staged: $stage"
    return $stage
}

Assert-VbaSync
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$staged = @()
foreach ($ver in $versions) { $staged += Stage-Version $ver }

if ($Apply) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    foreach ($ver in $versions) {
        $deploy = Join-Path $root "PatentMarker-$ver-deploy"
        $source = Join-Path $OutputRoot "$ver\PatentMarker.dll"
        $target = Join-Path $deploy "PatentMarker.dll"
        if (Test-Path -LiteralPath $target) {
            Copy-Item -LiteralPath $target -Destination "$target.bak.$stamp" -Force
        }
        Copy-Item -LiteralPath $source -Destination $target -Force
        Write-Host "[APPLY] $target (backup: $target.bak.$stamp)"
    }
}

Write-Host "Release staging completed: $OutputRoot"

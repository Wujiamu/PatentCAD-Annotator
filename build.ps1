# ============================================================================
# build.ps1 - PatentCAD-Annotator build & environment check script
#
# Usage:
#   .\build.ps1                        Build the 2025 edition (default)
#   .\build.ps1 -Version 2015          Build a specific edition
#   .\build.ps1 -Version all           Check/build all 5 editions
#   .\build.ps1 -Check                 Doctor mode: check environment only
#   .\build.ps1 -Structure             Structure integrity check (CI, no SDK DLL needed)
#
# Notes:
#   - AutoCAD SDK DLLs (acdbmgd.dll etc.) are NOT in the repo (licensing).
#     Copy them from your AutoCAD install dir into each edition's lib\ folder.
#   - 2025 edition uses SDK-style csproj -> built with "dotnet build".
#   - 2007/2010/2013/2015 use legacy MSBuild csproj -> need MSBuild/Visual Studio.
# ============================================================================

param(
    [string]$Version = "2025",
    [switch]$Check,
    [switch]$Structure
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# Edition -> required SDK DLLs
$script:DllMap = [ordered]@{
    "2007" = @("acdbmgd.dll", "acmgd.dll")
    "2010" = @("acdbmgd.dll", "acmgd.dll")
    "2013" = @("acdbmgd.dll", "acmgd.dll", "accoremgd.dll")
    "2015" = @("acdbmgd.dll", "acmgd.dll", "accoremgd.dll")
    "2025" = @("acdbmgd.dll", "acmgd.dll", "accoremgd.dll")
}

# Edition -> .NET / annotation API / build tool
$script:VersionInfo = [ordered]@{
    "2007" = @{ Net = ".NET 2.0"; Api = "Leader + MText"; Tool = "MSBuild"; DllNote = "AutoCAD 2007-2009 install dir" }
    "2010" = @{ Net = ".NET 3.5"; Api = "Leader + MText"; Tool = "MSBuild"; DllNote = "AutoCAD 2010-2012 install dir" }
    "2013" = @{ Net = ".NET 4.0"; Api = "MLeader";        Tool = "MSBuild"; DllNote = "AutoCAD 2013-2014 install dir" }
    "2015" = @{ Net = ".NET 4.5"; Api = "MLeader";        Tool = "MSBuild"; DllNote = "AutoCAD 2015-2024 install dir" }
    "2025" = @{ Net = ".NET 8.0"; Api = "MLeader";        Tool = "dotnet";  DllNote = "AutoCAD 2025+ install dir" }
}

function Write-Section($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-Ok($msg)      { Write-Host "  [OK]   $msg" -ForegroundColor Green }
function Write-Warn2($msg)   { Write-Host "  [WARN] $msg" -ForegroundColor Yellow }
function Write-Err2($msg)    { Write-Host "  [FAIL] $msg" -ForegroundColor Red }

function Get-ProjectDir($ver) {
    return Join-Path $root "cad-plugin\$ver\PatentMarker"
}

# Returns array of missing DLL file names (empty = all present)
function Get-MissingDlls($ver) {
    $libDir = Join-Path (Get-ProjectDir $ver) "lib"
    $missing = @()
    foreach ($dll in $script:DllMap[$ver]) {
        $p = Join-Path $libDir $dll
        if (-not (Test-Path $p)) { $missing += $dll }
    }
    return $missing
}

function Test-CommandExists($cmd) {
    return [bool](Get-Command $cmd -ErrorAction SilentlyContinue)
}

# ----------------------------------------------------------------------------
# Structure integrity check (CI; does NOT require SDK DLLs)
# ----------------------------------------------------------------------------
function Invoke-StructureCheck {
    Write-Section "Structure integrity check (no SDK DLL required)"
    $failCount = 0

    foreach ($ver in $script:DllMap.Keys) {
        $projDir = Get-ProjectDir $ver
        $csproj = Join-Path $projDir "PatentMarker.csproj"

        # 1. csproj exists and is parseable XML
        if (-not (Test-Path $csproj)) {
            Write-Err2 "$ver : PatentMarker.csproj not found"
            $failCount++
            continue
        }
        try {
            [xml]$xml = Get-Content $csproj -Raw
        } catch {
            Write-Err2 "$ver : csproj XML parse failed - $_"
            $failCount++
            continue
        }

        # 2. Legacy csproj: verify every <Compile Include> source file exists
        $compileNodes = $xml.SelectNodes("//*[local-name()='Compile']")
        $missingSrc = 0
        foreach ($node in $compileNodes) {
            $inc = $node.GetAttribute("Include")
            if ($inc) {
                $srcPath = Join-Path $projDir ($inc -replace '/', '\')
                if (-not (Test-Path $srcPath)) {
                    Write-Err2 "$ver : source file referenced in csproj is missing - $inc"
                    $missingSrc++
                }
            }
        }

        # 3. Deploy package completeness: PatentMarker.dll + 6 VBA modules
        $deployDir = Join-Path $root "PatentMarker-$ver-deploy"
        $deployDll = Join-Path $deployDir "PatentMarker.dll"
        $vbaFiles = @("Patterns.bas","DictModel.bas","JsonWriter.bas","PatentExtractor.bas","AutoExport.bas","clsSaveHook.cls")
        $missingVba = @()
        foreach ($v in $vbaFiles) {
            if (-not (Test-Path (Join-Path $deployDir "vba\$v"))) { $missingVba += $v }
        }

        if ($missingSrc -eq 0) { Write-Ok "$ver : csproj parseable, source refs complete" } else { $failCount += $missingSrc }
        if (-not (Test-Path $deployDll)) { Write-Warn2 "$ver : deploy package missing PatentMarker.dll" }
        if ($missingVba.Count -gt 0) { Write-Warn2 "$ver : deploy package missing VBA modules - $($missingVba -join ', ')" }
    }

    Write-Section "Structure check result"
    if ($failCount -eq 0) {
        Write-Ok "All structure checks passed. Real compilation needs local SDK DLLs: run .\build.ps1 -Check"
        exit 0
    } else {
        Write-Err2 "Found $failCount structure issue(s). Fix them and retry."
        exit 1
    }
}

# ----------------------------------------------------------------------------
# Doctor mode: check environment readiness for one edition
# ----------------------------------------------------------------------------
function Invoke-Doctor($ver) {
    $info = $script:VersionInfo[$ver]
    Write-Section "Edition $ver ($($info.Net) / $($info.Api))"

    $projDir = Get-ProjectDir $ver
    if (-not (Test-Path $projDir)) {
        Write-Err2 "Project dir not found: $projDir"
        return $false
    }
    Write-Ok "Project dir exists"

    # SDK DLLs
    $missing = Get-MissingDlls $ver
    if ($missing.Count -eq 0) {
        Write-Ok "SDK DLLs present ($($script:DllMap[$ver] -join ', '))"
    } else {
        Write-Err2 "Missing SDK DLLs: $($missing -join ', ')"
        Write-Host "         Copy these from the $($info.DllNote) into:" -ForegroundColor Yellow
        Write-Host "         $(Join-Path $projDir 'lib')" -ForegroundColor Yellow
        foreach ($m in $missing) { Write-Host "           - $m" -ForegroundColor Yellow }
        return $false
    }

    # Build toolchain
    if ($info.Tool -eq "dotnet") {
        if (Test-CommandExists "dotnet") {
            $dv = (dotnet --version) 2>$null
            Write-Ok "dotnet CLI available ($dv)"
        } else {
            Write-Err2 "dotnet CLI not found. Install the .NET 8 SDK."
            return $false
        }
    } else {
        if (Test-CommandExists "msbuild") {
            Write-Ok "MSBuild available"
        } else {
            Write-Warn2 "MSBuild not found. Edition $ver uses a legacy csproj and needs MSBuild from Visual Studio or Build Tools."
            return $false
        }
    }

    Write-Ok "Edition $ver environment ready. Build with: .\build.ps1 -Version $ver"
    return $true
}

# ----------------------------------------------------------------------------
# Build one edition
# ----------------------------------------------------------------------------
function Invoke-BuildVersion($ver) {
    $info = $script:VersionInfo[$ver]
    Write-Section "Building edition $ver ($($info.Net) / $($info.Api))"

    # Check DLLs first
    $missing = Get-MissingDlls $ver
    if ($missing.Count -gt 0) {
        Write-Err2 "Missing SDK DLLs: $($missing -join ', ')"
        Write-Host "         Copy them from the $($info.DllNote) into $(Join-Path (Get-ProjectDir $ver) 'lib')" -ForegroundColor Yellow
        return $false
    }

    $projDir = Get-ProjectDir $ver
    if ($info.Tool -eq "dotnet") {
        if (-not (Test-CommandExists "dotnet")) {
            Write-Err2 "dotnet CLI not found. Install the .NET 8 SDK."
            return $false
        }
        Push-Location $projDir
        try {
            dotnet build -c Release --nologo -v minimal
            $ok = ($LASTEXITCODE -eq 0)
        } finally {
            Pop-Location
        }
        if ($ok) { Write-Ok "Edition $ver built successfully"; return $true }
        else { Write-Err2 "Edition $ver build failed (exit $LASTEXITCODE)"; return $false }
    } else {
        if (-not (Test-CommandExists "msbuild")) {
            Write-Warn2 "MSBuild not found; cannot auto-build edition $ver (legacy csproj)."
            Write-Host "         Open cad-plugin\$ver\PatentMarker\PatentMarker.csproj in Visual Studio to build manually." -ForegroundColor Yellow
            return $false
        }
        $csproj = Join-Path $projDir "PatentMarker.csproj"
        & msbuild $csproj /t:Build /p:Configuration=Release /v:minimal /nologo
        $ok = ($LASTEXITCODE -eq 0)
        if ($ok) { Write-Ok "Edition $ver built successfully"; return $true }
        else { Write-Err2 "Edition $ver build failed (exit $LASTEXITCODE)"; return $false }
    }
}

# ============================================================================
# Main
# ============================================================================

if ($Structure) {
    Invoke-StructureCheck
    return
}

$versions = if ($Version -eq "all") { @($script:DllMap.Keys) } else { @($Version) }

# Validate edition names
foreach ($v in $versions) {
    if (-not $script:DllMap.Contains($v)) {
        Write-Err2 "Invalid edition: $v. Valid values: $($script:DllMap.Keys -join ', '), all"
        exit 1
    }
}

$allOk = $true
foreach ($v in $versions) {
    if ($Check) {
        $r = Invoke-Doctor $v
    } else {
        $r = Invoke-BuildVersion $v
    }
    if (-not $r) { $allOk = $false }
}

Write-Section "Done"
if ($allOk) {
    Write-Ok "All requested editions processed successfully"
    exit 0
} else {
    Write-Warn2 "Some editions did not pass. See [FAIL]/[WARN] messages above."
    exit 1
}

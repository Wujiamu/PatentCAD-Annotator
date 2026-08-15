# ============================================================================
# build.ps1 - PatentCAD-Annotator build & environment check script
#
# Usage:
#   .\build.ps1                        Build the 2025 edition (default)
#   .\build.ps1 -Version 2015          Build a specific edition
#   .\build.ps1 -Version all           Check/build all 5 editions
#   .\build.ps1 -Check                 Doctor mode: check environment only
#   .\build.ps1 -Structure             Structure integrity check (CI, no SDK DLL needed)
#   .\build.ps1 -Simulation            Run simulated host contract tests for 2007/2010/2013/2015
#   .\check-autocad-host.ps1            Read-only AutoCAD/COM/licensing prerequisite inventory
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
    [switch]$Structure,
    [switch]$Static,
    [switch]$Simulation
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
    "2013" = @{ Net = ".NET 4.0"; Api = "Leader + MText"; Tool = "MSBuild"; DllNote = "AutoCAD 2013-2014 install dir" }
    "2015" = @{ Net = ".NET 4.5"; Api = "Leader + MText"; Tool = "MSBuild"; DllNote = "AutoCAD 2015-2024 install dir" }
    "2025" = @{ Net = ".NET 8.0"; Api = "Leader + MText"; Tool = "dotnet";  DllNote = "AutoCAD 2025+ install dir" }
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

# Locate MSBuild even when Visual Studio Build Tools is installed without
# adding MSBuild.exe to PATH. Keep the fallback explicit so legacy editions
# remain buildable from a plain PowerShell prompt.
function Get-MSBuildPath {
    $command = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($command -and $command.Source) {
        return $command.Source
    }

    $vswhereCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"),
        (Join-Path $env:ProgramFiles "Microsoft Visual Studio\Installer\vswhere.exe")
    )
    foreach ($vswhere in $vswhereCandidates) {
        if (-not (Test-Path -LiteralPath $vswhere)) { continue }
        $installPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
        if ($LASTEXITCODE -eq 0 -and $installPath) {
            $candidate = Join-Path ($installPath | Select-Object -First 1) "MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path -LiteralPath $candidate) { return $candidate }
        }
    }

    $knownCandidates = @(
        "C:\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($candidate in $knownCandidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
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
        if (-not (Test-Path $deployDll)) {
            Write-Err2 "$ver : deploy package missing PatentMarker.dll"
            $failCount++
        }
        if ($missingVba.Count -gt 0) {
            Write-Err2 "$ver : deploy package missing VBA modules - $($missingVba -join ', ')"
            $failCount += $missingVba.Count
        }
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
# Static analysis check (CI; does NOT require SDK DLLs)
# ----------------------------------------------------------------------------
function Invoke-StaticCheck {
    Write-Section "Static analysis check (no SDK DLL required)"
    $failCount = 0
    $warnCount = 0

    # ---- 1. VBA cross-package consistency ----
    Write-Host "`n  --- VBA cross-package consistency ---"
    $vbaFiles = @("Patterns.bas","DictModel.bas","JsonWriter.bas","PatentExtractor.bas","AutoExport.bas","clsSaveHook.cls")
    $deployVersions = @("2007","2010","2013","2015","2025")
    foreach ($vf in $vbaFiles) {
        $hashes = @{}
        foreach ($ver in $deployVersions) {
            $fpath = Join-Path $root "PatentMarker-$ver-deploy\vba\$vf"
            if (Test-Path $fpath) {
                $h = (Get-FileHash $fpath -Algorithm SHA256).Hash
                $hashes[$ver] = $h
            } else {
                Write-Err2 "VBA $vf missing in $ver deploy package"
                $failCount++
            }
        }
        $unique = $hashes.Values | Select-Object -Unique
        if ($unique.Count -eq 1) {
            Write-Ok "VBA $vf : identical across all $($hashes.Count) packages"
        } elseif ($unique.Count -gt 1) {
            Write-Err2 "VBA $vf : DIFFERS across packages!"
            foreach ($ver in $hashes.Keys) {
                Write-Host "         $ver = $($hashes[$ver].Substring(0,12))..." -ForegroundColor Yellow
            }
            $failCount++
        }
    }

    # ---- 2. csproj TargetFramework validation ----
    Write-Host "`n  --- csproj TargetFramework validation ---"
    $expectedTf = @{
        "2007" = "v2.0"
        "2010" = "v3.5"
        "2013" = "v4.0"
        "2015" = "v4.5"
        "2025" = "net8.0-windows"
    }
    foreach ($ver in $script:DllMap.Keys) {
        $csproj = Join-Path (Get-ProjectDir $ver) "PatentMarker.csproj"
        if (-not (Test-Path $csproj)) { continue }

        if ($ver -eq "2025") {
            # SDK-style csproj
            try {
                [xml]$xml = Get-Content $csproj -Raw
                $tfNode = $xml.SelectSingleNode("//*[local-name()='TargetFramework']")
                $tf = if ($tfNode) { $tfNode.InnerText } else { "NOT_FOUND" }
                if ($tf -eq $expectedTf[$ver]) {
                    Write-Ok "$ver : TargetFramework = $tf"
                } else {
                    Write-Err2 "$ver : TargetFramework = $tf (expected $($expectedTf[$ver]))"
                    $failCount++
                }
            } catch {
                Write-Err2 "$ver : csproj parse failed - $_"
                $failCount++
            }
        } else {
            # Legacy csproj
            try {
                [xml]$xml = Get-Content $csproj -Raw
                $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
                $ns.AddNamespace("ms", "http://schemas.microsoft.com/developer/msbuild/2003")
                $tfNode = $xml.SelectSingleNode("//ms:TargetFrameworkVersion", $ns)
                $tf = if ($tfNode) { $tfNode.InnerText } else { "NOT_FOUND" }
                if ($tf -eq $expectedTf[$ver]) {
                    Write-Ok "$ver : TargetFramework = $tf"
                } else {
                    Write-Err2 "$ver : TargetFramework = $tf (expected $($expectedTf[$ver]))"
                    $failCount++
                }
            } catch {
                Write-Err2 "$ver : csproj parse failed - $_"
                $failCount++
            }
        }
    }

    # ---- 3. Canonical shared source layer (delegates to check-version-sync.ps1) ----
    Write-Host "`n  --- Canonical shared source layer ---"
    $syncScript = Join-Path $root "check-version-sync.ps1"
    if (Test-Path -LiteralPath $syncScript) {
        & $syncScript
        if ($LASTEXITCODE -ne 0) {
            Write-Err2 "Canonical shared source check failed (see output above)."
            $failCount++
        } else {
            Write-Ok "Canonical shared source layer verified by check-version-sync.ps1"
        }
    } else {
        Write-Err2 "check-version-sync.ps1 not found at repo root; cannot verify shared layer."
        $failCount++
    }

    # ---- 4. Version-local file consistency (within groups) ----
    Write-Host "`n  --- Version-local file consistency ---"
    # Files that still live in each edition directory (JSON adapters, entry
    # point). Shared logic lives in cad-plugin/Shared and is checked above.
    # Group A: 2013 vs 2015 (same Newtonsoft JSON stack + accoremgd era).
    # 2025 is excluded: System.Text.Json/.NET 8 makes its adapters legitimately
    # different. PatentMarkerApp.cs is excluded: per-edition entry point.
    $localFilesA = @(
        "IO\ConfigLoader.cs",
        "IO\RuntimeHost.cs",
        "IO\DictEntry.cs",
        "IO\DictWriter.cs"
    )
    $driftCount = 0
    foreach ($rel in $localFilesA) {
        $h1 = Join-Path (Get-ProjectDir "2013") $rel
        $h2 = Join-Path (Get-ProjectDir "2015") $rel
        if ((Test-Path $h1) -and (Test-Path $h2)) {
            $hash1 = (Get-FileHash $h1 -Algorithm SHA256).Hash
            $hash2 = (Get-FileHash $h2 -Algorithm SHA256).Hash
            if ($hash1 -ne $hash2) {
                Write-Warn2 "DRIFT: $rel differs between 2013/2015 (same JSON stack; review if intentional)"
                $driftCount++
            }
        } else {
            Write-Warn2 "$rel not present in both 2013 and 2015 (edition-local layout may have changed)"
            $driftCount++
        }
    }
    if ($driftCount -eq 0) {
        Write-Ok "Group 2013/2015 (Newtonsoft stack): $($localFilesA.Count) version-local files consistent"
    } else {
        $warnCount += $driftCount
    }

    # Group B: 2007 vs 2010 (SimpleJson stack). PatentMarkerApp.cs is excluded
    # (2007 targets C# 2.0 syntax, 2010 may use C# 3.0+).
    $localFilesB = @(
        "IO\ConfigLoader.cs",
        "IO\RuntimeHost.cs",
        "IO\DictEntry.cs",
        "IO\DictWriter.cs",
        "IO\SimpleJson.cs"
    )
    $driftCount2 = 0
    foreach ($rel in $localFilesB) {
        $h1 = Join-Path (Get-ProjectDir "2007") $rel
        $h2 = Join-Path (Get-ProjectDir "2010") $rel
        if ((Test-Path $h1) -and (Test-Path $h2)) {
            $hash1 = (Get-FileHash $h1 -Algorithm SHA256).Hash
            $hash2 = (Get-FileHash $h2 -Algorithm SHA256).Hash
            if ($hash1 -ne $hash2) {
                Write-Warn2 "DRIFT: $rel differs between 2007/2010 (same JSON stack; review if intentional)"
                $driftCount2++
            }
        } else {
            Write-Warn2 "$rel not present in both 2007 and 2010 (edition-local layout may have changed)"
            $driftCount2++
        }
    }
    if ($driftCount2 -eq 0) {
        Write-Ok "Group 2007/2010 (SimpleJson stack): $($localFilesB.Count) version-local files consistent"
    } else {
        $warnCount += $driftCount2
    }

    # ---- 4b. Active-document host boundary ----
    Write-Host "`n  --- Active-document host boundary ---"
    $boundaryViolations = 0
    foreach ($ver in $script:DllMap.Keys) {
        $sourceFiles = Get-ChildItem -LiteralPath (Get-ProjectDir $ver) -Recurse -File -Filter "*.cs" |
            Where-Object { $_.Name -ne "RuntimeHost.cs" }
        $directReads = $sourceFiles | Select-String -Pattern "\bMdiActiveDocument\b" -AllMatches -ErrorAction SilentlyContinue
        if ($directReads) {
            Write-Err2 "$ver : direct MdiActiveDocument read found outside IO\\RuntimeHost.cs"
            $directReads | ForEach-Object { Write-Host "         $($_.Path):$($_.LineNumber)" }
            $boundaryViolations++
        }
    }
    if ($boundaryViolations -eq 0) {
        Write-Ok "All editions route active-document reads through IO/RuntimeHost.cs"
    } else {
        $failCount += $boundaryViolations
    }

    # ---- 5. Deploy package version-specific checks ----
    Write-Host "`n  --- Deploy package version-specific checks ---"
    # 2013/2015/2025: Newtonsoft.Json must be merged into PatentMarker.dll (single-file deploy)
    # since v1.7: 2013/2015 ship no external Newtonsoft.Json.dll (ILRepack-merged at build time)
    foreach ($ver in @("2013","2015","2025")) {
        $nj = Join-Path $root "PatentMarker-$ver-deploy\Newtonsoft.Json.dll"
        if (Test-Path $nj) {
            Write-Err2 "$ver : Newtonsoft.Json.dll found but must be merged into PatentMarker.dll (single-file deploy)"
            $failCount++
        } else {
            Write-Ok "$ver : No external Newtonsoft.Json.dll (merged into PatentMarker.dll)"
        }
    }

    # ---- Summary ----
    Write-Section "Static check result"
    if ($failCount -eq 0 -and $warnCount -eq 0) {
        Write-Ok "All static checks passed."
        exit 0
    } elseif ($failCount -eq 0) {
        Write-Ok "Static checks passed with $warnCount warning(s) (non-blocking)."
        exit 0
    } else {
        Write-Err2 "Found $failCount error(s) and $warnCount warning(s). Fix errors and retry."
        exit 1
    }
}

# ----------------------------------------------------------------------------
# Simulated host contract tests (no Autodesk SDK DLL required)
# ----------------------------------------------------------------------------
function Invoke-SimulationTests {
    Write-Section "Simulated host contract tests"
    $testRoot = Join-Path $root "cad-plugin\RuntimeContract.Tests"
    if (-not (Test-Path -LiteralPath $testRoot)) {
        Write-Err2 "Simulation test directory not found: $testRoot"
        exit 1
    }

    $projects = Get-ChildItem -LiteralPath $testRoot -Filter "*.Tests.csproj" -File |
        Sort-Object Name
    if ($projects.Count -eq 0) {
        Write-Err2 "No simulated contract test projects found."
        exit 1
    }

    $allOk = $true
    foreach ($project in $projects) {
        Write-Host "`n--- $($project.BaseName) ---"
        & dotnet test $project.FullName --configuration Release --nologo -v minimal
        if ($LASTEXITCODE -ne 0) { $allOk = $false }
    }

    if ($allOk) {
        Write-Ok "All simulated host contract tests passed"
        exit 0
    }

    Write-Err2 "One or more simulated host contract test projects failed."
    exit 1
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
        $msbuildPath = Get-MSBuildPath
        if ($msbuildPath) {
            Write-Ok "MSBuild available ($msbuildPath)"
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
            $buildOutput = dotnet build -c Release --nologo -v minimal 2>&1
            $buildExit = $LASTEXITCODE
            $buildOutput | ForEach-Object { Write-Host $_ }
            $ok = ($buildExit -eq 0)
        } finally {
            Pop-Location
        }
        if ($ok) { Write-Ok "Edition $ver built successfully"; return $true }
        else { Write-Err2 "Edition $ver build failed (exit $buildExit)"; return $false }
    } else {
        $msbuildPath = Get-MSBuildPath
        if (-not $msbuildPath) {
            Write-Warn2 "MSBuild not found; cannot auto-build edition $ver (legacy csproj)."
            Write-Host "         Open cad-plugin\$ver\PatentMarker\PatentMarker.csproj in Visual Studio to build manually." -ForegroundColor Yellow
            return $false
        }
        $csproj = Join-Path $projDir "PatentMarker.csproj"
        # These legacy projects use packages.config/direct assembly references;
        # do not let stale SDK-style project.assets.json files trigger NuGet
        # runtime-identifier validation during a plain MSBuild build.
        $buildOutput = & $msbuildPath $csproj /t:Build /p:Configuration=Release /p:ResolveNuGetPackages=false /v:minimal /nologo 2>&1
        $buildExit = $LASTEXITCODE
        $buildOutput | ForEach-Object { Write-Host $_ }
        $ok = ($buildExit -eq 0)
        if ($ok) { Write-Ok "Edition $ver built successfully"; return $true }
        else { Write-Err2 "Edition $ver build failed (exit $buildExit)"; return $false }
    }
}

# ============================================================================
# Main
# ============================================================================

if ($Structure) {
    Invoke-StructureCheck
    return
}

if ($Static) {
    Invoke-StaticCheck
    return
}

if ($Simulation) {
    Invoke-SimulationTests
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

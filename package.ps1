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
$vbaFiles = @("Patterns.bas", "DictModel.bas", "JsonWriter.bas", "PatentExtractor.bas", "AutoExport.bas", "PatentMarkerBootstrap.bas", "clsSaveHook.cls", "PatentDictPanel.frm", "PatentDictPanel.frx")
$ilrepack = Join-Path $root "tools\ilrepack\tools\ILRepack.exe"
$releaseManifestProject = Join-Path $root "tools\ReleaseManifest\ReleaseManifest.csproj"
$releaseManifestTool = Join-Path $root "tools\ReleaseManifest\bin\Release\net8.0\PatentMarker.ReleaseManifest.exe"

function Fail($message) { throw $message }

function Get-ReleaseVersion {
    if (-not [string]::IsNullOrWhiteSpace($ReleaseVersion)) { return $ReleaseVersion.Trim() }
    if (-not [string]::IsNullOrWhiteSpace($env:PATENTMARKER_RELEASE_VERSION)) {
        return $env:PATENTMARKER_RELEASE_VERSION.Trim()
    }
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $described = (& git -C $root describe --tags --always --dirty 2>$null)
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($described)) {
            return ([string]$described).Trim()
        }
    }
    return "unversioned"
}

function Ensure-ReleaseManifestTool {
    $needsBuild = -not (Test-Path -LiteralPath $releaseManifestTool)
    if (-not $needsBuild) {
        $toolTime = (Get-Item -LiteralPath $releaseManifestTool).LastWriteTimeUtc
        $sourceTime = (Get-ChildItem -LiteralPath (Split-Path -Parent $releaseManifestProject) -File |
            Where-Object { $_.Extension -in @('.cs', '.csproj') } |
            Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum
        $needsBuild = $sourceTime -gt $toolTime
    }
    if ($needsBuild) {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
            Fail "dotnet CLI is required to build tools/ReleaseManifest"
        }
        & dotnet build $releaseManifestProject -c Release --nologo -v quiet | Out-Host
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $releaseManifestTool)) {
            Fail "Unable to build tools/ReleaseManifest"
        }
    }
}

function Write-ReleaseManifest($ver, $stage) {
    Ensure-ReleaseManifestTool
    $commit = (& git -C $root rev-parse HEAD 2>$null)
    $tree = (& git -C $root rev-parse 'HEAD^{tree}' 2>$null)
    $dirty = if ((& git -C $root status --porcelain --untracked-files=normal 2>$null)) { "true" } else { "false" }
    $command = "package.ps1 -Version $ver"
    $toolchain = "PowerShell $($PSVersionTable.PSVersion); dotnet $(& dotnet --version 2>$null)"
    $args = @(
        "generate",
        (Join-Path $stage "PatentMarker.dll"),
        (Join-Path $stage "PatentMarker-release.txt"),
        "--edition", $ver,
        "--version", (Get-ReleaseVersion),
        "--source-commit", ([string]$commit).Trim(),
        "--source-tree", ([string]$tree).Trim(),
        "--source-dirty", $dirty,
        "--source-state", $(if ($dirty -eq "true") { "dirty-worktree" } else { "clean-commit" }),
        "--build-tool", $toolchain,
        "--build-command", $command,
        "--dotm", (Join-Path $stage "PatentMarker.dotm"),
        "--vba-dir", (Join-Path $stage "vba")
    )
    & $releaseManifestTool @args | Out-Host
    if ($LASTEXITCODE -ne 0) { Fail "Release manifest generation failed for $ver" }
    & $releaseManifestTool verify (Join-Path $stage "PatentMarker-release.txt") (Join-Path $stage "PatentMarker.dll") --dotm (Join-Path $stage "PatentMarker.dotm") --vba-dir (Join-Path $stage "vba") | Out-Host
    if ($LASTEXITCODE -ne 0) { Fail "Release manifest verification failed for $ver" }
}

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
    # vba/ at the repository root is the canonical single source; every
    # deployment copy must be byte-identical to it. Edit vba/ and run
    # .\vba-sync.ps1 to propagate; never edit deployment copies directly.
    foreach ($file in $vbaFiles) {
        $canonical = Join-Path $root "vba\$file"
        if (-not (Test-Path -LiteralPath $canonical)) { Fail "Missing canonical VBA module: $canonical" }
        $canonicalHash = (Get-FileHash -LiteralPath $canonical -Algorithm SHA256).Hash
        foreach ($ver in $versions) {
            $path = Join-Path $root "PatentMarker-$ver-deploy\vba\$file"
            if (-not (Test-Path -LiteralPath $path)) { Fail "Missing VBA module: $path" }
            $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            if ($hash -ne $canonicalHash) {
                Fail "VBA module drift detected: $file (deploy $ver differs from canonical vba/$file; run .\vba-sync.ps1)"
            }
        }
    }

    foreach ($asset in @("PatentMarker.dotm", "install-vba.vbs", "uninstall-vba.vbs")) {
        $canonicalAsset = Join-Path $root "word-addin\$asset"
        if (-not (Test-Path -LiteralPath $canonicalAsset)) { Fail "Missing canonical Word add-in asset: $canonicalAsset" }
        $canonicalHash = (Get-FileHash -LiteralPath $canonicalAsset -Algorithm SHA256).Hash
        foreach ($ver in $versions) {
            $deployAsset = Join-Path $root "PatentMarker-$ver-deploy\$asset"
            if (-not (Test-Path -LiteralPath $deployAsset)) { Fail "Missing Word add-in asset: $deployAsset" }
            if ((Get-FileHash -LiteralPath $deployAsset -Algorithm SHA256).Hash -ne $canonicalHash) {
                Fail "Word add-in asset drift detected: $asset in $ver deployment; run .\sync-word-addin.ps1"
            }
        }
    }

    $dotmVerifier = Join-Path $root "tools\verify-dotm-package.ps1"
    if (-not (Test-Path -LiteralPath $dotmVerifier)) { Fail "Missing DOTM package verifier: $dotmVerifier" }
    & $dotmVerifier -Path (Join-Path $root "word-addin\PatentMarker.dotm")
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
        # Deployment templates are also used as live install directories, so
        # they contain logs, doctor reports, generated LSP files and timestamped
        # DLL backups.  Only installer assets belong in a release archive.
        if ($item.PSIsContainer) {
            if ($item.Name -ne "vba") { continue }
        }
        else {
            if ($item.Name -in @("PatentMarker.dll", "PatentMarker.repacked.dll", "PatentMarker.repacked.pdb")) { continue }
            if ($item.Name -like "*.bak.*" -or
                $item.Name -like "*report*.txt" -or
                $item.Extension -in @(".log", ".lsp")) { continue }
            if ($item.Extension -notin @(".bat", ".vbs", ".ps1", ".txt", ".dotm")) { continue }
        }
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
    Write-ReleaseManifest $ver $stage
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
        $manifestTarget = Join-Path $deploy "PatentMarker-release.txt"
        if (Test-Path -LiteralPath $manifestTarget) {
            Copy-Item -LiteralPath $manifestTarget -Destination "$manifestTarget.bak.$stamp" -Force
        }
        Copy-Item -LiteralPath (Join-Path $OutputRoot "$ver\PatentMarker-release.txt") `
            -Destination $manifestTarget -Force
        Write-Host "[APPLY] $target (backup: $target.bak.$stamp)"
        Write-Host "[APPLY] $manifestTarget"
    }
}

Write-Host "Release staging completed: $OutputRoot"

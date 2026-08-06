# Validate the AutoCAD managed API surface used by each edition without loading
# the native-dependent Autodesk assemblies into the PowerShell process.

param(
    [string]$Version = "all"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$toolProject = Join-Path $root "tools\ApiSurfaceCheck\ApiSurfaceCheck.csproj"
$versions = if ($Version -eq "all") { @("2010", "2013", "2015", "2025") } else { @($Version) }

foreach ($ver in $versions) {
    if ($ver -notin @("2010", "2013", "2015", "2025")) {
        Write-Error "Invalid edition: $ver. Valid values: 2010, 2013, 2015, 2025, all"
        exit 1
    }

    $lib = Join-Path $root "cad-plugin\$ver\PatentMarker\lib"
    if (-not (Test-Path -LiteralPath $lib)) {
        Write-Error "SDK directory not found: $lib"
        exit 1
    }

    $commandSource = Join-Path $root "cad-plugin\$ver\PatentMarker\Commands\PatMarkCommand.cs"
    if ($ver -eq "2010") {
        $forbidden = Select-String -LiteralPath $commandSource -Pattern "\bMLeader\s+[A-Za-z_]" -AllMatches -ErrorAction SilentlyContinue
        if ($forbidden) {
            Write-Error "2010 command source contains an MLeader type reference: $commandSource"
            exit 1
        }
        Write-Host "[OK] 2010 source profile excludes MLeader command types" -ForegroundColor Green
    }
    elseif ($ver -in @("2013", "2015", "2025")) {
        $forbidden = Select-String -LiteralPath $commandSource -Pattern "\bMLeader\b" -AllMatches -ErrorAction SilentlyContinue
        if ($forbidden) {
            Write-Error "$ver command source still contains an MLeader reference: $commandSource"
            exit 1
        }
        Write-Host "[OK] $ver source profile uses the Leader + MText construction path" -ForegroundColor Green
    }

    $directDocumentReads = Get-ChildItem -LiteralPath (Join-Path $root "cad-plugin\$ver\PatentMarker") -Recurse -File -Filter "*.cs" |
        Where-Object { $_.Name -ne "RuntimeHost.cs" } |
        Select-String -Pattern "\bMdiActiveDocument\b" -AllMatches -ErrorAction SilentlyContinue
    if ($directDocumentReads) {
        Write-Error "$ver contains direct MdiActiveDocument reads outside IO\\RuntimeHost.cs"
        $directDocumentReads | ForEach-Object { Write-Host "  $($_.Path):$($_.LineNumber):$($_.Line.Trim())" }
        exit 1
    }
    Write-Host "[OK] $ver routes active-document reads through IO/RuntimeHost" -ForegroundColor Green

    Write-Host "`n=== Checking API surface for $ver ===" -ForegroundColor Cyan
    & dotnet run --project $toolProject --configuration Release -- $ver $lib
    if ($LASTEXITCODE -ne 0) {
        Write-Error "API surface check failed for $ver"
        exit $LASTEXITCODE
    }
}

Write-Host "`n[OK] API surface checks passed for: $($versions -join ', ')" -ForegroundColor Green

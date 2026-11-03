param([switch]$SelfTest)

# Verifies that every deployment DLL matches its generated release manifest.
# The manifest intentionally contains artifact SHA-256, MVID, size and version;
# a stale or same-named DLL must fail this gate.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$toolProject = Join-Path $root "tools\ReleaseManifest\ReleaseManifest.csproj"
$toolExe = Join-Path $root "tools\ReleaseManifest\bin\Release\net8.0\PatentMarker.ReleaseManifest.exe"

if (-not (Test-Path -LiteralPath $toolExe)) {
    dotnet build $toolProject -c Release --nologo -v quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Unable to build ReleaseManifest tool" }
}

if ($SelfTest) {
    $sourceManifest = Join-Path $root "PatentMarker-2025-deploy\PatentMarker-release.txt"
    $sourceDll = Join-Path $root "PatentMarker-2025-deploy\PatentMarker.dll"
    $tempDir = Join-Path $env:TEMP ("PatentMarker-manifest-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempDir | Out-Null
    $tampered = Join-Path $tempDir "tampered.txt"
    (Get-Content -Encoding utf8 $sourceManifest) -replace "^artifact\.sha256=.*$", ("artifact.sha256=" + ("0" * 64)) | Set-Content -Encoding utf8 $tampered
    & $toolExe verify $tampered $sourceDll | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "Self-test: tampered manifest was accepted" }
    Write-Output "PASS self-test: tampered SHA-256 manifest rejected"
    Remove-Item $tempDir -Recurse -Force
    exit 0
}


$failures = 0
foreach ($edition in @("2007", "2010", "2013", "2015", "2025")) {
    $deploy = Join-Path $root "PatentMarker-$edition-deploy"
    $dll = Join-Path $deploy "PatentMarker.dll"
    $manifest = Join-Path $deploy "PatentMarker-release.txt"
    if (-not (Test-Path -LiteralPath $dll)) {
        Write-Host "[FAIL] $edition missing deployment DLL"
        $failures++
        continue
    }
    if (-not (Test-Path -LiteralPath $manifest)) {
        Write-Host "[FAIL] $edition missing PatentMarker-release.txt"
        $failures++
        continue
    }
    $dotm = Join-Path $deploy "PatentMarker.dotm"
    $vba = Join-Path $deploy "vba"
    & $toolExe verify $manifest $dll --dotm $dotm --vba-dir $vba | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[FAIL] $edition release manifest mismatch"
        $failures++
    } else {
        Write-Host "[OK]   $edition release manifest matches deployment DLL"
    }
}

if ($failures -ne 0) {
    Write-Host "[FAIL] release manifest verification failed: $failures edition(s)"
    exit 1
}
Write-Host "[OK] all deployment release manifests match their DLLs"
exit 0

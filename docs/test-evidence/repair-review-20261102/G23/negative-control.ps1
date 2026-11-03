param([string]$ProjectRoot = ".")

# G23 / R-035 negative control: a run that exits 0 without producing its own
# TRX must not be reported as a pass, even when an earlier all-passing TRX is
# sitting in the same results directory.

$ErrorActionPreference = "Stop"
$root = (Resolve-Path -LiteralPath $ProjectRoot).Path
$wrapper = Join-Path $root "tools\run-office-tests-vstest.ps1"
$text = [IO.File]::ReadAllText($wrapper)

# Replace only the two statements that would run the real build and vstest.
$text = $text.Replace(
    'dotnet build $project -c Release --nologo -v quiet | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Test project build failed: $project" }',
    '# build skipped: this control only exercises result attribution')
$text = $text.Replace(
    'dotnet vstest $dll --logger "trx;LogFileName=$trxName" "--ResultsDirectory:$results" | Out-Host',
    'Write-Output "(injected) vstest exits 0 but writes no TRX"; $global:LASTEXITCODE = 0')
if ($text -eq [IO.File]::ReadAllText($wrapper)) { throw "injection points not found; wrapper changed" }

$patched = Join-Path $env:TEMP ("G23-negative-" + [guid]::NewGuid().ToString("N") + ".ps1")
[IO.File]::WriteAllText($patched, $text, (New-Object Text.UTF8Encoding($true)))
try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $patched `
        -TestProject (Join-Path $root "office-com-addin\tests\PatentOffice.Visio.CodeTests\PatentOffice.Visio.CodeTests.csproj") `
        -ResultsDirectory (Join-Path $root "docs\test-evidence\repair-review-20261102\G23\shared-results")
    $code = $LASTEXITCODE
}
finally {
    Remove-Item -LiteralPath $patched -Force -ErrorAction SilentlyContinue
}
if ($code -eq 0) {
    Write-Output "FAIL|negative_control|wrapper reported a pass without this run's own TRX"
    exit 1
}
Write-Output ("PASS|negative_control|wrapper refused stale results (exit " + $code + ")")
exit 0
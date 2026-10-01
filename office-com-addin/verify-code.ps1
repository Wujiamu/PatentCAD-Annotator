[CmdletBinding()]
param(
    [string]$ResultsDirectory = (Join-Path $PSScriptRoot 'test-evidence/code-results')
)

$ErrorActionPreference = 'Stop'
$syntaxScript = Join-Path $PSScriptRoot 'verify-powershell-syntax.ps1'
& $syntaxScript -ScriptRoot $PSScriptRoot
if ($PSVersionTable.PSVersion.Major -gt 5) {
    $windowsPowerShell = (Get-Command powershell.exe -ErrorAction Stop).Source
    & $windowsPowerShell -NoProfile -NonInteractive -File $syntaxScript -ScriptRoot $PSScriptRoot
    if ($LASTEXITCODE -ne 0) { throw 'Windows PowerShell 5 syntax verification failed.' }
}

foreach ($hostName in @('PowerPoint', 'Visio')) {
    $buildScript = if ($hostName -eq 'PowerPoint') { 'build.ps1' } else { 'build-visio.ps1' }
    & (Join-Path $PSScriptRoot $buildScript)
    if (-not $?) { throw "$hostName build failed." }

    $project = Join-Path $PSScriptRoot "tests/PatentOffice.$hostName.CodeTests/PatentOffice.$hostName.CodeTests.csproj"
    $resultDirectory = Join-Path ([IO.Path]::GetFullPath($ResultsDirectory)) $hostName
    # A unique run directory prevents a stale successful TRX from being accepted.
    $resultDirectory = Join-Path $resultDirectory ([Guid]::NewGuid().ToString('N'))
    $trx = Join-Path $resultDirectory 'results.trx'
    dotnet test $project --configuration Release --nologo -v minimal --logger 'trx;LogFileName=results.trx' --results-directory $resultDirectory
    if ($LASTEXITCODE -ne 0) { throw "$hostName code tests failed." }
    if (-not (Test-Path -LiteralPath $trx -PathType Leaf)) { throw "$hostName produced no test result." }
    [xml]$result = Get-Content -Raw -LiteralPath $trx
    $counters = $result.TestRun.ResultSummary.Counters
    if ($null -eq $counters -or [int]$counters.total -le 0 -or
        [int]$counters.passed -ne [int]$counters.total -or
        [int]$counters.executed -ne [int]$counters.total) {
        throw "$hostName did not execute and pass every test."
    }
    Write-Output "PASS|OFFICE_CODE_TESTS|$hostName|$($counters.passed)/$($counters.total)"
}

& (Join-Path $PSScriptRoot 'verify-installer.ps1') -OfficeBitness '64'
if (-not $?) { throw 'PowerPoint isolated installer verification failed.' }
& (Join-Path $PSScriptRoot 'verify-visio-installer.ps1')
if (-not $?) { throw 'Visio isolated installer verification failed.' }
Write-Output 'PASS|OFFICE_CODE_AND_ISOLATED_INSTALLERS|NO_HOST_UI_COVERAGE'

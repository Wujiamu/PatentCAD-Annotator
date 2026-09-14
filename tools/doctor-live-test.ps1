# doctor-live-test.ps1 - verify the packaged 2025 DLL inside a real AutoCAD
# host using the SCR + /b batch approach.
#
# Why not COM: New-Object -ComObject attaches to a stale AutoCAD instance when
# one is left behind by a crash, and SendCommand hangs on modal dialogs
# (see acad.err 2026-08-15 16:30). The /b batch mode always starts a fresh
# process and runs the script to completion.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$deploy = Join-Path $root 'PatentMarker-2025-deploy'
$dll    = Join-Path $deploy 'PatentMarker.dll'
$report = Join-Path $deploy 'PatentMarker-doctor-report.txt'
$log    = Join-Path $deploy 'PatentMarker.log'
$acadExe = 'C:\Program Files\Autodesk\AutoCAD 2026\acad.exe'

if (-not (Test-Path -LiteralPath $acadExe)) { throw "acad.exe not found: $acadExe" }
if (-not (Test-Path -LiteralPath $dll))     { throw "packaged DLL not found: $dll" }

# Refuse to attach alongside a leftover instance; /b must run in a fresh host.
$leftover = Get-Process acad -ErrorAction SilentlyContinue
if ($leftover) {
    throw ("leftover acad.exe running (PID " + ($leftover.Id -join ',') + '); close it first')
}
Remove-Item $report -ErrorAction SilentlyContinue

$caseDir = Join-Path ([System.IO.Path]::GetTempPath()) ('PatentMarker-CadDoctor-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $caseDir
$dwg = Join-Path $caseDir 'doctor-case.dwg'
$dict = Join-Path $caseDir 'doctor-case.dict.json'
$scr = Join-Path $caseDir 'doctor-case.scr'
$dictJson = @'
{
  "metadata": {
    "source_file": "doctor-case.docx",
    "version": "live-test"
  },
  "entries": [
    {
      "number": "1",
      "name": "base"
    }
  ],
  "warnings": []
}
'@
[IO.File]::WriteAllText($dict, $dictJson, (New-Object Text.UTF8Encoding($false)))
@(
    '_.FILEDIA 0'
    '_.CMDDIA 0'
    '_.SAVEAS'
    '2018'
    ('"' + $dwg + '"')
    '_.SECURELOAD 0'
    ('_.NETLOAD "' + $dll + '"')
    '_.PATDOCTOR'
    '_.QUIT'
    '_N'
) | Set-Content -LiteralPath $scr -Encoding ASCII

Write-Output ("== launching AutoCAD batch mode ==")
$p = Start-Process -FilePath $acadExe -ArgumentList ('/b "' + $scr + '"') -PassThru -WindowStyle Hidden

# AutoCAD sometimes ignores the scripted QUIT, so poll for the report and
# clean the host up ourselves if needed.  A report file alone is not a pass:
# later checks validate the loaded assembly and the report summary.
$deadline = (Get-Date).AddMinutes(5)
while ((Get-Date) -lt $deadline -and -not (Test-Path -LiteralPath $report) -and -not $p.HasExited) {
    Start-Sleep -Seconds 5
}

if (-not (Test-Path -LiteralPath $report)) {
    if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit() }
    throw 'doctor report not generated within 5 minutes'
}
Start-Sleep -Seconds 3   # let the report writer finish flushing

if ($p.HasExited) {
    Write-Output ("== AutoCAD exited cleanly, code " + $p.ExitCode + " ==")
} else {
    Write-Output "== AutoCAD still running after report; stopping host =="
    $p.Kill()
    $p.WaitForExit()
}

Write-Output "== REPORT FOUND =="
$reportLines = @(Get-Content -LiteralPath $report)
$reportLines | ForEach-Object { Write-Output $_ }

$assemblyLine = $reportLines | Where-Object { $_ -match '^\- Assembly:\s+' } | Select-Object -First 1
if (-not $assemblyLine) {
    throw 'doctor report does not identify the loaded assembly'
}
$reportedAssembly = ($assemblyLine -replace '^\- Assembly:\s+', '').Trim()
$expectedAssembly = [IO.Path]::GetFullPath($dll)
$actualAssembly = [IO.Path]::GetFullPath($reportedAssembly)
if (-not [string]::Equals($actualAssembly, $expectedAssembly, [StringComparison]::OrdinalIgnoreCase)) {
    throw "wrong assembly loaded: expected '$expectedAssembly', report says '$actualAssembly'"
}

$summaryLine = $reportLines | Where-Object { $_ -match '^Summary:\s+PASS\s+\d+\s+/\s+FAIL\s+\d+\s+/\s+SKIP\s+\d+\s+\|\s+recent errors:\s+\d+' } | Select-Object -First 1
if (-not $summaryLine) {
    throw 'doctor report does not contain a parseable PASS/FAIL/SKIP summary'
}
$summaryMatch = [regex]::Match($summaryLine, 'PASS\s+(\d+)\s+/\s+FAIL\s+(\d+)\s+/\s+SKIP\s+(\d+)\s+\|\s+recent errors:\s+(\d+)')
$doctorFailures = [int]$summaryMatch.Groups[2].Value
$recentErrors = [int]$summaryMatch.Groups[4].Value
$failedRows = @($reportLines | Where-Object { $_ -match '^\|\s*\d+\s*\|.*\|\s*FAIL\s*\|' })
$dictRows = @($reportLines | Where-Object { $_ -match '^\|\s*5\s*\|' })

if ($doctorFailures -ne 0 -or $failedRows.Count -ne 0) {
    throw "PATDOCTOR reported failures: $summaryLine"
}
if ($recentErrors -ne 0) {
    throw "PATDOCTOR retained recent errors: $summaryLine"
}
if ($dictRows.Count -ne 1 -or $dictRows[0] -notmatch '\|\s*PASS\s*\|') {
    throw "dictionary check did not pass: $($dictRows -join '; ')"
}
if ($dictRows[0] -notmatch [regex]::Escape($dict)) {
    throw "PATDOCTOR checked the wrong dictionary: $($dictRows[0])"
}

Write-Output "PASS|CAD_FULL_DOCTOR|assembly=$actualAssembly|dictionary_check=PASS|recent_errors=0"

Write-Output "== PatentMarker.log tail =="
if (Test-Path -LiteralPath $log) {
    Get-Content -LiteralPath $log -Tail 12 | ForEach-Object { Write-Output $_ }
}

# Success cleanup is deliberately limited to this GUID-scoped test directory.
# On failure the directory is retained so the SCR, DWG and dictionary can be
# inspected without touching user drawings.
foreach ($path in @($dwg, [IO.Path]::ChangeExtension($dwg, '.bak'), $dict, $scr)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}
if ((Get-ChildItem -LiteralPath $caseDir -Force | Measure-Object).Count -eq 0) {
    Remove-Item -LiteralPath $caseDir
}
Write-Output "== TEST DONE =="

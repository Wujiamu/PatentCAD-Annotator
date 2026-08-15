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

$scr = Join-Path ([System.IO.Path]::GetTempPath()) 'patdoctor-live.scr'
@(
    '_.FILEDIA 0'
    '_.SECURELOAD 0'
    ('_.NETLOAD "' + $dll + '"')
    '_.PATDOCTOR'
    '_.QUIT'
    '_N'
) | Set-Content -LiteralPath $scr -Encoding ASCII

Write-Output ("== launching AutoCAD batch mode ==")
$p = Start-Process -FilePath $acadExe -ArgumentList ('/b "' + $scr + '"') -PassThru

# Pass criterion is the report file; AutoCAD sometimes ignores the scripted
# QUIT, so poll for the report and clean the host up ourselves if needed.
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
Get-Content -LiteralPath $report | ForEach-Object { Write-Output $_ }

Write-Output "== PatentMarker.log tail =="
if (Test-Path -LiteralPath $log) {
    Get-Content -LiteralPath $log -Tail 12 | ForEach-Object { Write-Output $_ }
}
Write-Output "== TEST DONE =="

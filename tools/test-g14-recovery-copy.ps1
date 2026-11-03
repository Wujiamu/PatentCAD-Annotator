param([string]$Root)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'office-com-addin/OfficeAddin.Common.psm1') -Force

$prod = Join-Path $Root 'PowerPoint'
New-Item -ItemType Directory -Path $prod -Force | Out-Null
foreach ($n in @('PatentOffice.PowerPoint.dll', 'Newtonsoft.Json.dll', 'install-manifest.json')) {
    [IO.File]::WriteAllText((Join-Path $prod $n), "content-$n")
}
$before = @{}
Get-ChildItem $prod -File | ForEach-Object { $before[$_.Name] = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }

$copy = New-PatentMarkerRecoveryCopy -InstallDirectory $prod -ExpectedParent $Root
Write-Output ("recovery-copy|" + (Split-Path $copy -Leaf))

# Simulate the half-deleted state a failing recursive delete leaves behind.
$staged = Join-Path $Root '.PowerPoint.uninstall-test'
Move-Item -LiteralPath $prod -Destination $staged
Remove-Item -LiteralPath (Join-Path $staged 'Newtonsoft.Json.dll') -Force
Write-Output ("staged|" + ((Get-ChildItem $staged -File | Select-Object -ExpandProperty Name) -join ','))

$ok = Restore-PatentMarkerDirectoryFromCopy -RecoveryCopy $copy -InstallDirectory $prod -ExpectedParent $Root
Write-Output ("restore-returned|" + $ok)

$after = @{}
Get-ChildItem $prod -File | ForEach-Object { $after[$_.Name] = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
$diff = @($before.Keys | Where-Object { $before[$_] -ne $after[$_] })
Write-Output ("files|before={0}|after={1}|mismatched={2}" -f $before.Count, $after.Count, $diff.Count)
if ($before.Count -ne $after.Count -or $diff.Count -ne 0) { throw 'restore did not reproduce the complete file set' }

# The old design (rename the leftover back) must NOT look like a restore.
$leftover = @(Get-ChildItem $staged -File).Count
Write-Output ("leftover-file-count-after-partial-delete|" + $leftover)
if ($leftover -ge $before.Count) { throw 'partial-delete simulation did not actually lose a file' }
Write-Output 'PASS|G14_RECOVERY_COPY_RESTORES_COMPLETE_FILE_SET'

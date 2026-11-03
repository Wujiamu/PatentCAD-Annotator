param([string]$ProjectRoot='.')
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ProjectRoot).Path
$pass=0;$fail=0
function Check([string]$Id,[bool]$Ok,[string]$Detail){if($Ok){$script:pass++}else{$script:fail++};$(if($Ok){'PASS'}else{'FAIL'})+'|'+$Id+'|'+$Detail}
function Assert-PatentMarkerSiblingDirectory {
    param($Candidate,$ExpectedParent)
    $full=[IO.Path]::GetFullPath($Candidate);$parent=[IO.Path]::GetFullPath($ExpectedParent)
    if([IO.Path]::GetDirectoryName($full) -ne $parent -or -not $parent.StartsWith($PSScriptRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture boundary violated'}
    $full
}
function Assert-VisioSiblingDirectory{param($Candidate,$ExpectedParent);Assert-PatentMarkerSiblingDirectory $Candidate $ExpectedParent}
function Remove-PatentMarkerSiblingDirectory{param($Candidate,$ExpectedParent);$full=Assert-PatentMarkerSiblingDirectory $Candidate $ExpectedParent;Remove-Item -LiteralPath $full -Recurse -Force}
function Remove-VisioSiblingDirectory{param($Candidate,$ExpectedParent);Remove-PatentMarkerSiblingDirectory $Candidate $ExpectedParent}
function Move-Item{
    [CmdletBinding()]param([string]$LiteralPath,[string]$Destination)
    if($state.FailRestore -and $LiteralPath -eq $backupDirectory -and $Destination -eq $installFull){throw [IO.IOException]::new('injected failure moving verified old backup back')}
    Microsoft.PowerShell.Management\Move-Item -LiteralPath $LiteralPath -Destination $Destination
}
function Remove-PatentMarkerRegistryTree{param($BaseKey,$Path);$state.Enabled[$Path]=$false}
function Remove-VisioRegistryTree{param($BaseKey,$Path);$state.Enabled[$Path]=$false}
function Restore-PatentMarkerRegistrySnapshot{param($BaseKey,$Snapshot);if($null -ne $Snapshot){$state.Enabled[$Snapshot.Path]=$true}}
function Restore-VisioRegistrySnapshot{param($BaseKey,$Snapshot);if($null -ne $Snapshot){$state.Enabled[$Snapshot.Path]=$true}}
function Assert-PatentMarkerRegistrySnapshotRestored{param($BaseKey,$Path,$Snapshot);if($state.Enabled[$Path] -ne ($null -ne $Snapshot)){throw 'Registration does not match initial snapshot'}}
function Assert-VisioRegistrySnapshotRestored{param($BaseKey,$Path,$Snapshot);Assert-PatentMarkerRegistrySnapshotRestored $BaseKey $Path $Snapshot}
function Assert-PatentMarkerOwnedDirectory{
    param($InstallDirectory)
    $old=Join-Path $InstallDirectory 'old.dll'
    if(-not(Test-Path -LiteralPath $old) -or (Get-FileHash -LiteralPath $old).Hash -ne $state.OldHash){throw 'Old complete fixture not restored'}
}
function Assert-VisioOwnedDirectory{param($InstallDirectory);Assert-PatentMarkerOwnedDirectory $InstallDirectory}
function Scenario([string]$Product,[string]$Phase,[bool]$FailRestore){
    $file=if($Product -eq 'ppt'){'install-office-addin.ps1'}else{'install-visio-addin.ps1'}
    $source=Join-Path $root ('office-com-addin/'+$file)
    $ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$null,[ref]$null)
    $catch=@($ast.FindAll({param($n)$n -is [Management.Automation.Language.CatchClauseAst] -and $n.Body.Statements.Count -gt 0 -and $n.Body.Statements[0].Extent.Text -eq '$failure = $_'},$true))
    if($catch.Count -ne 1){throw 'Recovery catch not found'}
    $body=$catch[0].Body.Extent.Text
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot ($Product+'-catch.txt')),$body)
    $installParent=Join-Path $PSScriptRoot ('installer-fixtures/'+$Product+'-'+$Phase+'-'+$FailRestore+'-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $installParent | Out-Null
    $installLeaf='product';$installFull=Join-Path $installParent $installLeaf;$backupDirectory=Join-Path $installParent 'old-backup'
    $sentinel=Join-Path $installParent 'unrelated.txt';[IO.File]::WriteAllText($sentinel,'non-product sentinel')
    $hadExistingInstall=$Phase -ne 'first-new-files'
    $targetMovedToBackup=$Phase -in @('old-moved','new-files','new-registry');$oldMoved=$targetMovedToBackup
    $newTargetInstalled=$Phase -in @('new-files','new-registry','first-new-files');$newInstalled=$newTargetInstalled
    $registrationTouched=$Phase -eq 'new-registry';$FaultInjectionPoint='None';$baseKey=$null
    $state=@{Enabled=@{};OldHash='';FailRestore=$FailRestore}
    $registryPaths=@('classes','progid','addin');$registrySnapshots=@()
    foreach($p in $registryPaths){$state.Enabled[$p]=$hadExistingInstall;$registrySnapshots+=,$(if($hadExistingInstall){[pscustomobject]@{Path=$p}}else{$null})}
    if($hadExistingInstall){
        $oldDir=if($targetMovedToBackup){$backupDirectory}else{$installFull}
        New-Item -ItemType Directory -Path $oldDir | Out-Null
        [IO.File]::WriteAllText((Join-Path $oldDir 'old.dll'),'complete old payload')
        $state.OldHash=(Get-FileHash -LiteralPath (Join-Path $oldDir 'old.dll')).Hash
    }
    if($newTargetInstalled){New-Item -ItemType Directory -Path $installFull | Out-Null;[IO.File]::WriteAllText((Join-Path $installFull 'new.dll'),'new product payload')}
    if($registrationTouched){foreach($p in $registryPaths){$state.Enabled[$p]=$true}}
    $block=[scriptblock]::Create("try {throw 'original install failure'} catch "+$body)
    try{& $block *> (Join-Path $installParent 'catch-log.txt')}catch{if($_.Exception.Message -ne 'original install failure'){throw}}
    $complete=$hadExistingInstall -and (Test-Path -LiteralPath (Join-Path $installFull 'old.dll'))
    if($complete){$complete=(Get-FileHash -LiteralPath (Join-Path $installFull 'old.dll')).Hash -eq $state.OldHash}
    $enabled=@($state.Enabled.Values|Where-Object{$_}).Count -gt 0
    $id=$Product+'-'+$Phase+'-restore-fails-'+$FailRestore
    Check ('I2-registration-'+$id) ($enabled -eq $complete) ("complete=$complete|enabled=$enabled|registration-touched=$registrationTouched")
    $material=if($hadExistingInstall -and -not $complete){Test-Path -LiteralPath (Join-Path $backupDirectory 'old.dll')}else{$complete -or -not $hadExistingInstall}
    Check ('I2-material-'+$id) $material ('backup-kept='+$material)
    Check ('I3-sentinel-'+$id) ([IO.File]::ReadAllText($sentinel) -ceq 'non-product sentinel') ''
}
foreach($product in @('ppt','visio')){foreach($phase in @('stage','old-moved','new-files','new-registry','first-new-files')){foreach($bad in @($false,$true)){Scenario $product $phase $bad}}}
"SUMMARY|passed=$pass|failed=$fail"
if($fail){exit 1}

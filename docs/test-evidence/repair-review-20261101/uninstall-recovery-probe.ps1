param([string]$ProjectRoot)
$ErrorActionPreference='Stop'
$reviewRoot=$PSScriptRoot
function Read-CatchBody([string]$source){$m=[regex]::Match($source,'(?m)^catch\s*\{(?=\r?\n\s+\$failure)');if(-not $m.Success){throw 'Top-level recovery catch missing'};$b=$source.IndexOf('{',$m.Index);$d=0;for($n=$b;$n -lt $source.Length;$n++){if($source[$n] -eq '{'){$d++};if($source[$n] -eq '}'){$d--;if(-not $d){return $source.Substring($b+1,$n-$b-1)}}};throw 'Unclosed recovery catch'}
function Remove-PatentMarkerRegistryTree {param($BaseKey,$Path);$state.Trace.Add('registry.remove');$state.Enabled=$false}
function Remove-VisioRegistryTree {param($BaseKey,$Path);$state.Trace.Add('registry.remove');$state.Enabled=$false}
function Restore-PatentMarkerRegistrySnapshot {param($BaseKey,$Snapshot);$state.Trace.Add('registry.restore');$state.Enabled=$true}
function Restore-VisioRegistrySnapshot {param($BaseKey,$Snapshot);$state.Trace.Add('registry.restore');$state.Enabled=$true}
function Restore-PatentMarkerDirectoryFromCopy {param($RecoveryCopy,$InstallDirectory,$ExpectedParent);$state.Trace.Add('files.restore');if($state.FailFiles){throw [IO.IOException]::new('injected recovery I/O failure')};return $true}
function Restore-VisioDirectoryFromCopy {param($RecoveryCopy,$InstallDirectory,$ExpectedParent);$state.Trace.Add('files.restore');if($state.FailFiles){throw [IO.IOException]::new('injected recovery I/O failure')};return $true}
$lines=New-Object Collections.Generic.List[string];$failed=0
foreach($hostName in @('ppt','visio')){
 $relative=if($hostName -eq 'ppt'){'office-com-addin/uninstall-office-addin.ps1'}else{'office-com-addin/uninstall-visio-addin.ps1'}
 $body=Read-CatchBody ([IO.File]::ReadAllText((Join-Path $ProjectRoot $relative)))
 [IO.File]::WriteAllText((Join-Path $reviewRoot ('uninstall-'+$hostName+'-catch.ps1')),$body,[Text.UTF8Encoding]::new($false))
 foreach($failFiles in @($false,$true)){
  $fixture=[IO.Path]::GetFullPath((Join-Path $reviewRoot ('recovery-'+$hostName+'-'+[guid]::NewGuid().ToString('N'))))
  [IO.Directory]::CreateDirectory($fixture)|Out-Null
  $parent=$fixture;$installFull=Join-Path $fixture 'product';$backupDirectory=Join-Path $fixture 'staged';$backup=$backupDirectory;$recoveryDirectory=Join-Path $fixture 'complete-recovery';$recovery=$recoveryDirectory
  foreach($owned in @($installFull,$backupDirectory,$recoveryDirectory)){
   if([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($owned)) -cne $fixture){throw 'Fixture deletion escaped its root'}
   [IO.Directory]::CreateDirectory($owned)|Out-Null
   [IO.File]::WriteAllText((Join-Path $owned 'owned-fixture.txt'),'synthetic')
  }
  $registryPaths=@('ProductOnly');$registrySnapshots=@('snapshot');$snapshots=$registrySnapshots;$baseKey=$null
  $state=[pscustomobject]@{Enabled=$false;FailFiles=$failFiles;Trace=[Collections.Generic.List[string]]::new()}
  try{throw 'injected uninstall deletion failure'}catch{try{& ([scriptblock]::Create($body))}catch{}}
  $expectedEnabled=-not $failFiles;$ok=$state.Enabled -eq $expectedEnabled;if(-not $ok){$failed++}
  $mark=if($ok){'PASS'}else{'FAIL'}
  $lines.Add("$mark|uninstall_${hostName}_recovery|file_restore_fails=$failFiles|expected_registry_enabled=$expectedEnabled|actual=$($state.Enabled)|trace=$($state.Trace -join ',')")
 }
}
$lines.Add('SUMMARY|total=4|failed='+$failed)
$lines|Set-Content -LiteralPath (Join-Path $reviewRoot 'uninstall-recovery-probe.txt') -Encoding utf8
$lines
if($failed){exit 1}
param([string]$ProjectRoot)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ProjectRoot).Path
$fail = 0
$total = 0
# Registry calls are controlled in memory. The catch body is extracted from
# the production PowerShell AST without modifying its branch conditions.
function Remove-PatentMarkerRegistryTree { param($BaseKey,$Path); $state.Enabled[$Path]=$false; $state.Trace.Add('registry.remove') }
function Remove-VisioRegistryTree { param($BaseKey,$Path); $state.Enabled[$Path]=$false; $state.Trace.Add('registry.remove') }
function Restore-PatentMarkerRegistrySnapshot { param($BaseKey,$Snapshot); if($null -ne $Snapshot){$state.Enabled[$Snapshot.Path]=$true}; $state.Trace.Add('registry.restore') }
function Restore-VisioRegistrySnapshot { param($BaseKey,$Snapshot); if($null -ne $Snapshot){$state.Enabled[$Snapshot.Path]=$true}; $state.Trace.Add('registry.restore') }
function Assert-PatentMarkerRegistrySnapshotRestored { param($BaseKey,$Path,$Snapshot); if($null -ne $Snapshot -and -not $state.Enabled[$Path]){throw 'not restored'}; $state.Trace.Add('registry.verify') }
function Assert-VisioRegistrySnapshotRestored { param($BaseKey,$Path,$Snapshot); if($null -ne $Snapshot -and -not $state.Enabled[$Path]){throw 'not restored'}; $state.Trace.Add('registry.verify') }
function Assert-PatentMarkerOwnedDirectory { param($InstallDirectory); if(-not (Test-Path -LiteralPath (Join-Path $InstallDirectory 'old.dll'))){throw 'incomplete'}; $state.Trace.Add('files.verify') }
function Assert-VisioOwnedDirectory { param($InstallDirectory); if(-not (Test-Path -LiteralPath (Join-Path $InstallDirectory 'old.dll'))){throw 'incomplete'}; $state.Trace.Add('files.verify') }
function Assert-PatentMarkerSiblingDirectory {
    param($Candidate,$ExpectedParent)
    $full=[IO.Path]::GetFullPath($Candidate)
    if([IO.Path]::GetDirectoryName($full) -ne [IO.Path]::GetFullPath($ExpectedParent)){throw 'Fixture path outside parent'}
    $full
}
function Assert-VisioSiblingDirectory { param($Candidate,$ExpectedParent); Assert-PatentMarkerSiblingDirectory $Candidate $ExpectedParent }
function Run-Case([string]$product,[string]$phase) {
    $sourcePath = Join-Path $root ('office-com-addin/install-' + $(if($product -eq 'ppt'){'office'}else{'visio'}) + '-addin.ps1')
    $ast = [Management.Automation.Language.Parser]::ParseFile($sourcePath,[ref]$null,[ref]$null)
    $catches = @($ast.FindAll({param($n) $n -is [Management.Automation.Language.CatchClauseAst] -and $n.Body.Statements.Count -gt 0 -and $n.Body.Statements[0].Extent.Text -eq '$failure = $_'},$true))
    if($catches.Count -ne 1){throw 'Cannot identify top-level rollback catch'}
    $body = $catches[0].Body.Extent.Text
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot ($product + '-install-catch.txt')),$body)
    $fixture = Join-Path $PSScriptRoot ('installer-fixtures/' + $product + '-' + $phase + '-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $fixture | Out-Null
    $installFull = Join-Path $fixture 'product'
    $installParent = $fixture
    $backupDirectory = Join-Path $fixture 'backup'
    $quarantineDirectory = Join-Path $fixture 'quarantine'
    $newTargetInstalled=$false; $newInstalled=$false
    $targetMovedToBackup=$false; $oldMoved=$false
    $registryPaths=@('class','prog','addin')
    $registrySnapshots=@()
    $baseKey=$null
    $FaultInjectionPoint='None'
    $state=@{Enabled=@{}; Trace=[Collections.Generic.List[string]]::new()}
    if($phase -ne 'first-install') {
        New-Item -ItemType Directory -Path $installFull | Out-Null
        [IO.File]::WriteAllText((Join-Path $installFull 'old.dll'),'complete old payload')
        $before=(Get-FileHash -LiteralPath (Join-Path $installFull 'old.dll')).Hash
        foreach($path in $registryPaths){$state.Enabled[$path]=$true; $registrySnapshots+=,[pscustomobject]@{Path=$path}}
        if($phase -eq 'after-old-move') {
            Move-Item -LiteralPath $installFull -Destination $backupDirectory
            $targetMovedToBackup=$true; $oldMoved=$true
        }
    }
    else { foreach($path in $registryPaths){$state.Enabled[$path]=$false; $registrySnapshots+=,$null} }
    $block=[scriptblock]::Create("try { throw 'injected staging/move failure' } catch " + $body)
    try { & $block *>&1 | ForEach-Object { [string]$_ } | Set-Content -LiteralPath (Join-Path $fixture 'trace.txt') }
    catch { if($_.Exception.Message -ne 'injected staging/move failure'){throw} }
    $filesOk=if($phase -eq 'first-install'){-not(Test-Path -LiteralPath $installFull)}else{(Get-FileHash -LiteralPath (Join-Path $installFull 'old.dll')).Hash -eq $before}
    $regOk=@($registryPaths | Where-Object { $state.Enabled[$_] -ne ($phase -ne 'first-install') }).Count -eq 0
    foreach($check in @(@('files',$filesOk),@('registration',$regOk))) {
        $script:total++; if(-not $check[1]){$script:fail++}
        $mark=if($check[1]){'PASS'}else{'FAIL'}
        "$mark|$product-$phase-$($check[0])|enabled=$($state.Enabled.Values -join ',')|trace=$($state.Trace -join ',')"
    }
}
foreach($p in @('ppt','visio')){foreach($phase in @('before-old-move','after-old-move','first-install')){Run-Case $p $phase}}
"SUMMARY|total=$total|failed=$fail"
if($fail){exit 1}

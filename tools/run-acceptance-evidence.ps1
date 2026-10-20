param(
    [Parameter(Mandatory=$true)][string]$PreFixRoot,
    [Parameter(Mandatory=$true)][string]$PostFixRoot,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(2,10)][int]$Repetitions=3,
    [int]$Seed=20261018
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$pre=(Resolve-Path -LiteralPath $PreFixRoot).Path
$post=(Resolve-Path -LiteralPath $PostFixRoot).Path
if($pre -eq $post){throw 'Pre/post snapshots must be distinct directories'}
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw "Output already exists: $out"}
New-Item -ItemType Directory -Path $out | Out-Null
$evidence=Join-Path $out 'evidence';New-Item -ItemType Directory -Path $evidence | Out-Null
$envNames=@('PATGEOM_REPORT_DIR','PATGEOM_RUN_ID','PATGEOM_REQUIRE_BRACES','PATGEOM_REQUIRE_TEXTS','PATGEOM_STRICT')
$parentEnvironment=[ordered]@{};foreach($name in $envNames){$parentEnvironment[$name]=[Environment]::GetEnvironmentVariable($name)}
$commands=[Collections.Generic.List[object]]::new()
function Invoke-Recorded([string]$Id,[string]$Exe,[string[]]$Arguments,[string]$WorkingDirectory,[int]$TimeoutSeconds=120) {
    $dir=Join-Path $evidence "commands/$Id";New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $psi=[Diagnostics.ProcessStartInfo]::new();$psi.FileName=$Exe;$psi.WorkingDirectory=$WorkingDirectory
    $psi.UseShellExecute=$false;$psi.CreateNoWindow=$true;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
    foreach($arg in $Arguments){[void]$psi.ArgumentList.Add($arg)}
    $p=[Diagnostics.Process]::new();$p.StartInfo=$psi;$timer=[Diagnostics.Stopwatch]::StartNew();$started=[DateTimeOffset]::Now.ToString('o')
    $code=-1;$failure=$null;$stdout='';$stderr=''
    try{
        [void]$p.Start();$readOut=$p.StandardOutput.ReadToEndAsync();$readErr=$p.StandardError.ReadToEndAsync()
        if(-not $p.WaitForExit($TimeoutSeconds*1000)){$p.Kill($true);$p.WaitForExit();$failure='timeout';$code=124}else{$code=$p.ExitCode}
        $stdout=$readOut.GetAwaiter().GetResult();$stderr=$readErr.GetAwaiter().GetResult()
    }catch{$failure=$_.Exception.Message}finally{$timer.Stop();$p.Dispose()}
    [IO.File]::WriteAllText((Join-Path $dir 'stdout.txt'),$stdout,[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $dir 'stderr.txt'),$stderr,[Text.UTF8Encoding]::new($false))
    $record=[ordered]@{id=$Id;executable=$Exe;arguments=$Arguments;cwd=$WorkingDirectory;started=$started;exit_code=$code;elapsed_ms=$timer.Elapsed.TotalMilliseconds;error=$failure;stdout="evidence/commands/$Id/stdout.txt";stderr="evidence/commands/$Id/stderr.txt";environment=[ordered]@{PATGEOM=$parentEnvironment;TEMP=$env:TEMP;TMP=$env:TMP}}
    $record|ConvertTo-Json -Depth 8|Set-Content (Join-Path $dir 'command.json') -Encoding utf8
    $commands.Add([pscustomobject]$record);Write-Host "$Id exit=$code"
    return [pscustomobject]$record
}
function Source-Identity([string]$Name,[string]$Path){
    $identityDir=Join-Path $evidence "sources/$Name";New-Item -ItemType Directory -Path $identityDir -Force|Out-Null
    $files=Get-ChildItem (Join-Path $Path 'cad-plugin') -File -Recurse|Where-Object {$_.FullName -notmatch '[\\/](bin|obj|lib|packages|tools)[\\/]' -and $_.Extension -in @('.cs','.csproj')}
    $files+=Get-ChildItem (Join-Path $Path 'tools') -File -Filter '*.ps1'
    $files+=Get-Item (Join-Path $Path 'build.ps1'),(Join-Path $Path 'package.ps1')
    $manifest=@($files|Sort-Object FullName|ForEach-Object{[ordered]@{path=[IO.Path]::GetRelativePath($Path,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
    $manifestPath=Join-Path $identityDir 'source-manifest.json';$manifest|ConvertTo-Json -Depth 4|Set-Content $manifestPath -Encoding utf8
    $diff=Invoke-Recorded "$Name-git-diff" 'git' @('diff','--binary','HEAD') $Path
    $status=Invoke-Recorded "$Name-git-status" 'git' @('status','--porcelain','--untracked-files=normal') $Path
    $commit=(& git -C $Path rev-parse HEAD);$tree=(& git -C $Path rev-parse 'HEAD^{tree}')
    return [ordered]@{root=$Path;commit=$commit;tree=$tree;dirty=([IO.File]::ReadAllText((Join-Path $out $status.stdout)).Length -gt 0);source_snapshot_id=(Get-FileHash $manifestPath).Hash.ToLowerInvariant();manifest="evidence/sources/$Name/source-manifest.json";diff_sha256=(Get-FileHash (Join-Path $out $diff.stdout)).Hash.ToLowerInvariant();diff=$diff.stdout;diff_scope='tracked git diff --binary HEAD; untracked C# and scripts are covered by source-manifest';diff_encoding='captured UTF-8 stdout'}
}
$identity=[ordered]@{report_id=Split-Path $out -Leaf;created=[DateTimeOffset]::Now.ToString('o');timezone='Asia/Shanghai';verification_actor='independent_acceptance_reviewer';operator=(& whoami);reviewer='Codex primary reviewer';independence_statement='Same machine/account and NuGet cache; separate pre-fix frozen directory and freshly copied post-fix source, rebuilt contract assemblies; identical acceptance harness and oracle. Independent from developer self-test, not an external auditor.';powershell=$PSVersionTable.PSVersion.ToString();sdk=(& dotnet --version);pre_fix=(Source-Identity 'pre' $pre);post_fix=(Source-Identity 'post' $post);repetitions=$Repetitions;randomization_seed=$Seed;order_randomized=$true;process_isolation='one fresh child per probe case';temporary_files='reports retained as evidence; harness binaries kept outside evidence directory';PATGEOM_parent_before=$parentEnvironment}
$rng=[Random]::new($Seed)
$work=Join-Path $repo ('_paneltest/acceptance-harness-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $work|Out-Null
$harness=Join-Path $repo 'tools/AcceptanceProbe/AcceptanceProbe.csproj'
$cases=@('sharp','baseline','moved-text','moved-previous','mtext-only','textlocation-only','record-only','all-synchronized','translated-large','legacy-leader','geometry-matrix','scale')
foreach($phase in @('pre','post')){
    $snapshot=if($phase -eq 'pre'){$pre}else{$post}
    $project=Join-Path $snapshot 'cad-plugin/RuntimeContract.Tests/PatentMarker.RuntimeContract.2010.Tests.csproj'
    $built=Invoke-Recorded "$phase-contract-build" 'dotnet' @('build',$project,'-c','Release','--nologo','-v','minimal') $snapshot
    if($built.exit_code -ne 0){throw "$phase contract build failed"}
    $contract=Join-Path $snapshot 'cad-plugin/RuntimeContract.Tests/bin/Release/net8.0/PatentMarker.RuntimeContract.2010.Tests.dll'
    $binary=Join-Path $work $phase
    $built=Invoke-Recorded "$phase-harness-build" 'dotnet' @('build',$harness,'-c','Release','--nologo','-v','minimal',('-p:ContractAssemblyPath='+$contract),'-o',$binary) $repo
    if($built.exit_code -ne 0){throw "$phase harness build failed"}
    $identity[$phase+'_assembly']=[ordered]@{path=$contract;sha256=(Get-FileHash $contract).Hash.ToLowerInvariant()}
    for($iteration=1;$iteration -le $Repetitions;$iteration++){
        $order=@($cases|Sort-Object {$rng.Next()});$caseOrderPath=Join-Path $evidence "$phase-$iteration-order.json";$order|ConvertTo-Json|Set-Content $caseOrderPath -Encoding utf8
        foreach($case in $order){
            $caseDir=Join-Path $evidence "cases/$phase/$iteration/$case"
            $record=Invoke-Recorded "$phase-$iteration-$case" 'dotnet' @((Join-Path $binary 'AcceptanceProbe.dll'),$case,$caseDir) $snapshot 90
        }
        $reportRunner=Join-Path $repo 'tools/acceptance-report-cases.ps1'
        $record=Invoke-Recorded "$phase-$iteration-report-matrix" (Get-Process -Id $PID).Path @('-NoProfile','-File',$reportRunner,'-Verifier',(Join-Path $snapshot 'tools/verify-geometry.ps1'),'-OutputDirectory',(Join-Path $evidence "report-cases/$phase/$iteration")) $snapshot
    }
}
$selectors=@('SharpElbowWithFakeEndTangents_IsRejectedByReferenceCurve','MovedTextWithoutChainUpdate_IsRejectedByT3','ProductionGeometry_WithDifferentCurveSamples_PassesReferenceCheck','CommandLevelRun_GeneratesPassReportForBraceAndAnnotation','Summary_DistinguishesPassSkipAndRequiredFailures')|ForEach-Object{'PatentMarker.RuntimeContract.Tests.PatGeomVerifyTests.'+$_}
$filter=($selectors|ForEach-Object{'FullyQualifiedName='+$_}) -join '|'
foreach($edition in @('2007','2010','2013','2015')){
    $project=Join-Path $post "cad-plugin/RuntimeContract.Tests/PatentMarker.RuntimeContract.$edition.Tests.csproj"
    for($iteration=1;$iteration -le $Repetitions;$iteration++){
        $record=Invoke-Recorded "post-$edition-selected-$iteration" 'dotnet' @('test',$project,'-c','Release','--nologo','-v','minimal','--filter',$filter,'--logger',"trx;LogFileName=$edition-$iteration.trx",'--results-directory',(Join-Path $evidence 'trx')) $post
    }
}
$identity['test_selectors']=$selectors;$identity['PATGEOM_parent_after']=[ordered]@{}
foreach($name in $envNames){$identity.PATGEOM_parent_after[$name]=[Environment]::GetEnvironmentVariable($name)}
$identity['parent_environment_restored']=($parentEnvironment|ConvertTo-Json -Compress) -eq ($identity.PATGEOM_parent_after|ConvertTo-Json -Compress)
$identity['commands']=$commands
$identity|ConvertTo-Json -Depth 12|Set-Content (Join-Path $out 'run.json') -Encoding utf8
Write-Host "Evidence retained: $out; findings must be derived from business assertions, not this runner's completion."

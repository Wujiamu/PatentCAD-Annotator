#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Prepare','Start','Watch','Launch','Mark','Status','Stop','Restore','Finalize','SelfTest')][string]$Action,
    [string]$SessionDirectory,
    [string]$CandidateRoot,
    [string]$HostExecutable='C:\Program Files\Autodesk\AutoCAD 2026\acad.exe',
    [ValidateSet('2007','2010','2013','2015','2025')][string]$Edition='2025',
    [string]$Drawing,
    [string]$CaseId,
    [ValidateSet('PASS','FAIL','SKIP/BLOCKED')][string]$Result='SKIP/BLOCKED',
    [string]$Notes='',
    [string[]]$EvidencePath=@(),
    [int]$PollSeconds=2,
    [int]$Hours=8,
    [switch]$Once
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$utf8=[Text.UTF8Encoding]::new($false)
function Now { [DateTimeOffset]::UtcNow.ToString('o') }
function WriteJson($path,$value) {
    $temp=$path+'.writing-'+$PID
    [IO.File]::WriteAllText($temp,($value|ConvertTo-Json -Depth 18),$utf8)
    [IO.File]::Move($temp,$path,$true)
}
function ReadJson($path) { Get-Content -LiteralPath $path -Raw -Encoding utf8|ConvertFrom-Json }
function Hash($path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function AppendJson($path,$value) { [IO.File]::AppendAllText($path,($value|ConvertTo-Json -Depth 12 -Compress)+"`n",$utf8) }
function CheckPackage($path) {
    $values=@{}
    foreach($line in Get-Content -LiteralPath (Join-Path $path 'PatentMarker-release.txt') -Encoding utf8){if($line -match '^([^#=]+)=(.*)$'){$values[$matches[1]]=$matches[2]}}
    if(!(Test-Path (Join-Path $path 'PatentMarker.dll')) -or (Hash (Join-Path $path 'PatentMarker.dll')) -ne $values['artifact.sha256']){throw "Candidate/manifest SHA mismatch: $path"}
    if(!$values['artifact.mvid']){throw "Candidate manifest MVID missing: $path"}
    return $values
}
function CheckFrozen($package) {
    $manifest=CheckPackage $package.directory
    if($manifest['artifact.sha256'] -ne $package.sha256 -or $manifest['artifact.mvid'] -ne $package.mvid -or (Hash (Join-Path $package.directory 'PatentMarker-release.txt')) -ne $package.manifest_sha256){throw "Candidate differs from session's frozen identity: $($package.edition)"}
}
if($Action -eq 'Prepare') {
    if(!$CandidateRoot){$CandidateRoot='C:\Users\wjm\WorkBuddy\PatentMarker-acceptance-r3-post-20261018\acceptance-stage'}
    if(!$SessionDirectory){$SessionDirectory=Join-Path $repo ('docs/test-evidence/manual-cad-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,6))}
    $SessionDirectory=[IO.Path]::GetFullPath($SessionDirectory)
    if(Test-Path -LiteralPath $SessionDirectory){throw 'Session directory already exists; never overwrite a run'}
    New-Item -ItemType Directory -Path $SessionDirectory|Out-Null
    foreach($sub in 'packages','drawings','screenshots','archive','launches','results','backup','tools','fixtures'){New-Item -ItemType Directory -Path (Join-Path $SessionDirectory $sub)|Out-Null}
    $packages=@(foreach($ed in '2007','2010','2013','2015','2025'){
        $source=Join-Path $CandidateRoot $ed;$m=CheckPackage $source
        $target=Join-Path $SessionDirectory "packages/$ed";New-Item -ItemType Directory -Path $target|Out-Null
        # Only CAD assets; do not ship or install unrelated Office add-ins.
        Get-ChildItem -LiteralPath $source -File|Where-Object {$_.Name -eq 'PatentMarker.dll' -or $_.Name -eq 'PatentMarker-release.txt' -or $_.Name -eq 'README.txt' -or $_.Name -match "^(install|uninstall|doctor)-$ed\.(ps1|vbs|bat)$"}|Copy-Item -Destination $target
        [ordered]@{edition=$ed;directory=$target;sha256=$m['artifact.sha256'];mvid=$m['artifact.mvid'];manifest_sha256=(Hash (Join-Path $target 'PatentMarker-release.txt'))}
    })
    $fixture=if(Test-Path (Join-Path $repo 'fixtures/empty.dwg')){Join-Path $repo 'fixtures'}else{Join-Path $repo 'docs/test-evidence/acceptance-r3-20261018/evidence/real-host'}
    Copy-Item -LiteralPath (Join-Path $fixture 'empty.dwg') -Destination (Join-Path $SessionDirectory 'fixtures/empty.dwg')
    foreach($name in 'A','B'){
        Copy-Item -LiteralPath (Join-Path $fixture 'empty.dwg') -Destination (Join-Path $SessionDirectory "drawings/manual-$name.dwg")
        $number=if($name -eq 'A'){'901'}else{'902'}
        WriteJson (Join-Path $SessionDirectory "drawings/manual-$name.dict.json") ([ordered]@{metadata=@{source_file="manual-$name.docx";extracted_at=Now;version='manual-acceptance'};entries=@(@{number=$number;name="Manual-$name";occurrences=1;conflicts=@()});warnings=@()})
    }
    Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $SessionDirectory 'tools/manual-cad-acceptance.ps1')
    $settings=[ordered]@{schema_version=1;session_id=(Split-Path $SessionDirectory -Leaf);created_utc=Now;timezone='Asia/Shanghai';actor=[Security.Principal.WindowsIdentity]::GetCurrent().Name;candidate_origin=$CandidateRoot;packages=$packages;host_executable=$HostExecutable;edition=$Edition;poll_seconds=$PollSeconds;expires_utc=[DateTimeOffset]::UtcNow.AddHours($Hours).ToString('o');monitor_disabled=$true;monitor_mode='offline_manual';known_findings=@('R3-01','R3-02','R3-03','R3-04','R3-05','R3-06');scope='Offline manual evidence only; continuous monitor disabled after user reported CAD freeze; never upgrades the R3 FAIL verdict';initial_processes=@(Get-Process acad,accoreconsole -ErrorAction SilentlyContinue|Select-Object Id,Path,StartTime)}
    $acceptance=Join-Path $repo 'docs/acceptance-findings.json'
    if(Test-Path $acceptance){$base=ReadJson $acceptance;$settings['base_report_id']=$base.report_id;$settings['source_snapshot_id']=$base.source_snapshot_id;$settings['base_findings_sha256']=Hash $acceptance}
    WriteJson (Join-Path $SessionDirectory 'session.json') $settings
    $cases=@(foreach($id in 'G01-identity','G02-brace-display','G03-brace-edit','G04-mleader-display','G05-mleader-grips','G06-brace-grips','G07-undo-redo','G08-palette-esc','G09-multidoc','G10-save-reopen','H2007','H2010','H2013','H2015'){@{case_id=$id;result='SKIP/BLOCKED';verification_actor='human_pending';notes='Not yet performed'}})
    WriteJson (Join-Path $SessionDirectory 'required-cases.json') $cases
    Write-Output "SESSION_DIRECTORY=$SessionDirectory"
    exit 0
}
if($Action -eq 'SelfTest') {
    $SessionDirectory=Join-Path $repo ('_paneltest/manual-monitor-selftest-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $SessionDirectory|Out-Null
    $fixture=Join-Path $SessionDirectory 'report.txt';$expected=@{directory='C:\candidate';sha256='a'*64;mvid='11111111-1111-1111-1111-111111111111'}
    # Tests are dispatched below after helper definitions; never writes registry or launches CAD.
} else {
    if(!$SessionDirectory){throw 'SessionDirectory required'}
    $SessionDirectory=(Resolve-Path -LiteralPath $SessionDirectory).Path
    $session=ReadJson (Join-Path $SessionDirectory 'session.json')
    if($Action -in 'Start','Watch'){throw 'Continuous monitoring is disabled by user. Use offline evidence collection after CAD has closed.'}
    if(!$PSBoundParameters.ContainsKey('Edition')){$Edition=$session.edition}
    if(!$PSBoundParameters.ContainsKey('HostExecutable')){$HostExecutable=$session.host_executable}
    if((Test-Path (Join-Path $SessionDirectory 'evidence-index.json')) -and $Action -in 'Start','Launch','Mark','Watch'){throw 'Session is sealed; prepare a new session'}
}
function Emit([string]$type,$detail) { AppendJson (Join-Path $SessionDirectory 'events.ndjson') ([ordered]@{utc=Now;type=$type;detail=$detail}) }
function Signal([string]$type,$detail) { AppendJson (Join-Path $SessionDirectory 'alerts.ndjson') ([ordered]@{id=[guid]::NewGuid().ToString('N');utc=Now;type=$type;detail=$detail}) }
function NoCad { if(Get-Process acad,accoreconsole -ErrorAction SilentlyContinue){throw 'Close all CAD processes yourself first; monitor never kills user apps'} }
function InstallCandidate {
    NoCad
    if($Edition -ne '2025'){throw 'Automatic install isolation supports 2025/2026 only. Use documented per-version installer/backup steps on older hosts.'}
    $statePath=Join-Path $SessionDirectory 'backup/install-state.json'
    if(Test-Path $statePath){
        if(!(Test-Path (Join-Path $SessionDirectory 'backup/restored.json'))){return}
        throw 'Installation was already restored; prepare a fresh session for another installation cycle'
    }
    $registry=@(foreach($release in 'R25.0','R25.1','R26.0'){
        $names=@(foreach($hive in 'HKCU','HKLM'){$r="$($hive):\Software\Autodesk\AutoCAD\$release";if(Test-Path $r){Get-ChildItem $r|Where-Object PSChildName -Like 'ACAD-*'|ForEach-Object PSChildName}})|Sort-Object -Unique
        foreach($name in $names){$p="HKCU:\Software\Autodesk\AutoCAD\$release\$name\Applications\PatentMarker";$exists=Test-Path $p;$vals=if($exists){$k=Get-Item $p;@($k.GetValueNames()|ForEach-Object{@{name=$_;value=$k.GetValue($_);kind=$k.GetValueKind($_).ToString()}})}else{@()};@{path=$p;existed=$exists;values=$vals}}
    })
    if(!$registry.Count){throw 'No supported installed AutoCAD registry release found'}
    $bundle=Join-Path $env:APPDATA 'Autodesk/ApplicationPlugins/PatentMarker.bundle'
    $saved=Join-Path $SessionDirectory 'backup/PatentMarker.bundle'
    $bundleFiles=@(if(Test-Path $bundle){Get-ChildItem $bundle -File -Recurse|ForEach-Object{@{relative=[IO.Path]::GetRelativePath($bundle,$_.FullName);sha256=Hash $_.FullName}}})
    $otherFiles=@(Get-ChildItem (Split-Path $bundle -Parent) -File -Recurse -ErrorAction SilentlyContinue|Where-Object {!$_.FullName.StartsWith($bundle+'\',[StringComparison]::OrdinalIgnoreCase)}|ForEach-Object{@{path=$_.FullName;sha256=Hash $_.FullName}})
    WriteJson $statePath @{utc=Now;registry=$registry;bundle=$bundle;bundle_backup=$saved;bundle_files=$bundleFiles;non_product=$otherFiles}
    try {
        if(Test-Path $bundle){
            if([IO.Path]::GetFullPath($bundle) -ne [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Autodesk/ApplicationPlugins/PatentMarker.bundle')) -or ![IO.Path]::GetFullPath($saved).StartsWith($SessionDirectory+'\backup\')){throw 'Unsafe bundle backup paths'}
            Move-Item -LiteralPath $bundle -Destination $saved
        }
        $package=Join-Path $SessionDirectory 'packages/2025';CheckPackage $package|Out-Null
        & (Get-Process -Id $PID).Path -NoProfile -File (Join-Path $package 'install-2025.ps1') -DllPath (Join-Path $package 'PatentMarker.dll') -NoPause *> (Join-Path $SessionDirectory 'backup/install.stdout.txt')
        if($LASTEXITCODE -ne 0){throw "Installer exit $LASTEXITCODE"}
        foreach($r in $registry){$v=Get-ItemProperty $r.path;if($v.LOADER -ne (Join-Path $package 'PatentMarker.dll') -or $v.LOADCTRLS -ne 14 -or $v.MANAGED -ne 1){throw 'Installed registration mismatch'}}
        WriteJson (Join-Path $SessionDirectory 'backup/installed.json') @{utc=Now;result='PASS';scope='Candidate registration only; no host behavior assertion'}
    } catch { RestoreCandidate;throw }
}
function RestoreCandidate {
    NoCad
    $file=Join-Path $SessionDirectory 'backup/install-state.json'
    if(!(Test-Path $file)){return}
    if(Test-Path (Join-Path $SessionDirectory 'backup/restored.json')){return}
    $state=ReadJson $file;$checks=@()
    foreach($r in $state.registry){
        if($r.path -notmatch '^HKCU:\\Software\\Autodesk\\AutoCAD\\R(25\.[01]|26\.0)\\ACAD-[^\\]+\\Applications\\PatentMarker$'){throw 'Unsafe registry restore path'}
        if($r.existed){
            if(!(Test-Path $r.path)){New-Item -Path $r.path -Force|Out-Null}
            $k=Get-Item $r.path
            foreach($name in $k.GetValueNames()){if($name -notin @($r.values.name)){Remove-ItemProperty -LiteralPath $r.path -Name $name}}
            foreach($v in $r.values){$value=$v.value;if($v.kind -eq 'DWord'){$value=[int]$value};New-ItemProperty -LiteralPath $r.path -Name $v.name -Value $value -PropertyType $v.kind -Force|Out-Null}
            $k=Get-Item $r.path
            foreach($v in $r.values){$checks+=@{type='registry';path=$r.path;name=$v.name;ok=([string]$k.GetValue($v.name) -eq [string]$v.value -and $k.GetValueKind($v.name).ToString() -eq $v.kind)}}
        }elseif(Test-Path $r.path){Remove-Item -LiteralPath $r.path}
    }
    if(Test-Path $state.bundle_backup){
        $allowed=[IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Autodesk/ApplicationPlugins/PatentMarker.bundle'))
        if([IO.Path]::GetFullPath($state.bundle) -ne $allowed -or ![IO.Path]::GetFullPath($state.bundle_backup).StartsWith($SessionDirectory+'\backup\')){throw 'Unsafe bundle restore paths'}
        if(Test-Path $state.bundle){throw 'Bundle destination occupied; retained backup, no overwrite'}
        Move-Item -LiteralPath $state.bundle_backup -Destination $state.bundle
    }
    foreach($f in $state.bundle_files){$checks+=@{type='bundle';path=$f.relative;ok=((Hash (Join-Path $state.bundle $f.relative)) -eq $f.sha256)}}
    foreach($f in $state.non_product){$checks+=@{type='non-product';path=$f.path;ok=((Hash $f.path) -eq $f.sha256)}}
    $ok=@($checks|Where-Object {!$_.ok}).Count -eq 0
    WriteJson (Join-Path $SessionDirectory 'backup/restoration-check.json') @{utc=Now;result=$(if($ok){'PASS'}else{'FAIL'});checks=$checks}
    if(!$ok){throw 'Restoration comparison failed; backup metadata retained'}
    WriteJson (Join-Path $SessionDirectory 'backup/restored.json') @{utc=Now;result='PASS'}
}
function AnalyzeReport([string]$text,$expected,[string]$expectedRunId) {
    $fields=@{};$kind=if($text -match 'PATGEOMVERIFY -'){'geometry'}else{'doctor'}
    if($kind -eq 'geometry'){
        foreach($key in 'RUN_ID','ASSEMBLY_PATH','ASSEMBLY_SHA256','ASSEMBLY_MVID','RESULT'){$m=[regex]::Match($text,'(?m)^'+$key+':\s*([^\r\n]*)');$fields[$key]=$m.Groups[1].Value.Trim()}
        $path=$fields.ASSEMBLY_PATH;$sha=$fields.ASSEMBLY_SHA256;$mvid=$fields.ASSEMBLY_MVID
        $fresh=$expectedRunId -and $fields.RUN_ID -eq $expectedRunId
    }else{
        foreach($key in 'Assembly','Assembly SHA-256','Assembly MVID'){$m=[regex]::Match($text,'(?m)^- '+[regex]::Escape($key)+':\s*([^\r\n]*)');$fields[$key]=$m.Groups[1].Value.Trim()}
        $path=$fields.Assembly;$sha=$fields['Assembly SHA-256'];$mvid=$fields['Assembly MVID'];$fresh=$true
    }
    $identity=$false
    try {$identity=[string]::Equals([IO.Path]::GetFullPath($path),[IO.Path]::GetFullPath((Join-Path $expected.directory 'PatentMarker.dll')),[StringComparison]::OrdinalIgnoreCase) -and $sha -eq $expected.sha256 -and $mvid -eq $expected.mvid}catch{}
    [ordered]@{kind=$kind;reported_fields=$fields;identity=$(if($identity){'PASS'}else{'FAIL'});run_id_matches=$fresh;business_result=$(if($kind -eq 'geometry'){$fields.RESULT}else{([regex]::Match($text,'Summary: [^\r\n]+')).Value});scope='Reported observation only; not human GUI acceptance and not complete geometry gate validation'}
}
if($Action -eq 'SelfTest') {
    $text="PATGEOMVERIFY - geometry regression verification`nRUN_ID: test`nASSEMBLY_PATH: C:\candidate\PatentMarker.dll`nASSEMBLY_SHA256: $('a'*64)`nASSEMBLY_MVID: $($expected.mvid)`nRESULT: PASS"
    $good=AnalyzeReport $text $expected 'test';$bad=AnalyzeReport ($text.Replace('a'*64,'b'*64)) $expected 'test';$stale=AnalyzeReport $text $expected 'new'
    $doctor="- Assembly: C:\candidate\PatentMarker.dll`n- Assembly SHA-256: $('a'*64)`n- Assembly MVID: $($expected.mvid)`nSummary: PASS 7 / FAIL 0 / SKIP 0"
    $d=AnalyzeReport $doctor $expected ''
    WriteJson $fixture @{state='first'};$first=Hash $fixture;Copy-Item $fixture (Join-Path $SessionDirectory 'archived-first.json');WriteJson $fixture @{state='second'}
    $rows=@(@{case='identity-good';ok=($good.identity -eq 'PASS' -and $good.run_id_matches)},@{case='wrong-sha';ok=($bad.identity -eq 'FAIL')},@{case='stale-run';ok=(!$stale.run_id_matches)},@{case='doctor-identity';ok=($d.identity -eq 'PASS')},@{case='overwrite-snapshot-retained';ok=((Hash (Join-Path $SessionDirectory 'archived-first.json')) -eq $first -and (Hash $fixture) -ne $first)})
    WriteJson (Join-Path $SessionDirectory 'results.json') $rows
    $rows|ForEach-Object {Write-Output ($_.case+': '+$_.ok)}
    Write-Output "SELFTEST_DIRECTORY=$SessionDirectory"
    if(@($rows|Where-Object {!$_.ok}).Count){exit 1};exit 0
}
switch($Action){
    'Start' {
        $statusPath=Join-Path $SessionDirectory 'monitor-status.json'
        if(Test-Path $statusPath){$old=ReadJson $statusPath;if($old.state -eq 'RUNNING' -and (Get-Process -Id $old.pid -ErrorAction SilentlyContinue)){Write-Output 'Monitor already running';exit 0}}
        Remove-Item -LiteralPath (Join-Path $SessionDirectory 'stop.request') -ErrorAction SilentlyContinue
        $script=Join-Path $SessionDirectory 'tools/manual-cad-acceptance.ps1'
        $process=Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoProfile','-File',('"'+$script+'"'),'-Action','Watch','-SessionDirectory',('"'+$SessionDirectory+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $SessionDirectory 'monitor.stdout.txt') -RedirectStandardError (Join-Path $SessionDirectory 'monitor.stderr.txt')
        WriteJson (Join-Path $SessionDirectory 'monitor-process.json') @{pid=$process.Id;started_utc=Now;script=$script;script_sha256=Hash $script}
        Write-Output "MONITOR_PID=$($process.Id)";break
    }
    'Launch' {
        NoCad
        $package=$session.packages|Where-Object edition -eq $Edition;CheckFrozen $package
        if($Edition -eq '2025'){InstallCandidate}
        if(!$Drawing -and $Edition -eq '2025'){$Drawing=Join-Path $SessionDirectory 'drawings/manual-A.dwg'}
        if($Drawing){$Drawing=(Resolve-Path -LiteralPath $Drawing).Path}
        $HostExecutable=(Resolve-Path -LiteralPath $HostExecutable).Path
        $launchId=(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,6)
        $out=Join-Path $SessionDirectory "launches/$launchId";New-Item -ItemType Directory -Path $out|Out-Null
        $psi=[Diagnostics.ProcessStartInfo]::new();$psi.FileName=$HostExecutable;$psi.UseShellExecute=$false
        if($Drawing){[void]$psi.ArgumentList.Add($Drawing)};[void]$psi.ArgumentList.Add('/nologo')
        $psi.Environment['PATGEOM_RUN_ID']=$launchId;$psi.Environment['PATGEOM_REPORT_DIR']=$out;$psi.Environment['PATGEOM_REQUIRE_BRACES']='1';$psi.Environment['PATGEOM_REQUIRE_TEXTS']='1';$psi.Environment['PATGEOM_STRICT']='1'
        $before=Now;$inputHash=if($Drawing){Hash $Drawing}else{$null};$p=[Diagnostics.Process]::Start($psi)
        WriteJson (Join-Path $out 'launch.json') @{utc=$before;pid=$p.Id;process_start_utc=$p.StartTime.ToUniversalTime().ToString('o');exe=$HostExecutable;exe_version=(Get-Item $HostExecutable).VersionInfo.FileVersion;drawing=$Drawing;drawing_sha256_before=$inputHash;run_id=$launchId;edition=$Edition;expected=$package;environment=@{PATGEOM_RUN_ID=$launchId;PATGEOM_REPORT_DIR=$out;PATGEOM_REQUIRE_BRACES='1';PATGEOM_REQUIRE_TEXTS='1';PATGEOM_STRICT='1'};scope='Normal visible GUI launch; no /b, NETLOAD or initialization helper'}
        Write-Output "GUI_LAUNCHED_PID=$($p.Id); RUN_ID=$launchId";break
    }
    'Mark' {
        if($session.monitor_disabled){NoCad}
        $cases=ReadJson (Join-Path $SessionDirectory 'required-cases.json')
        if($CaseId -notin @($cases.case_id)){throw 'Unknown case ID'}
        if(!$Notes){throw 'Notes required: expected/actual and manual steps'}
        if($Result -eq 'PASS' -and !$EvidencePath.Count){throw 'Human PASS needs saved screenshots/DWG/log evidence; no evidence is not PASS'}
        $refs=@(foreach($path in $EvidencePath){$p=(Resolve-Path -LiteralPath $path).Path;$frozen=Join-Path $SessionDirectory ('archive/manual-'+[guid]::NewGuid().ToString('N')+'-'+[IO.Path]::GetFileName($p));Copy-Item -LiteralPath $p -Destination $frozen;@{path=$frozen;source=$p;sha256=Hash $frozen}})
        WriteJson (Join-Path $SessionDirectory ('results/'+$CaseId+'-'+[guid]::NewGuid().ToString('N')+'.json')) @{utc=Now;case_id=$CaseId;result=$Result;verification_actor='human_attestation';notes=$Notes;evidence=$refs;scope='User manual result, pending independent evidence review'}
        Write-Output 'Human result recorded; original acceptance FAIL unchanged';break
    }
    'Status' {if(Test-Path (Join-Path $SessionDirectory 'monitor-status.json')){Get-Content (Join-Path $SessionDirectory 'monitor-status.json') -Raw}else{'Monitor has not started'};break}
    'Stop' {WriteJson (Join-Path $SessionDirectory 'stop.request') @{utc=Now};Write-Output 'Stop requested; CAD is left under user control';break}
    'Restore' {RestoreCandidate;Write-Output 'Product installation restored (or no install was performed)';break}
    'Finalize' {
        NoCad;RestoreCandidate
        WriteJson (Join-Path $SessionDirectory 'stop.request') @{utc=Now}
        $until=[DateTimeOffset]::UtcNow.AddSeconds(15)
        do{$status=if(Test-Path (Join-Path $SessionDirectory 'monitor-status.json')){ReadJson (Join-Path $SessionDirectory 'monitor-status.json')};if(!$status -or $status.state -ne 'RUNNING'){break};Start-Sleep -Milliseconds 500}while([DateTimeOffset]::UtcNow -lt $until)
        if($status.state -eq 'RUNNING'){throw 'Monitor did not stop; not finalizing a moving evidence directory'}
        if($status.pid){while((Get-Process -Id $status.pid -ErrorAction SilentlyContinue) -and [DateTimeOffset]::UtcNow -lt $until){Start-Sleep -Milliseconds 200};if(Get-Process -Id $status.pid -ErrorAction SilentlyContinue){throw 'Monitor process still closing; retry Finalize'}}
        $files=@(Get-ChildItem $SessionDirectory -File -Recurse|Where-Object {$_.Name -notin 'evidence-index.json','reviewer-cursor.json','reviewer-monitor.json'}|Sort-Object FullName|ForEach-Object{@{path=[IO.Path]::GetRelativePath($SessionDirectory,$_.FullName);bytes=$_.Length;sha256=Hash $_.FullName}})
        WriteJson (Join-Path $SessionDirectory 'evidence-index.json') @{utc=Now;files=$files;result='evidence_sealed_not_product_PASS'}
        Write-Output 'Restored installation, stopped monitor, sealed evidence. Human/independent review still required.';break
    }
    'Watch' {
        $cache=@{};$seenPids=@{};$seenEvents=@{};$observations=@();$stopped='STOPPED';$started=Now;$lastCrashCheck=[DateTimeOffset]::MinValue;$lastPackageCheck=[DateTimeOffset]::MinValue
        try {
            do {
                $launches=@(Get-ChildItem (Join-Path $SessionDirectory 'launches') -Filter launch.json -Recurse|ForEach-Object {ReadJson $_.FullName})
                $latest=$launches|Sort-Object utc|Select-Object -Last 1
                $candidate=$session.packages|Where-Object edition -eq $session.edition
                $sources=@(Get-ChildItem (Join-Path $SessionDirectory 'packages') -Recurse -File|Where-Object {$_.Name -in 'PatentMarker-doctor-report.txt','PatentMarker-geom-verify.txt','PatentMarker.log'})
                $sources+=@(Get-ChildItem (Join-Path $SessionDirectory 'launches') -Recurse -File|Where-Object {$_.Name -in 'PatentMarker-doctor-report.txt','PatentMarker-geom-verify.txt','PatentMarker.log'})
                $sources+=@(Get-ChildItem (Join-Path $SessionDirectory 'drawings'),(Join-Path $SessionDirectory 'screenshots'),(Join-Path $SessionDirectory 'results') -Recurse -File|Where-Object {$_.Extension -in '.dwg','.json','.png','.jpg','.jpeg','.txt'})
                # Also observe current registered product locations to detect an old same-name DLL winning demand-load.
                foreach($release in 'R25.0','R25.1','R26.0'){
                    $r="HKCU:\Software\Autodesk\AutoCAD\$release"
                    if(Test-Path $r){foreach($profile in Get-ChildItem $r){$key=Join-Path $profile.PSPath 'Applications/PatentMarker';if(Test-Path $key){$loader=(Get-ItemProperty $key).LOADER;if($loader){foreach($name in 'PatentMarker-doctor-report.txt','PatentMarker.log'){$p=Join-Path (Split-Path $loader -Parent) $name;if(Test-Path $p){$sources+=Get-Item $p}}}}}}
                }
                foreach($base in $env:APPDATA,$env:ProgramData){foreach($name in 'PatentMarker-doctor-report.txt','PatentMarker.log'){$p=Join-Path $base ('Autodesk/ApplicationPlugins/PatentMarker.bundle/'+$name);if(Test-Path $p){$sources+=Get-Item $p}}}
                foreach($f in $sources|Sort-Object FullName -Unique){
                    if($f.LastWriteTimeUtc -lt ([DateTimeOffset]::Parse($session.created_utc)).UtcDateTime){continue}
                    $stamp=$f.Length.ToString()+':'+$f.LastWriteTimeUtc.Ticks
                    if($cache[$f.FullName] -eq $stamp){continue}
                    try {
                        # Read one complete byte snapshot; do not hash one version and archive another.
                        $stream=[IO.File]::Open($f.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
                        try{$memory=[IO.MemoryStream]::new();$stream.CopyTo($memory);$bytes=$memory.ToArray()}finally{$stream.Dispose()}
                        $after=Get-Item $f.FullName
                        if(($after.Length.ToString()+':'+$after.LastWriteTimeUtc.Ticks) -ne $stamp){continue}
                        $sha=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
                        $pathId=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($f.FullName))).Substring(0,10)
                        $target=Join-Path $SessionDirectory ('archive/'+(Get-Date -Format 'yyyyMMdd-HHmmssfff')+'-'+$pathId+'-'+$sha.Substring(0,12)+'-'+$f.Name)
                        [IO.File]::WriteAllBytes($target,$bytes);$cache[$f.FullName]=$stamp
                        $entry=[ordered]@{utc=Now;source=$f.FullName;source_mtime_utc=$f.LastWriteTimeUtc.ToString('o');bytes=$bytes.Length;sha256=$sha;archive=$target;observation=$null}
                        if($f.Name -in 'PatentMarker-doctor-report.txt','PatentMarker-geom-verify.txt'){
                            $entry.observation=AnalyzeReport ($utf8.GetString($bytes)) $candidate $latest.run_id
                            if(!$latest -or $f.LastWriteTimeUtc -lt ([DateTimeOffset]::Parse($latest.utc)).UtcDateTime){$entry.observation['attribution']='SKIP/BLOCKED: no fresh registered manual launch'}else{
                                $entry.observation['attribution']='Time/path/run correlation only; process concurrency still requires review'
                                if($entry.observation.identity -eq 'FAIL' -or ($entry.observation.kind -eq 'geometry' -and !$entry.observation.run_id_matches)){Signal 'IDENTITY_OR_RUN_MISMATCH' $entry}
                                if($entry.observation.business_result -eq 'FAIL' -or $entry.observation.business_result -match 'FAIL [1-9]'){Signal 'REPORT_FAIL' $entry}
                            }
                            $observations+=,$entry
                        }
                        AppendJson (Join-Path $SessionDirectory 'file-index.ndjson') $entry
                    }catch {Emit 'FILE_BUSY_OR_READ_ERROR' @{path=$f.FullName;message=$_.Exception.Message}}
                }
                $active=@(Get-Process acad,accoreconsole -ErrorAction SilentlyContinue|ForEach-Object{@{pid=$_.Id;path=$_.Path;started_utc=$_.StartTime.ToUniversalTime().ToString('o')}})
                foreach($p in $active){$key=[string]$p.pid+':'+$p.started_utc;if(!$seenPids.ContainsKey($key)){$seenPids[$key]=$true;Emit 'CAD_PROCESS_OBSERVED' $p}}
                if([DateTimeOffset]::UtcNow - $lastPackageCheck -gt [TimeSpan]::FromSeconds(30)){
                    foreach($p in $session.packages){
                        try{CheckFrozen $p}catch{if(!$seenEvents.ContainsKey($p.edition)){$seenEvents[$p.edition]=$true;Signal 'CANDIDATE_CHANGED' @{edition=$p.edition;error=$_.Exception.Message}}}
                    }
                    $lastPackageCheck=[DateTimeOffset]::UtcNow
                }
                if([DateTimeOffset]::UtcNow - $lastCrashCheck -gt [TimeSpan]::FromSeconds(10)){
                    foreach($event in @(Get-WinEvent -FilterHashtable @{LogName='Application';Id=1000;StartTime=([DateTimeOffset]::Parse($session.created_utc)).LocalDateTime} -MaxEvents 25 -ErrorAction SilentlyContinue)){
                        if($seenEvents.ContainsKey([string]$event.RecordId)){continue};$seenEvents[[string]$event.RecordId]=$true
                        [xml]$xml=$event.ToXml();$values=@{};foreach($d in $xml.Event.EventData.Data){$values[[string]$d.Name]=[string]$d.'#text'}
                        if($values.AppName -notin 'acad.exe','accoreconsole.exe'){continue}
                        $crashedId=try{[Convert]::ToInt32($values.ProcessId.Replace('0x',''),16)}catch{-1}
                        if($crashedId -in @($launches.pid)){$path=Join-Path $SessionDirectory ('archive/crash-'+$event.RecordId+'.xml');[IO.File]::WriteAllText($path,$event.ToXml(),$utf8);Signal 'OWNED_CAD_CRASH' @{pid=$crashedId;record_id=$event.RecordId;file=$path;data=$values}}
                    }
                    $lastCrashCheck=[DateTimeOffset]::UtcNow
                }
                $manual=@(Get-ChildItem (Join-Path $SessionDirectory 'results') -Filter '*.json'|ForEach-Object {ReadJson $_.FullName})
                WriteJson (Join-Path $SessionDirectory 'monitor-status.json') @{state='RUNNING';pid=$PID;started_utc=$started;updated_utc=Now;expires_utc=$session.expires_utc;active_cad=$active;launch_count=$launches.Count;archived_versions=$cache.Count;report_observations=@($observations|Select-Object -Last 8);manual_results=$manual;overall='NOT_RELEASED';note='No automatic GUI PASS; R3 known FAILs remain open'}
                if($Once -or (Test-Path (Join-Path $SessionDirectory 'stop.request')) -or [DateTimeOffset]::UtcNow -ge [DateTimeOffset]::Parse($session.expires_utc)){break}
                Start-Sleep -Seconds $session.poll_seconds
            }while($true)
        }catch {$stopped='ERROR';Signal 'MONITOR_ERROR' @{error=$_.ToString()};throw}
        finally {WriteJson (Join-Path $SessionDirectory 'monitor-status.json') @{state=$stopped;pid=$PID;started_utc=$started;updated_utc=Now;overall='NOT_RELEASED';note='Passive monitor stopped; manual review and restoration are separate actions'}}
    }
}

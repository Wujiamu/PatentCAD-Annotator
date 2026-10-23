param([Parameter(Mandatory=$true)][string]$Verifier,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$good=@'
RUN_ID: same-case-run
ASSEMBLY_SHA256: abc123
ASSEMBLY_MVID: same-case-mvid
ASSEMBLY_PATH: C:\selftest\PatentMarker.dll
REQUIRED: braces>=1 texts>=1 strict=true
[PASS] B0c planar-contract: ok
[PASS] B1 no-axis-crossing: ok
[PASS] B2 tip-at-full-width: ok
[PASS] B3 stems-at-half-width: ok
[PASS] B4 within-selected-width: ok
[PASS] B5 endpoints-exact: ok
[PASS] B6 cusp-at-half-height: ok
[PASS] B7 mirror-symmetry: ok
[PASS] B8 single-cusp: ok
[PASS] B9 corner-curves: ok
[PASS] T1 text-horizontal: ok
[PASS] T2 text-x-direction: ok
[PASS] T3 leader-text-retract: ok
SUMMARY: braces=1 passed=1 failed=0 unreadable=0; texts=1 passed=1 failed=0 unreadable=0; requiredBraces=1 requiredTexts=1; strict=true
ASSERTIONS: pass=13 fail=0 n/a=0
RESULT: PASS
'@
$separator='-----------------------------------------------------'
$braceLines=($good -split "`r?`n"|Where-Object {$_ -match '^\[PASS\] B'}) -join "`n"
$textLines=($good -split "`r?`n"|Where-Object {$_ -match '^\[PASS\] T'}) -join "`n"
$textLines+="`n[PASS] T3a recorded-text-anchor: ok`n[PASS] T3b mleader-text-location: ok`n[PASS] T3c actual-previous-anchor: ok`n[PASS] T3d actual-endpoint-anchor: ok"
function Structured([string]$case){
    $second=if($case -eq 'structured-required-na'){$braceLines.Replace('[PASS] B9','[N/A] B9')}else{$braceLines}
    $empty=if($case -eq 'structured-empty-object'){"`n$separator`nLWPOLYLINE 99`n"}else{''}
    $pass=if($case -eq 'structured-required-na'){26}else{27};$na=if($case -eq 'structured-required-na'){1}else{0}
    return (($good -split "`r?`n"|Select-Object -First 5) -join "`n")+"`n$separator`nLWPOLYLINE 1`n$braceLines`n=> PASS`n$separator`nLWPOLYLINE 2`n$second`n=> PASS`n$separator`nMLEADER 3`n$textLines`n$empty`n$separator`nSUMMARY: braces=2 passed=2 failed=0 unreadable=0; texts=1 passed=1 failed=0 unreadable=0; requiredBraces=1 requiredTexts=1; strict=true`nASSERTIONS: pass=$pass fail=0 n/a=$na`nRESULT: PASS`n"
}
$cases=@('baseline','original-A03','missing-b0c','missing-assertions','missing-required','wrong-mvid','wrong-count','wrong-strict','old-run-id','duplicate-pass','braces-zero-passed-one','texts-zero-passed-one','flat-unaccounted-braces','flat-unaccounted-texts','structured-baseline','structured-required-na','structured-empty-object','brace-only','standalone-only','brace-and-standalone','leader-required-na','actual-baseline','actual-legacy')
$parameters=(Get-Command -Name $Verifier).Parameters
$rows=foreach($case in $cases){
    $text=$good
    $expectedRun='same-case-run';$expectedSha='abc123';$expectedMvid='same-case-mvid';$expectedPath='C:\selftest\PatentMarker.dll'
    $requiredBraces=1;$requiredTexts=1
    switch($case){
        'original-A03' {$text=$text -replace '(?m)^\[PASS\] B0c[^\r\n]*\r?\n','' -replace '(?m)^ASSERTIONS:[^\r\n]*\r?\n','' -replace '(?m)^REQUIRED:[^\r\n]*\r?\n','' -replace 'same-case-mvid','deliberately-wrong-mvid'}
        'missing-b0c' {$text=$text -replace '(?m)^\[PASS\] B0c[^\r\n]*\r?\n','' -replace 'pass=13','pass=12'}
        'missing-assertions' {$text=$text -replace '(?m)^ASSERTIONS:[^\r\n]*\r?\n',''}
        'missing-required' {$text=$text -replace '(?m)^REQUIRED:[^\r\n]*\r?\n',''}
        'wrong-mvid' {$text=$text -replace 'same-case-mvid','wrong-mvid'}
        'wrong-count' {$text=$text -replace 'pass=13','pass=12'}
        'wrong-strict' {$text=$text -replace 'strict=true','strict=false'}
        'old-run-id' {$text=$text -replace 'RUN_ID: same-case-run','RUN_ID: old-run'}
        'duplicate-pass' {$text=$text -replace 'ASSERTIONS: pass=13',"[PASS] B9 corner-curves: duplicate`nASSERTIONS: pass=14"}
        'braces-zero-passed-one' {$text=$text -replace 'SUMMARY: braces=1 passed=1','SUMMARY: braces=0 passed=1'}
        'texts-zero-passed-one' {$text=$text -replace '; texts=1 passed=1','; texts=0 passed=1'}
        'flat-unaccounted-braces' {$text=$text -replace 'SUMMARY: braces=1 passed=1','SUMMARY: braces=2 passed=1'}
        'flat-unaccounted-texts' {$text=$text -replace '; texts=1 passed=1','; texts=2 passed=1'}
        {$_ -in @('structured-baseline','structured-required-na','structured-empty-object')} {$text=Structured $case}
        {$_ -in @('brace-only','standalone-only','brace-and-standalone','leader-required-na')} {
            $requiredBraces=if($case -eq 'standalone-only'){0}else{1}
            $requiredTexts=if($case -eq 'brace-only'){0}else{1}
            $header=(($good -split "`r?`n"|Select-Object -First 5) -join "`n").Replace('braces>=1 texts>=1',"braces>=$requiredBraces texts>=$requiredTexts")
            $body='';$passCount=0;$naCount=0
            if($requiredBraces){$body+="`n$separator`nLWPOLYLINE 1`n$braceLines`n=> PASS";$passCount+=10}
            if($requiredTexts){
                $textChecks=($textLines -split "`n"|Where-Object {$_ -match '^\[PASS\] T[12] '}) -join "`n"
                $textHeader=if($case -eq 'leader-required-na'){'LEADER 2'}else{'MTEXT 2 (standalone)'}
                $body+="`n$separator`n$textHeader`n$textChecks`n[N/A] T3 leader-text-retract: no leader";$passCount+=2;$naCount++
            }
            $text=$header+$body+"`n$separator`nSUMMARY: braces=$requiredBraces passed=$requiredBraces failed=0 unreadable=0; texts=$requiredTexts passed=$requiredTexts failed=0 unreadable=0; requiredBraces=$requiredBraces requiredTexts=$requiredTexts; strict=true`nASSERTIONS: pass=$passCount fail=0 n/a=$naCount`nRESULT: PASS`n"
        }
        {$_ -in @('actual-baseline','actual-legacy')} {
            $iteration=Split-Path $OutputDirectory -Leaf;$phase=Split-Path (Split-Path $OutputDirectory -Parent) -Leaf
            $evidence=Split-Path (Split-Path (Split-Path $OutputDirectory -Parent) -Parent) -Parent
            $mode=if($case -eq 'actual-baseline'){'baseline'}else{'legacy-leader'}
            $text=Get-Content (Join-Path $evidence "cases/$phase/$iteration/$mode/PatentMarker-geom-verify.txt") -Raw -Encoding utf8
            $expectedRun=[regex]::Match($text,'(?m)^RUN_ID: (.*)$').Groups[1].Value.Trim()
            $expectedSha=[regex]::Match($text,'(?m)^ASSEMBLY_SHA256: (.*)$').Groups[1].Value.Trim()
            $expectedMvid=[regex]::Match($text,'(?m)^ASSEMBLY_MVID: (.*)$').Groups[1].Value.Trim()
            $expectedPath=[regex]::Match($text,'(?m)^ASSEMBLY_PATH: (.*)$').Groups[1].Value.Trim()
        }
    }
    $file=Join-Path $OutputDirectory ($case+'.txt');[IO.File]::WriteAllText($file,$text,[Text.UTF8Encoding]::new($false))
    $arguments=@('-NoProfile','-File',$Verifier,'-ReportPath',$file,'-RunId',$expectedRun,'-ExpectedDllSha256',$expectedSha,'-ExpectedAssemblyPath',$expectedPath)
    $arguments+=@('-RequireBraces',[string]$requiredBraces,'-RequireTexts',[string]$requiredTexts)
    # MVID argument is supplied only when supported; fixture and oracle stay identical.
    if($parameters.ContainsKey('ExpectedMvid')){$arguments+=@('-ExpectedMvid',$expectedMvid)}
    $psi=[Diagnostics.ProcessStartInfo]::new();$psi.FileName=(Get-Process -Id $PID).Path;$psi.UseShellExecute=$false;$psi.CreateNoWindow=$true;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
    foreach($arg in $arguments){[void]$psi.ArgumentList.Add($arg)}
    $p=[Diagnostics.Process]::Start($psi);$stdout=$p.StandardOutput.ReadToEndAsync();$stderr=$p.StandardError.ReadToEndAsync()
    if(-not $p.WaitForExit(30000)){$p.Kill($true);throw "Report case timed out: $case"}
    $code=$p.ExitCode
    [IO.File]::WriteAllText((Join-Path $OutputDirectory ($case+'.stdout.txt')),$stdout.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $OutputDirectory ($case+'.stderr.txt')),$stderr.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
    $p.Dispose();$expectedAccepted=$case -in @('baseline','structured-baseline','actual-baseline','actual-legacy','brace-only','standalone-only','brace-and-standalone');$actualAccepted=$code -eq 0
    [ordered]@{case=$case;expected_acceptance=$expectedAccepted;actual_acceptance=$actualAccepted;exit_code=$code;result=$(if($expectedAccepted -eq $actualAccepted){'PASS'}else{'FAIL'});command=[ordered]@{executable=$psi.FileName;arguments=$arguments;cwd=$PWD.Path};fixture_sha256=(Get-FileHash $file).Hash.ToLowerInvariant()}
}
$rows|ConvertTo-Json -Depth 6|Set-Content (Join-Path $OutputDirectory 'results.json') -Encoding utf8
$rows|ForEach-Object{Write-Output ($_.case+' '+$_.result+' exit='+$_.exit_code)}
if($rows.result -contains 'FAIL'){exit 1}

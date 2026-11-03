param([string]$ProjectRoot='.')
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ProjectRoot).Path
Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $root 'cad-plugin/Shared/IO/MarkingTextParser.cs')))
$cases=[Collections.Generic.List[object]]::new()
$names=@('支架','壳体X123 支架','壳体1123 支架','壳体AX123 支架')
$separators=@('，','；',',',';','、','。','.')
$gaps=@(' ',"`t",[string][char]0x3000," `t")
$numbers=@('1','12345','S1','1342A','123A1','10-1')
foreach($number in $numbers){foreach($sep in $separators){foreach($gap in $gaps){foreach($form in 0..3){
    $name='支架'
    $text=switch($form){0{$number+$gap+$name+$sep}1{$name+$gap+$number+$sep}2{$name+$gap+'('+$number+')'+$sep}3{$name+$gap+'（'+$number+'）'+$sep}}
    $cases.Add(@(('base-'+$cases.Count),$text,($number+'|'+$name)))
}}}}
foreach($number in @('123','12345','X123')){foreach($name in $names){foreach($sep in $separators){foreach($gap in $gaps){foreach($form in 1..3){
    $text=switch($form){1{$name+$gap+$number+$sep}2{$name+$gap+'('+$number+')'+$sep}3{$name+$gap+'（'+$number+'）'+$sep}}
    $cases.Add(@(('repeat-'+$cases.Count),$text,($number+'|'+$name)))
}}}}}
foreach($number in @('123456','S123456','10-123456')){foreach($sep in $separators){foreach($gap in $gaps){foreach($form in 0..3){
    $name='支架'
    $text=switch($form){0{$number+$gap+$name+$sep}1{$name+$gap+$number+$sep}2{$name+$gap+'('+$number+')'+$sep}3{$name+$gap+'（'+$number+'）'+$sep}}
    $cases.Add(@(('overlength-'+$cases.Count),$text,''))
}}}}
$failed=0
$output=[Collections.Generic.List[string]]::new()
foreach($c in $cases){
    $actual=@([PatentMarker.IO.MarkingTextParser]::ExtractAll([PatentMarker.IO.MarkingTextParser]::Preprocess($c[1]))|ForEach-Object{$_.Number+'|'+$_.Name.Trim()}) -join ';'
    $ok=$actual -ceq $c[2]; if(-not $ok){$failed++}
    $mark=if($ok){'PASS'}else{'FAIL'}
    $output.Add("$mark|$($c[0])|expected=$($c[2])|actual=$actual|input=$($c[1])")
}
$output.Add("SUMMARY|total=$($cases.Count)|failed=$failed")
$output | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'parser-csharp.txt') -Encoding utf8
$flags=[Reflection.BindingFlags]'NonPublic,Public,Static'
$dump=@([PatentMarker.IO.MarkingTextParser].GetFields($flags)|Where-Object{$_.Name -like 'G02_*' -and $_.FieldType -eq [string]}|ForEach-Object{$_.Name+[char]1+[string]$_.GetValue($null)})
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'patterns.txt'),($dump -join "`r`n"),[Text.Encoding]::Unicode)
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'cases.txt'),(($cases|ForEach-Object{$_[0]+[char]1+$_[1]}) -join "`r`n"),[Text.Encoding]::Unicode)
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'expected.txt'),(($cases|ForEach-Object{if($_[2]){'S '+$_[0]+"`t1`t"+$_[2]}else{'S '+$_[0]+"`t0"}}) -join "`r`n"),[Text.UTF8Encoding]::new($false))
"SUMMARY|total=$($cases.Count)|failed=$failed"
if($failed){$output|Where-Object{$_ -like 'FAIL*'}|Select-Object -First 12; exit 1}

param([string]$ProjectRoot)
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $ProjectRoot 'cad-plugin/Shared/IO/MarkingTextParser.cs')))
$cases=@(
 @('number_first_S1','S1底座，','S1'),
 @('number_first_sub','10-1支架，','10-1'),
 @('number_first_six_digits','123456底座，',''),
 @('name_first_six_digits','底座123456、',''),
 @('name_first_six_digits_space','底座 123456、',''),
 @('parenthesized_six_digits','底座(123456)、',''),
 @('bare_six_digits','底座 123456',''),
 @('name_first_five_digits','底座12345、','12345'),
 @('mixed_order','支架2、1底座，','2,1'),
 @('consecutive_name_first_table',"底座`r$([char]7)1`r$([char]7)支架`r$([char]7)2`r$([char]7)",'1,2'),
 @('consecutive_number_first_table',"1`r$([char]7)底座`r$([char]7)2`r$([char]7)支架`r$([char]7)",'1,2')
)
$failed=0
foreach($c in $cases){$prepared=[PatentMarker.IO.MarkingTextParser]::Preprocess($c[1]);$h=[PatentMarker.IO.MarkingTextParser]::ExtractAll($prepared);$actual=(@($h|ForEach-Object Number)-join ',');$ok=$actual -ceq $c[2];if(-not $ok){$failed++};$mark=if($ok){'PASS'}else{'FAIL'};$names=(@($h|ForEach-Object Name)-join ',');"$mark|$($c[0])|expected=$($c[2])|actual=$actual|names=$names"}
'SUMMARY|total='+$cases.Count+'|failed='+$failed
if($failed){exit 1}
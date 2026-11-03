$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$outPath=Join-Path $PSScriptRoot 'word-table-probe.txt'
if(Get-Process -Name WINWORD -ErrorAction SilentlyContinue){'SKIP/BLOCKED|existing_WINWORD|user_instance_not_touched'|Set-Content -LiteralPath $outPath -Encoding utf8;exit 2}
Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $root 'cad-plugin/Shared/IO/MarkingTextParser.cs')))
$app=$null;$doc=$null;$rows=New-Object Collections.Generic.List[string];$failed=0
try{
 $app=New-Object -ComObject Word.Application
 $app.Visible=$false
 foreach($orientation in @('number_first','name_first')){
  $doc=$app.Documents.Add()
  $range=$doc.Range(0,0)
  $table=$doc.Tables.Add($range,2,2)
  $data=if($orientation -eq 'number_first'){@('1','底座','2','支架')}else{@('底座','1','支架','2')}
  for($r=1;$r -le 2;$r++){for($c=1;$c -le 2;$c++){$table.Cell($r,$c).Range.Text=$data[($r-1)*2+($c-1)]}}
  $text=[string]$doc.Content.Text
  $prepared=[PatentMarker.IO.MarkingTextParser]::Preprocess($text)
  $hits=[PatentMarker.IO.MarkingTextParser]::ExtractAll($prepared)
  $actual=(@($hits|ForEach-Object Number)-join ',');$names=(@($hits|ForEach-Object Name)-join ',')
  $ok=($actual -ceq '1,2') -and ($names -ceq '底座,支架');if(-not $ok){$failed++}
  $mark=if($ok){'PASS'}else{'FAIL'}
  $escaped=$text.Replace("`r",'<CR>').Replace("`n",'<LF>').Replace([string][char]7,'<CELL>')
  $rows.Add("$mark|word_native_2x2_$orientation|version=$($app.Version)|expected=1:底座,2:支架|numbers=$actual|names=$names|raw=$escaped")
  $doc.Close(0);$doc=$null
 }
}finally{if($doc){$doc.Close(0)};if($app){$app.Quit(0)}}
$rows.Add('SUMMARY|cases=2|failed='+$failed)
$rows|Set-Content -LiteralPath $outPath -Encoding utf8
$rows
if($failed){exit 1}
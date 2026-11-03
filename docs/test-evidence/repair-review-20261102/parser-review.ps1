param([string]$ProjectRoot)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ProjectRoot).Path
$out = $PSScriptRoot
Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $root 'cad-plugin/Shared/IO/MarkingTextParser.cs')))
$vba = [Text.Encoding]::GetEncoding(936).GetString([IO.File]::ReadAllBytes((Join-Path $root 'vba/Patterns.bas')))
$constants = @{}
$logical = $vba -replace '_\r?\n\s*', ''
foreach ($definition in [regex]::Matches($logical, '(?m)^Public Const (G02_\w+) As String = (.+)$')) {
    $value = ''
    foreach ($token in [regex]::Matches($definition.Groups[2].Value, '"(?:[^"]|"")*"|G02_\w+')) {
        if ($token.Value.StartsWith('"')) { $value += $token.Value.Substring(1, $token.Value.Length - 2).Replace('""','"') }
        else {
            if (-not $constants.ContainsKey($token.Value)) { throw "Unknown constant $($token.Value)" }
            $value += $constants[$token.Value]
        }
    }
    $constants[$definition.Groups[1].Value] = $value
}
$pattern = $constants['G02_P2']
if (-not $pattern) { throw 'Production G02_P2 missing' }
if (-not $vba.Contains('numberOffset = InStr(1, CStr(m.Value), number, vbBinaryCompare)')) { throw 'Boundary expression changed; reassess probe' }
$re = New-Object -ComObject VBScript.RegExp
$re.Global = $true
$re.Pattern = $pattern
$cases = @(
    @('plain', '底座123、', '123|底座'),
    @('digits-in-name', '第2定位件 123、', '123|第2定位件'),
    @('repeat-after-chinese', '第123定位件 123、', '123|第123定位件'),
    @('repeat-after-letter', '壳体X123 支架 123、', '123|壳体X123 支架'),
    @('repeat-after-digit', '壳体1123 支架 123、', '123|壳体1123 支架'),
    @('repeat-prefixed-number', '壳体AX123 支架 X123、', 'X123|壳体AX123 支架'),
    @('overlength', '底座123456、', '')
)
$failed = 0
foreach ($c in $cases) {
    $cs = @([PatentMarker.IO.MarkingTextParser]::ExtractAll([PatentMarker.IO.MarkingTextParser]::Preprocess($c[1])) | ForEach-Object { $_.Number + '|' + $_.Name.Trim() }) -join ';'
    $vb = [Collections.Generic.List[string]]::new()
    $trace = [Collections.Generic.List[string]]::new()
    $matches = $re.Execute($c[1])
    for ($i = 0; $i -lt $matches.Count; $i++) {
        $m = $matches.Item($i)
        $name = [string]$m.SubMatches.Item(0)
        $number = [string]$m.SubMatches.Item(1)
        # Exact semantics of production InStr(1,...,vbBinaryCompare), zero-based here.
        $offset = ([string]$m.Value).IndexOf($number, [StringComparison]::Ordinal)
        $start = [int]$m.FirstIndex + $offset
        $before = if ($start -le 0) { '' } else { $c[1].Substring($start - 1, 1) }
        $accept = $start -le 0 -or $before -cnotmatch '[0-9A-Za-z-]'
        $trace.Add("capture=$number/$name;first-occurrence=$offset;preceding=$before;accepted=$accept")
        if ($accept) { $vb.Add($number + '|' + $name.Trim()) }
    }
    $actual = $vb -join ';'
    foreach ($engine in @(@('csharp-production',$cs), @('vbscript-production-boundary',$actual))) {
        $ok = $engine[1] -ceq $c[2]
        if (-not $ok) { $failed++ }
        $mark = if ($ok) { 'PASS' } else { 'FAIL' }
        "$mark|$($c[0])|engine=$($engine[0])|expected=$($c[2])|actual=$($engine[1])|trace=$($trace -join ';')"
    }
}
$flags=[Reflection.BindingFlags]'NonPublic,Public,Static'
$dump=[Collections.Generic.List[string]]::new()
foreach($key in $constants.Keys){
    $field=[PatentMarker.IO.MarkingTextParser].GetField($key,$flags)
    if($null -ne $field){$dump.Add($key+[char]1+[string]$field.GetValue($null))}
}
[IO.File]::WriteAllText((Join-Path $out 'patterns.txt'),($dump -join "`r`n"),[Text.Encoding]::Unicode)
[IO.File]::WriteAllText((Join-Path $out 'cases.txt'),(($cases|ForEach-Object{$_[0]+[char]1+$_[1]}) -join "`r`n"),[Text.Encoding]::Unicode)
[IO.File]::WriteAllText((Join-Path $out 'expected.txt'),(($cases|ForEach-Object{if($_[2]){'S '+$_[0]+"`t1`t"+$_[2]}else{'S '+$_[0]+"`t0"}}) -join "`r`n"),[Text.UTF8Encoding]::new($false))
"SUMMARY|total=$($cases.Count * 2)|failed=$failed"
if ($failed) { exit 1 }

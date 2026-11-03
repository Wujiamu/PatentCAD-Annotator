# Extract the pre-fix (baseline) pattern text from the frozen red-source VBA files
# and emit it in the same "<name> U+0001 <pattern>" format the green C# dump uses.
#
# This exists so the red VBScript run uses the REAL pre-fix pattern text instead of
# hand-copied strings. In the pre-fix sources the patterns are inline string literals
# inside the CollectHits calls, the re3.pattern assignment, the patterns = Array(...)
# block and the re.pattern assignment (there are no G02_* constants yet), so the
# literals are read straight out of those call sites.
#
# Output: UTF-16LE, one "<name> U+0001 <pattern>" per line.

param(
    [Parameter(Mandatory = $true)][string]$VbaSourceDir,
    [Parameter(Mandatory = $true)][string]$OutputFile
)

$ErrorActionPreference = "Stop"
$enc = [Text.Encoding]::GetEncoding(936)

function Read-Cp936([string]$path) {
    [IO.File]::ReadAllText($path, $enc) -replace "`r`n", "`n"
}

$patterns = Read-Cp936 (Join-Path $VbaSourceDir "Patterns.bas")
$dict = Read-Cp936 (Join-Path $VbaSourceDir "DictModel.bas")

$out = New-Object System.Collections.Generic.List[string]

# --- CollectHits patterns (4) ---
# Each call spans a continuation line: CollectHits a, b, text, _
#                                     "<literal>", <bool>
$collect = [regex]::Matches($patterns,
    'CollectHits\s+[^\n]*_\s*\n\s*"((?:[^"\n])*)"\s*,\s*(?:False|True)')
if ($collect.Count -ne 4) {
    throw "Expected 4 CollectHits pattern literals in Patterns.bas, found $($collect.Count)"
}
$names = @("G02_P1", "G02_PB", "G02_P2", "G02_PA")
for ($i = 0; $i -lt 4; $i++) { $out.Add($names[$i] + [char]1 + $collect[$i].Groups[1].Value) }

# --- Bare-list pattern ---
$bare = [regex]::Match($patterns, 're3\.pattern\s*=\s*"((?:[^"\n])*)"')
if (-not $bare.Success) { throw "re3.pattern literal not found in Patterns.bas" }
$out.Add("G02_P3" + [char]1 + $bare.Groups[1].Value)

# --- Marking header patterns (6) ---
$hdrBlock = [regex]::Match($dict, 'patterns\s*=\s*Array\(\s*_\s*\n((?:[^\n]*\n){6})')
if (-not $hdrBlock.Success) { throw "patterns = Array(...) block not found in DictModel.bas" }
$hdrLines = $hdrBlock.Groups[1].Value -split "`n"
if ($hdrLines.Count -lt 6) { throw "Expected 6 header lines, found $($hdrLines.Count)" }
for ($i = 0; $i -lt 6; $i++) {
    $lit = [regex]::Match($hdrLines[$i], '"((?:[^"\n])*)"')
    if (-not $lit.Success) { throw "Header literal $i not found" }
    $out.Add("G02_HDR$i" + [char]1 + $lit.Groups[1].Value)
}

# --- Section end expression (pre-fix; still contains \Z = the R-032 defect) ---
$sec = [regex]::Match($dict, 're\.pattern\s*=\s*"((?:[^"\n])*)"')
if (-not $sec.Success) { throw "section-end literal not found in DictModel.bas" }
$out.Add("G02_SECTIONEND" + [char]1 + $sec.Groups[1].Value)

# --- Table stream patterns (2), each spread over a continuation line ---
$table = [regex]::Matches($dict,
    'reTbl\.pattern\s*=\s*"((?:[^"\n])*)"\s*&\s*vbCr\s*&\s*Chr\(7\)\s*&\s*_\s*\n\s*"((?:[^"\n])*)"')
if ($table.Count -ne 2) { throw "Expected 2 table patterns in DictModel.bas, found $($table.Count)" }
$cr = [string][char]13
$bel = [string][char]7
$out.Add("G02_TABLEA" + [char]1 + $table[0].Groups[1].Value + $cr + $bel + $table[0].Groups[2].Value + $cr + $bel)
$out.Add("G02_TABLEB" + [char]1 + $table[1].Groups[1].Value + $cr + $bel + $table[1].Groups[2].Value + $cr + $bel)

[IO.File]::WriteAllText($OutputFile, (($out -join "`r`n") + "`r`n"), [Text.Encoding]::Unicode)
Write-Output "wrote $($out.Count) legacy patterns to $OutputFile"
foreach ($line in $out) {
    $parts = $line.Split([char]1)
    Write-Output ("  " + $parts[0] + " = [" + ($parts[1] -replace "`r", "<CR>" -replace [string][char]7, "<BEL>") + "]")
}

$dirs = @("PatentMarker-2007-deploy","PatentMarker-2007-v2","PatentMarker-2010-deploy","PatentMarker-2013-deploy","PatentMarker-2015-deploy","PatentMarker-2025-deploy")
$gbk = [System.Text.Encoding]::GetEncoding(936)
$out = New-Object System.Text.StringBuilder
foreach ($d in $dirs) {
    $p = "c:\Users\wjm\WorkBuddy\2026-06-20-00-50-28\$d\vba\Patterns.bas"
    if (Test-Path $p) {
        $bytes = [System.IO.File]::ReadAllBytes($p)
        $text = $gbk.GetString($bytes)
        $lines = $text -split "`r?`n"
        $found = $false
        foreach ($line in $lines) {
            if ($line -match '\\d\{1,5\}') {
                [void]$out.AppendLine("${d} PATTERN1: " + $line.Trim())
                $found = $true
                break
            }
        }
        if (-not $found) { [void]$out.AppendLine("${d} PATTERN1: NOT-FOUND") }
    } else {
        [void]$out.AppendLine("${d}: MISSING")
    }
}
[System.IO.File]::WriteAllText("c:\Users\wjm\WorkBuddy\2026-06-20-00-50-28\patterns-compare.txt", $out.ToString(), (New-Object System.Text.UTF8Encoding($true)))
Write-Output "done"

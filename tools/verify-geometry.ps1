param(
    [string]$PackageDirectory = "",
    [string]$Drawing = "",
    [string]$AcCoreConsole = "",
    [string]$OutputRoot = "",
    [string]$ReportPath = "",
    [string]$ExpectedDllSha256 = "",
    [string]$ExpectedAssemblyPath = "",
    [string]$ExpectedMvid = "",
    [string]$RunId = "",
    [int]$RequireBraces = 1,
    [int]$RequireTexts = 1,
    [int]$TimeoutSeconds = 300,
    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function New-RunId {
    return (Get-Date -Format "yyyyMMdd-HHmmss") + "-" + ([guid]::NewGuid().ToString("N").Substring(0, 8))
}

function Read-ReportField([string[]]$Lines, [string]$Name) {
    $prefix = $Name + ":"
    foreach ($line in $Lines) {
        if ($line.StartsWith($prefix, [StringComparison]::Ordinal)) {
            return $line.Substring($prefix.Length).Trim()
        }
    }
    return ""
}


function Read-ManifestValue([string[]]$Lines, [string]$Key) {
    $prefix = $Key + "="
    foreach ($line in $Lines) {
        if ($line.StartsWith($prefix, [StringComparison]::Ordinal)) {
            return $line.Substring($prefix.Length).Trim()
        }
    }
    return ""
}


function Test-GeometryReport {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedRunId,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256,
        [string]$ExpectedAssemblyPath = "",
        [string]$ExpectedMvid = "",
        [int]$ExpectedBraces = 1,
        [int]$ExpectedTexts = 1
    )
    if (-not (Test-Path -LiteralPath $Path)) { throw "report missing: $Path" }
    $lines = Get-Content -Encoding utf8 $Path
    $actualRunId = Read-ReportField $lines "RUN_ID"
    if ($actualRunId -ne $ExpectedRunId) { throw "report run ID mismatch: expected '$ExpectedRunId' actual '$actualRunId'" }
    $actualSha = Read-ReportField $lines "ASSEMBLY_SHA256"
    $actualAssemblyPath = Read-ReportField $lines "ASSEMBLY_PATH"
    if (-not [string]::IsNullOrEmpty($ExpectedAssemblyPath)) {
        $expectedFull = [IO.Path]::GetFullPath($ExpectedAssemblyPath)
        $actualFull = [IO.Path]::GetFullPath($actualAssemblyPath)
        if (-not [string]::Equals($expectedFull, $actualFull, [StringComparison]::OrdinalIgnoreCase)) {
            throw "loaded assembly path mismatch: expected '$expectedFull' actual '$actualFull'"
        }
    }
    if ([string]::IsNullOrEmpty($ExpectedSha256)) { throw "expected DLL SHA-256 was not supplied" }
    if (-not [string]::Equals($actualSha, $ExpectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "loaded DLL SHA-256 mismatch: expected '$ExpectedSha256' actual '$actualSha'"
    }
    if ([string]::IsNullOrEmpty($ExpectedMvid)) { throw "expected DLL MVID was not supplied" }
    $actualMvid = Read-ReportField $lines "ASSEMBLY_MVID"
    if (-not [string]::Equals($actualMvid, $ExpectedMvid, [StringComparison]::OrdinalIgnoreCase)) {
        throw "loaded DLL MVID mismatch: expected '$ExpectedMvid' actual '$actualMvid'"
    }
    $result = Read-ReportField $lines "RESULT"
    if ($result -ne "PASS") { throw "report RESULT is '$result', not PASS" }
    $summary = Read-ReportField $lines "SUMMARY"
    if ([string]::IsNullOrEmpty($summary)) { throw "report SUMMARY missing" }
    if ($summary -notmatch "braces=(\d+) passed=(\d+) failed=(\d+) unreadable=(\d+)") {
        throw "report SUMMARY cannot be parsed: $summary"
    }
    $braces = [int]$matches[1]
    $bracesPassed = [int]$matches[2]
    $bracesFailed = [int]$matches[3]
    $bracesUnreadable = [int]$matches[4]
    if ($bracesFailed -ne 0 -or $bracesUnreadable -ne 0) { throw "brace failures/unreadable present in SUMMARY" }
    if ($bracesPassed -lt $ExpectedBraces) { throw "required brace count missing: passed=$bracesPassed expected>=$ExpectedBraces" }
    if ($summary -notmatch "texts=(\d+) passed=(\d+) failed=(\d+) unreadable=(\d+)") {
        throw "text counts missing from SUMMARY: $summary"
    }
    $texts = [int]$matches[1]
    $textsPassed = [int]$matches[2]
    $textsFailed = [int]$matches[3]
    $textsUnreadable = [int]$matches[4]
    if ($textsFailed -ne 0 -or $textsUnreadable -ne 0) { throw "text failures/unreadable present in SUMMARY" }
    if ($textsPassed -lt $ExpectedTexts) { throw "required text count missing: passed=$textsPassed expected>=$ExpectedTexts" }
    if (($bracesPassed + $bracesFailed + $bracesUnreadable) -ne $braces) {
        throw "brace totals contradict SUMMARY: total=$braces passed=$bracesPassed failed=$bracesFailed unreadable=$bracesUnreadable"
    }
    if (($textsPassed + $textsFailed + $textsUnreadable) -ne $texts) {
        throw "text totals contradict SUMMARY: total=$texts passed=$textsPassed failed=$textsFailed unreadable=$textsUnreadable"
    }
    $requiredLine = Read-ReportField $lines "REQUIRED"
    if ($requiredLine -notmatch '^braces>=(\d+)\s+texts>=(\d+)\s+strict=(true|false)$') {
        throw "report REQUIRED line cannot be parsed: $requiredLine"
    }
    if ([int]$matches[1] -ne $ExpectedBraces -or [int]$matches[2] -ne $ExpectedTexts) {
        throw "report REQUIRED counts do not match requested: $requiredLine"
    }
    if ($matches[3] -ne "true") { throw "report is not a strict run: $requiredLine" }

    $assertionsLine = Read-ReportField $lines "ASSERTIONS"
    if ($assertionsLine -notmatch '^pass=(\d+)\s+fail=(\d+)\s+n/a=(\d+)$') {
        throw "report ASSERTIONS line cannot be parsed: $assertionsLine"
    }
    $declaredPass = [int]$matches[1]
    $declaredFail = [int]$matches[2]
    $declaredNa = [int]$matches[3]

    $reportText = $lines -join "`n"
    if ($reportText -match "\[FAIL\]") { throw "report contains [FAIL]" }
    $actualPass = ([regex]::Matches($reportText, '\[PASS\]')).Count
    $actualFail = ([regex]::Matches($reportText, '\[FAIL\]')).Count
    $actualNa = ([regex]::Matches($reportText, '\[N/A\]')).Count
    if ($declaredPass -ne $actualPass -or $declaredFail -ne $actualFail -or $declaredNa -ne $actualNa) {
        throw "ASSERTIONS counts do not match report lines: declared=$declaredPass/$declaredFail/$declaredNa actual=$actualPass/$actualFail/$actualNa"
    }
    $blockItems = New-Object System.Collections.Generic.List[object]
    $currentBlock = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ($line.Trim() -eq "-----------------------------------------------------") {
            if ($currentBlock.Count -gt 0) {
                [void]$blockItems.Add($currentBlock.ToArray())
                $currentBlock.Clear()
            }
        }
        else {
            $currentBlock.Add($line)
        }
    }
    if ($currentBlock.Count -gt 0) { [void]$blockItems.Add($currentBlock.ToArray()) }

    $structured = $false
    $braceBlocks = 0
    $textBlocks = 0
    $unreadableBlocks = 0
    $bracePass = 0
    $braceFail = 0
    $textPass = 0
    $textFail = 0
    $textUnreadable = 0
    foreach ($block in $blockItems) {
        $ids = @()
        foreach ($line in $block) {
            if ($line -match '^\s*\[(PASS|FAIL|N/A)\]\s+([A-Za-z0-9]+)') { $ids += $matches[2] }
        }
        $dupes = @($ids | Group-Object | Where-Object { $_.Count -gt 1 })
        $header = @($block | Where-Object { $_.Trim() -ne "" } | Select-Object -First 1)
        $headerText = if ($header.Count -gt 0) { [string]$header[0] } else { "" }
        if ($dupes.Count -gt 0) {
            throw "duplicate assertion $(($dupes | ForEach-Object { $_.Name }) -join ',') within object block '$headerText'"
        }
        $blockText = $block -join "`n"
        $idSet = @($block | ForEach-Object {
            if ($_ -match '^\s*\[PASS\]\s+([A-Za-z0-9]+)\s') { $matches[1] }
        })
        if ($headerText -match '^(LWPOLYLINE|MLEADER|MTEXT|LEADER|UNREADABLE OBJECT)\b') { $structured = $true }
        if ($headerText -match '^LWPOLYLINE\b') {
            foreach ($requiredId in @("B0c","B1","B2","B3","B4","B5","B6","B7","B8","B9")) {
                if ($idSet -notcontains $requiredId) { throw "brace object block missing assertion $requiredId" }
            }
            $braceBlocks++
            if ($blockText -match '(?m)^\s*=> PASS\s*$') { $bracePass++ }
            elseif ($blockText -match '(?m)^\s*=> FAIL\s*$') { $braceFail++ }
        }
        elseif ($headerText -match '^MLEADER\b') {
            foreach ($requiredId in @("T1","T2","T3","T3a","T3b","T3c","T3d")) {
                if ($idSet -notcontains $requiredId) { throw "MLEADER object block missing assertion $requiredId" }
            }
            $textBlocks++
            if ($blockText -match '\[FAIL\] T0 ') { $textUnreadable++ }
            elseif ($blockText -match '\[FAIL\]') { $textFail++ }
            else { $textPass++ }
        }
        elseif ($headerText -match '^LEADER\b') {
            foreach ($requiredId in @("T1","T2","T3")) {
                if ($idSet -notcontains $requiredId) { throw "LEADER object block missing assertion $requiredId" }
            }
            $textBlocks++
            if ($blockText -match '\[FAIL\] T0 ') { $textUnreadable++ }
            elseif ($blockText -match '\[FAIL\]') { $textFail++ }
            else { $textPass++ }
        }
        elseif ($headerText -match '^MTEXT\b') {
            foreach ($requiredId in @("T1","T2")) {
                if ($idSet -notcontains $requiredId) { throw "MTEXT object block missing assertion $requiredId" }
            }
            $textBlocks++
            if ($blockText -match '\[FAIL\] T0 ') { $textUnreadable++ }
            elseif ($blockText -match '\[FAIL\]') { $textFail++ }
            else { $textPass++ }
        }
        elseif ($headerText -match '^UNREADABLE OBJECT\b') {
            $unreadableBlocks++
        }
    }
    if ($structured) {
        if ($braceBlocks -ne $braces) { throw "brace object blocks=$braceBlocks do not match SUMMARY total=$braces" }
        if ($textBlocks -ne $texts) { throw "text object blocks=$textBlocks do not match SUMMARY total=$texts" }
        if ($bracePass -ne $bracesPassed -or $braceFail -ne $bracesFailed -or ($bracePass + $braceFail) -ne $braceBlocks) {
            throw "brace PASS/FAIL blocks=$bracePass/$braceFail do not match SUMMARY passed=$bracesPassed failed=$bracesFailed blocks=$braceBlocks"
        }
        if ($textPass -ne $textsPassed -or $textFail -ne $textsFailed -or $textUnreadable -ne $textsUnreadable -or ($textPass + $textFail + $textUnreadable) -ne $textBlocks) {
            throw "text PASS/FAIL/UNREADABLE blocks=$textPass/$textFail/$textUnreadable do not match SUMMARY passed=$textsPassed failed=$textsFailed unreadable=$textsUnreadable blocks=$textBlocks"
        }
        if ($unreadableBlocks -ne $bracesUnreadable) {
            throw "unreadable object blocks=$unreadableBlocks do not match SUMMARY unreadable=$bracesUnreadable"
        }
    }
    else {
        # Compatibility reports have no object headers, so standalone text
        # applicability cannot be established: retain the leader T3 gate.
        $requiredIds = @()
        if ($braces -gt 0) { $requiredIds += @('B0c','B1','B2','B3','B4','B5','B6','B7','B8','B9') }
        if ($texts -gt 0) { $requiredIds += @('T1','T2','T3') }
        foreach ($id in $requiredIds) {
            if ($reportText -notmatch ('(?m)^\s*\[PASS\]\s+' + $id + '\s')) { throw "$id PASS assertion missing" }
        }
    }
    return [pscustomobject]@{
        RunId = $actualRunId
        AssemblyPath = $actualAssemblyPath
        Sha256 = $actualSha
        Mvid = $actualMvid
        Braces = $braces
        Texts = $textsPassed
        Summary = $summary
    }
}
function Resolve-AcCoreConsole {
    if (-not [string]::IsNullOrEmpty($AcCoreConsole)) {
        if (-not (Test-Path -LiteralPath $AcCoreConsole)) { throw "accoreconsole.exe not found: $AcCoreConsole" }
        return (Resolve-Path $AcCoreConsole).Path
    }
    $candidates = @(
        (Join-Path $env:ProgramFiles "Autodesk\AutoCAD 2026\accoreconsole.exe"),
        (Join-Path $env:ProgramFiles "Autodesk\AutoCAD 2025\accoreconsole.exe"),
        (Join-Path $env:ProgramFiles "Autodesk\AutoCAD 2024\accoreconsole.exe"),
        (Join-Path $env:ProgramFiles "Autodesk\AutoCAD 2023\accoreconsole.exe")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return ""
}

if ($SelfTest) {
    $dir = Join-Path $env:TEMP ("PatentMarker-geometry-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $dir | Out-Null
    $good = Join-Path $dir "good.txt"
    @"
PATGEOMVERIFY - geometry regression verification
RUN_ID: selftest-run
ASSEMBLY_SHA256: abc123
ASSEMBLY_MVID: selftest-mvid
ASSEMBLY_PATH: C:\selftest\PatentMarker.dll
REQUIRED: braces>=1 texts>=1 strict=true
  [PASS] B0c planar-contract: ok
  [PASS] B1 no-axis-crossing: 0
  [PASS] B2 tip-at-full-width: ok
  [PASS] B3 stems-at-half-width: ok
  [PASS] B4 within-selected-width: ok
  [PASS] B5 endpoints-exact: ok
  [PASS] B6 cusp-at-half-height: ok
  [PASS] B7 mirror-symmetry: ok
  [PASS] B8 single-cusp-no-self-intersection: ok
  [PASS] B9 corner-curves: ok
  [PASS] T1 text-horizontal: 0
  [PASS] T2 text-x-direction: ok
  [PASS] T3 leader-text-retract: ok
SUMMARY: braces=1 passed=1 failed=0 unreadable=0; texts=1 passed=1 failed=0 unreadable=0; requiredBraces=1 requiredTexts=1; strict=true
ASSERTIONS: pass=13 fail=0 n/a=0
RESULT: PASS
"@ | Set-Content -Encoding utf8 $good
    $result = Test-GeometryReport -Path $good -ExpectedRunId "selftest-run" -ExpectedSha256 "abc123" -ExpectedAssemblyPath "C:\selftest\PatentMarker.dll" -ExpectedMvid "selftest-mvid" -ExpectedBraces 1 -ExpectedTexts 1
    if ($result.Braces -ne 1 -or $result.Texts -ne 1) { throw "self-test parsed counts incorrectly" }

    $bad = Join-Path $dir "bad.txt"
    (Get-Content -Encoding utf8 $good) -replace "RESULT: PASS", "RESULT: FAIL" | Set-Content -Encoding utf8 $bad
    try { Test-GeometryReport -Path $bad -ExpectedRunId "selftest-run" -ExpectedSha256 "abc123" -ExpectedMvid "selftest-mvid" | Out-Null; throw "FAIL report was accepted" }
    catch { if ($_.Exception.Message -eq "FAIL report was accepted") { throw } }

    $wrongRun = Join-Path $dir "wrong-run.txt"
    (Get-Content -Encoding utf8 $good) -replace "RUN_ID: selftest-run", "RUN_ID: stale-run" | Set-Content -Encoding utf8 $wrongRun
    try { Test-GeometryReport -Path $wrongRun -ExpectedRunId "selftest-run" -ExpectedSha256 "abc123" -ExpectedMvid "selftest-mvid" | Out-Null; throw "stale run was accepted" }
    catch { if ($_.Exception.Message -eq "stale run was accepted") { throw } }

    $wrongHash = Join-Path $dir "wrong-hash.txt"
    (Get-Content -Encoding utf8 $good) -replace "ASSEMBLY_SHA256: abc123", "ASSEMBLY_SHA256: deadbeef" | Set-Content -Encoding utf8 $wrongHash
    try { Test-GeometryReport -Path $wrongHash -ExpectedRunId "selftest-run" -ExpectedSha256 "abc123" -ExpectedMvid "selftest-mvid" | Out-Null; throw "wrong DLL hash was accepted" }
    catch { if ($_.Exception.Message -eq "wrong DLL hash was accepted") { throw } }

    $wrongPath = Join-Path $dir "wrong-path.txt"
    (Get-Content -Encoding utf8 $good) -replace [regex]::Escape("ASSEMBLY_PATH: C:\selftest\PatentMarker.dll"), "ASSEMBLY_PATH: C:\selftest\Other.dll" | Set-Content -Encoding utf8 $wrongPath
    try { Test-GeometryReport -Path $wrongPath -ExpectedRunId "selftest-run" -ExpectedSha256 "abc123" -ExpectedAssemblyPath "C:\selftest\PatentMarker.dll" -ExpectedMvid "selftest-mvid" | Out-Null; throw "wrong assembly path was accepted" }
    catch { if ($_.Exception.Message -eq "wrong assembly path was accepted") { throw } }
    $missingB9 = Join-Path $dir "missing-b9.txt"
    (Get-Content -Encoding utf8 $good) | Where-Object { $_ -notmatch '\[PASS\] B9' } | Set-Content -Encoding utf8 $missingB9
    try { Test-GeometryReport -Path $missingB9 -ExpectedRunId "selftest-run" -ExpectedSha256 "abc123" -ExpectedMvid "selftest-mvid" | Out-Null; throw "missing B9 was accepted" }
    catch { if ($_.Exception.Message -eq "missing B9 was accepted") { throw } }

    $badBraceTotals = Join-Path $dir "bad-brace-totals.txt"
    (Get-Content -Encoding utf8 $good) -replace "braces=1 passed=1", "braces=0 passed=1" | Set-Content -Encoding utf8 $badBraceTotals
    try { Test-GeometryReport -Path $badBraceTotals -ExpectedRunId "selftest-run" -ExpectedSha256 "abc123" -ExpectedAssemblyPath "C:\selftest\PatentMarker.dll" -ExpectedMvid "selftest-mvid" -ExpectedBraces 1 -ExpectedTexts 1 | Out-Null; throw "contradictory brace totals were accepted" }
    catch { if ($_.Exception.Message -eq "contradictory brace totals were accepted") { throw } }

    $badTextTotals = Join-Path $dir "bad-text-totals.txt"
    (Get-Content -Encoding utf8 $good) -replace "; texts=1 passed=1", "; texts=0 passed=1" | Set-Content -Encoding utf8 $badTextTotals
    try { Test-GeometryReport -Path $badTextTotals -ExpectedRunId "selftest-run" -ExpectedSha256 "abc123" -ExpectedAssemblyPath "C:\selftest\PatentMarker.dll" -ExpectedMvid "selftest-mvid" -ExpectedBraces 1 -ExpectedTexts 1 | Out-Null; throw "contradictory text totals were accepted" }
    catch { if ($_.Exception.Message -eq "contradictory text totals were accepted") { throw } }

    $duplicatePass = Join-Path $dir "duplicate-pass.txt"
    (Get-Content -Encoding utf8 $good) -replace "ASSERTIONS: pass=13", "[PASS] B9 corner-curves: duplicate`nASSERTIONS: pass=14" | Set-Content -Encoding utf8 $duplicatePass
    try { Test-GeometryReport -Path $duplicatePass -ExpectedRunId "selftest-run" -ExpectedSha256 "abc123" -ExpectedAssemblyPath "C:\selftest\PatentMarker.dll" -ExpectedMvid "selftest-mvid" -ExpectedBraces 1 -ExpectedTexts 1 | Out-Null; throw "duplicate PASS was accepted" }
    catch { if ($_.Exception.Message -eq "duplicate PASS was accepted") { throw } }

    $wrongMvid = Join-Path $dir "wrong-mvid.txt"
    (Get-Content -Encoding utf8 $good) -replace "ASSEMBLY_MVID: selftest-mvid", "ASSEMBLY_MVID: deliberately-wrong-mvid" | Set-Content -Encoding utf8 $wrongMvid
    try { Test-GeometryReport -Path $wrongMvid -ExpectedRunId "selftest-run" -ExpectedSha256 "abc123" -ExpectedMvid "selftest-mvid" | Out-Null; throw "wrong DLL MVID was accepted" }
    catch { if ($_.Exception.Message -eq "wrong DLL MVID was accepted") { throw } }

    $missingB0c = Join-Path $dir "missing-b0c.txt"
    (Get-Content -Encoding utf8 $good) | Where-Object { $_ -notmatch '\[PASS\] B0c' } | ForEach-Object { $_ -replace "ASSERTIONS: pass=13", "ASSERTIONS: pass=12" } | Set-Content -Encoding utf8 $missingB0c
    try { Test-GeometryReport -Path $missingB0c -ExpectedRunId "selftest-run" -ExpectedSha256 "abc123" -ExpectedMvid "selftest-mvid" | Out-Null; throw "missing B0c was accepted" }
    catch { if ($_.Exception.Message -eq "missing B0c was accepted") { throw } }

    $missingAssertions = Join-Path $dir "missing-assertions.txt"
    (Get-Content -Encoding utf8 $good) | Where-Object { $_ -notmatch '^ASSERTIONS:' } | Set-Content -Encoding utf8 $missingAssertions
    try { Test-GeometryReport -Path $missingAssertions -ExpectedRunId "selftest-run" -ExpectedSha256 "abc123" -ExpectedMvid "selftest-mvid" | Out-Null; throw "missing ASSERTIONS was accepted" }
    catch { if ($_.Exception.Message -eq "missing ASSERTIONS was accepted") { throw } }

    $wrongCount = Join-Path $dir "wrong-assertion-count.txt"
    (Get-Content -Encoding utf8 $good) -replace "ASSERTIONS: pass=13", "ASSERTIONS: pass=12" | Set-Content -Encoding utf8 $wrongCount
    try { Test-GeometryReport -Path $wrongCount -ExpectedRunId "selftest-run" -ExpectedSha256 "abc123" -ExpectedMvid "selftest-mvid" | Out-Null; throw "wrong assertion count was accepted" }
    catch { if ($_.Exception.Message -eq "wrong assertion count was accepted") { throw } }

    $wrongRequired = Join-Path $dir "wrong-required.txt"
    (Get-Content -Encoding utf8 $good) -replace "REQUIRED: braces>=1 texts>=1 strict=true", "REQUIRED: braces>=0 texts>=1 strict=true" | Set-Content -Encoding utf8 $wrongRequired
    try { Test-GeometryReport -Path $wrongRequired -ExpectedRunId "selftest-run" -ExpectedSha256 "abc123" -ExpectedMvid "selftest-mvid" | Out-Null; throw "wrong REQUIRED counts were accepted" }
    catch { if ($_.Exception.Message -eq "wrong REQUIRED counts were accepted") { throw } }

    $nonStrict = Join-Path $dir "non-strict.txt"
    (Get-Content -Encoding utf8 $good) -replace "REQUIRED: braces>=1 texts>=1 strict=true", "REQUIRED: braces>=1 texts>=1 strict=false" | Set-Content -Encoding utf8 $nonStrict
    try { Test-GeometryReport -Path $nonStrict -ExpectedRunId "selftest-run" -ExpectedSha256 "abc123" -ExpectedMvid "selftest-mvid" | Out-Null; throw "non-strict report was accepted" }
    catch { if ($_.Exception.Message -eq "non-strict report was accepted") { throw } }

    Write-Output "PASS self-test: good report accepted; stale/Fail/wrong-hash/wrong-path/wrong-mvid/missing-B0c/missing-ASSERTIONS/wrong-count/wrong-required/non-strict/bad-totals/duplicate-PASS/missing-B9 reports rejected"
    exit 0
}

if ([string]::IsNullOrEmpty($ReportPath)) {
    if ([string]::IsNullOrEmpty($PackageDirectory)) { throw "PackageDirectory is required unless -ReportPath or -SelfTest is used" }
    if ([string]::IsNullOrEmpty($Drawing)) { throw "Drawing is required for a host run" }
    $package = (Resolve-Path $PackageDirectory).Path
    $dll = Join-Path $package "PatentMarker.dll"
    $manifest = Join-Path $package "PatentMarker-release.txt"
    if (-not (Test-Path -LiteralPath $dll)) { throw "deployment DLL missing: $dll" }
    if (-not (Test-Path -LiteralPath $manifest)) { throw "release manifest missing: $manifest" }
    $manifestLines = Get-Content -Encoding utf8 $manifest
    $ExpectedDllSha256 = Read-ManifestValue $manifestLines "artifact.sha256"
    if ([string]::IsNullOrEmpty($ExpectedDllSha256)) { throw "manifest has no artifact.sha256" }
    $ExpectedMvid = Read-ManifestValue $manifestLines "artifact.mvid"
    if ([string]::IsNullOrEmpty($ExpectedMvid)) { throw "manifest has no artifact.mvid" }
    $dllHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $dll).Hash.ToLowerInvariant()
    if ($dllHash -ne $ExpectedDllSha256.ToLowerInvariant()) { throw "deployment DLL does not match its manifest" }
    $ExpectedAssemblyPath = (Resolve-Path $dll).Path
}

if ([string]::IsNullOrEmpty($RunId)) { $RunId = New-RunId }
if ([string]::IsNullOrEmpty($ExpectedDllSha256)) { throw "ExpectedDllSha256 is required with -ReportPath" }
if ([string]::IsNullOrEmpty($ExpectedMvid)) { throw "ExpectedMvid is required with -ReportPath" }

if (-not [string]::IsNullOrEmpty($ReportPath)) {
    $checked = Test-GeometryReport -Path $ReportPath -ExpectedRunId $RunId `
        -ExpectedSha256 $ExpectedDllSha256 -ExpectedAssemblyPath $ExpectedAssemblyPath -ExpectedMvid $ExpectedMvid -ExpectedBraces $RequireBraces -ExpectedTexts $RequireTexts
    $checked | ConvertTo-Json -Depth 4
    exit 0
}

if ([string]::IsNullOrEmpty($OutputRoot)) {
    $OutputRoot = Join-Path $env:TEMP ("PatentMarker-geometry-" + $RunId)
}
$runDir = Join-Path $OutputRoot $RunId
if (Test-Path -LiteralPath $runDir) { throw "run directory already exists: $runDir" }
New-Item -ItemType Directory -Path $runDir -Force | Out-Null

$accore = Resolve-AcCoreConsole
if ([string]::IsNullOrEmpty($accore)) {
    Write-Output "SKIP/BLOCKED: accoreconsole.exe not found. Supply -AcCoreConsole or run on an AutoCAD host."
    exit 3
}
if (-not (Test-Path -LiteralPath $accore)) { throw "accoreconsole.exe not found: $accore" }
if (-not (Test-Path -LiteralPath $Drawing)) { throw "drawing not found: $Drawing" }

$drawingPath = (Resolve-Path $Drawing).Path
$drawingSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $drawingPath).Hash.ToLowerInvariant()
$manifestSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $manifest).Hash.ToLowerInvariant()
$reportDir = Join-Path $runDir "report"
New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
$reportFile = Join-Path $reportDir "PatentMarker-geom-verify.txt"
if (Test-Path -LiteralPath $reportFile) { throw "report already exists before run: $reportFile" }
$accoreStdout = Join-Path $runDir "accoreconsole.stdout.txt"
$accoreStderr = Join-Path $runDir "accoreconsole.stderr.txt"

$scr = Join-Path $runDir "run.scr"
@('FILEDIA 0', ('NETLOAD "' + $ExpectedAssemblyPath + '"'), 'PATGEOMVERIFY', 'QUIT', 'Y') | Set-Content -Encoding ascii $scr

$oldRunId = $env:PATGEOM_RUN_ID
$oldReportDir = $env:PATGEOM_REPORT_DIR
$oldBraces = $env:PATGEOM_REQUIRE_BRACES
$oldTexts = $env:PATGEOM_REQUIRE_TEXTS
$oldStrict = $env:PATGEOM_STRICT
$process = $null
$processExit = $null
$runError = $null
$stdoutTask = $null
$stderrTask = $null
try {
    $env:PATGEOM_RUN_ID = $RunId
    $env:PATGEOM_REPORT_DIR = $reportDir
    $env:PATGEOM_REQUIRE_BRACES = [string]$RequireBraces
    $env:PATGEOM_REQUIRE_TEXTS = [string]$RequireTexts
    $env:PATGEOM_STRICT = "1"
    $argString = '/i "' + $drawingPath + '" /s "' + $scr + '" /l en-US'
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $accore
    $psi.Arguments = $argString
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill() } catch { }
        throw "accoreconsole timed out after $TimeoutSeconds seconds"
    }
    $process.WaitForExit()
    $processExit = $process.ExitCode
    if ($null -ne $stdoutTask) { [IO.File]::WriteAllText($accoreStdout, $stdoutTask.Result, (New-Object Text.UTF8Encoding($false))) }
    if ($null -ne $stderrTask) { [IO.File]::WriteAllText($accoreStderr, $stderrTask.Result, (New-Object Text.UTF8Encoding($false))) }
}
catch {
    $runError = $_.Exception.Message
}
finally {
    $env:PATGEOM_RUN_ID = $oldRunId
    $env:PATGEOM_REPORT_DIR = $oldReportDir
    $env:PATGEOM_REQUIRE_BRACES = $oldBraces
    $env:PATGEOM_REQUIRE_TEXTS = $oldTexts
    $env:PATGEOM_STRICT = $oldStrict
}

$checked = $null
try {
    $checked = Test-GeometryReport -Path $reportFile -ExpectedRunId $RunId `
        -ExpectedSha256 $ExpectedDllSha256 -ExpectedAssemblyPath $ExpectedAssemblyPath `
        -ExpectedMvid $ExpectedMvid -ExpectedBraces $RequireBraces -ExpectedTexts $RequireTexts
    if ($processExit -ne 0) { throw "accoreconsole exited with $processExit after report $($checked.Summary)" }
}
catch {
    if ([string]::IsNullOrEmpty($runError)) { $runError = $_.Exception.Message }
}

$result = if ($null -eq $runError) { "PASS" } else { "FAIL" }
$evidence = [pscustomobject]@{
    run_id = $RunId
    package_directory = $PackageDirectory
    drawing = $drawingPath
    drawing_sha256 = $drawingSha256
    manifest_sha256 = $manifestSha256
    accoreconsole = $accore
    accore_stdout = $accoreStdout
    accore_stderr = $accoreStderr
    run_script = $scr
    candidate_dll_sha256 = $ExpectedDllSha256
    expected_mvid = $ExpectedMvid
    assembly_sha256 = if ($null -ne $checked) { $checked.Sha256 } else { $ExpectedDllSha256 }
    assembly_mvid = if ($null -ne $checked) { $checked.Mvid } else { $ExpectedMvid }
    assembly_path = if ($null -ne $checked) { $checked.AssemblyPath } else { $ExpectedAssemblyPath }
    braces = if ($null -ne $checked) { $checked.Braces } else { $null }
    texts = if ($null -ne $checked) { $checked.Texts } else { $null }
    process_exit = $processExit
    report = $reportFile
    summary = if ($null -ne $checked) { $checked.Summary } else { $null }
    result = $result
    error = $runError
}
$evidencePath = Join-Path $runDir "geometry-evidence.json"
$evidence | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $evidencePath
if ($result -ne "PASS") {
    throw "geometry verification failed: $runError (evidence: $evidencePath)"
}
$evidence | ConvertTo-Json -Depth 5
exit 0

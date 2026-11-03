param([Parameter(Mandatory = $true)][string]$ProjectRoot)

# Builds red-replay-source: the archived pre-fix rollback catch body combined
# with the same test-only injection scaffolding the fixed installers carry.
# Everything outside the catch clause is the fixed file verbatim, so the only
# variable between the red and green runs is the rollback logic itself.
#
# Two invariants are asserted, not assumed:
#   * the replay catch body is byte-identical to the archived pre-fix catch;
#   * the replay and fixed files are identical outside that catch clause.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ProjectRoot).Path
$g26 = Join-Path $root 'docs\test-evidence\repair-review-20261102-r2\G26'
$prefixDir = Join-Path $g26 'red-prefix-source'
$replayDir = Join-Path $g26 'red-replay-source'
if (-not (Test-Path -LiteralPath $replayDir)) { New-Item -ItemType Directory -Path $replayDir | Out-Null }

function Read-Lf([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) {
        throw "Missing UTF-8 BOM: $Path"
    }
    $text = [Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3)
    return $text.Replace("`r`n", "`n")
}

function Get-TopLevelTry([string]$Text, [string]$Label) {
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$errors)
    if ($errors.Count -gt 0) { throw "$Label does not parse" }
    $try = $null
    foreach ($statement in $ast.EndBlock.Statements) {
        if ($statement -is [Management.Automation.Language.TryStatementAst]) { $try = $statement }
    }
    if ($null -eq $try) { throw "$Label has no top-level try/catch" }
    if ($try.CatchClauses.Count -ne 1) { throw "$Label has an unexpected catch count" }
    return $try
}

$report = New-Object System.Collections.Generic.List[string]
foreach ($pair in @(
        @{ Name = 'install-office-addin.ps1'; Other = 'install-visio-addin.ps1' },
        @{ Name = 'install-visio-addin.ps1'; Other = 'install-office-addin.ps1' }
    )) {
    $name = $pair.Name
    $prefixText = Read-Lf (Join-Path $prefixDir $name)
    $fixedText = Read-Lf (Join-Path $root ('office-com-addin\' + $name))

    $prefixCatch = (Get-TopLevelTry $prefixText $name).CatchClauses[0].Extent.Text
    $fixedTry = Get-TopLevelTry $fixedText $name
    $fixedCatch = $fixedTry.CatchClauses[0].Extent.Text

    $offset = $fixedTry.CatchClauses[0].Extent.StartOffset
    $replayText = $fixedText.Substring(0, $offset) + $prefixCatch +
        $fixedText.Substring($offset + $fixedCatch.Length)

    $path = Join-Path $replayDir $name
    [IO.File]::WriteAllText($path, $replayText.Replace("`n", "`r`n"), (New-Object System.Text.UTF8Encoding($true)))

    # Invariant 1: the executed rollback body is the archived pre-fix body.
    $replayCatch = (Get-TopLevelTry $replayText $name).CatchClauses[0].Extent.Text
    $catchSame = ($replayCatch -ceq $prefixCatch)
    # Invariant 2: nothing outside the catch differs from the fixed file.
    $replayOutside = $replayText.Substring(0, $offset) + $replayText.Substring($offset + $replayCatch.Length)
    $fixedOutside = $fixedText.Substring(0, $offset) + $fixedText.Substring($offset + $fixedCatch.Length)
    $outsideSame = ($replayOutside -ceq $fixedOutside)

    $sha = {
        param([string]$Value)
        $sha256 = [Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value))) -replace '-', '') }
        finally { $sha256.Dispose() }
    }
    $report.Add(('replay|{0}|catch-identical-to-prefix={1}|catch-sha256={2}|pre-fix-catch-sha256={3}|outside-catch-identical-to-fixed={4}|bytes={5}' -f `
        $name, $catchSame, (& $sha $replayCatch), (& $sha $prefixCatch), $outsideSame, $replayText.Length))
    if (-not $catchSame) { throw "Replay catch differs from the archived pre-fix catch: $name" }
    if (-not $outsideSame) { throw "Replay differs from the fixed file outside the catch: $name" }
}
[IO.File]::WriteAllLines((Join-Path $g26 'red-replay-build.txt'), $report, (New-Object System.Text.UTF8Encoding($false)))
$report

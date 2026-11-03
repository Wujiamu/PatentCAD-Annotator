param(
    [Parameter(Mandatory = $true)][string]$ProjectRoot,
    [Parameter(Mandatory = $true)][ValidateSet('ppt', 'visio')][string]$Product,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [switch]$UseRedSource
)

# Runs the real isolation-matrix verifier against either the production
# installers or the archived pre-fix replay, from an isolated copy of the
# office-com-addin directory. Nothing outside the temporary copy is touched, and
# the verifier itself is byte-identical between red and green runs.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ProjectRoot).Path
$sourceDir = Join-Path $root 'office-com-addin'
$runId = [Guid]::NewGuid().ToString('N')
$sandbox = Join-Path ([IO.Path]::GetTempPath()) ('PatentMarkerG22Run-' + $runId)
New-Item -ItemType Directory -Path $sandbox -Force | Out-Null

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# G22 red/green evidence')
$lines.Add('time=' + (Get-Date).ToString('o'))
$lines.Add('product=' + $Product)
$lines.Add('mode=' + $(if ($UseRedSource) { 'RED (pre-fix replay catch)' } else { 'GREEN (current production)' }))
$lines.Add('powershell=' + $PSVersionTable.PSVersion)
$lines.Add('os=' + [Environment]::OSVersion.VersionString)
$lines.Add('user=' + [Security.Principal.WindowsIdentity]::GetCurrent().Name)
$lines.Add('sandbox=' + $sandbox)
$lines.Add('')

try {
    # Copy the whole add-in directory so dist/, the modules and the verifier are
    # all local to the sandbox.
    Copy-Item -Path (Join-Path $sourceDir '*') -Destination $sandbox -Recurse -Force

    if ($UseRedSource) {
        $redDir = Join-Path $root 'docs\test-evidence\repair-review-20261102\G22\red-replay-source'
        $name = if ($Product -eq 'ppt') { 'install-office-addin.ps1' } else { 'install-visio-addin.ps1' }
        Copy-Item -LiteralPath (Join-Path $redDir $name) -Destination (Join-Path $sandbox $name) -Force
    }

    $installerName = if ($Product -eq 'ppt') { 'install-office-addin.ps1' } else { 'install-visio-addin.ps1' }
    $verifierName = if ($Product -eq 'ppt') { 'verify-installer.ps1' } else { 'verify-visio-installer.ps1' }

    # Record the hashes of what is actually executed, so red/green attribution is
    # provable from the evidence file alone.
    foreach ($name in @($installerName, $verifierName, 'OfficeAddin.Common.psm1', 'VisioAddin.Common.psm1')) {
        $path = Join-Path $sandbox $name
        if (Test-Path -LiteralPath $path) {
            $lines.Add(('sha256|{0}|{1}' -f $name, (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash))
        }
    }
    $lines.Add('')

    $verifier = Join-Path $sandbox $verifierName
    $arguments = if ($Product -eq 'ppt') { @('-OfficeBitness', '64') } else { @() }
    # A verifier that reports FAIL writes to stderr and exits non-zero. Keep that
    # as ordinary captured output instead of letting it terminate the runner.
    $previousErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $verifier @arguments 2>&1 |
            ForEach-Object { [string]$_ })
        $exit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorAction }

    foreach ($line in $output) { $lines.Add($line) }
    $lines.Add('')
    $lines.Add('exit=' + $exit)
    $pass = @($output | Where-Object { $_ -match '^(PASS|PASS:)' }).Count
    $fail = @($output | Where-Object { $_ -match '^FAIL' }).Count
    $lines.Add(('summary|pass={0}|fail={1}|exit={2}' -f $pass, $fail, $exit))
}
catch {
    $lines.Add('RUNNER-ERROR|' + $_.Exception.Message)
    $lines.Add('exit=99')
}
finally {
    # Always release any injected ACE before deleting the sandbox.
    try {
        $acl = Get-Acl -LiteralPath $sandbox -ErrorAction Stop
        foreach ($rule in @($acl.Access | Where-Object { $_.AccessControlType -eq 'Deny' })) {
            $acl.RemoveAccessRule($rule) | Out-Null
        }
        Set-Acl -LiteralPath $sandbox -AclObject $acl
    }
    catch { }
    Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue
    $outFull = [IO.Path]::GetFullPath($OutputPath)
    $outDir = Split-Path -Parent $outFull
    if ($outDir -and -not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
    [IO.File]::WriteAllLines($outFull, $lines, (New-Object System.Text.UTF8Encoding($false)))
    Write-Output ('WROTE|' + $outFull)
    Write-Output ($lines | Where-Object { $_ -match '^(summary|exit|RUNNER-ERROR)' } | Select-Object -Last 3)
}

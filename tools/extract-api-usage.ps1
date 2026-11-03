param(
    [Parameter(Mandatory = $true)][string]$AssemblyPath,
    [Parameter(Mandatory = $true)][string]$OutputJson
)

$ErrorActionPreference = "Stop"
$assembly = (Resolve-Path $AssemblyPath).Path
$ildasmCandidates = @(
    "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\ildasm.exe",
    "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\x64\ildasm.exe"
)
$ildasm = $ildasmCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ([string]::IsNullOrEmpty($ildasm)) { throw "ildasm.exe not found; supply a .NET Framework SDK tool installation" }

$tempDir = Join-Path $env:TEMP ("PatentMarker-api-usage-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir | Out-Null
$il = Join-Path $tempDir "assembly.il"
try {
    & $ildasm /text /nobar /out=$il $assembly | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $il)) { throw "ildasm failed: $assembly" }
    $lines = Get-Content -Encoding utf8 $il
    $types = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $members = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $regex = [regex]'\[(?<asm>acdbmgd|acmgd|accoremgd)\](?<name>[A-Za-z0-9_.`/<>]+)(?:::(?<member>[A-Za-z0-9_.<>`]+))?'
    foreach ($line in $lines) {
        foreach ($match in $regex.Matches($line)) {
            $typeName = $match.Groups['name'].Value
            if ([string]::IsNullOrEmpty($typeName)) { continue }
            [void]$types.Add($typeName)
            $memberName = $match.Groups['member'].Value
            if (-not [string]::IsNullOrEmpty($memberName)) {
                [void]$members.Add($match.Groups['asm'].Value.ToLowerInvariant() + "|" + $typeName + "::" + $memberName)
            }
        }
    }
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $assembly).Hash.ToLowerInvariant()
    $mvid = ""
    foreach ($line in $lines) {
        if ($line -match '// MVID: \{(?<mvid>[0-9A-Fa-f-]+)\}') { $mvid = $matches['mvid'].ToLowerInvariant(); break }
    }
    $result = [pscustomobject]@{
        assembly = $assembly
        sha256 = $hash
        mvid = $mvid
        sdk_types = @($types | Sort-Object)
        sdk_members = @($members | Sort-Object)
        sdk_type_count = $types.Count
        sdk_member_count = $members.Count
        method = "ildasm textual scan; type/member names only, signatures unresolved"
    }
    New-Item -ItemType Directory -Force (Split-Path -Parent $OutputJson) | Out-Null
    $result | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $OutputJson
    Write-Output ("PASS types={0} members={1} output={2}" -f $types.Count, $members.Count, $OutputJson)
} finally {
    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

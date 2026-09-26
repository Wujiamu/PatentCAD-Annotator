[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [Parameter(Mandatory = $true)][string]$DictionaryPath
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'OfficeAddin.Common.psm1') -Force
$installRoot = Join-Path $env:LOCALAPPDATA 'PatentMarker\OfficeAddin'
$productRoot = Join-Path $installRoot 'PowerPoint'
$dictionaryFull = [IO.Path]::GetFullPath($DictionaryPath)
$bitness = Get-PatentMarkerOfficeBitness -Requested Auto
$baseKey = Open-PatentMarkerCurrentUserRegistry -OfficeBitness $bitness
try {
    $otherFiles = @()
    if (Test-Path -LiteralPath $installRoot -PathType Container) {
        foreach ($file in (Get-ChildItem -LiteralPath $installRoot -Recurse -File -Force | Sort-Object FullName)) {
            if ($file.FullName.StartsWith($productRoot + [IO.Path]::DirectorySeparatorChar,
                    [StringComparison]::OrdinalIgnoreCase)) { continue }
            $otherFiles += [ordered]@{
                RelativePath = $file.FullName.Substring($installRoot.Length).TrimStart('\')
                Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            }
        }
    }
    $otherAddins = @()
    $addinsRoot = 'Software\Microsoft\Office\PowerPoint\Addins'
    $addinsKey = $baseKey.OpenSubKey($addinsRoot)
    if ($addinsKey) {
        try {
            foreach ($name in ($addinsKey.GetSubKeyNames() | Sort-Object)) {
                if ($name -eq 'PatentOffice.PowerPointAddIn') { continue }
                $otherAddins += ,(Get-PatentMarkerRegistrySnapshot -BaseKey $baseKey -Path ($addinsRoot + '\' + $name))
            }
        }
        finally { $addinsKey.Dispose() }
    }
    $productFiles = @()
    if (Test-Path -LiteralPath $productRoot -PathType Container) {
        foreach ($file in (Get-ChildItem -LiteralPath $productRoot -File -Force | Sort-Object Name)) {
            $productFiles += [ordered]@{
                Name = $file.Name
                Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            }
        }
    }
    $productPaths = @(Get-PatentMarkerRegistryPaths)
    $productRegistry = @()
    foreach ($path in $productPaths) {
        $productRegistry += ,(Get-PatentMarkerRegistrySnapshot -BaseKey $baseKey -Path $path)
    }
    $snapshot = [ordered]@{
        CapturedAt = [DateTime]::UtcNow.ToString('o')
        Identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        PowerPointProcesses = @((Get-Process POWERPNT -ErrorAction SilentlyContinue | Select-Object Id,StartTime,MainWindowTitle))
        OfficeBitness = $bitness
        DictionarySha256 = (Get-FileHash -LiteralPath $dictionaryFull -Algorithm SHA256).Hash
        ProductFiles = $productFiles
        ProductRegistry = $productRegistry
        OtherFiles = $otherFiles
        OtherAddins = $otherAddins
    }
    $outputFull = [IO.Path]::GetFullPath($OutputPath)
    $snapshot | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $outputFull -Encoding UTF8
    Write-Output "PASS|PPT_RELEASE_STATE|$outputFull"
}
finally { $baseKey.Dispose() }

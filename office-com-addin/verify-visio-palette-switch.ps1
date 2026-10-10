[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AssemblyPath,
    [Parameter(Mandatory = $true)][string]$DictionaryPath,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $PSScriptRoot 'test-evidence'
}
if (Get-Process VISIO -ErrorAction SilentlyContinue) { throw 'Visio is already running.' }
$assemblyFull = [IO.Path]::GetFullPath($AssemblyPath)
$dictionaryFull = [IO.Path]::GetFullPath($DictionaryPath)
if (-not (Test-Path -LiteralPath $assemblyFull -PathType Leaf)) { throw 'Assembly is missing.' }
if (-not (Test-Path -LiteralPath $dictionaryFull -PathType Leaf)) { throw 'Dictionary is missing.' }

$root = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ('visio-palette-switch-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $root | Out-Null
$aPath = Join-Path $root 'a.vsdx'
$bPath = Join-Path $root 'b.vsdx'
$application = $null
$a = $null
$b = $null
$form = $null
$result = $null
try {
    $assembly = [Reflection.Assembly]::LoadFrom($assemblyFull)
    $addinType = $assembly.GetType('PatentOffice.Visio.VisioAddIn', $true)
    $formType = $assembly.GetType('PatentOffice.Visio.PaletteForm', $true)
    $flags = [Reflection.BindingFlags]'Instance,NonPublic'
    $application = New-Object -ComObject Visio.Application
    if ([int]$application.Documents.Count -ne 0) { throw 'Unexpected open Visio documents.' }
    $a = $application.Documents.Add('')
    $null = $a.SaveAs($aPath)

    $addin = [Activator]::CreateInstance($addinType)
    $addinType.GetField('_application', $flags).SetValue($addin, $application)
    $form = [Activator]::CreateInstance($formType, [Reflection.BindingFlags]'Instance,NonPublic,Public', $null, @($addin), $null)
    $formType.GetMethod('BindDictionaryPath', $flags).Invoke($form, @($a, $dictionaryFull))
    $aKey = [string]$formType.GetField('_activeDocumentKey', $flags).GetValue($form)

    $b = $application.Documents.Add('')
    $null = $b.SaveAs($bPath)
    if (-not [string]::Equals([IO.Path]::GetFullPath([string]$application.ActiveDocument.FullName), $bPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Document B did not become active.'
    }
    $formType.GetMethod('CheckButton_Click', $flags).Invoke($form, @($null, [EventArgs]::Empty))
    $label = $formType.GetField('_statusLabel', $flags).GetValue($form)
    $status = [string]$label.Text
    $bKey = [string]$formType.GetField('_activeDocumentKey', $flags).GetValue($form)
    $guarded = $status.Contains('文档已切换') -and -not [string]::Equals($aKey, $bKey, [StringComparison]::OrdinalIgnoreCase)
    $result = [ordered]@{
        AssemblyPath = $assemblyFull
        AssemblySha256 = (Get-FileHash -LiteralPath $assemblyFull -Algorithm SHA256).Hash
        HostVersion = [string]$application.Version
        DocumentA = $aPath
        DocumentB = $bPath
        StatusAfterImmediateCheck = $status
        Guarded = $guarded
        Outcome = if ($guarded) { 'PASS' } else { 'FAIL' }
    }
}
finally {
    if ($form) { try { $form.CloseFromHost() } catch { } }
    if ($a) { try { $null = $a.Save() } catch { } }
    if ($b) { try { $null = $b.Save() } catch { } }
    if ($b) { try { $null = $b.Close() } catch { } }
    if ($a) { try { $null = $a.Close() } catch { } }
    if ($application) { try { $null = $application.Quit() } catch { } }
}
if ($result -eq $null) { throw 'No assertion was reached.' }
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'result.json') -Encoding UTF8
Write-Output "RESULT|VISIO_PALETTE_SWITCH|$($result.Outcome)|$root"
if ($result.Outcome -ne 'PASS') { exit 1 }

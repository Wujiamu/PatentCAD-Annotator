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
$assemblyFull = [IO.Path]::GetFullPath($AssemblyPath)
$dictionaryFull = [IO.Path]::GetFullPath($DictionaryPath)
if (-not (Test-Path -LiteralPath $assemblyFull -PathType Leaf)) { throw 'Assembly is missing.' }
if (-not (Test-Path -LiteralPath $dictionaryFull -PathType Leaf)) { throw 'Dictionary is missing.' }

Add-Type -TypeDefinition @'
namespace PatentMarkerPptSwitchTest {
    public sealed class FakePresentations { public int Count { get { return 2; } } }
    public sealed class FakeSlides { public int Count { get { return 0; } } }
    public sealed class FakeParts { public int Count { get { return 0; } } }
    public sealed class FakeCustomXmlParts {
        public FakeParts SelectByNamespace(string value) { return new FakeParts(); }
    }
    public sealed class FakePresentation {
        public string FullName { get; set; }
        public FakeCustomXmlParts CustomXMLParts { get; private set; }
        public FakeSlides Slides { get; private set; }
        public FakePresentation(string path) {
            FullName = path;
            CustomXMLParts = new FakeCustomXmlParts();
            Slides = new FakeSlides();
        }
    }
    public sealed class FakeApplication {
        public FakePresentations Presentations { get; private set; }
        public FakePresentation ActivePresentation { get; set; }
        public FakeApplication() { Presentations = new FakePresentations(); }
    }
}
'@

$root = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ('ppt-palette-switch-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $root | Out-Null
$aPath = Join-Path $root 'a.pptx'
$bPath = Join-Path $root 'b.pptx'
[IO.File]::WriteAllBytes($aPath, [byte[]]@(0))
[IO.File]::WriteAllBytes($bPath, [byte[]]@(0))

$form = $null
$result = $null
try {
    $assembly = [Reflection.Assembly]::LoadFrom($assemblyFull)
    $addinType = $assembly.GetType('PatentOffice.PowerPoint.PowerPointAddIn', $true)
    $formType = $assembly.GetType('PatentOffice.PowerPoint.PaletteForm', $true)
    $readerType = $assembly.GetType('PatentOffice.PowerPoint.DictionaryReader', $true)
    $flags = [Reflection.BindingFlags]'Instance,NonPublic'
    $publicStatic = [Reflection.BindingFlags]'Static,Public'
    $application = New-Object PatentMarkerPptSwitchTest.FakeApplication
    $a = New-Object PatentMarkerPptSwitchTest.FakePresentation($aPath)
    $b = New-Object PatentMarkerPptSwitchTest.FakePresentation($bPath)
    $application.ActivePresentation = $a

    $addin = [Activator]::CreateInstance($addinType)
    $addinType.GetField('_application', $flags).SetValue($addin, $application)
    $form = [Activator]::CreateInstance($formType, [Reflection.BindingFlags]'Instance,NonPublic,Public', $null, @($addin), $null)
    $snapshot = $readerType.GetMethod('Read', $publicStatic).Invoke($null, @($dictionaryFull))
    $dictionary = $snapshot.GetType().GetProperty('Dictionary').GetValue($snapshot, $null)
    $formType.GetField('_dictionary', $flags).SetValue($form, $dictionary)
    $formType.GetField('_dictionaryCurrent', $flags).SetValue($form, $true)
    $formType.GetField('_boundPath', $flags).SetValue($form, $dictionaryFull)
    $formType.GetMethod('RenderEntries', $flags).Invoke($form, @())

    $application.ActivePresentation = $b
    $formType.GetMethod('CheckButton_Click', $flags).Invoke($form, @($null, [EventArgs]::Empty))
    $status = [string]$formType.GetField('_statusLabel', $flags).GetValue($form).Text
    $activePath = [string]$formType.GetField('_activePresentationPath', $flags).GetValue($form)
    $guarded = $status.Contains('演示文稿已切换') -and
        [string]::Equals($activePath, $bPath, [StringComparison]::OrdinalIgnoreCase)
    $result = [ordered]@{
        AssemblySha256 = (Get-FileHash -LiteralPath $assemblyFull -Algorithm SHA256).Hash
        StatusAfterImmediateCheck = $status
        ActivePathAfterCheck = $activePath
        Guarded = $guarded
        Outcome = if ($guarded) { 'PASS' } else { 'FAIL' }
    }
}
finally {
    if ($form) { $form.Dispose() }
}
if ($result -eq $null) { throw 'No assertion was reached.' }
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'result.json') -Encoding UTF8
Write-Output "RESULT|PPT_PALETTE_SWITCH|$($result.Outcome)|$root"
if ($result.Outcome -ne 'PASS') { exit 1 }

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AssemblyPath,
    [Parameter(Mandatory = $true)][string]$DictionaryPath,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $PSScriptRoot 'test-evidence' }
$assemblyFull = [IO.Path]::GetFullPath($AssemblyPath)
$dictionaryFull = [IO.Path]::GetFullPath($DictionaryPath)
if (-not (Test-Path -LiteralPath $assemblyFull -PathType Leaf)) { throw 'Assembly is missing.' }
if (-not (Test-Path -LiteralPath $dictionaryFull -PathType Leaf)) { throw 'Dictionary is missing.' }

Add-Type -TypeDefinition @'
namespace PatentMarkerPptBindingTest {
    public sealed class FakePart { public string XML { get; set; } }
    public sealed class FakeParts {
        private readonly FakePart _part;
        public FakeParts(FakePart part) { _part = part; }
        public int Count { get { return 1; } }
        public FakePart Item(int index) { return _part; }
    }
    public sealed class FakeCustomXmlParts {
        private readonly FakePart _part;
        public FakeCustomXmlParts(FakePart part) { _part = part; }
        public FakeParts SelectByNamespace(string value) { return new FakeParts(_part); }
    }
    public sealed class FakePresentation {
        public string FullName { get; set; }
        public FakeCustomXmlParts CustomXMLParts { get; set; }
    }
}
'@

$root = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ('ppt-binding-kind-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $root | Out-Null
$pptPath = Join-Path $root 'binding-test.pptx'
[IO.File]::WriteAllBytes($pptPath, [byte[]]@(0))
$assembly = [Reflection.Assembly]::LoadFrom($assemblyFull)
$bindingType = $assembly.GetType('PatentOffice.PowerPoint.PresentationBinding', $true)
$resolve = $bindingType.GetMethod('Resolve', [Reflection.BindingFlags]'Public,Static')
function Resolve-TestBinding {
    param([string]$Kind, [string]$StoredPath)
    $part = New-Object PatentMarkerPptBindingTest.FakePart
    $part.XML = '<PatentMarkerBinding xmlns="urn:patentmarker:powerpoint:binding:v1" version="1" kind="' + $Kind + '">' +
        [Security.SecurityElement]::Escape($StoredPath) + '</PatentMarkerBinding>'
    $presentation = New-Object PatentMarkerPptBindingTest.FakePresentation
    $presentation.FullName = $pptPath
    $presentation.CustomXMLParts = New-Object PatentMarkerPptBindingTest.FakeCustomXmlParts($part)
    return $resolve.Invoke($null, @($presentation))
}

$invalid = Resolve-TestBinding -Kind 'mystery' -StoredPath $dictionaryFull
$valid = Resolve-TestBinding -Kind 'absolute' -StoredPath $dictionaryFull
$invalidError = [string]$invalid.GetType().GetProperty('Error').GetValue($invalid, $null)
$validError = [string]$valid.GetType().GetProperty('Error').GetValue($valid, $null)
$validPath = [string]$valid.GetType().GetProperty('Path').GetValue($valid, $null)
$guarded = $invalidError.Contains('路径类型无法识别') -and [string]::IsNullOrEmpty($validError) -and
    [string]::Equals($validPath, $dictionaryFull, [StringComparison]::OrdinalIgnoreCase)
$result = [ordered]@{
    AssemblySha256 = (Get-FileHash -LiteralPath $assemblyFull -Algorithm SHA256).Hash
    InvalidKindError = $invalidError
    ValidAbsolutePathAccepted = [string]::Equals($validPath, $dictionaryFull, [StringComparison]::OrdinalIgnoreCase)
    Outcome = if ($guarded) { 'PASS' } else { 'FAIL' }
}
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'result.json') -Encoding UTF8
Write-Output "RESULT|PPT_BINDING_KIND|$($result.Outcome)|$root"
if (-not $guarded) { exit 1 }

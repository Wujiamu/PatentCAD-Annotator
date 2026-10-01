[CmdletBinding()]
param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'test-evidence'),
    [string]$AssemblyPath = (Join-Path $env:LOCALAPPDATA 'PatentMarker\OfficeAddin\Visio\PatentOffice.Visio.dll'),
    [string]$DictionaryPath = '',
    [switch]$SkipNegativeCases
)

$ErrorActionPreference = 'Stop'
# The net40 production assembly must be probed in the .NET Framework runtime.
# PowerShell Core's reflection/COM path previously stalled on a rejected selection.
if ($PSVersionTable.PSVersion.Major -ne 5 -or
    [Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    Write-Output 'SKIP|VISIO_HOST_COM|Run with Windows PowerShell 5.1 -STA; no host mutation performed.'
    exit 1
}
if (-not (Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) {
    throw "未找到待测 Visio 程序集：$AssemblyPath"
}
$assemblyPath = [IO.Path]::GetFullPath($AssemblyPath)

$runId = [Guid]::NewGuid().ToString('N')
$runDirectory = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ("visio-host-com-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $runId.Substring(0, 8))
if (Test-Path -LiteralPath $runDirectory) { throw "证据目录已存在，拒绝覆盖：$runDirectory" }
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$stepLogPath = Join-Path $runDirectory 'steps.log'
function Write-TestStep([string]$name) {
    Add-Content -LiteralPath $stepLogPath -Value ((Get-Date).ToString('o') + "`t" + $name) -Encoding UTF8
}
$documentPath = Join-Path $runDirectory 'visio-host-test.vsdx'
$dictionaryWasGenerated = [string]::IsNullOrWhiteSpace($DictionaryPath)
if ($dictionaryWasGenerated) {
    $dictionaryPath = Join-Path $runDirectory 'word-export-shape-fixture.dict.json'
    $dictionaryJson = @'
{
  "metadata": {
    "source_file": "visio-host-test.docx",
    "extracted_at": "2026-09-24T00:00:00Z",
    "version": "1"
  },
  "entries": [
    { "number": "1", "name": "外壳", "occurrences": 1 },
    { "number": "2", "name": "弹簧", "occurrences": 1 }
  ],
  "warnings": []
}
'@
    [IO.File]::WriteAllText($dictionaryPath, $dictionaryJson, [Text.UTF8Encoding]::new($false))
}
else {
    $dictionaryPath = [IO.Path]::GetFullPath($DictionaryPath)
    if (-not (Test-Path -LiteralPath $dictionaryPath -PathType Leaf)) {
        throw "指定的 Word 字典不存在：$dictionaryPath"
    }
}
$dictionaryBytesBefore = [IO.File]::ReadAllBytes($dictionaryPath)

$helperTypeName = 'PatentMarkerVisioHostTest.ComActiveObjectHelper'
if (-not ($helperType = $helperTypeName -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace PatentMarkerVisioHostTest {
    public static class ComActiveObjectHelper {
        [DllImport("oleaut32.dll", PreserveSig = false)]
        private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved,
            [MarshalAs(UnmanagedType.IUnknown)] out object value);
        public static object Get(string progId) {
            Type type = Type.GetTypeFromProgID(progId, true);
            Guid clsid = type.GUID;
            object value;
            GetActiveObject(ref clsid, IntPtr.Zero, out value);
            return value;
        }
    }
}
'@ | Out-Null
}

$application = [PatentMarkerVisioHostTest.ComActiveObjectHelper]::Get('Visio.Application')
if ([string]$application.Version -ne '16.0') { throw "非预期 Visio 版本：$($application.Version)" }

$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$flags = [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$diagnostics = $assembly.GetType('PatentOffice.Shared.OfficeDiagnostics', $true)
$logField = $diagnostics.GetField('_logPath', [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static)
$logField.SetValue($null, [IO.Path]::GetTempPath())
$bindingType = $assembly.GetType('PatentOffice.Visio.VisioDocumentBinding', $true)
$readerType = $assembly.GetType('PatentOffice.Shared.DictionaryReader', $true)
$annotationType = $assembly.GetType('PatentOffice.Visio.VisioAnnotations', $true)
$entryType = $assembly.GetType('PatentOffice.Shared.PatentEntry', $true)

function Invoke-StaticMethod {
    param([Reflection.MethodInfo]$Method, [object[]]$Arguments)
    return $Method.Invoke($null, $Arguments)
}

function Assert-MarkFailure {
    param([Reflection.MethodInfo]$Method, [object[]]$Arguments, [string]$ExpectedText)
    $actual = $null
    try { Invoke-StaticMethod -Method $Method -Arguments $Arguments | Out-Null }
    catch {
        $actual = $_.Exception
        if ($actual -is [Reflection.TargetInvocationException] -and $actual.InnerException) {
            $actual = $actual.InnerException
        }
    }
    if (-not $actual -or $actual.Message.IndexOf($ExpectedText, [StringComparison]::Ordinal) -lt 0) {
        throw "Visio 标注错误路径未按预期失败：expected=$ExpectedText;actual=$($actual.Message)"
    }
}

$previousDocument = $null
try { if ([int]$application.Documents.Count -gt 0) { $previousDocument = $application.ActiveDocument } } catch { }
$document = $null
$reopenedDocument = $null
$result = [ordered]@{
    RunId = $runId
    HostVersion = [string]$application.Version
    HostBitness = [IntPtr]::Size * 8
    AssemblyPath = $assemblyPath
    AssemblySha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    DocumentPath = $documentPath
    DictionaryPath = $dictionaryPath
    DictionaryBytesUnchanged = $false
    Binding = 'SKIP'
    Annotation = 'SKIP'
    Persistence = 'SKIP'
    Scanner = 'SKIP'
    EmptySelection = 'SKIP'
    WrongShape = 'SKIP'
    RepeatLine = 'SKIP'
    Level = if ($SkipNegativeCases) { 'L2_REAL_VISIO_COM_POSITIVE_PATH_NO_PANEL_NEGATIVE_CASES_SKIPPED' } else { 'L2_REAL_VISIO_COM_OBJECTS_NO_PANEL' }
    Limitation = if ($dictionaryWasGenerated) { 'Uses a locally generated schema fixture and invokes internal production methods by reflection; it does not cover the panel or Word UI export.' } else { 'Uses the specified dictionary file and invokes internal production methods by reflection; it covers the real Word export file bytes but not the Visio panel UI.' }
}

try {
    Write-TestStep 'document.add.begin'
    $document = $application.Documents.Add('')
    Write-TestStep 'document.add.complete'
    $document.SaveAs($documentPath)
    Write-TestStep 'document.saveas.complete'
    if (-not (Test-Path -LiteralPath $documentPath -PathType Leaf)) { throw 'Visio SaveAs did not create the test VSDX.' }

    $readArguments = [object[]]::new(1)
    $readArguments[0] = [string]$dictionaryPath
    $snapshot = Invoke-StaticMethod -Method ($readerType.GetMethod('Read', $flags)) -Arguments $readArguments
    Write-TestStep 'dictionary.read.complete'
    if ([int]$snapshot.Dictionary.Entries.Count -ne 2) { throw 'DictionaryReader did not read the expected two dictionary entries.' }
    $sourceEntry = $snapshot.Dictionary.Entries[0]
    $expectedNumber = [string]$sourceEntry.Number
    $expectedName = [string]$sourceEntry.Name
    $bindingSaveArguments = [object[]]::new(2)
    $bindingSaveArguments[0] = $document
    $bindingSaveArguments[1] = [string]$dictionaryPath
    Write-TestStep 'binding.save.begin'
    Invoke-StaticMethod -Method ($bindingType.GetMethod('Save', $flags)) -Arguments $bindingSaveArguments | Out-Null
    Write-TestStep 'binding.save.complete'
    $bindingResolveArguments = [object[]]::new(1)
    $bindingResolveArguments[0] = $document
    $resolvedBinding = Invoke-StaticMethod -Method ($bindingType.GetMethod('Resolve', $flags)) -Arguments $bindingResolveArguments
    Write-TestStep 'binding.resolve.complete'
    if (-not $resolvedBinding.IsBound -or -not [string]::Equals([IO.Path]::GetFullPath($resolvedBinding.Path), [IO.Path]::GetFullPath($dictionaryPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Visio document binding did not resolve to the selected dictionary: $($resolvedBinding.Error)"
    }
    $result.Binding = 'PASS'

    Write-TestStep 'drawline.begin'
    $page = $application.ActivePage
    $line = $page.DrawLine(1.0, 3.0, 3.0, 1.0)
    Write-TestStep 'drawline.complete'
    $application.ActiveWindow.DeselectAll()
    $application.ActiveWindow.Select($line, 2)
    Write-TestStep 'selectline.complete'
    if ([int]$application.ActiveWindow.Selection.Count -ne 1) { throw 'Visio did not select exactly the newly drawn 1D line.' }

    $entry = [Activator]::CreateInstance($entryType, $true)
    $entryType.GetProperty('Number').SetValue($entry, $expectedNumber, $null)
    $entryType.GetProperty('Name').SetValue($entry, $expectedName, $null)
    $markArguments = [object[]]::new(2)
    $markArguments[0] = $application
    $markArguments[1] = $entry
    $markMethod = $annotationType.GetMethod('MarkSelectedLine', $flags)

    if ($SkipNegativeCases) {
        $result.EmptySelection = 'SKIP'
        $result.WrongShape = 'SKIP'
        Write-TestStep 'negative.selection.cases.skipped'
    }
    else {
        $application.ActiveWindow.DeselectAll()
        Write-TestStep 'emptyselection.mark.begin'
        Assert-MarkFailure -Method $markMethod -Arguments $markArguments -ExpectedText '只选中一条'
        Write-TestStep 'emptyselection.mark.complete'
        $result.EmptySelection = 'PASS'

        $ordinary = $page.DrawRectangle(4.0, 3.0, 5.0, 2.0)
        $application.ActiveWindow.Select($ordinary, 2)
        Write-TestStep 'wrongshape.mark.begin'
        Assert-MarkFailure -Method $markMethod -Arguments $markArguments -ExpectedText '不是 Visio 一维线条'
        Write-TestStep 'wrongshape.mark.complete'
        $result.WrongShape = 'PASS'
    }

    $application.ActiveWindow.DeselectAll()
    $application.ActiveWindow.Select($line, 2)
    Write-TestStep 'annotation.create.begin'
    Invoke-StaticMethod -Method $markMethod -Arguments $markArguments | Out-Null
    Write-TestStep 'annotation.create.complete'
    if ([int]$line.OneD -eq 0) { throw 'The selected source shape is not a Visio 1D shape.' }
    if ([string]$line.CellsU('EndArrow').FormulaU -ne '13') { throw 'The line endpoint did not persist the expected arrow formula.' }

    if ($SkipNegativeCases) {
        $result.RepeatLine = 'SKIP'
    }
    else {
        $application.ActiveWindow.DeselectAll()
        $application.ActiveWindow.Select($line, 2)
        Assert-MarkFailure -Method $markMethod -Arguments $markArguments -ExpectedText '已是产品标注的一部分'
        $result.RepeatLine = 'PASS'
    }

    $scanArguments = [object[]]::new(1)
    $scanArguments[0] = $document
    $scan = Invoke-StaticMethod -Method ($annotationType.GetMethod('ScanDocument', $flags)) -Arguments $scanArguments
    $marked = @($scan.MarkedNumbers)
    if ($marked.Count -ne 1 -or $marked[0] -ne $expectedNumber -or [int]$scan.MalformedCount -ne 0) {
        throw "Pre-save annotation scan failed: marked=$($marked -join ','); malformed=$($scan.MalformedCount)"
    }
    $result.Annotation = 'PASS'
    $result.Scanner = 'PASS'

    $document.Save()
    $document.Close()
    $document = $null
    $reopenedDocument = $application.Documents.Open($documentPath)
    $reopenResolveArguments = [object[]]::new(1)
    $reopenResolveArguments[0] = $reopenedDocument
    $resolvedAfterReopen = Invoke-StaticMethod -Method ($bindingType.GetMethod('Resolve', $flags)) -Arguments $reopenResolveArguments
    if (-not $resolvedAfterReopen.IsBound -or -not [string]::Equals([IO.Path]::GetFullPath($resolvedAfterReopen.Path), [IO.Path]::GetFullPath($dictionaryPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Binding did not survive VSDX reopen: $($resolvedAfterReopen.Error)"
    }
    $reopenScanArguments = [object[]]::new(1)
    $reopenScanArguments[0] = $reopenedDocument
    $reopenedScan = Invoke-StaticMethod -Method ($annotationType.GetMethod('ScanDocument', $flags)) -Arguments $reopenScanArguments
    $reopenedMarked = @($reopenedScan.MarkedNumbers)
    if ($reopenedMarked.Count -ne 1 -or $reopenedMarked[0] -ne $expectedNumber -or [int]$reopenedScan.MalformedCount -ne 0) {
        throw "Post-reopen scan failed: marked=$($reopenedMarked -join ','); malformed=$($reopenedScan.MalformedCount)"
    }
    $result.Persistence = 'PASS'
    $dictionaryBytesAfter = [IO.File]::ReadAllBytes($dictionaryPath)
    $result.DictionaryBytesUnchanged = [System.Collections.StructuralComparisons]::StructuralEqualityComparer.Equals(
        $dictionaryBytesBefore, $dictionaryBytesAfter)
    if (-not $result.DictionaryBytesUnchanged) { throw 'Visio test modified the dictionary fixture bytes.' }
    $result.Outcome = if ($SkipNegativeCases) { 'SKIP' } else { 'PASS' }
}
catch {
    $result.Outcome = 'FAIL'
    $result.Error = $_.Exception.ToString()
    throw
}
finally {
    if ($reopenedDocument) { try { $reopenedDocument.Close() } catch { } }
    if ($document) { try { $document.Close() } catch { } }
    if ($previousDocument) { try { $previousDocument.Activate() } catch { } }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $runDirectory 'result.json') -Encoding UTF8
}

Write-Output "$($result.Outcome)|VISIO_HOST_COM|$runDirectory"

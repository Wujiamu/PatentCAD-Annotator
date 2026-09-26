[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExistingDocumentPath,
    [Parameter(Mandatory = $true)][string]$DictionaryPath,
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'test-evidence')
)

$ErrorActionPreference = 'Stop'
$existingFull = [IO.Path]::GetFullPath($ExistingDocumentPath)
$dictionaryFull = [IO.Path]::GetFullPath($DictionaryPath)
if (-not (Test-Path -LiteralPath $existingFull -PathType Leaf) -or
    -not (Test-Path -LiteralPath $dictionaryFull -PathType Leaf)) { throw '测试图或原始字典不存在。' }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace PatentMarkerVisioRecoveryTest {
    public static class ActiveObject {
        [DllImport("oleaut32.dll", PreserveSig = false)]
        private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved,
            [MarshalAs(UnmanagedType.IUnknown)] out object value);
        public static object Get(string progId) {
            Guid clsid = Type.GetTypeFromProgID(progId, true).GUID;
            object value;
            GetActiveObject(ref clsid, IntPtr.Zero, out value);
            return value;
        }
    }
}
'@

$application = [PatentMarkerVisioRecoveryTest.ActiveObject]::Get('Visio.Application')
if ([int]$application.Documents.Count -ne 1) { throw '当前 Visio 不是单份测试图状态。' }
$original = $application.ActiveDocument
if (-not [string]::Equals([IO.Path]::GetFullPath([string]$original.FullName), $existingFull,
        [StringComparison]::OrdinalIgnoreCase)) { throw '当前 Visio 图纸不是预期的已标注测试图。' }

$runId = [Guid]::NewGuid().ToString('N')
$runDirectory = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ("visio-recovery-" +
    (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $runId.Substring(0, 8))
if (Test-Path -LiteralPath $runDirectory) { throw "证据目录已存在：$runDirectory" }
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$testDictionary = Join-Path $runDirectory 'recovery.dict.json'
$testDocument = Join-Path $runDirectory 'recovery-test.vsdx'
Copy-Item -LiteralPath $dictionaryFull -Destination $testDictionary
if ((Get-FileHash -LiteralPath $dictionaryFull -Algorithm SHA256).Hash -ne
    (Get-FileHash -LiteralPath $testDictionary -Algorithm SHA256).Hash) { throw '测试字典副本与 Word 原始导出不一致。' }

$document = $null
try {
    $document = $application.Documents.Add('')
    $document.SaveAs($testDocument)
    if (-not [string]::Equals([IO.Path]::GetFullPath([string]$application.ActiveDocument.FullName),
            $testDocument, [StringComparison]::OrdinalIgnoreCase)) { throw '新测试图未成为活动文档。' }
    $result = [ordered]@{
        RunId = $runId
        OriginalDocument = $existingFull
        OriginalDictionary = $dictionaryFull
        OriginalDictionarySha256 = (Get-FileHash -LiteralPath $dictionaryFull -Algorithm SHA256).Hash
        TestDocument = $testDocument
        TestDictionary = $testDictionary
        TestDictionarySha256 = (Get-FileHash -LiteralPath $testDictionary -Algorithm SHA256).Hash
    }
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runDirectory 'prepared.json') -Encoding UTF8
    Write-Output "PASS|VISIO_RECOVERY_PREPARED|$runDirectory"
}
catch {
    if ($document) { try { $document.Close() } catch { } }
    throw
}

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DictionaryPath,
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'test-evidence')
)

$ErrorActionPreference = 'Stop'
$dictionaryFull = [IO.Path]::GetFullPath($DictionaryPath)
if (-not (Test-Path -LiteralPath $dictionaryFull -PathType Leaf)) { throw "字典不存在：$dictionaryFull" }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace PatentMarkerVisioPanelTest {
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

$application = [PatentMarkerVisioPanelTest.ActiveObject]::Get('Visio.Application')
if ([int]$application.Documents.Count -ne 0) { throw '当前 Visio 已有打开的图纸，拒绝准备 UI 测试。' }
$runId = [Guid]::NewGuid().ToString('N')
$runDirectory = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ("visio-panel-ui-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $runId.Substring(0, 8))
if (Test-Path -LiteralPath $runDirectory) { throw "证据目录已存在：$runDirectory" }
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$documentPath = Join-Path $runDirectory 'panel-test.vsdx'
$document = $null
try {
    $document = $application.Documents.Add('')
    $document.SaveAs($documentPath)
    $line = $application.ActivePage.DrawLine(1.0, 3.0, 3.0, 1.0)
    $application.ActiveWindow.DeselectAll()
    $application.ActiveWindow.Select($line, 2)
    if ([int]$application.ActiveWindow.Selection.Count -ne 1 -or [int]$line.OneD -eq 0) {
        throw '未能准备一条已选中的 Visio 一维线。'
    }
    $result = [ordered]@{
        RunId = $runId
        HostVersion = [string]$application.Version
        HostBitness = [IntPtr]::Size * 8
        DocumentPath = $documentPath
        DictionaryPath = $dictionaryFull
        DictionarySha256Before = (Get-FileHash -LiteralPath $dictionaryFull -Algorithm SHA256).Hash
        SelectedLineId = [int]$line.ID
        Preparation = 'PASS'
        Limitation = 'The line was prepared and selected through Visio COM; the panel actions remain to be exercised through the user interface.'
    }
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runDirectory 'prepared.json') -Encoding UTF8
    Write-Output "PASS|VISIO_PANEL_PREPARED|$runDirectory"
}
catch {
    if ($document) { try { $document.Close() } catch { } }
    throw
}

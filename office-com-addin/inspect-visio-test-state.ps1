[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$DocumentPath)

$ErrorActionPreference = 'Stop'
$expectedPath = [IO.Path]::GetFullPath($DocumentPath)
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace PatentMarkerVisioInspectTest {
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
$application = [PatentMarkerVisioInspectTest.ActiveObject]::Get('Visio.Application')
if ([int]$application.Documents.Count -ne 1) { throw 'Visio 当前不是单份测试图状态。' }
$document = $application.ActiveDocument
if (-not [string]::Equals([IO.Path]::GetFullPath([string]$document.FullName), $expectedPath,
        [StringComparison]::OrdinalIgnoreCase)) { throw '当前图纸不是指定测试图。' }
$selection = $application.ActiveWindow.Selection
$result = [ordered]@{
    HostVersion = [string]$application.Version
    HostBitness = [IntPtr]::Size * 8
    DocumentPath = $expectedPath
    PageCount = [int]$document.Pages.Count
    ActivePage = [string]$application.ActivePage.NameU
    SelectionCount = [int]$selection.Count
    SelectionShapeId = if ([int]$selection.Count -eq 1) { [int]$selection.Item(1).ID } else { $null }
    SelectionNameU = if ([int]$selection.Count -eq 1) { [string]$selection.Item(1).NameU } else { $null }
    SelectionOneD = if ([int]$selection.Count -eq 1) { [int]$selection.Item(1).OneD } else { $null }
    Saved = [bool]$document.Saved
}
$result | ConvertTo-Json -Compress

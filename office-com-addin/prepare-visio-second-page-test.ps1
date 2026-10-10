[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$DocumentPath)

$ErrorActionPreference = 'Stop'
$expectedPath = [IO.Path]::GetFullPath($DocumentPath)
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace PatentMarkerVisioSecondPageTest {
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
$application = [PatentMarkerVisioSecondPageTest.ActiveObject]::Get('Visio.Application')
if ([int]$application.Documents.Count -ne 1) { throw 'Visio 当前不是单份测试图状态。' }
$document = $application.ActiveDocument
if (-not [string]::Equals([IO.Path]::GetFullPath([string]$document.FullName), $expectedPath,
        [StringComparison]::OrdinalIgnoreCase)) { throw '当前图纸不是指定测试图。' }
if ([int]$document.Pages.Count -ne 1) { throw '测试图页数不符合前置条件。' }

$page = $document.Pages.Add()
$page.NameU = 'Release-Page-2'
$application.ActiveWindow.Page = $page
$line = $page.DrawLine(1.0, 3.0, 3.0, 1.0)
$application.ActiveWindow.DeselectAll()
if ([int]$page.Background -ne 0 -or [int]$page.Shapes.Count -ne 1 -or [int]$line.OneD -eq 0 -or
    [int]$application.ActivePage.ID -ne [int]$page.ID) { throw '第二前景页或测试线准备失败。' }
Write-Output "PASS|VISIO_SECOND_PAGE_PREPARED|page=$($page.NameU)|lineId=$($line.ID)|selection=$($application.ActiveWindow.Selection.Count)"

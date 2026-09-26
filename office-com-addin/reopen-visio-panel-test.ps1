[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$DocumentPath)

$ErrorActionPreference = 'Stop'
$documentFull = [IO.Path]::GetFullPath($DocumentPath)
if (-not (Test-Path -LiteralPath $documentFull -PathType Leaf)) { throw "测试图不存在：$documentFull" }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace PatentMarkerVisioReopenTest {
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

$application = [PatentMarkerVisioReopenTest.ActiveObject]::Get('Visio.Application')
if ([int]$application.Documents.Count -ne 0) { throw '当前 Visio 已有打开的图纸，拒绝覆盖用户会话。' }
$opened = $application.Documents.Open($documentFull)
if (-not [string]::Equals([IO.Path]::GetFullPath([string]$opened.FullName), $documentFull,
        [StringComparison]::OrdinalIgnoreCase)) { throw 'Visio 打开的不是指定测试图。' }
Write-Output "PASS|VISIO_REOPENED|host=$($application.Version)|bits=$([IntPtr]::Size * 8)|path=$documentFull"

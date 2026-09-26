[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DictionaryPath,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $PSScriptRoot 'test-evidence'
}
$dictionaryFull = [IO.Path]::GetFullPath($DictionaryPath)
if (-not (Test-Path -LiteralPath $dictionaryFull -PathType Leaf)) { throw '测试字典不存在。' }
$processes = @(Get-Process -Name POWERPNT -ErrorAction SilentlyContinue)
if ($processes.Count -ne 1) { throw '要求恰好一个已启动的 PowerPoint 进程。' }
$application = [Runtime.InteropServices.Marshal]::GetActiveObject('PowerPoint.Application')
if ($null -eq $application) { throw '无法附加到已启动的 PowerPoint。' }

$root = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ('ppt-panel-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $root | Out-Null
$presentationPath = Join-Path $root 'powerpoint-picture-test.pptx'
$imagePath = Join-Path $root 'diagram.png'
Add-Type -AssemblyName System.Drawing
$bitmap = [Drawing.Bitmap]::new(120, 120)
try {
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::White)
        $brush = [Drawing.SolidBrush]::new([Drawing.Color]::SteelBlue)
        try { $graphics.FillEllipse($brush, 10, 10, 100, 100) }
        finally { $brush.Dispose() }
    }
    finally { $graphics.Dispose() }
    $bitmap.Save($imagePath, [Drawing.Imaging.ImageFormat]::Png)
}
finally { $bitmap.Dispose() }

$presentation = $application.Presentations.Add(-1)
try {
    $slide1 = $presentation.Slides.Add(1, 12)
    $slide2 = $presentation.Slides.Add(2, 12)
    [void]$slide1.Shapes.AddPicture($imagePath, 0, -1, 300, 140, 120, 120)
    [void]$slide2.Shapes.AddPicture($imagePath, 0, -1, 300, 140, 120, 120)
    $line1 = $slide1.Shapes.AddLine(100, 100, 340, 200)
    $line2 = $slide2.Shapes.AddLine(100, 300, 340, 200)
    $line1.Name = 'PM_TEST_LINE_1'
    $line2.Name = 'PM_TEST_LINE_2'
    $presentation.SaveAs($presentationPath, 24)
    $application.ActiveWindow.View.GotoSlide(1)
    $line1.Select()
    $result = [ordered]@{
        CreatedAt = (Get-Date).ToString('o')
        PowerPointPid = $processes[0].Id
        PowerPointVersion = [string]$application.Version
        PresentationPath = $presentationPath
        ImagePath = $imagePath
        DictionaryPath = $dictionaryFull
        DictionarySha256 = (Get-FileHash -LiteralPath $dictionaryFull -Algorithm SHA256).Hash
        SlideCount = [int]$presentation.Slides.Count
        SelectedLineName = [string]$line1.Name
    }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'prepared.json') -Encoding UTF8
    Write-Output "PASS|PPT_PANEL_PREPARED|$root"
}
catch {
    try { $presentation.Close() }
    catch { }
    throw
}

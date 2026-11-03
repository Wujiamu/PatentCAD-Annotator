# G11 host-level probe: does a product annotation stay recognised after the
# user groups it with another shape and saves/reopens the file?
#
# Uses a private PowerPoint instance and a brand-new presentation written to a
# temporary path. Never opens a user document.
param([string]$OutRoot)
$ErrorActionPreference = 'Stop'
$asmPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'office-com-addin/dist/PatentOffice.PowerPoint.dll'
if ([string]::IsNullOrWhiteSpace($OutRoot)) { $OutRoot = Join-Path $env:TEMP 'g11-host' }
$outRoot = $OutRoot
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null
$log = @()
function Emit($s) { $script:log += $s; Write-Host $s }

$ppt = New-Object -ComObject PowerPoint.Application
$ppt.Visible = -1
$pres = $ppt.Presentations.Add($true)
$file = Join-Path $outRoot 'g11-host.pptx'
try {
    $slide = $pres.Slides.Add(1, 12)

    # Build the product annotation the same way the product does: line + label
    # grouped, with PATMARKER/PATNUMBER tags.
    $line = $slide.Shapes.AddLine(200, 200, 500, 200)
    $line.Line.EndArrowheadStyle = 2
    $label = $slide.Shapes.AddTextbox(1, 168, 189, 64, 22)
    $label.TextFrame.TextRange.Text = '301'
    $labelName = [string]$label.Name
    $lineName = [string]$line.Name
    $range = $slide.Shapes.Range(@($lineName, $labelName))
    $group = $range.Group()
    $group.Tags.Add('PATMARKER', 'PATENTMARKER')
    $group.Tags.Add('PATNUMBER', '301')
    Emit ("created|group=" + $group.Name + "|items=" + $group.GroupItems.Count)

    # The staged DLL is downloaded-blocked; load its bytes so no zone marker applies.
    $asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Resolve-Path $asmPath).Path))
    $ann = $asm.GetType('PatentOffice.PowerPoint.PowerPointAnnotations', $true)
    $flags = [Reflection.BindingFlags]'Public,Static'
    $getMarked = $ann.GetMethod('GetMarkedNumbers', $flags)

    function Invoke-Marked { return (($getMarked.Invoke($null, @($pres)) | ForEach-Object { $_ }) -join ',') }

    Emit ("top-level|marked=" + (Invoke-Marked))

    # The user groups the annotation together with an unrelated shape.
    $extra = $slide.Shapes.AddShape(1, 700, 300, 100, 60)   # msoShapeRectangle
    $outer = $slide.Shapes.Range(@([string]$group.Name, [string]$extra.Name)).Group()
    Emit ("nested|outerItems=" + $outer.GroupItems.Count + "|marked=" + (Invoke-Marked))

    # Save and reopen: does the tag survive the file round trip?
    $pres.SaveAs($file)
    $pres.Close()
    $reopened = $ppt.Presentations.Open($file, $false, $false, $false)
    Emit ("after-reopen|marked=" + (Invoke-Marked $reopened))

    # The user retypes the label text inside the annotation group.
    $slide2 = $reopened.Slides.Item(1)
    $outerShape = $null
    for ($i = 1; $i -le $slide2.Shapes.Count; $i++) {
        $s = $slide2.Shapes.Item($i)
        if ($s.Type -eq 6) { $outerShape = $s; break }
    }
    $annotation = $outerShape.GroupItems.Item(1).GroupItems.Item(1)
    $textItem = $outerShape.GroupItems.Item(1).GroupItems.Item(2)
    $textItem.TextFrame.TextRange.Text = '999'
    Emit ("after-text-edit|tag=" + $annotation.Tags.Value($annotation.Tags.Count) + "|visible=999|marked=" + (Invoke-Marked $reopened))
}
finally {
    try { if ($pres -and $pres.Name) { $pres.Saved = $true; $pres.Close() } } catch { }
    $ppt.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($ppt)
}
Set-Content -LiteralPath (Join-Path $outRoot 'probe.txt') -Value $log -Encoding UTF8

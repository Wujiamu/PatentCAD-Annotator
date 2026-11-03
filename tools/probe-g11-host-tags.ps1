param([string]$OutRoot)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutRoot)) { $OutRoot = Join-Path $env:TEMP 'g11-tags' }
New-Item -ItemType Directory -Path $OutRoot -Force | Out-Null
$log = @()
function Emit($s) { $script:log += $s; Write-Host $s }

function Dump-Shape($prefix, $shape) {
    $tagText = ''
    try {
        $tags = $shape.Tags
        for ($i = 1; $i -le $tags.Count; $i++) { $tagText += ("{0}={1};" -f $tags.Name($i), $tags.Value($i)) }
    } catch { $tagText = '<no tags>' }
    Emit ("{0}|name={1}|type={2}|items={3}|tags=[{4}]" -f $prefix, $shape.Name, $shape.Type,
        $(try { $shape.GroupItems.Count } catch { '-' }), $tagText)
}

$ppt = New-Object -ComObject PowerPoint.Application
$ppt.Visible = -1
$pres = $ppt.Presentations.Add($true)
try {
    $slide = $pres.Slides.Add(1, 12)
    $line = $slide.Shapes.AddLine(200, 200, 500, 200)
    $label = $slide.Shapes.AddTextbox(1, 168, 189, 64, 22)
    $label.TextFrame.TextRange.Text = '301'
    $range = $slide.Shapes.Range(@([string]$line.Name, [string]$label.Name))
    $group = $range.Group()
    $group.Tags.Add('PATMARKER', 'PATENTMARKER')
    $group.Tags.Add('PATNUMBER', '301')
    Dump-Shape 'inner-group' $group

    # How does PowerPoint model "group an existing group with a rectangle"?
    $extra = $slide.Shapes.AddShape(1, 700, 300, 100, 60)
    $outer = $slide.Shapes.Range(@([string]$group.Name, [string]$extra.Name)).Group()
    Dump-Shape 'outer-group' $outer
    for ($i = 1; $i -le $outer.GroupItems.Count; $i++) {
        Dump-Shape ("  outer.item{0}" -f $i) $outer.GroupItems.Item($i)
    }

    # And a真 nested group: group two plain shapes first, then group that with the annotation.
    $pres.Slides.Add(2, 12) | Out-Null
    $slide2 = $pres.Slides.Item(2)
    $a = $slide2.Shapes.AddShape(1, 100, 100, 50, 50)
    $b = $slide2.Shapes.AddShape(1, 200, 100, 50, 50)
    $innerPlain = $slide2.Shapes.Range(@([string]$a.Name, [string]$b.Name)).Group()
    $line2 = $slide2.Shapes.AddLine(300, 300, 600, 300)
    $label2 = $slide2.Shapes.AddTextbox(1, 268, 289, 64, 22)
    $label2.TextFrame.TextRange.Text = '302'
    $g2 = $slide2.Shapes.Range(@([string]$line2.Name, [string]$label2.Name)).Group()
    $g2.Tags.Add('PATMARKER', 'PATENTMARKER')
    $g2.Tags.Add('PATNUMBER', '302')
    $outer2 = $slide2.Shapes.Range(@([string]$innerPlain.Name, [string]$g2.Name)).Group()
    Dump-Shape 'outer2' $outer2
    for ($i = 1; $i -le $outer2.GroupItems.Count; $i++) {
        Dump-Shape ("  outer2.item{0}" -f $i) $outer2.GroupItems.Item($i)
        $child = $outer2.GroupItems.Item($i)
        try {
            for ($j = 1; $j -le $child.GroupItems.Count; $j++) {
                Dump-Shape ("    outer2.item{0}.item{1}" -f $i, $j) $child.GroupItems.Item($j)
            }
        } catch { }
    }
}
finally {
    try { $pres.Saved = $true; $pres.Close() } catch { }
    $ppt.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($ppt)
}
Set-Content -LiteralPath (Join-Path $OutRoot 'probe.txt') -Value $log -Encoding UTF8

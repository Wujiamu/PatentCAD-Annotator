# R-031 probe: record real PowerPoint line geometry properties.
# Read-only w.r.t. user documents: works on a brand-new unsaved presentation
# in a private PowerPoint instance and quits it at the end.
$ErrorActionPreference = 'Stop'
$out = @()
function Emit($s) { $script:out += $s; Write-Host $s }

$ppt = New-Object -ComObject PowerPoint.Application
$pres = $ppt.Presentations.Add($true)   # WithWindow = true (COM requires a window)
try {
    $slide = $pres.Slides.Add(1, 12)     # ppLayoutBlank

    function Describe($label, $shape) {
        $line = $shape.Line
        Emit ("{0}|left={1}|top={2}|width={3}|height={4}|rotation={5}|flipH={6}|flipV={7}|beginArrow={8}|endArrow={9}" -f `
            $label, $shape.Left, $shape.Top, $shape.Width, $shape.Height, $shape.Rotation,
            $shape.HorizontalFlip, $shape.VerticalFlip, $line.BeginArrowheadStyle, $line.EndArrowheadStyle)
    }

    # Case 1: forward draw (left-top to right-bottom)
    $a = $slide.Shapes.AddLine(100, 100, 300, 200)
    Describe 'forward-0' $a

    # Case 2: reverse draw (right-bottom to left-top)
    $b = $slide.Shapes.AddLine(300, 200, 100, 100)
    Describe 'reverse-0' $b

    # Case 3: same bounding box, rotation applied
    foreach ($angle in 90, 180, 270) {
        $c = $slide.Shapes.AddLine(100, 100, 300, 200)
        $c.Rotation = $angle
        Describe ("forward-rot{0}" -f $angle) $c
        $c.Delete()
    }

    # Case 4: horizontal line rotated 180 (the R-031 model counter-example)
    $h = $slide.Shapes.AddLine(10, 20, 110, 20)
    Describe 'horizontal-0' $h
    $h.Rotation = 180
    Describe 'horizontal-rot180' $h

    # Case 5: flips on a horizontal line
    $h.HorizontalFlip = -1
    Describe 'horizontal-rot180-flipH' $h
    $h.HorizontalFlip = 0
    $h.VerticalFlip = -1
    Describe 'horizontal-rot180-flipV' $h
    $h.VerticalFlip = 0
    $h.Rotation = 0
    Describe 'horizontal-reset' $h

    # Case 6: diagonal with both flips, no rotation
    $d = $slide.Shapes.AddLine(100, 100, 300, 200)
    $d.HorizontalFlip = -1
    $d.VerticalFlip = -1
    Describe 'diagonal-flipHV' $d

    # Case 7: arrow direction marker - which end carries the arrowhead
    $e = $slide.Shapes.AddLine(100, 100, 300, 100)
    $e.Line.BeginArrowheadStyle = 1
    $e.Line.EndArrowheadStyle = 2
    Describe 'arrow-end' $e

    Emit ("POWERPOINT_VERSION|{0}" -f $ppt.Version)
}
finally {
    $pres.Saved = $true          # discard: never write a file
    $pres.Close()
    $ppt.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($ppt)
}

$target = if ($args.Count -ge 1) { $args[0] } else { Join-Path $env:TEMP 'r031-probe.txt' }
$dir = Split-Path -Parent $target
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
Set-Content -LiteralPath $target -Value $out -Encoding UTF8
Write-Host "written: $target"

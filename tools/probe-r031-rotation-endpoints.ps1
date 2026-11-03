# R-031 verification probe: does Shape.Rotation move the logical line endpoints?
#
# Method: a straight segment rotated 180 degrees covers exactly the same pixels,
# so a rendered image alone cannot tell which end is "start". The observable
# difference is the arrowhead: it is drawn at the end arrowhead position. We set
# an end arrowhead, rotate the line 180 degrees, export the slide to PNG and find
# which side of the segment carries the dark arrowhead pixels.
#
# Uses a private PowerPoint instance and a brand-new unsaved presentation.
# Never opens or saves a user document.
$ErrorActionPreference = 'Stop'
$target = if ($args.Count -ge 1) { $args[0] } else { Join-Path $env:TEMP 'r031-visual' }
New-Item -ItemType Directory -Path $target -Force | Out-Null
$lines = @()
function Emit($s) { $script:lines += $s; Write-Host $s }

Add-Type -AssemblyName System.Drawing

function Measure-DarkExtents($png, $roiX, $roiY, $roiW, $roiH) {
    $bmp = [System.Drawing.Bitmap]::FromFile($png)
    try {
        $minX = [int]::MaxValue; $maxX = -1; $minY = [int]::MaxValue; $maxY = -1
        $count = 0
        for ($y = $roiY; $y -lt [Math]::Min($roiY + $roiH, $bmp.Height); $y++) {
            for ($x = $roiX; $x -lt [Math]::Min($roiX + $roiW, $bmp.Width); $x++) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.R -lt 128 -and $c.G -lt 128 -and $c.B -lt 128) {
                    $count++
                    if ($x -lt $minX) { $minX = $x }
                    if ($x -gt $maxX) { $maxX = $x }
                    if ($y -lt $minY) { $minY = $y }
                    if ($y -gt $maxY) { $maxY = $y }
                }
            }
        }
        return [pscustomobject]@{ Count = $count; MinX = $minX; MaxX = $maxX; MinY = $minY; MaxY = $maxY }
    }
    finally { $bmp.Dispose() }
}

$ppt = New-Object -ComObject PowerPoint.Application
$pres = $ppt.Presentations.Add($true)
try {
    $slide = $pres.Slides.Add(1, 12)
    # One horizontal line, wide enough for a clear left/right arrowhead signal.
    $line = $slide.Shapes.AddLine(120, 200, 600, 200)
    $line.Line.Weight = 2.0
    $line.Line.BeginArrowheadStyle = 1   # msoArrowheadNone
    $line.Line.EndArrowheadStyle = 2     # msoArrowheadTriangle
    Emit ("shape|left={0}|top={1}|width={2}|height={3}|rotation={4}|flipH={5}|flipV={6}" -f `
        $line.Left, $line.Top, $line.Width, $line.Height, $line.Rotation,
        $line.HorizontalFlip, $line.VerticalFlip)

    function Snap($tag) {
        $png = Join-Path $target ("$tag.png")
        if (Test-Path $png) { Remove-Item $png -Force }
        $slide.Export($png, 'PNG', 1280, 720)
        Start-Sleep -Milliseconds 150
        # ROI around the segment row band; the arrowhead is thicker than the line.
        $m = Measure-DarkExtents $png 0 150 1280 120
        Emit ("{0}|darkCount={1}|minX={2}|maxX={3}|minY={4}|maxY={5}" -f `
            $tag, $m.Count, $m.MinX, $m.MaxX, $m.MinY, $m.MaxY)
        return $m
    }

    $before = Snap 'rot0'
    $line.Rotation = 180
    Emit ("shape-after-rot180|left={0}|top={1}|width={2}|height={3}|rotation={4}|flipH={5}|flipV={6}" -f `
        $line.Left, $line.Top, $line.Width, $line.Height, $line.Rotation,
        $line.HorizontalFlip, $line.VerticalFlip)
    $after = Snap 'rot180'

    # Compare the vertical thickness at both ends: the arrowhead end is thicker.
    function EndThickness($png, $x, $y0, $y1) {
        $bmp = [System.Drawing.Bitmap]::FromFile($png)
        try {
            $n = 0
            for ($y = $y0; $y -lt [Math]::Min($y1, $bmp.Height); $y++) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.R -lt 128 -and $c.G -lt 128 -and $c.B -lt 128) { $n++ }
            }
            return $n
        }
        finally { $bmp.Dispose() }
    }

    foreach ($pair in @(@('rot0', $before), @('rot180', $after))) {
        $tag = $pair[0]; $m = $pair[1]
        $leftX = $m.MinX + 6
        $rightX = $m.MaxX - 6
        $lt = EndThickness (Join-Path $target "$tag.png") $leftX 150 270
        $rt = EndThickness (Join-Path $target "$tag.png") $rightX 150 270
        Emit ("{0}|leftEdgeThickness={1}|rightEdgeThickness={2}|arrowEnd={3}" -f `
            $tag, $lt, $rt, $(if ($lt -gt $rt) { 'LEFT' } else { 'RIGHT' }))
    }
}
finally {
    $pres.Saved = $true
    $pres.Close()
    $ppt.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($ppt)
}

Set-Content -LiteralPath (Join-Path $target 'probe.txt') -Value $lines -Encoding UTF8
Write-Host "written: $target"

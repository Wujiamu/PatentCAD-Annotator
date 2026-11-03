# R-031 verification probe, part 2: determine the rotation sign and the
# effect of flips, using the arrowhead as the observable "end" marker.
#
# A straight segment rotated by N degrees covers the same pixels as its
# unrotated self only at 0/180, so the arrowhead is the only reliable way to
# see which logical end is which. The arrowhead is thicker than the line, so
# the column with the most non-white pixels marks the arrow end.
#
# Uses a private PowerPoint instance and a brand-new unsaved presentation.
$ErrorActionPreference = 'Stop'
$target = if ($args.Count -ge 1) { $args[0] } else { Join-Path $env:TEMP 'r031-sign' }
New-Item -ItemType Directory -Path $target -Force | Out-Null
$lines = @()
function Emit($s) { $script:lines += $s; Write-Host $s }

Add-Type -AssemblyName System.Drawing

function Column-Thickness($png) {
    $bmp = [System.Drawing.Bitmap]::FromFile($png)
    try {
        $cols = @{}
        $minX = [int]::MaxValue; $maxX = -1; $minY = [int]::MaxValue; $maxY = -1
        for ($y = 0; $y -lt $bmp.Height; $y++) {
            for ($x = 0; $x -lt $bmp.Width; $x++) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.R -gt 245 -and $c.G -gt 245 -and $c.B -gt 245) { continue }
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
                if ($cols.ContainsKey($x)) { $cols[$x]++ } else { $cols[$x] = 1 }
            }
        }
        if ($maxX -lt 0) { return $null }
        # Arrow end = the outermost column band with more pixels than the shaft.
        $band = [Math]::Max(3, [int](($maxX - $minX) * 0.06))
        $leftMax = 0; $rightMax = 0
        for ($x = $minX; $x -lt [Math]::Min($minX + $band, $maxX); $x++) {
            if ($cols.ContainsKey($x) -and $cols[$x] -gt $leftMax) { $leftMax = $cols[$x] }
        }
        for ($x = [Math]::Max($maxX - $band, $minX); $x -le $maxX; $x++) {
            if ($cols.ContainsKey($x) -and $cols[$x] -gt $rightMax) { $rightMax = $cols[$x] }
        }
        return [pscustomobject]@{
            MinX = $minX; MaxX = $maxX; MinY = $minY; MaxY = $maxY
            LeftThickness = $leftMax; RightThickness = $rightMax
            TopThickness = 0; BottomThickness = 0
        }
    }
    finally { $bmp.Dispose() }
}

$ppt = New-Object -ComObject PowerPoint.Application
$pres = $ppt.Presentations.Add($true)
try {
    Emit ("POWERPOINT_VERSION|{0}" -f $ppt.Version)

    function Snap($tag) {
        $png = Join-Path $target ("$tag.png")
        if (Test-Path $png) { Remove-Item $png -Force }
        $script:slide.Export($png, 'PNG', 1280, 720)
        Start-Sleep -Milliseconds 120
        return (Column-Thickness $png)
    }

    # --- A: horizontal segment, arrowhead on the END, rotations 0/90/180/270
    foreach ($angle in 0, 90, 180, 270) {
        $script:slide = $pres.Slides.Add($pres.Slides.Count + 1, 12)
        $line = $script:slide.Shapes.AddLine(300, 360, 900, 360)
        $line.Line.Weight = 2.5
        $line.Line.BeginArrowheadStyle = 1
        $line.Line.EndArrowheadStyle = 2
        $line.Rotation = $angle
        $m = Snap ("h-rot$angle")
        if ($null -eq $m) { Emit ("h-rot{0}|NO_INK" -f $angle); continue }
        Emit ("h-rot{0}|left={1}|top={2}|w={3}|h={4}|rot={5}|flipH={6}|flipV={7}|inkMinX={8}|inkMaxX={9}|inkMinY={10}|inkMaxY={11}|Lthick={12}|Rthick={13}" -f `
            $angle, $line.Left, $line.Top, $line.Width, $line.Height, $line.Rotation,
            $line.HorizontalFlip, $line.VerticalFlip, $m.MinX, $m.MaxX, $m.MinY, $m.MaxY,
            $m.LeftThickness, $m.RightThickness)
    }

    # --- B: diagonal, arrowhead on the END, rotations 0/90
    foreach ($angle in 0, 90) {
        $script:slide = $pres.Slides.Add($pres.Slides.Count + 1, 12)
        $line = $script:slide.Shapes.AddLine(300, 200, 900, 560)
        $line.Line.Weight = 2.5
        $line.Line.BeginArrowheadStyle = 1
        $line.Line.EndArrowheadStyle = 2
        $line.Rotation = $angle
        $m = Snap ("d-rot$angle")
        if ($null -eq $m) { Emit ("d-rot{0}|NO_INK" -f $angle); continue }
        Emit ("d-rot{0}|left={1}|top={2}|w={3}|h={4}|rot={5}|flipH={6}|flipV={7}|inkMinX={8}|inkMaxX={9}|inkMinY={10}|inkMaxY={11}|Lthick={12}|Rthick={13}" -f `
            $angle, $line.Left, $line.Top, $line.Width, $line.Height, $line.Rotation,
            $line.HorizontalFlip, $line.VerticalFlip, $m.MinX, $m.MaxX, $m.MinY, $m.MaxY,
            $m.LeftThickness, $m.RightThickness)
    }

    # --- C: forward vs reverse draw (does drawing direction encode in flips?)
    $script:slide = $pres.Slides.Add($pres.Slides.Count + 1, 12)
    $fwd = $script:slide.Shapes.AddLine(300, 360, 900, 360)
    $fwd.Line.BeginArrowheadStyle = 1; $fwd.Line.EndArrowheadStyle = 2
    $m = Snap 'fwd'
    Emit ("fwd|left={0}|top={1}|w={2}|h={3}|rot={4}|flipH={5}|flipV={6}|Lthick={7}|Rthick={8}" -f `
        $fwd.Left, $fwd.Top, $fwd.Width, $fwd.Height, $fwd.Rotation,
        $fwd.HorizontalFlip, $fwd.VerticalFlip, $m.LeftThickness, $m.RightThickness)
    $fwd.Delete()

    $rev = $script:slide.Shapes.AddLine(900, 360, 300, 360)
    $rev.Line.BeginArrowheadStyle = 1; $rev.Line.EndArrowheadStyle = 2
    $m = Snap 'rev'
    Emit ("rev|left={0}|top={1}|w={2}|h={3}|rot={4}|flipH={5}|flipV={6}|Lthick={7}|Rthick={8}" -f `
        $rev.Left, $rev.Top, $rev.Width, $rev.Height, $rev.Rotation,
        $rev.HorizontalFlip, $rev.VerticalFlip, $m.LeftThickness, $m.RightThickness)
}
finally {
    $pres.Saved = $true
    $pres.Close()
    $ppt.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($ppt)
}

Set-Content -LiteralPath (Join-Path $target 'probe.txt') -Value $lines -Encoding UTF8
Write-Host "written: $target"

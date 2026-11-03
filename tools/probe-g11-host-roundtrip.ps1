param([string]$OutRoot)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutRoot)) { $OutRoot = Join-Path $env:TEMP 'g11-tags2' }
New-Item -ItemType Directory -Path $OutRoot -Force | Out-Null
$log = @()
function Emit($s) { $script:log += $s; Write-Host $s }

function TagText($shape) {
    try {
        $t = ''
        $tags = $shape.Tags
        for ($i = 1; $i -le $tags.Count; $i++) { $t += ("{0}={1};" -f $tags.Name($i), $tags.Value($i)) }
        return $t
    } catch { return '<no tags>' }
}

$asmPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'office-com-addin/dist/PatentOffice.PowerPoint.dll'
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Resolve-Path $asmPath).Path))
$ann = $asm.GetType('PatentOffice.PowerPoint.PowerPointAnnotations', $true)
$flags = [Reflection.BindingFlags]'Public,Static'
$getMarked = $ann.GetMethod('GetMarkedNumbers', $flags)

$ppt = New-Object -ComObject PowerPoint.Application
$ppt.Visible = -1
$pres = $ppt.Presentations.Add($true)
$file = Join-Path $OutRoot 'g11-roundtrip.pptx'
try {
    $slide = $pres.Slides.Add(1, 12)
    $line = $slide.Shapes.AddLine(200, 200, 500, 200)
    $label = $slide.Shapes.AddTextbox(1, 168, 189, 64, 22)
    $label.TextFrame.TextRange.Text = '301'
    $g = $slide.Shapes.Range(@([string]$line.Name, [string]$label.Name)).Group()
    $g.Tags.Add('PATMARKER', 'PATENTMARKER')
    $g.Tags.Add('PATNUMBER', '301')
    Emit ("1.created|tags=[" + (TagText $g) + "]|marked=" + (($getMarked.Invoke($null, @($pres)) -join ',')))

    # Duplicate (copy/paste) - does a copy keep the tags?
    $g.Copy()
    $slide.Shapes.Paste() | Out-Null
    $pasted = $slide.Shapes.Item($slide.Shapes.Count)
    Emit ("2.pasted|name=" + $pasted.Name + "|type=" + $pasted.Type + "|tags=[" + (TagText $pasted) + "]|marked=" + (($getMarked.Invoke($null, @($pres)) -join ',')))

    # Edit the visible label text, then save/reopen.
    $pres.SaveAs($file)
    $pres.Close()
    $re = $ppt.Presentations.Open($file, $false, $false, $false)
    Emit ("3.reopened|marked=" + (($getMarked.Invoke($null, @($re)) -join ',')))
    $s = $re.Slides.Item(1)
    $annGroup = $null
    for ($i = 1; $i -le $s.Shapes.Count; $i++) {
        $sh = $s.Shapes.Item($i)
        if ($sh.Type -eq 6 -and (TagText $sh) -ne '') { $annGroup = $sh; break }
    }
    if ($annGroup) {
        for ($i = 1; $i -le $annGroup.GroupItems.Count; $i++) {
            $child = $annGroup.GroupItems.Item($i)
            if ($child.Type -eq 17) {
                $child.TextFrame.TextRange.Text = '999'
                Emit ("4a.readback|child=" + $child.Name + "|text=[" + $child.TextFrame.TextRange.Text + "]|parentItems=" + $annGroup.GroupItems.Count + "|parentTags=[" + (TagText $annGroup) + "]")
        # re-read the group's own view of the same child
        $again = $annGroup.GroupItems.Item($i)
        Emit ("4b.reread|text=[" + $again.TextFrame.TextRange.Text + "]")
            }
        }
        Emit ("4.text-edited|tags=[" + (TagText $annGroup) + "]|marked=" + (($getMarked.Invoke($null, @($re)) -join ',')))
        $re.Save()
        $re.Close()
        $re2 = $ppt.Presentations.Open($file, $false, $false, $false)
        Emit ("5.reopen-after-edit|marked=" + (($getMarked.Invoke($null, @($re2)) -join ',')))
        $re2.Close()
    }
}
finally {
    try { if ($pres.Name) { $pres.Saved = $true; $pres.Close() } } catch { }
    $ppt.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($ppt)
}
Set-Content -LiteralPath (Join-Path $OutRoot 'probe.txt') -Value $log -Encoding UTF8

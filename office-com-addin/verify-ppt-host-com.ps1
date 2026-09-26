[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AssemblyPath,
    [Parameter(Mandatory = $true)][string]$DictionaryPath,
    [Parameter(Mandatory = $true)][string]$PresentationPath,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $PSScriptRoot 'test-evidence' }
$assemblyFull = [IO.Path]::GetFullPath($AssemblyPath)
$dictionaryFull = [IO.Path]::GetFullPath($DictionaryPath)
$presentationFull = [IO.Path]::GetFullPath($PresentationPath)
foreach ($path in @($assemblyFull, $dictionaryFull, $presentationFull)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "输入文件不存在：$path" }
}
$processes = @(Get-Process -Name POWERPNT -ErrorAction SilentlyContinue)
if ($processes.Count -ne 1) { throw '要求恰好一个正在运行的 PowerPoint 进程。' }
$app = [Runtime.InteropServices.Marshal]::GetActiveObject('PowerPoint.Application')
if ($app.Presentations.Count -ne 1) { throw '测试前只能打开一份演示文稿。' }
$presentation = $app.Presentations.Item(1)
if (-not [string]::Equals([string]$presentation.FullName, $presentationFull,
        [StringComparison]::OrdinalIgnoreCase)) { throw '当前演示文稿不是本次测试产物。' }
if ($presentation.Slides.Count -ne 2) { throw '测试前必须有两页。' }

$root = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ('ppt-host-com-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $root | Out-Null
$dictHashBefore = (Get-FileHash -LiteralPath $dictionaryFull -Algorithm SHA256).Hash
$assembly = [Reflection.Assembly]::LoadFrom($assemblyFull)
$flags = [Reflection.BindingFlags]'Public,Static'
$reader = $assembly.GetType('PatentOffice.PowerPoint.DictionaryReader', $true)
$binding = $assembly.GetType('PatentOffice.PowerPoint.PresentationBinding', $true)
$annotations = $assembly.GetType('PatentOffice.PowerPoint.PowerPointAnnotations', $true)
$snapshot = $reader.GetMethod('Read', $flags).Invoke($null, @($dictionaryFull))
$dictionary = $snapshot.GetType().GetProperty('Dictionary').GetValue($snapshot, $null)
$entries = $dictionary.GetType().GetProperty('Entries').GetValue($dictionary, $null)
if ($entries.Count -ne 2) { throw '真实 Word 字典应有两个条目。' }

function Invoke-ProductMethod {
    param([Reflection.MethodInfo]$Method, [object[]]$Arguments)
    try { return $Method.Invoke($null, $Arguments) }
    catch [Reflection.TargetInvocationException] { throw $_.Exception.InnerException }
}

$saveBinding = $binding.GetMethod('Save', $flags)
$resolveBinding = $binding.GetMethod('Resolve', $flags)
$markLine = $annotations.GetMethod('MarkSelectedLine', $flags)
$getMarked = $annotations.GetMethod('GetMarkedNumbers', $flags)
$initialMarked = Invoke-ProductMethod $getMarked @($presentation)
if ($initialMarked.Count -ne 0) { throw '测试前不应有产品标注。' }
Invoke-ProductMethod $saveBinding @($presentation, $dictionaryFull) | Out-Null
$resolved = Invoke-ProductMethod $resolveBinding @($presentation)
$resolvedPath = [string]$resolved.GetType().GetProperty('Path').GetValue($resolved, $null)
if (-not [string]::Equals($resolvedPath, $dictionaryFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Custom XML 绑定解析结果不等于手选字典。'
}

$slide1 = $presentation.Slides.Item(1)
$slide2 = $presentation.Slides.Item(2)
$picture = $slide1.Shapes.Item(1)
$pictureName = [string]$picture.Name
$app.ActiveWindow.View.GotoSlide(1)
$picture.Select()
$negativeRejected = $false
try { Invoke-ProductMethod $markLine @($app, $entries[0]) | Out-Null }
catch { $negativeRejected = $_.Exception.Message -like '*不是 PowerPoint 原生直线*' }
if (-not $negativeRejected -or $slide1.Shapes.Count -ne 2) {
    throw '普通图片选择未被安全拒绝，或负例改变了形状。'
}

$line1 = $slide1.Shapes.Item('PM_TEST_LINE_1')
$line1.Select()
Invoke-ProductMethod $markLine @($app, $entries[0]) | Out-Null
$marked = Invoke-ProductMethod $getMarked @($presentation)
if ($marked.Count -ne 1 -or -not $marked.Contains('1')) { throw '第一页编号 1 标注未被识别。' }

$app.ActiveWindow.View.GotoSlide(2)
$line2 = $slide2.Shapes.Item('PM_TEST_LINE_2')
$line2.Select()
Invoke-ProductMethod $markLine @($app, $entries[0]) | Out-Null
$marked = Invoke-ProductMethod $getMarked @($presentation)
if ($marked.Count -ne 1 -or -not $marked.Contains('1')) { throw '同号重复标注后去重检查失败。' }

$line3 = $slide2.Shapes.AddLine(100, 70, 345, 195)
$line3.Name = 'PM_TEST_LINE_3'
$line3.Select()
Invoke-ProductMethod $markLine @($app, $entries[1]) | Out-Null
$marked = Invoke-ProductMethod $getMarked @($presentation)
if ($marked.Count -ne 2 -or -not $marked.Contains('1') -or -not $marked.Contains('2')) {
    throw '跨页编号 1、2 扫描结果错误。'
}
if ([string]$slide1.Shapes.Item($pictureName).Name -ne $pictureName -or
    [int]$slide1.Shapes.Item($pictureName).Type -ne 13) {
    throw '原图片被修改或替换。'
}

$presentation.Save()
$presentation.Close()
$reopened = $app.Presentations.Open($presentationFull)
try {
    $rebound = Invoke-ProductMethod $resolveBinding @($reopened)
    $reboundPath = [string]$rebound.GetType().GetProperty('Path').GetValue($rebound, $null)
    $markedAfterReopen = Invoke-ProductMethod $getMarked @($reopened)
    if (-not [string]::Equals($reboundPath, $dictionaryFull, [StringComparison]::OrdinalIgnoreCase) -or
        $markedAfterReopen.Count -ne 2 -or -not $markedAfterReopen.Contains('1') -or
        -not $markedAfterReopen.Contains('2')) {
        throw '保存重开后绑定或产品 Tags 未恢复。'
    }
    $dictHashAfter = (Get-FileHash -LiteralPath $dictionaryFull -Algorithm SHA256).Hash
    if ($dictHashAfter -ne $dictHashBefore) { throw 'Word 字典字节发生变化。' }
    $result = [ordered]@{
        Outcome = 'PASS'
        Level = 'L2_REAL_POWERPOINT_COM_OBJECTS_NO_PANEL'
        ProcessId = $processes[0].Id
        HostVersion = [string]$app.Version
        AssemblySha256 = (Get-FileHash -LiteralPath $assemblyFull -Algorithm SHA256).Hash
        DictionarySha256Before = $dictHashBefore
        DictionarySha256After = $dictHashAfter
        PresentationSha256 = (Get-FileHash -LiteralPath $presentationFull -Algorithm SHA256).Hash
        NegativePictureRejected = $negativeRejected
        MarkedNumbersAfterReopen = @('1', '2')
        DuplicateNumberAccepted = $true
    }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'result.json') -Encoding UTF8
    Write-Output "PASS|PPT_HOST_COM|$root"
}
finally { $reopened.Close() }

# 测试脚本：模拟 VBA Patterns.bas + DictModel.bas 的识别逻辑（使用 VBScript.RegExp COM，行为一致）
$ErrorActionPreference = 'Stop'
$out = New-Object System.Text.StringBuilder
function Log([string]$s) { [void]$out.AppendLine($s) }

function New-Regex([string]$pattern, [bool]$globalMatch, [bool]$ignoreCase) {
    $re = New-Object -ComObject VBScript.RegExp
    $re.Global = $globalMatch
    $re.IgnoreCase = $ignoreCase
    $re.Multiline = $false
    $re.Pattern = $pattern
    return $re
}

# ---- 模拟 Patterns.ExtractAll（$fixedP1：是否修复模式1缺全角分号；$debug：输出各模式命中）----
function ExtractAll([string]$text, [bool]$fixedP1, [bool]$debug) {
    $allHits = New-Object System.Collections.ArrayList
    $keepRanges = New-Object System.Collections.ArrayList

    # 预处理
    $text = $text.Replace('<br/>', ' ').Replace('<br />', ' ').Replace('<br>', ' ').Replace('<BR/>', ' ')
    $text = $text.Replace([string][char]13, [string][char]10)  # vbCr -> vbLf

    # 第一梯队：旧格式（编号在前）
    if ($fixedP1) {
        $p1 = '(\d{1,5})\s*([\u4e00-\u9fa5A-Za-z0-9]*[\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9]*)\s*[，；;,、。.]'
    } else {
        $p1 = '(\d{1,5})\s*([\u4e00-\u9fa5A-Za-z0-9]*[\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9]*)\s*[，;,、。.]'
    }
    $re = New-Regex $p1 $true $false
    $m = $re.Execute($text)
    for ($i = 0; $i -lt $m.Count; $i++) {
        $matchObj = $m.Item($i)
        $number = [string]$matchObj.SubMatches.Item(0)
        $name = [string]$matchObj.SubMatches.Item(1)
        $pos = [int]$matchObj.FirstIndex
        $len = [int]$matchObj.Length
        if ($debug) { Log ("[p1] " + $number + " = " + $name) }
        [void]$allHits.Add(@($number, $name, $pos, $len))
        [void]$keepRanges.Add(@($pos, ($pos + $len - 1)))
    }

    $pB = '(\d{1,5})\s*[（(]([\u4e00-\u9fa5A-Za-z0-9]*[\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9]*)[）)]\s*[，;；,、。.]'
    $reB = New-Regex $pB $true $false
    $mB = $reB.Execute($text)
    for ($i = 0; $i -lt $mB.Count; $i++) {
        $matchObj = $mB.Item($i)
        $number = [string]$matchObj.SubMatches.Item(0)
        $name = [string]$matchObj.SubMatches.Item(1)
        $pos = [int]$matchObj.FirstIndex
        $len = [int]$matchObj.Length
        if ($debug) { Log ("[pB] " + $number + " = " + $name) }
        [void]$allHits.Add(@($number, $name, $pos, $len))
        [void]$keepRanges.Add(@($pos, ($pos + $len - 1)))
    }

    # 第二梯队：新格式（名称在前），与第一梯队重叠则丢弃
    $candHits = New-Object System.Collections.ArrayList
    $candRanges = New-Object System.Collections.ArrayList

    $p2 = '([\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9 ]*?)\s*([A-Z]?\d{1,5}(?:-[A-Z]?\d{1,5})?[a-z]?)\s*[、；。，;,.]'
    $re2 = New-Regex $p2 $true $false
    $m2 = $re2.Execute($text)
    for ($i = 0; $i -lt $m2.Count; $i++) {
        $matchObj = $m2.Item($i)
        $name = [string]$matchObj.SubMatches.Item(0)
        $number = [string]$matchObj.SubMatches.Item(1)
        $pos = [int]$matchObj.FirstIndex
        $len = [int]$matchObj.Length
        if ($debug) { Log ("[p2] " + $number + " = " + $name) }
        [void]$candHits.Add(@($number, $name, $pos, $len))
        [void]$candRanges.Add(@($pos, ($pos + $len - 1)))
    }

    $pA = '([\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9 ]*?)\s*[（(]([A-Z]?\d{1,5}(?:-[A-Z]?\d{1,5})?[a-z]?)[）)]\s*[、；。，;,.]'
    $reA = New-Regex $pA $true $false
    $mA = $reA.Execute($text)
    for ($i = 0; $i -lt $mA.Count; $i++) {
        $matchObj = $mA.Item($i)
        $name = [string]$matchObj.SubMatches.Item(0)
        $number = [string]$matchObj.SubMatches.Item(1)
        $pos = [int]$matchObj.FirstIndex
        $len = [int]$matchObj.Length
        if ($debug) { Log ("[pA] " + $number + " = " + $name) }
        [void]$candHits.Add(@($number, $name, $pos, $len))
        [void]$candRanges.Add(@($pos, ($pos + $len - 1)))
    }

    function Test-Overlaps($hit, $ranges) {
        $start = $hit[2]; $end = $hit[2] + $hit[3] - 1
        foreach ($r in $ranges) {
            if ($start -le $r[1] -and $end -ge $r[0]) { return $true }
        }
        return $false
    }

    for ($i = 0; $i -lt $candHits.Count; $i++) {
        $ch = $candHits[$i]
        if (-not (Test-Overlaps $ch $keepRanges)) {
            if ($debug) { Log ("[cand-keep] " + $ch[0] + " = " + $ch[1]) }
            [void]$allHits.Add($ch)
            [void]$keepRanges.Add(@($ch[2], ($ch[2] + $ch[3] - 1)))
        }
    }

    # 第三梯队：裸列表
    $p3 = '^\s*([\u4e00-\u9fa5A-Za-z ]+?)\s*([A-Z]?\d{1,5}(?:-[A-Z]?\d{1,5})?[a-z]?)\s*$'
    $re3 = New-Regex $p3 $true $false
    $re3.Multiline = $true
    $m3 = $re3.Execute($text)
    for ($i = 0; $i -lt $m3.Count; $i++) {
        $matchObj = $m3.Item($i)
        $name3 = [string]$matchObj.SubMatches.Item(0)
        $number3 = [string]$matchObj.SubMatches.Item(1)
        $pos3 = [int]$matchObj.FirstIndex
        $len3 = [int]$matchObj.Length
        $cand3 = @($number3, $name3, $pos3, $len3)
        if (-not (Test-Overlaps $cand3 $keepRanges)) {
            if ($debug) { Log ("[p3] " + $number3 + " = " + $name3) }
            [void]$allHits.Add($cand3)
        }
    }

    # 去重（number|name）
    $dict = @{}
    $outHits = New-Object System.Collections.ArrayList
    foreach ($h in $allHits) {
        $key = "$($h[0])|$($h[1])"
        if (-not $dict.ContainsKey($key)) {
            $dict[$key] = $true
            [void]$outHits.Add($h)
        }
    }
    return $outHits
}

# ---- 模拟 DictModel.ExtractMarkingSection ----
function ExtractMarkingSection([string]$text) {
    $patterns = @(
        "附图标记说明如下[：:\n\r]*",
        "附图标记说明[：:]\s*",
        "附图标记[：:]\s*",
        "标记说明如下[：:\n\r]*",
        "标记说明[：:]\s*",
        "标号说明[：:]\s*"
    )
    $found = $false
    $match = $null
    foreach ($p in $patterns) {
        $re = New-Regex $p $false $false
        $m = $re.Execute($text)
        if ($m.Count -gt 0) { $found = $true; $match = $m.Item(0); break }
    }
    if (-not $found) { return $text }
    $startPos = $match.FirstIndex + $match.Length
    $after = $text.Substring([int]$startPos)
    $reCut = New-Regex '[\s\S]*?(\r?\n\s*\r?\n|\Z)' $false $false
    $mCut = $reCut.Execute($after)
    if ($mCut.Count -gt 0) { return $mCut.Item(0).Value }
    return $after
}

function RunCase([string]$label, [string]$text, [bool]$fixedP1, [bool]$debug) {
    Log "======================================================"
    Log $label
    Log "======================================================"
    # 模拟 DictModel 预处理（<br/> -> vbCr）
    $textPre = $text.Replace('<br/>', [string][char]13).Replace('<br />', [string][char]13).Replace('<br>', [string][char]13)
    Log "--- 段落定位 ---"
    $section = ExtractMarkingSection $textPre
    if ($section -eq $textPre) {
        Log "[失败] 未找到标记头，回退全文！"
    } else {
        Log "[OK] 找到标记头，截取段落长度=$($section.Length)"
    }
    Log ""
    Log "--- ExtractAll 结果 ---"
    $hits = ExtractAll $section $fixedP1 $debug
    Log "提取到 $($hits.Count) 条:"
    foreach ($h in $hits) { Log ("  {0} = {1}" -f $h[0], $h[1]) }
    Log ""
}

# ============ 测试用例 ============
Log "########## 测试 A：当前代码（模式1缺全角分号）##########"
Log ""
$filePath = 'c:\Users\wjm\WorkBuddy\2026-06-20-00-50-28\附图标记说明识别错误示例.txt'
$bytes = [System.IO.File]::ReadAllBytes($filePath)
$text = [System.Text.Encoding]::UTF8.GetString($bytes)
if (-not $text.Contains('附图')) {
    $text = [System.Text.Encoding]::GetEncoding(936).GetString($bytes)
    Log "(示例文件为 GBK 编码，已按 GBK 读取)"
}
Log "原始文本: $text"
Log ""
RunCase '测试 A1: 用户示例（前缀文字+混合分隔符），当前代码' $text $false $true
RunCase '测试 A2: 全分号文本，当前代码' "附图标记说明如下：`n10叶轮；20电机；30上泵体；40下泵体；1轴部；2圆盘部；3叶片；4环状部；5沟槽。" $false $true
RunCase '测试 A3: 全逗号文本，当前代码' "附图标记说明如下：`n3叶片，31第一叶片，32第二叶片，33槽部，331外侧面，332内侧面，34片部，341第一片部，342第二片部，343平面结构，4环状部，5沟槽。" $false $true

Log "########## 测试 B：修复模式1（补全角分号）##########"
Log ""
RunCase '测试 B1: 用户示例（前缀文字+混合分隔符），修复后' $text $true $true
RunCase '测试 B2: 全分号文本，修复后' "附图标记说明如下：`n10叶轮；20电机；30上泵体；40下泵体；1轴部；2圆盘部；3叶片；4环状部；5沟槽。" $true $false
RunCase '测试 B3: 全逗号文本，修复后' "附图标记说明如下：`n3叶片，31第一叶片，32第二叶片，33槽部，331外侧面，332内侧面，34片部，341第一片部，342第二片部，343平面结构，4环状部，5沟槽。" $true $false

Log "期望: 10=叶轮 20=电机 30=上泵体 40=下泵体 1=轴部 2=圆盘部 3=叶片 31=第一叶片 32=第二叶片 33=槽部 331=外侧面 332=内侧面 34=片部 341=第一片部 342=第二片部 343=平面结构 4=环状部 5=沟槽"

# 写入结果文件（UTF-8）
$outFile = 'c:\Users\wjm\WorkBuddy\2026-06-20-00-50-28\test-vba-patterns-result.txt'
[System.IO.File]::WriteAllText($outFile, $out.ToString(), (New-Object System.Text.UTF8Encoding($true)))
Write-Output "done: $outFile"

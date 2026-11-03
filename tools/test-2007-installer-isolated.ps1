param([switch]$KeepArtifacts)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$encoding = [Text.Encoding]::GetEncoding(936)
$fake = @'

Class FakeShell
    Function RegRead(ByVal key)
        RegRead = "0409"
    End Function
    Function ExpandEnvironmentStrings(ByVal value)
        If value = "%APPDATA%" Then
            ExpandEnvironmentStrings = WScript.Arguments(1)
        Else
            ExpandEnvironmentStrings = value
        End If
    End Function
End Class

Class FakeReg
    Private profiles, values, tracePath
    Private Sub Class_Initialize()
        Set profiles = CreateObject("Scripting.Dictionary")
        Set values = CreateObject("Scripting.Dictionary")
        tracePath = WScript.Arguments(2)
        Dim fs, input, parts, line, profileKey, fullKey, h
        Set fs = CreateObject("Scripting.FileSystemObject")
        Set input = fs.OpenTextFile(WScript.Arguments(0), 1)
        Do Until input.AtEndOfStream
            line = input.ReadLine
            parts = Split(line, "|")
            profileKey = parts(0) & "|" & LCase("Software\Autodesk\AutoCAD\" & parts(1))
            If profiles.Exists(profileKey) Then
                profiles(profileKey) = profiles(profileKey) & "," & parts(2)
            Else
                profiles.Add profileKey, parts(2)
            End If
            fullKey = "Software\Autodesk\AutoCAD\" & parts(1) & "\" & parts(2) & "\Applications\PatentMarker"
            For Each h In Array("HKCU", "HKLM")
                values(h & "|" & LCase(fullKey) & "|loader") = "existing-loader"
            Next
            If UBound(parts) >= 3 Then
                fullKey = "Software\Autodesk\AutoCAD\" & parts(1) & "\" & parts(2)
                If Left(parts(3), 2) = "S=" Then
                    values(parts(0) & "|" & LCase(fullKey & "\Fixed Profile\General\ACAD") & "|acad") = Mid(parts(3), 3)
                ElseIf Left(parts(3), 2) = "L=" Then
                    values(parts(0) & "|" & LCase(fullKey) & "|acadlocation") = Mid(parts(3), 3)
                End If
            End If
        Loop
        input.Close
    End Sub

    Private Function HiveName(ByVal hive)
        If hive = &H80000001 Then HiveName = "HKCU" Else HiveName = "HKLM"
    End Function

    Private Sub Trace(ByVal operation, ByVal hive, ByVal key)
        Dim fs, output
        Set fs = CreateObject("Scripting.FileSystemObject")
        Set output = fs.OpenTextFile(tracePath, 8, True)
        output.WriteLine operation & "|" & HiveName(hive) & "|" & LCase(key)
        output.Close
    End Sub

    Sub EnumKey(ByVal hive, ByVal key, ByRef result)
        Dim k
        k = HiveName(hive) & "|" & LCase(key)
        If profiles.Exists(k) Then result = Split(profiles(k), ",") Else result = Null
    End Sub

    Sub CreateKey(ByVal hive, ByVal key)
        Trace "CREATE", hive, key
    End Sub

    Sub DeleteKey(ByVal hive, ByVal key)
        Trace "DELETE", hive, key
    End Sub

    Sub SetStringValue(ByVal hive, ByVal key, ByVal name, ByVal value)
        values(HiveName(hive) & "|" & LCase(key) & "|" & LCase(name)) = value
    End Sub

    Sub SetDWORDValue(ByVal hive, ByVal key, ByVal name, ByVal value)
        values(HiveName(hive) & "|" & LCase(key) & "|" & LCase(name)) = value
    End Sub

    Sub GetStringValue(ByVal hive, ByVal key, ByVal name, ByRef result)
        Dim k
        k = HiveName(hive) & "|" & LCase(key) & "|" & LCase(name)
        If values.Exists(k) Then result = values(k) Else result = Null
    End Sub

    Sub GetDWORDValue(ByVal hive, ByVal key, ByVal name, ByRef result)
        GetStringValue hive, key, name, result
    End Sub
End Class
'@

$scenarios = @(
    @{ Name = 'R17.0-only'; Profiles = @('HKCU|R17.0|ACAD-100') }
    @{ Name = 'R17.1-only'; Profiles = @('HKLM|R17.1|ACAD-200') }
    @{ Name = 'R17.2-only'; Profiles = @('HKCU|R17.2|ACAD-300') }
    @{ Name = 'mixed-and-duplicates'; Profiles = @(
        'HKCU|R17.0|ACAD-SAME', 'HKLM|R17.0|ACAD-SAME',
        'HKCU|R17.1|ACAD-SAME', 'HKLM|R17.1|ACAD-OTHER',
        'HKLM|R17.2|ACAD-LAST'); Excluded = @('HKCU|R18.0|ACAD-OUTSIDE') }
    @{ Name = 'language-product'; Profiles = @('HKLM|R17.2|ACAD-400:409') }
    @{ Name = 'location-hklm'; Profiles = @('HKLM|R17.1|ACAD-500', 'HKLM|R17.2|ACAD-600'); Source = 'Location' }
)
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('PatentMarker-G09-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$failures = 0
foreach ($scenario in $scenarios) {
    $dir = Join-Path $testRoot $scenario.Name
    $appdata = Join-Path $dir 'appdata'
    New-Item -ItemType Directory -Path $dir | Out-Null
    [IO.File]::WriteAllText((Join-Path $dir 'PatentMarker.dll'), 'test only')
    $inputPath = Join-Path $dir 'profiles.txt'
    $targets = @{}
    $inputLines = @()
    foreach ($line in $scenario.Profiles) {
        $parts = $line.Split('|')
        $id = ($parts[1] + '|' + $parts[2]).ToLowerInvariant()
        if (-not $targets.ContainsKey($id)) {
            $setting = ''
            if ($scenario.Source -eq 'Location') {
                $location = Join-Path $dir ("location-$($targets.Count)")
                $support = Join-Path $location 'Support'
                $setting = 'L=' + $location
            } elseif ($parts[2].Contains(':')) {
                $support = Join-Path $dir ("registered-support-$($targets.Count)")
                $setting = 'S=' + $support
            } else {
                $support = Join-Path $appdata ("Autodesk\AutoCAD\$($parts[1])\$($parts[2])\enu\Support")
            }
            $targets[$id] = @{ Version = $parts[1]; Code = $parts[2]; Support = $support; Setting = $setting }
            New-Item -ItemType Directory -Path $support -Force | Out-Null
            [IO.File]::WriteAllText((Join-Path $support 'acad.lsp'), "; user sentinel`r`n")
        }
        if ($targets[$id].Setting) { $inputLines += "$line|$($targets[$id].Setting)" } else { $inputLines += $line }
    }
    $excludedFiles = @()
    foreach ($line in $scenario.Excluded) {
        $inputLines += $line
        $parts = $line.Split('|')
        $support = Join-Path $appdata ("Autodesk\AutoCAD\$($parts[1])\$($parts[2])\enu\Support")
        New-Item -ItemType Directory -Path $support -Force | Out-Null
        $file = Join-Path $support 'acad.lsp'
        [IO.File]::WriteAllText($file, '; outside sentinel')
        $excludedFiles += $file
    }
    [IO.File]::WriteAllLines($inputPath, [string[]]$inputLines, $encoding)
    foreach ($operation in @('install', 'uninstall')) {
        $sourcePath = Join-Path $root ("PatentMarker-2007-deploy\$operation-2007.vbs")
        $source = [IO.File]::ReadAllText($sourcePath, $encoding)
        $registryAnchor = 'Set reg = GetObject("winmgmts:{impersonationLevel=impersonate}!\\.\root\default:StdRegProv")'
        $shellAnchor = 'Set shell = CreateObject("WScript.Shell")'
        $languageAnchor = 'Set r = CreateObject("WScript.Shell")'
        if (-not $source.Contains($registryAnchor) -or -not $source.Contains($shellAnchor) -or -not $source.Contains($languageAnchor)) { throw "Cannot isolate $sourcePath" }
        $source = $source.Replace($registryAnchor, 'Set reg = New FakeReg').Replace($shellAnchor, 'Set shell = New FakeShell').Replace($languageAnchor, 'Set r = New FakeShell')
        $script = Join-Path $dir ("$operation.vbs")
        [IO.File]::WriteAllText($script, $source + "`r`n" + $fake, $encoding)
        $trace = Join-Path $dir ("$operation.trace")
        $output = @(& cscript.exe //nologo $script $inputPath $appdata $trace 2>&1 | ForEach-Object { [string]$_ })
        $code = $LASTEXITCODE
        [IO.File]::WriteAllLines((Join-Path $dir ("$operation.output.txt")), [string[]]$output, [Text.Encoding]::UTF8)
        $expected = @()
        $verb = if ($operation -eq 'install') { 'CREATE' } else { 'DELETE' }
        foreach ($target in $targets.Values) {
            foreach ($hive in @('HKCU', 'HKLM')) {
                $key = ("Software\Autodesk\AutoCAD\$($target.Version)\$($target.Code)\Applications\PatentMarker").ToLowerInvariant()
                $expected += "$verb|$hive|$key"
            }
        }
        $actual = @()
        if (Test-Path -LiteralPath $trace) {
            $actual = @(Get-Content -LiteralPath $trace | Where-Object { $_ -like "$verb|*\applications\patentmarker" })
        }
        $missing = @($expected | Where-Object { $_ -notin $actual })
        $extra = @($actual | Where-Object { $_ -notin $expected })
        $duplicated = @($actual | Group-Object | Where-Object Count -ne 1)
        $badFiles = @()
        foreach ($target in $targets.Values) {
            $file = Join-Path $target.Support 'acad.lsp'
            $content = if (Test-Path -LiteralPath $file) { [IO.File]::ReadAllText($file) } else { '' }
            if ($operation -eq 'install') {
                if (-not $content.StartsWith("; user sentinel`r`n") -or -not $content.Contains('; --- PatentMarker autoload ---')) { $badFiles += $file }
            } elseif ($content -cne "; user sentinel`r`n") {
                $badFiles += $file
            }
        }
        foreach ($file in $excludedFiles) {
            if (-not (Test-Path -LiteralPath $file) -or [IO.File]::ReadAllText($file) -cne '; outside sentinel') { $badFiles += $file }
        }
        if ($code -ne 0 -or $missing.Count -or $extra.Count -or $duplicated.Count -or $badFiles.Count) {
            $failures++
            Write-Output "[FAIL] $($scenario.Name) $operation exit=$code missing=$($missing.Count) extra=$($extra.Count) duplicates=$($duplicated.Count) support=$($badFiles.Count)"
        } else {
            Write-Output "[PASS] $($scenario.Name) $operation targets=$($targets.Count), registry and support paths"
        }
    }
}
if ($KeepArtifacts) {
    Write-Output "Evidence: $testRoot"
} else {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $prefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()) + 'PatentMarker-G09-'
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe cleanup path: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
if ($failures -gt 0) { exit 1 }
exit 0

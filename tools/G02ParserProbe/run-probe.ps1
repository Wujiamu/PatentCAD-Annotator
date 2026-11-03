param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("red", "green")]
    [string]$Variant,
    [string]$SourceRoot
)

# G02 双引擎探针的 C# 侧运行器。
#   -Variant red   : 编译修复前快照（-SourceRoot 默认指向证据目录的 red-source）
#   -Variant green : 编译当前工作树的生产源码
# 同一份 csproj、同一份语料、同一份独立业务期望，判定标准不随阶段改变。
# 退出码直接来自探针进程：0 = 全部业务断言通过，1 = 有 FAIL，2 = SKIP/BLOCKED。

$ErrorActionPreference = "Stop"
$probeDir = $PSScriptRoot
$root = Split-Path -Parent (Split-Path -Parent $probeDir)
$evidence = Join-Path $root "docs\test-evidence\repair-20261031\G02"

if (-not $SourceRoot) {
    if ($Variant -eq "red") {
        $SourceRoot = Join-Path $evidence "red-source"
    } else {
        $SourceRoot = Join-Path $root "cad-plugin\Shared\IO"
    }
}
$parserFile = Join-Path $SourceRoot "MarkingTextParser.cs"
if (-not (Test-Path $parserFile)) { throw "Parser source not found: $parserFile" }

$project = Join-Path $probeDir "G02ParserProbe.csproj"
$cases = Join-Path $probeDir "g02-cases.txt"
$expected = Join-Path $probeDir "g02-expected.txt"

# Keep red and green in separate obj/bin trees so neither build can reuse the other's output.
$objDir = Join-Path $probeDir "obj\$Variant\"
$binDir = Join-Path $probeDir "bin\$Variant\"

dotnet build $project -c Release --nologo -v quiet `
    -p:Variant=$Variant -p:ParserSourceFile=$parserFile `
    -p:BaseIntermediateOutputPath=$objDir -p:OutputPath=$binDir | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Probe build failed for variant $Variant" }

$dll = Join-Path $binDir "G02ParserProbe.dll"
if (-not (Test-Path $dll)) { throw "Probe assembly not found: $dll" }

Write-Output "PARSER_SOURCE_SHA256=$((Get-FileHash $parserFile -Algorithm SHA256).Hash.ToLower())"
Write-Output "PROBE_DLL_SHA256=$((Get-FileHash $dll -Algorithm SHA256).Hash.ToLower())"
dotnet $dll $cases $expected
exit $LASTEXITCODE

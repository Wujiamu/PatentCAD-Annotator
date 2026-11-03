param(
    [string]$EvidenceDir = (Join-Path ([IO.Path]::GetTempPath()) ("FactRunner-G01-" + [guid]::NewGuid().ToString("N"))),
    [string]$FixturesFrom = ""
)

$ErrorActionPreference = "Stop"
if (Test-Path -LiteralPath $EvidenceDir) { throw "Evidence directory already exists: $EvidenceDir" }
New-Item -ItemType Directory -Path $EvidenceDir | Out-Null
$EvidenceDir = (Resolve-Path -LiteralPath $EvidenceDir).Path
if ($FixturesFrom -ne "") { $FixturesFrom = (Resolve-Path -LiteralPath $FixturesFrom).Path }
$root = Split-Path -Parent $PSScriptRoot
$runnerProject = Join-Path $root "tools\FactRunner\FactRunner.csproj"
$runnerDll = Join-Path $root "tools\FactRunner\bin\Release\net8.0-windows\PatentMarker.FactRunner.dll"
$utf8 = New-Object System.Text.UTF8Encoding($false)
$nl = [Environment]::NewLine

$buildOutput = & dotnet build $runnerProject -c Release --nologo -v quiet 2>&1
$buildCode = $LASTEXITCODE
[IO.File]::WriteAllLines((Join-Path $EvidenceDir "runner-build.txt"), [string[]]$buildOutput, $utf8)
if ($buildCode -ne 0) { throw "Runner build failed; see $EvidenceDir\runner-build.txt" }

$project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <RunAnalyzers>false</RunAnalyzers>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit" Version="2.6.6" />
  </ItemGroup>
</Project>
'@
$valid = '[Fact] public void Valid() { Assert.True(true); }'
$cases = @(
    @{ Name="empty"; Source='public class FixtureTests { }'; Exit=1; Summary='passed=0 failed=0 skipped=0'; Required='SKIP/BLOCKED'; Forbidden='PASS FixtureTests' },
    @{ Name="all-skipped"; Source='public class FixtureTests { [Fact(Skip="later")] public void Skipped() { } }'; Exit=1; Summary='passed=0 failed=0 skipped=1'; Required='SKIP FixtureTests.Skipped'; Forbidden='PASS FixtureTests' },
    @{ Name="empty-skip"; Source='public class FixtureTests { [Fact(Skip="")] public void Skipped() { } }'; Exit=1; Summary='passed=0 failed=0 skipped=1'; Required='SKIP FixtureTests.Skipped'; Forbidden='PASS FixtureTests' },
    @{ Name="mixed-skip"; Source=('public class FixtureTests { ' + $valid + ' [Fact(Skip="later")] public void Skipped() { } }'); Exit=0; Summary='passed=1 failed=0 skipped=1'; Required='SKIP FixtureTests.Skipped'; Forbidden='PASS FixtureTests.Skipped' },
    @{ Name="pass"; Source='public class FixtureTests { [Fact] public void Valid() { Assert.Equal(2, 1 + 1); } }'; Exit=0; Summary='passed=1 failed=0 skipped=0'; Required='PASS FixtureTests.Valid'; Forbidden='FAIL FixtureTests' },
    @{ Name="assertion-fails"; Source='public class FixtureTests { [Fact] public void Fails() { Assert.Equal(2, 1); } }'; Exit=1; Summary='passed=0 failed=1 skipped=0'; Required='FAIL FixtureTests.Fails'; Forbidden='PASS FixtureTests.Fails' },
    @{ Name="task"; Source=('public class FixtureTests { ' + $valid + ' [Fact] public Task Unsupported() => Task.FromException(new Exception("failed")); }'); Exit=1; Summary='passed=1 failed=0 skipped=1'; Required='SKIP/BLOCKED FixtureTests.Unsupported'; Forbidden='PASS FixtureTests.Unsupported' },
    @{ Name="value-task"; Source=('public class FixtureTests { ' + $valid + ' [Fact] public ValueTask Unsupported() => ValueTask.CompletedTask; }'); Exit=1; Summary='passed=1 failed=0 skipped=1'; Required='SKIP/BLOCKED FixtureTests.Unsupported'; Forbidden='PASS FixtureTests.Unsupported' },
    @{ Name="async-void"; Source=('public class FixtureTests { ' + $valid + ' [Fact] public async void Unsupported() { await Task.Yield(); } }'); Exit=1; Summary='passed=1 failed=0 skipped=1'; Required='SKIP/BLOCKED FixtureTests.Unsupported'; Forbidden='PASS FixtureTests.Unsupported' },
    @{ Name="static"; Source=('public class FixtureTests { ' + $valid + ' [Fact] public static void Unsupported() { } }'); Exit=1; Summary='passed=1 failed=0 skipped=1'; Required='SKIP/BLOCKED FixtureTests.Unsupported'; Forbidden='PASS FixtureTests.Unsupported' },
    @{ Name="static-class"; Source=('public class FixtureTests { ' + $valid + ' } public static class StaticTests { [Fact] public static void Unsupported() { } }'); Exit=1; Summary='passed=1 failed=0 skipped=1'; Required='SKIP/BLOCKED StaticTests.Unsupported'; Forbidden='PASS StaticTests.Unsupported' },
    @{ Name="static-skipped"; Source=('public class FixtureTests { ' + $valid + ' [Fact(Skip="later")] public static void Unsupported() { } }'); Exit=1; Summary='passed=1 failed=0 skipped=1'; Required='SKIP/BLOCKED FixtureTests.Unsupported'; Forbidden='PASS FixtureTests.Unsupported' },
    @{ Name="parameter"; Source=('public class FixtureTests { ' + $valid + ' [Fact] public void Unsupported(int n) { } }'); Exit=1; Summary='passed=1 failed=0 skipped=1'; Required='SKIP/BLOCKED FixtureTests.Unsupported'; Forbidden='PASS FixtureTests.Unsupported' },
    @{ Name="theory"; Source=('public class FixtureTests { ' + $valid + ' [Theory] [InlineData(1)] public void Unsupported(int n) { } }'); Exit=1; Summary='passed=1 failed=0 skipped=1'; Required='SKIP/BLOCKED FixtureTests.Unsupported'; Forbidden='PASS FixtureTests.Unsupported' },
    @{ Name="private"; Source=('public class FixtureTests { ' + $valid + ' [Fact] private void Unsupported() { } }'); Exit=1; Summary='passed=1 failed=0 skipped=1'; Required='SKIP/BLOCKED FixtureTests.Unsupported'; Forbidden='PASS FixtureTests.Unsupported' },
    @{ Name="generic"; Source=('public class FixtureTests { ' + $valid + ' [Fact] public void Unsupported<T>() { } }'); Exit=1; Summary='passed=1 failed=0 skipped=1'; Required='SKIP/BLOCKED FixtureTests.Unsupported'; Forbidden='PASS FixtureTests.Unsupported' },
    @{ Name="generic-class"; Source=('public class FixtureTests { ' + $valid + ' } public class GenericTests<T> { [Fact] public void Unsupported() { } }'); Exit=1; Summary='passed=1 failed=0 skipped=1'; Required='SKIP/BLOCKED GenericTests'; Forbidden='PASS GenericTests' },
    @{ Name="struct"; Source=('public class FixtureTests { ' + $valid + ' } public struct StructTests { [Fact] public void Unsupported() { } }'); Exit=1; Summary='passed=1 failed=0 skipped=1'; Required='SKIP/BLOCKED StructTests.Unsupported'; Forbidden='PASS StructTests.Unsupported' },
    @{ Name="constructor"; Source=('public class FixtureTests { ' + $valid + ' } public class ConstructorTests { public ConstructorTests(int n) { } [Fact] public void Unsupported() { } }'); Exit=1; Summary='passed=1 failed=0 skipped=1'; Required='SKIP/BLOCKED ConstructorTests.Unsupported'; Forbidden='PASS ConstructorTests.Unsupported' },
    @{ Name="non-void"; Source=('public class FixtureTests { ' + $valid + ' [Fact] public int Unsupported() => 1; }'); Exit=1; Summary='passed=1 failed=0 skipped=1'; Required='SKIP/BLOCKED FixtureTests.Unsupported'; Forbidden='PASS FixtureTests.Unsupported' },
    @{ Name="timeout"; Source=('public class FixtureTests { ' + $valid + ' [Fact(Timeout=1000)] public void Unsupported() { } }'); Exit=1; Summary='passed=1 failed=0 skipped=1'; Required='SKIP/BLOCKED FixtureTests.Unsupported'; Forbidden='PASS FixtureTests.Unsupported' },
    @{ Name="custom-fact"; Source=('public class DerivedFactAttribute : FactAttribute { } public class FixtureTests { ' + $valid + ' [DerivedFact] public void Unsupported() { } }'); Exit=1; Summary='passed=1 failed=0 skipped=1'; Required='SKIP/BLOCKED FixtureTests.Unsupported'; Forbidden='PASS FixtureTests.Unsupported' },
    @{ Name="class-fixture"; Source='public class Resource { } public class FixtureTests : IClassFixture<Resource> { [Fact] public void Unsupported() { } }'; Exit=1; Summary='passed=0 failed=0 skipped=1'; Required='SKIP/BLOCKED FixtureTests.Unsupported'; Forbidden='PASS FixtureTests.Unsupported' },
    @{ Name="collection-fixture"; Source='public class Resource { } [CollectionDefinition("Example")] public class FixtureDefinition : ICollectionFixture<Resource> { } [Collection("Example")] public class FixtureTests { [Fact] public void Unsupported() { } }'; Exit=1; Summary='passed=0 failed=0 skipped=1'; Required='SKIP/BLOCKED: xUnit collection fixtures'; Forbidden='PASS FixtureTests.Unsupported' },
    @{ Name="async-lifetime"; Source='public class FixtureTests : IAsyncLifetime { public Task InitializeAsync() => Task.CompletedTask; public Task DisposeAsync() => Task.CompletedTask; [Fact] public void Unsupported() { } }'; Exit=1; Summary='passed=0 failed=0 skipped=1'; Required='SKIP/BLOCKED FixtureTests.Unsupported'; Forbidden='PASS FixtureTests.Unsupported' },
    @{ Name="async-disposable"; Source='public class FixtureTests : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; [Fact] public void Unsupported() { } }'; Exit=1; Summary='passed=0 failed=0 skipped=1'; Required='SKIP/BLOCKED FixtureTests.Unsupported'; Forbidden='PASS FixtureTests.Unsupported' },
    @{ Name="hook-method"; Source='[AttributeUsage(AttributeTargets.Method)] public sealed class HookAttribute : Xunit.Sdk.BeforeAfterTestAttribute { } public class FixtureTests { [Fact, Hook] public void Valid() { Assert.True(true); } }'; Exit=1; Summary='passed=0 failed=0 skipped=1'; Required='SKIP/BLOCKED'; Forbidden='PASS FixtureTests.Valid' },
    @{ Name="hook-class"; Source='[AttributeUsage(AttributeTargets.Class, Inherited=true)] public sealed class HookAttribute : Xunit.Sdk.BeforeAfterTestAttribute { } [Hook] public class FixtureTests { [Fact] public void Valid() { Assert.True(true); } }'; Exit=1; Summary='passed=0 failed=0 skipped=1'; Required='SKIP/BLOCKED'; Forbidden='PASS FixtureTests.Valid' },
    @{ Name="hook-assembly"; Source='[assembly: Hook] [AttributeUsage(AttributeTargets.Assembly)] public sealed class HookAttribute : Xunit.Sdk.BeforeAfterTestAttribute { } public class FixtureTests { [Fact] public void Valid() { Assert.True(true); } }'; Exit=1; Summary='passed=0 failed=0 skipped=1'; Required='SKIP/BLOCKED'; Forbidden='PASS FixtureTests.Valid' },
    @{ Name="case-orderer"; Source='[TestCaseOrderer("Orderer", "Fixture")] public class FixtureTests { [Fact] public void Valid() { Assert.True(true); } }'; Exit=1; Summary='passed=0 failed=0 skipped=1'; Required='SKIP/BLOCKED'; Forbidden='PASS FixtureTests.Valid' },
    @{ Name="collection-orderer"; Source='[assembly: TestCollectionOrderer("Orderer", "Fixture")] public class FixtureTests { [Fact] public void Valid() { Assert.True(true); } }'; Exit=1; Summary='passed=0 failed=0 skipped=1'; Required='SKIP/BLOCKED'; Forbidden='PASS FixtureTests.Valid' },
    @{ Name="custom-framework"; Source='[assembly: TestFramework("CustomFramework", "Fixture")] public class FixtureTests { [Fact] public void Valid() { Assert.True(true); } }'; Exit=1; Summary='passed=0 failed=0 skipped=1'; Required='SKIP/BLOCKED'; Forbidden='PASS FixtureTests.Valid' },
    @{ Name="dispose-failure"; Source='public class FixtureTests : IDisposable { [Fact] public void Valid() { Assert.True(true); } public void Dispose() { throw new Exception("cleanup failed"); } }'; Exit=1; Summary='passed=0 failed=1 skipped=0'; Required='FAIL FixtureTests.Valid'; Forbidden='PASS FixtureTests.Valid' }
)

$failed = 0
$results = New-Object 'System.Collections.Generic.List[string]'
$hashes = New-Object 'System.Collections.Generic.List[string]'
foreach ($inputPath in @($PSCommandPath, $runnerProject, (Join-Path $PSScriptRoot "FactRunner\Program.cs"), $runnerDll)) {
    $hashes.Add((Get-FileHash -Algorithm SHA256 -LiteralPath $inputPath).Hash + "  " + $inputPath)
}
foreach ($case in $cases) {
    $caseDir = Join-Path $EvidenceDir $case.Name
    New-Item -ItemType Directory -Path $caseDir | Out-Null
    if ($FixturesFrom -eq "") {
        $projectPath = Join-Path $caseDir "Fixture.csproj"
        [IO.File]::WriteAllText($projectPath, $project, $utf8)
        [IO.File]::WriteAllText((Join-Path $caseDir "Fixture.cs"), "using System; using System.Threading.Tasks; using Xunit;" + $nl + $case.Source, $utf8)
        $buildOutput = & dotnet build $projectPath -c Release --nologo -v quiet 2>&1
        $buildCode = $LASTEXITCODE
        [IO.File]::WriteAllLines((Join-Path $caseDir "build.txt"), [string[]]$buildOutput, $utf8)
        if ($buildCode -ne 0) { throw "Fixture build failed for $($case.Name); see $caseDir\build.txt" }
        $fixtureDir = $caseDir
    } else {
        $fixtureDir = Join-Path $FixturesFrom $case.Name
    }
    $assemblyPath = (Resolve-Path -LiteralPath (Join-Path $fixtureDir "bin\Release\net8.0\Fixture.dll")).Path
    $hashes.Add((Get-FileHash -Algorithm SHA256 -LiteralPath $assemblyPath).Hash + "  " + $assemblyPath)
    $output = & dotnet exec $runnerDll $assemblyPath 2>&1
    $code = $LASTEXITCODE
    $outputText = ($output | Out-String).TrimEnd()
    [IO.File]::WriteAllText((Join-Path $caseDir "runner.txt"), "exit=$code" + $nl + $outputText + $nl, $utf8)
    $ok = $code -eq $case.Exit -and $outputText.Contains("SUMMARY: " + $case.Summary) -and
        $outputText.Contains($case.Required) -and -not $outputText.Contains($case.Forbidden)
    if ($ok) { $line = "PASS $($case.Name)" }
    else {
        $failed++
        $line = "FAIL $($case.Name): exit=$code; expected=$($case.Exit), summary=$($case.Summary)"
    }
    $results.Add($line)
    Write-Host $line
}

Write-Host "Evidence: $EvidenceDir"
$line = "G01 SUMMARY: passed=$($cases.Count - $failed) failed=$failed"
$results.Add($line)
Write-Host $line
[IO.File]::WriteAllLines((Join-Path $EvidenceDir "summary.txt"), $results, $utf8)
[IO.File]::WriteAllLines((Join-Path $EvidenceDir "inputs-sha256.txt"), $hashes, $utf8)
if ($failed -ne 0) { exit 1 }
exit 0

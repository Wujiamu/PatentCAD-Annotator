# G09 / G12 独立验收

验收者：`/root/g01`，未参与 G09、G12 的实现；执行身份 `local-user-sha256:c683831d6b0066c74ee53f1234eb0c59406496f43c89a3697b6839921c14c6c8`，2026-11-01（Asia/Shanghai），.NET SDK 8.0.422，net48 测试宿主 64 位 .NET Framework 4.8.9363.0。所有执行均使用本目录下的临时复制，未改用户根工作树或冻结候选。

原始基线清单 SHA-256 `7a8c5653d2d691d8c210b87776ad7251ef5a6f8ff84248f624da7ba000aa9034`。G09 的 2007 安装、卸载基线 SHA-256 分别为 `0162006ae2c0161a9fdbe7a0b2ca2c978ec83a1d429b650a0d0222ef1e362ac8` 和 `f30467f7c8b7d0580ef5adfbffec7b48edfef072f4db93d378f82e2b9dddf568`；候选分别为 `8ecaea70ae40df258c051c3344a6b3ad36ff6b62a44974ca7372796ce8965320` 和 `bac5140ba3598e2c2b65a52931ff4cfb6d2d82721bb48509d43651d9ee49a47e`。G12 的 `VisioAnnotations.cs` 基线 SHA-256 `13d6a0affaeb4fefdf24f6f8bdda7095a77ce257a9dbec674e044a481f109b14`、候选 `6df42fce274177e0b9a4d1374d5d5e4a9b59e3a4a16aa152c337e81fc477bdc4`。均与实际冻结源或原基线清单核对一致。G12 使用的 18 个源码／工程文件哈希及红绿差异见 `g12-source-snapshot.json`；只有生产 `VisioAnnotations.cs` 不同，测试源码相同。

## G09（R-011）

审阅隔离工具：原脚本以 `FakeReg`、`FakeShell` 替换注册表和环境访问；执行的 VBS 与支持目录均在新建临时目录内，实际文件操作限定在这些合成路径。没有访问注册表，也没有安装、启动或探测旧 CAD。复制的模拟脚本的生产部分与所测候选逐字一致，仅替换隔离入口；附加检查在 fake registry 的 SET 方法增加追踪。

在 `G09-red` 和 `G09` 目录分别执行：

`powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\test-2007-installer-isolated.ps1 -KeepArtifacts`

基线退出 1：R17.0 对照的安装／卸载 2 项 PASS，R17.1、R17.2、混装、语言产品和 HKLM 路径对应 10 项 FAIL。候选退出 0，12/12 PASS，包含安装及卸载各自的精确目标、支持目录和已有文件哨兵；日志分别为 `g09-red.log`、`g09-author-harness.log`。原始轨迹和合成数据复制至 `G09-baseline-artifacts`、`G09-original-artifacts`。

独立新增 `python .\g09-independent-check.py`，退出 0，`42 PASS / 0 FAIL`：构造同一 R17.1 多个产品配置、HKCU 内重复及跨 HKCU/HKLM 重复、同一产品跨 R17.0/R17.1/R17.2，以及 R18 外部哨兵；每次检查全部模拟 CREATE、DELETE、SETSTRING 和 SETDWORD 的路径、名称、值和次数，仅允许配对的临时管理员探针。第一次安装、重复安装、卸载各核对用户原有字节、额外非产品文件、R18 哨兵和标记块数量。详细结果见 `g09-independent-results.json`，原始追踪见 `G09-independent`。该测试工具源 SHA-256 `86c0e3458196d76d29e46e77458621bf43a8c2d2eff0ffd710bb13ac0dd1dac7`。

在 `G09` 中另执行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check-installer-contract.ps1`，退出 0，10 个版本／卸载静态契约及其 12 个隔离场景通过，见 `g09-contract.log`。

## G12（R-027）

在 `G12-red` 和 `G12` 中以同一测试源码、同一选择器执行：

`dotnet test .\office-com-addin\tests\PatentOffice.Visio.CodeTests\PatentOffice.Visio.CodeTests.csproj --configuration Release --nologo --filter 'FullyQualifiedName~PatentOffice.Visio.CodeTests.G12AnnotationPairTests|FullyQualifiedName~PatentOffice.Visio.CodeTests.AnnotationScanTests' --logger 'console;verbosity=normal'`

基线退出 1：10 项中 7 PASS、3 FAIL；失败为 `TwoLinesAndOneLabel_ReportsOrphanAndKeepsValidPair`、`OneLineAndTwoLabels_ReportsOrphanAndKeepsValidPair`、`SameIdDifferentNumbers_CannotBorrowEachOthersLabels`，三者均预期异常 ID 数为 1、实际 0。候选退出 0：10/10 PASS，包含上述相同反例及完整复制、不同 ID 同号、不同页、损坏／空文字等对照。日志 `g12-red.log`、`g12-dotnet-test.log` 记录 VSTest 真正发现并执行的各项名称及结果。使用 FakeVisio 模型；不是 Visio 安装后黑盒。

## 未覆盖

- **GitHub 外部宿主验收 — SKIP/BLOCKED**：2007/2010/2013/2015 旧 CAD 目标环境未回传；本轮未在本机安装或探测它们。
- **Visio 真实宿主 — SKIP/BLOCKED**：实际组合复制、删部件、保存重开及候选安装后路径未执行，不能由上述模拟结果外推。
- 候选包真实安装／卸载、实际注册表及真实 CAD 自动加载均未执行；这是隔离范围，不标为 PASS。

机器可读结果为 `independent-findings.json`；证据文件和测试程序集 SHA-256 位于 `evidence-sha256.json`，产品／测试程序集还列在 findings 中。

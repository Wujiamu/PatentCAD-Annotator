# G13 独立验收：R-025

验收者 `/root/g01` 未参与 G13 实现；执行身份 `local-user-sha256:c683831d6b0066c74ee53f1234eb0c59406496f43c89a3697b6839921c14c6c8`，2026-11-01（Asia/Shanghai），.NET SDK 8.0.422，模拟测试宿主 .NET 8.0.28 64 位。所有执行在本目录临时副本中进行；冻结候选与用户根工作树未修改。原始基线清单 SHA-256 `7a8c5653d2d691d8c210b87776ad7251ef5a6f8ff84248f624da7ba000aa9034`。

候选 `PatBraceGeometry.cs` SHA-256 `0062bda79c2697a523ea351e86a6baba80eaa216d39afae55a0e9ae8f768f033`、`PatBraceEntity.cs` `fa837be499966e78a71509e48f9951d27e7958f79a07a8475f730570f5b20aba`；基线分别为 `8ca5e55434dc3c512d920899910841af277de315f0c892fb245ffbe07caa659d`、`b137f18c13a4e705f4c6268955317763cc0351a05ce98f1019f85118945bc010`，与基线清单一致。134 个候选源码／工程文件与冻结副本核对一致，见 `candidate-source-snapshot.json`。`PatBraceCommand.cs` 红绿相同，SHA-256 `87ec15a36ff2c0b641795b634b4f8078887944a37f412b74bb50e38507aa9abb`。

实现者早期 `green.txt` 中 `PatBraceEntity.cs` 哈希为 `fd34824d…`，不同于当前冻结候选 `fa837be4…`；因此不把该早期绿灯当成当前候选证据。以下独立运行均使用当前冻结哈希。

## 同标准源码模拟：PASS

测试选择器 `FullyQualifiedName~G13BracePlanarityTests`；同一测试源码 SHA-256 `71e02b12f439b4031d1f969fc72fdc0dcf504802f57b4526ad39479f3c1c1db2`。分别在 `baseline-run` 和 `candidate` 下运行，示例命令：

`dotnet test cad-plugin/RuntimeContract.Tests/PatentMarker.RuntimeContract.2007.Tests.csproj --configuration Release --nologo --filter FullyQualifiedName~G13BracePlanarityTests --logger "console;verbosity=normal"`

| 快照／模拟版本 | 退出码 | 实际业务断言结果 |
|---|---:|---|
| 基线／2007 | 1 | 9 项中 2 PASS、7 FAIL |
| 候选／2007 | 0 | 9 PASS、0 FAIL |
| 候选／2010 | 0 | 9 PASS、0 FAIL |
| 候选／2013 | 0 | 9 PASS、0 FAIL |
| 候选／2015 | 0 | 9 PASS、0 FAIL |

`simulation-runs.json` 记录每次精确参数、退出码与日志；日志为 `baseline-run-2007.log` 和 `candidate-<year>.log`。这里的四套是模拟项目，不是对应年份的 CAD 宿主。

## 命令路径独立反例：PASS（模拟范围）

在 `command-baseline`、`command-candidate` 中对完全相同的测试、模拟提示／选择接口及 `PatBraceCommand.cs` 执行：

`dotnet test .\cad-plugin\RuntimeContract.Tests\PatentMarker.RuntimeContract.2007.Tests.csproj --configuration Release --nologo --filter 'FullyQualifiedName~G13IndependentCommandTests' --logger 'console;verbosity=normal'`

基线退出 1，5 项中 2 PASS／3 FAIL；候选退出 0，5/5 PASS。失败反例在旧源码中实际创建了非共面对象，或向用户报告编辑成功；候选同样操作给出同高程提示并阻止修改。5 项选择器：

- `UserCreateRejectsNonPlanarBeforeAnyAppend`：无提交实体，也无模拟 append。
- `UserEndpointEditRejectsAndPreservesAllVerticesAndRecord`：拒绝不同高程端点，逐点比较全部顶点，逐项比较完整 Xrecord 数据，保持实体数。
- `UserSizeEditCannotFlattenLegacyOrModifyItsRecord`：已有 V1 非共面定义的尺寸编辑被拒绝，所有顶点及记录不变。
- `UserExplicitlyRepairsLegacyEndpointsAndWritesConsistentDefinition`：历史 V1 记录先读取且读操作不改记录；显式改为共面端点后实体与定义高程一致。
- `UserCreationKeepsZeroAndNonzeroPlaneInEntityAndRecord`：高程 0、2、-8.5 的实体所有顶点及参数记录一致。

这套额外测试调用真实生产 `PatBraceCommand`，但用本目录 `IndependentPromptStubs.cs` 扩充模拟提示、选择和尺寸输入；只在副本的模拟桩和测试工程中接入。测试源码 SHA-256 `1368161c027e6013e6f617299c6d266616d659fa9a859373616814bfe543cafc`；红绿输入清单 `command-baseline-hashes.json`、`command-candidate-hashes.json` 中只有两份待修生产源码不同。详细断言与输出见 `command-baseline.log`、`command-candidate.log`。V1 记录为合成模拟输入，不证明真实 DWG 的组码可读或保存重开。

## 五版编译：PASS

在 `candidate` 下执行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Version all`，退出 0，2007／2010／2013／2015／2025 均显示成功，且每版产生 DLL。13 个 Autodesk SDK 文件哈希与基线清单一致；740 个参考程序集／包文件与实现者依赖清单一致。各版本独立生成 DLL 的具体路径和 SHA-256 见 `build-outputs.json`，SDK 哈希见 `sdk-hashes.json`，日志 `independent-build-all.log`。2025 构建显示 320 个警告，其中含 MSB3277 WindowsBase 版本冲突；编译成功不证明运行时加载。

## 未覆盖

- **真实 CAD 命令、保存重开及当前 2025/2026 宿主 — SKIP/BLOCKED**：模拟和编译不能替代真实宿主。
- **GitHub 外部宿主验收 — SKIP/BLOCKED**：2007／2010／2013／2015 目标环境未回传，未在本机安装或探测。
- **R-028 — SKIP/BLOCKED、先核验**：未实测真实 Xrecord 的 1000／1040／1070 组码及保存重开；没有迁移格式，也没有关闭此项。

机器可读结果为 `independent-findings.json`，全部关键日志、源码、桩、测试和产物哈希见 `evidence-sha256.json`。源码／模拟及编译结果可供协调者集成；宿主缺口不得记为 PASS。

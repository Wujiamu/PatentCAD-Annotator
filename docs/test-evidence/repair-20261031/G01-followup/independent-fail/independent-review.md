# G01 独立验收：FAIL

验收者 `/root/g03` 未参与该运行器实现；全部操作在当前临时目录，未操作宿主或根工作树。候选源码、工具与输入按 `frozen-inputs.json` 冻结。原有 26 组真实 net8 输入按原样独立重跑：基线 3 PASS / 23 FAIL；候选 26 PASS / 0 FAIL。

对照 xUnit 2.6.6 + VSTest 2.5.6 的新增反例中，14 组被候选错误报绿，2 组单测试计数不准，1 组正对照通过，另 2 组未证实差异。拒绝未支持形态时，最低期望为非零退出且明确 `SKIP/BLOCKED`；作用域受影响的测试不得打印 PASS。

| 夹具（`extra/<名称>`） | 业务情形 | 同 DLL 的 xUnit | 候选 | 修补后最低期望 | 验收 |
|---|---|---|---|---|---|
| `beforeafter` | 方法级 Before 抛错 | 退出 1；1 项，FAIL 1 | 退出 0；1 PASS/0 FAIL/0 SKIP | nonzero, explicit SKIP/BLOCKED, no PASS for FixtureTests.Valid | FAIL |
| `after-method` | 方法级 After 抛错 | 退出 1；1 项，FAIL 1 | 退出 0；1 PASS/0 FAIL/0 SKIP | nonzero, explicit SKIP/BLOCKED, no PASS for FixtureTests.Valid | FAIL |
| `before-class` | 类级 Before 抛错 | 退出 1；1 项，FAIL 1 | 退出 0；1 PASS/0 FAIL/0 SKIP | nonzero, explicit SKIP/BLOCKED, no PASS for FixtureTests.Valid | FAIL |
| `after-class` | 类级 After 抛错 | 退出 1；1 项，FAIL 1 | 退出 0；1 PASS/0 FAIL/0 SKIP | nonzero, explicit SKIP/BLOCKED, no PASS for FixtureTests.Valid | FAIL |
| `before-assembly` | 程序集级 Before 抛错 | 退出 1；1 项，FAIL 1 | 退出 0；1 PASS/0 FAIL/0 SKIP | nonzero, explicit SKIP/BLOCKED, no PASS for FixtureTests.Valid | FAIL |
| `before-inherited-class` | 基类上的类级 Before 抛错 | 退出 1；1 项，FAIL 1 | 退出 0；1 PASS/0 FAIL/0 SKIP | nonzero, explicit SKIP/BLOCKED, no PASS for FixtureTests.Valid | FAIL |
| `before-inherited-method` | 继承的 Fact 方法含 Before | 退出 1；1 项，FAIL 1 | 退出 0；1 PASS/0 FAIL/0 SKIP | nonzero, explicit SKIP/BLOCKED, no PASS for FixtureTests.Valid | FAIL |
| `before-derived-attribute` | BeforeAfterTest 派生两层属性 | 退出 1；1 项，FAIL 1 | 退出 0；1 PASS/0 FAIL/0 SKIP | nonzero, explicit SKIP/BLOCKED, no PASS for FixtureTests.Valid | FAIL |
| `before-mixed` | 普通 Fact 与带 Before 的 Fact 并存 | 退出 1；2 项，FAIL 1 | 退出 0；2 PASS/0 FAIL/0 SKIP | nonzero, explicit SKIP/BLOCKED, no PASS for FixtureTests.Unsupported | FAIL |
| `custom-orderer` | 类级 TestCaseOrderer 使顺序反转 | 退出 1；2 项，FAIL 1 | 退出 0；2 PASS/0 FAIL/0 SKIP | nonzero, explicit SKIP/BLOCKED, no tests reported PASS | FAIL |
| `assembly-orderer` | 程序集级 TestCaseOrderer 使顺序反转 | 退出 1；2 项，FAIL 1 | 退出 0；2 PASS/0 FAIL/0 SKIP | nonzero, explicit SKIP/BLOCKED, no tests reported PASS | FAIL |
| `collection-orderer` | 集合定义级 TestCaseOrderer 使顺序反转 | 退出 1；2 项，FAIL 1 | 退出 0；2 PASS/0 FAIL/0 SKIP | nonzero, explicit SKIP/BLOCKED, no tests reported PASS | FAIL |
| `collection-type-orderer` | 程序集 TestCollectionOrderer 使集合顺序反转 | 退出 1；2 项，FAIL 1 | 退出 0；2 PASS/0 FAIL/0 SKIP | nonzero, explicit SKIP/BLOCKED, no tests reported PASS | FAIL |
| `framework-initialization` | 程序集 TestFramework 的构造器改变前置状态 | 退出 1；1 项，FAIL 1 | 退出 0；1 PASS/0 FAIL/0 SKIP | nonzero, explicit SKIP/BLOCKED, no tests reported PASS | FAIL |
| `benign-attribute` | 无关自定义属性与 Trait，未使用的钩子定义 | 退出 0；1 项 | 退出 0；1 PASS/0 FAIL/0 SKIP | exit 0 and one passing Fact | PASS |
| `dispose-failure` | Fact 成功但 Dispose 失败 | 退出 1；1 项，FAIL 1 | 退出 1；1 PASS/1 FAIL/0 SKIP | exit nonzero and exactly one failed test, no PASS for it | FAIL |
| `assertion-and-dispose` | 断言和 Dispose 均失败 | 退出 1；1 项，FAIL 1 | 退出 1；0 PASS/2 FAIL/0 SKIP | exit nonzero and exactly one failed test, no PASS for it | FAIL |
| `abstract-static` | 抽象类仅含静态 Fact，xUnit 忽略 | 退出 0；1 项 | 退出 0；1 PASS/0 FAIL/0 SKIP | xUnit and old candidate agreed; not established as a false-green case | SKIP/BLOCKED |
| `custom-framework` | 框架构造抛错；xUnit 仍通过 | 退出 0；1 项 | 退出 0；1 PASS/0 FAIL/0 SKIP | xUnit and old candidate agreed; not established as a false-green case | SKIP/BLOCKED |

额外检查：包装脚本对普通通过、断言失败、Task 不支持、零用例四组均正确透传退出码及旧 `SUMMARY: passed=N failed=N skipped=N` 格式；其输出位于 `wrapper/`。该结果不能弥补底层对钩子和自定义执行策略的假绿。

调用原脚本的首次 PowerShell 5.1 尝试因 Codex 运行时同名 Utility 模块遮蔽系统 `Get-FileHash` 而 BLOCKED；将子进程 `PSModulePath` 指向 `C:\Windows\System32\WindowsPowerShell\v1.0\Modules` 后独立重跑，未修改被测脚本。首次输出和成功重跑日志分别保存在 `*-suite.txt` 与 `*-suite-v2.txt`。

## 冻结证据和复测入口

- 机器 findings：`<USERPROFILE>\AppData\Local\Temp\PatentCAD-independent-G01-i0iw6avz\independent-findings.json`；SHA-256 `48d47241733d7dc0fdca1d25a85f6af5fc82c7ada2a6652de7d4536bd31a434b`。
- 19 组真实 DLL 与依赖的哈希：`<USERPROFILE>\AppData\Local\Temp\PatentCAD-independent-G01-i0iw6avz\extra-fixtures-sha256.json`；SHA-256 `3cb719de4b94675e05f6ab2db66dd9e576ee90db9a10600b9d1bd7d9e76f0e98`。
- 原有 26 组 DLL、原始/候选源码与脚本冻结清单：`<USERPROFILE>\AppData\Local\Temp\PatentCAD-independent-G01-i0iw6avz\frozen-inputs.json`；SHA-256 `c33dfeedee0c961622ae16d57c9aefa233422260618ceed370e9d6a48d8ccaad`。
- 复测脚本：`<USERPROFILE>\AppData\Local\Temp\PatentCAD-independent-G01-i0iw6avz\audit_g01.py`；SHA-256 `14b3ac5da12a2b93250e81a84baf036a1095fc9935525914890a7e53da540f05`。脚本不改输入 DLL，拒绝已存在的输出目录。修补后基于相同 DLL 运行：

```powershell
python '<USERPROFILE>\AppData\Local\Temp\PatentCAD-independent-G01-i0iw6avz\audit_g01.py' --runner '<新编译的 PatentMarker.FactRunner.dll>' --source '<新候选 Program.cs>' --output '<新的不存在目录>'
```

脚本对 17 个可判定情形要求 PASS；两项未证实差异继续标 `SKIP/BLOCKED`，不能记作通过。脚本输出 `findings.json`、逐例命令及原始输出；最终仍需重新运行原有 26 组，检查真实项目兼容性和源码/工具哈希。测试结果仅覆盖 net8 合成程序集，不等于所有 xUnit 特性或宿主环境通过。

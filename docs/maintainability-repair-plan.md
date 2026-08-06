# PatentCAD-Annotator 可维护性修复清单

生成日期：2026-08-05
依据：当前代码库只读结构与可维护性检查

## 1. 总体结论

当前项目存在局部技术债，建议按本文顺序定点处理，不需要暂停全部开发进行全面重构。

主要风险集中在：

1. 自动测试未成为持续门禁；
2. 字典缓存和配置没有按图纸隔离；
3. 编号比较规则在不同功能中不一致；
4. `DictPaletteControl` 同时承担 UI、文件、状态和 CAD 数据库操作；
5. 识别规则及五版生产代码依赖人工复制同步；
6. 编译结果到部署包之间缺少可验证的发布流程。

修复原则：先补保护，再纠正状态和领域规则，随后拆分职责，最后治理跨版本源码与发布流程。

## 2. 修复顺序

### 第 1 项：把现有测试纳入默认验证流程

优先级：最高
目标：后续结构调整前先建立可重复的回归保护。

现状：

- `cad-plugin/2025/PatentMarker.Tests/` 当前测试可运行，但未加入 `PatentCAD.sln`；
- `.github/workflows/build.yml` 只运行结构检查和静态检查，没有执行 `dotnet test`；
- 当前工作区的 `CorpusComparisonTests.cs` 尚未纳入版本控制；
- 测试只链接 2025 版部分 IO/I18n 源码，不覆盖面板、命令、配置切换、CAD 实体操作及旧版 JSON 实现。

建议处理：

1. 确认语料测试是否应正式保留；若保留，将测试和所需基线数据纳入版本控制；
2. 在 CI 中增加 `dotnet test cad-plugin/2025/PatentMarker.Tests/PatentMarker.Tests.csproj`；
3. 将测试项目加入解决方案或提供根目录统一测试命令；
4. 为后续要抽离的会话状态、编号规则和面板协调逻辑预留测试入口；
5. 保留对应 AutoCAD 版本中的人工 `NETLOAD` 冒烟验证，不把纯单元测试当成 CAD 集成验证的替代品。

完成标准：

- 干净克隆可以执行全部已登记测试；
- push/PR 中测试失败会阻止质量门禁通过；
- 测试数量和语料来源不再依赖未跟踪文件。

### 第 2 项：统一“标记编号相同”的领域规则

优先级：最高
目标：消除合并、校验、Diff 和改号之间的行为分歧。

现状：

- `DictWriter` 的合并和编号冲突判断忽略大小写；
- `PatEntityHelper.RenameNumberInModelSpace` 忽略大小写；
- `PatCheckCommand` 的 `Dictionary`/`HashSet` 使用默认的区分大小写比较；
- `DictDiff` 直接使用字符串 `==` 比较。

建议处理：

1. 明确编号是否区分大小写，并记录为项目级业务规则；
2. 定义唯一的编号规范化方法或比较器；
3. 在 `DictWriter`、`DictDiff`、`PatCheckCommand`、`PatEntityHelper` 中统一使用；
4. 增加 `1342A/1342a`、前后空白、连字符子编号等回归用例；
5. 将该规则同步到全部五个 CAD 版本。

完成标准：

- 同一对编号在写回、校验、Diff、改号中得到一致结论；
- 五版相关测试或契约用例一致通过。

### 第 3 项：按图纸隔离字典缓存、Diff 基线和配置

优先级：最高
目标：避免多文档切换时状态串用。

现状：

- `DictLoader` 使用单组静态 `_cachedModel`、`_cachedPath`、`_previousModel`；
- 切换到另一张图纸时，上一张图纸模型可能被当成当前图纸的 Diff 基线；
- `ConfigLoader.Current` 只在插件初始化时设置一次；
- `DocumentActivated` 只重载字典，没有按当前 DWG 重载 `config.local.json`/`config.json`。

建议处理：

1. 以文档标识或规范化字典路径为键维护字典会话；
2. 仅在同一路径文件更新时保存 `PreviousModel`；
3. 文档激活时成组切换配置、当前字典、时间戳和 Diff 基线；
4. 文档关闭时清理对应会话；
5. 增加 A/B 两张图纸反复切换、同名文件和默认字典回退的测试。

完成标准：

- A、B 图纸之间不会互相产生 Diff；
- 每张图纸使用自己目录下的配置；
- 文档关闭后不保留无效状态。

### 第 4 项：建立单一运行设置对象，清理分散默认值

优先级：高
目标：让配置、面板状态和标注创建使用同一份有效设置。

现状：

- `PatConfig.PatStyle.TextHeight` 已定义，但生产代码没有读取该值；
- 文字高度 `3.5` 分别硬编码在 `ConfigLoader`、`PatPaletteCommand`、`DictPaletteControl` 和 `PatStyleInitializer`；
- 箭头大小、箭头开关、线型和三点模式也保存在 `PatPaletteCommand` 的公开静态字段中；
- `PatMarkCommand` 直接读取这些 UI 所属的静态字段。

建议处理：

1. 建立明确的运行设置模型，包含文字高度、箭头、箭头大小、线型、点数模式和对齐边距；
2. 配置加载后统一生成当前图纸设置；
3. 面板通过设置对象读写状态；
4. `PatMarkCommand` 和 `PatStyleInitializer` 只依赖设置对象，不反向依赖面板命令类；
5. 明确哪些设置按图纸保存、哪些只在当前会话生效。

完成标准：

- 修改一个默认值只需要改一个定义位置；
- `patStyle.textHeight` 等配置项实际生效；
- 命令层不再依赖 `PatPaletteCommand` 的公开可变字段。

### 第 5 项：拆分 `DictPaletteControl` 的非 UI 职责

优先级：高
目标：降低面板功能继续迭代时的连带影响。

现状：

`DictPaletteControl` 超过 1,050 行，当前同时负责：

- WinForms 控件创建和语言刷新；
- 搜索、选择和状态栏展示；
- 两秒轮询和字典变化检测；
- Diff 计算及高亮；
- 编辑、新增、粘贴识别和冲突裁决流程；
- CAD 实体改号、批量删除、事务和文档锁；
- 通过命令字符串调度 `PATMARK`、`PATSELECTALL`。

建议处理：

1. 先抽出“字典会话/刷新协调器”；
2. 抽出“CAD 标记实体操作服务”，统一改号、扫描和删除；
3. 抽出“编辑与冲突裁决工作流”；
4. 面板只保留控件、显示模型和事件转发；
5. 用现有行为测试锁定刷新、编辑、保存并标注和冲突裁决流程。

完成标准：

- 面板事件处理器不直接创建 CAD 事务或操作字典文件；
- 会话、CAD 操作和冲突流程可以脱离 WinForms 做单元测试；
- 拆分不改变现有命令及用户交互。

### 第 6 项：为 VBA 与 C# 识别器建立唯一契约

优先级：中高
目标：降低识别规则双实现导致的漂移风险。

现状：

- VBA 的 `Patterns.bas`、`DictModel.bas` 实现正则、预处理和段落截取；
- C# 的 `MarkingTextParser.cs` 手工移植同一逻辑；
- C# 文件又复制到五个 CAD 版本；
- 修复一种格式时需要同时确认 VBA、C# 和所有部署包副本。

建议处理：

1. 建立受版本控制的共享语料和期望 JSON/命中结果；
2. 同一份语料分别运行 C# 测试与 VBA/Word COM 验证；
3. 将规则版本号写入契约，避免只依赖注释中的 v3.0/v3.2；
4. 评估由一份规则清单生成 VBA/C# 正则常量的可行性；
5. 短期无法生成时，至少让跨语言语料比较成为发布前硬性检查。

完成标准：

- 新增或修复规则时只维护一份期望语料；
- VBA 与 C# 对全部契约样本输出一致；
- 干净克隆可以复现比较结果。

### 第 7 项：减少五版业务源码的人工复制

优先级：中高
目标：保留 AutoCAD 版本隔离，同时建立业务代码单一来源。

现状：

- 五版运行时和 AutoCAD API 分离是合理约束；
- 但大量 IO、I18n、对话框和面板业务代码以整文件复制存在；
- `check-version-sync.ps1` 只能报告哈希不同，无法识别预期差异；
- `build.ps1 -Static` 的跨版本漂移目前是非阻断警告；
- `downlevel-port.js` 写死本机路径，且只处理部分 2025→2015 文件。

建议处理：

1. 将文件分为“完全共享”“按 API 家族共享”“版本专属”三类；
2. 对完全共享代码采用链接文件、生成复制或独立核心项目；
3. 对 Leader/MLeader 差异保留薄适配层；
4. 将降级脚本改为基于仓库根目录、可重复、可校验的生成命令；
5. 为允许差异建立清单，使未知漂移成为失败而不是常态警告。

完成标准：

- 共享业务修复有唯一修改源；
- 生成后工作区可通过命令验证无未知漂移；
- 静态检查不再长期输出无法行动的基线警告。

### 第 8 项：建立可验证的本地发布/打包流程

优先级：中高
目标：证明部署包 DLL 与当前源码、依赖和 VBA 模块一致。

现状：

- `build.ps1` 负责编译，但不完成部署包更新；
- 2013/2015 的 ILRepack、DLL 复制及依赖验证依赖人工步骤；
- 结构检查只验证部署 DLL 存在；
- 静态检查只验证部署目录没有外部 Newtonsoft.Json；
- 当前部署 DLL 是否与源码完全一致仍待确认。

建议处理：

1. 新增本地 release/package 命令，按版本完成编译、ILRepack、复制和 VBA 同步；
2. 校验 2013/2015 合并后不再引用外部 Newtonsoft.Json；
3. 记录源码提交、程序集版本、文件哈希和目标 AutoCAD 版本；
4. 打包后执行结构检查、静态检查、单元测试和人工冒烟检查清单；
5. 不把 Autodesk SDK DLL 写入仓库或部署包。

完成标准：

- 从准备好 SDK 的干净工作区可以用单一命令生成五套部署包；
- 部署 DLL 的来源和依赖可验证；
- 发布过程不再依赖手工复制文件。

## 3. 经审阅的解除阻碍计划

本节为本轮执行前经过确认的顺序计划。当前机器可以安装 MSBuild，但本轮 AutoCAD 实机验证环境仅提供 2007 和 2025；2010、2013、2015 只能完成编译、静态和发布物检查，不能据此推断运行时正确。

### 第 1 步：建立完整编译工具链

安装并确认 MSBuild、所需 .NET Framework targeting pack、NuGet/ILRepack。先尝试编译五个版本，记录旧版项目、SDK 引用和 Newtonsoft.Json 合并的实际问题。Autodesk SDK DLL 由本机提供，不提交仓库。

### 第 2 步：固化验证矩阵

明确并记录以下验收层级：2007/2025 执行编译、自动化测试和对应 AutoCAD 实机回归；2010/2013/2015 执行编译、静态检查及（2013/2015）ILRepack 依赖检查；缺少对应 AutoCAD 时统一标记为“编译已验证、运行时待确认”。

### 第 3 步：固化 VBA/C# 解析契约

将脱敏输入样本、预期字典结果、编码和异常处理规则纳入版本控制，使干净环境可以复现 C# 测试与 VBA/Word COM 结果比较。若缺少 Word/VBA 自动化环境，则把人工确认过的 VBA 导出结果作为基准样本，并明确其验证边界。

### 第 4 步：完成多文档状态隔离

处理 `ConfigLoader.Current`、`DictLoader` 和 `PatSettings` 的文档生命周期，以文档/规范化字典路径为边界切换配置、字典、时间戳和 Diff 基线，并在文档关闭时释放状态。覆盖文档切换、未保存图纸、同名不同路径和关闭重开场景。

### 第 5 步：拆分 `DictPaletteControl`

在状态边界稳定后，按“字典刷新协调器 → CAD 标记实体服务 → 编辑/冲突工作流 → 纯 WinForms 视图”的顺序抽取职责，保持现有命令和交互不变。每次抽取后在 2007/2025 AutoCAD 环境验证面板打开、切换、刷新、标注和冲突处理。

### 第 6 步：同步五版本并完成编译闭环

按“2025 行为验证 → 2013/2015/2025 Leader + MText/依赖适配 → 2007/2010 Leader/旧 CLR 适配”同步共享逻辑。每批修改后运行五版本编译；2013/2015 同时执行 ILRepack，并确认合并程序集不再引用外部 Newtonsoft.Json。

### 第 7 步：执行 2007/2025 实机回归

在对应 AutoCAD 中覆盖 `NETLOAD`、`BZ`、`BZM`、`BZC`、`BZA`、文档切换、字典更新和关闭重开；2007 重点验证 Leader + MText/CLR 2.0，2025 重点验证 Leader + MText/.NET 8 和面板事件。

### 第 8 步：封存无法完成的运行时验证

为 2010、2013、2015 保留最小实机验证清单及所需 AutoCAD 年份。交付状态明确写成“编译通过、运行时待验证”，不以缺少环境为由伪造闭环，也不让它阻塞可以独立完成的结构修复。

## 4. 建议实施阶段

### 阶段 A：先建立保护

依次完成第 1、2 项。此阶段不做大规模目录调整，先固定现有行为和编号规则。

### 阶段 B：修正状态边界

依次完成第 3、4 项。完成后再继续增加依赖多文档、配置或标注样式的新功能。

### 阶段 C：降低面板复杂度

完成第 5 项，并用阶段 A 建立的测试保护现有交互。

### 阶段 D：治理跨语言和跨版本复制

依次完成第 6、7 项。优先治理仍在频繁变化的识别器、字典写回和面板工作流。

### 阶段 E：闭合发布链路

完成第 8 项，使源码修改、五版构建和部署包交付形成可重复闭环。

## 5. 不建议立即进行的工作

- 不建议把五版强行合并为一个可加载 DLL；AutoCAD API 与 CLR 版本边界必须保留；
- 不建议一次性重写全部 WinForms UI；应先抽离非 UI 行为；
- 不建议仅为了减少文件数而引入旧版 .NET 无法运行的新依赖；
- 不建议在缺少回归测试时同时修改解析规则、字典格式和 CAD 实体逻辑；
- 不建议把 Autodesk SDK DLL 提交到仓库来换取 CI 编译。

## 6. 验证基线

本轮执行前工作区已有用户修改和未跟踪文件；执行过程中没有重置、删除或覆盖这些文件。以下结果只记录本轮实际执行过的检查，不把主机运行时验证等同于编译通过：

- MSBuild 已从 `C:\BuildTools\MSBuild\Current\Bin\MSBuild.exe` 发现；
- 2025 测试项目：106/106 通过（新增 `RuntimeHost` 边界测试）；
- 五个版本 `build.ps1 -Version all`：全部编译通过；2025 仅保留 AutoCAD SDK 与 `WindowsBase` 的既有版本冲突警告；
- `build.ps1 -Structure`：五版通过；
- `build.ps1 -Static`：通过，但有 13 条非阻断跨版本漂移警告（新增 `IO/RuntimeHost.cs` 的 2025 可空性差异属于预期版本适配）；
- `check-version-sync.ps1`：`NumberIdentity.cs`、`PatSettings.cs` 两项关键共享契约通过，其余差异符合 API/TFM/JSON 版本矩阵或待人工确认；
- `node downlevel-port.js`：dry-run，0 个残留语法问题，未写入文件；
- `package.ps1 -Version all`：五版暂存和 2013/2015 ILRepack 引用检查通过，最新暂存目录为 `C:\Users\wjm\AppData\Local\Temp\PatentCAD-Annotator-release-20260806-000022`；未覆盖现有部署包；
- 本机未发现 AutoCAD 2007 或 2025 可执行文件；AutoCAD 2026 的 COM 启动尝试返回 `80080005 (CO_E_SERVER_EXEC_FAILURE)`，未形成可重复的交互式主机验证。

## 7. 本轮按顺序执行情况

已完成或已达到当前环境可验证边界：

1. 将测试项目加入 `PatentCAD.sln`，并在 GitHub Actions 中加入 2025 版可移植单元测试门禁；新增 8 个受版本控制的解析契约样例、面板会话测试、主机边界测试和可选本地语料跳过逻辑，当前本地 Release 测试为 106/106。
2. 保留并扩大五版共用的 `IO/NumberIdentity.cs`，统一编号 trim、大小写不敏感比较，覆盖写回、Diff、校验、改号和面板查找。
3. 将 `DictLoader`、`ConfigLoader`、`PatSettings` 和文档关闭事件改为按图纸路径显式激活/释放；`Current` 仍作为兼容投影保留，实际 AutoCAD 多文档切换尚待主机回归。
4. 将配置、面板、标注创建、样式初始化和对齐命令接入同一运行时设置，并按图纸路径隔离面板开关。
5. 从五版 `DictPaletteControl` 抽出 `Palette/DictPaletteSession.cs`，集中字典列表、过滤、Diff 和计数逻辑，并加入单元测试；后续已继续抽出共用的字典工作流、CAD 事务服务和实体辅助层，WinForms 视图生命周期仍留在版本目录中。
6. 修正 `build.ps1` 的 MSBuild 发现和失败码传播；清理 2013/2015 旧 NuGet 资产导致的 RID 误报；五版编译均通过。新增 `package.ps1`，默认在临时目录生成五版发布暂存，校验 VBA 同步及 Newtonsoft.Json 合并引用。
7. VBA 与 C# 仍是两套解析实现；本轮固化了 8 个脱敏 C# 解析契约样例，但干净克隆环境中的 Word COM/VBA 完整对照仍待确认。

8. 在五个版本新增 `IO/RuntimeHost.cs` 主机边界，并将命令、样式初始化、配置/字典读取和面板中的活动文档读取统一接入；文档事件订阅仍保留原生 `DocumentManager`，避免改变事件生命周期。静态检查现在会阻止新增绕过边界的 `MdiActiveDocument` 读取。
9. 新增 `cad-plugin/RuntimeContract.Tests/`：使用严格的 Editor/Database/Transaction 模拟约束，直接链接 2010/2013/2015 的 `PatMarkCommand`，覆盖三点输入、取消、自由模式、事务回滚和跨文档设置隔离；三版各 5/5，共 15/15 通过。
10. 新增 `check-api-contract.ps1` 与 `tools/ApiSurfaceCheck/`，以 `MetadataLoadContext` 检查五版当前 Leader + MText 所需的实际 SDK 类型、属性和方法；高版本 MLeader 仅作为历史兼容面保留，不进入新建标注路径；本机 SDK API 表面检查全部通过。
11. AutoCAD 2026 Core Console 已对编译后的 2025 DLL 执行一次非交互脚本冒烟：进程退出码为 0，输出包含 `_.NETLOAD` 和 `_.PATCHECK`；由于 Core Console 不提供面板交互，且该次输出没有可独立确认的业务结果，记录为“部分冒烟证据”，不计作完整 2025 UI/实体回归。
12. 新增 `check-autocad-host.ps1` 只读诊断：检查 AutoCAD/Core Console 文件、`AdskLicensingService` 和 COM 注册，不启动 AutoCAD、不改注册表/服务/安全设置；当前报告为主机文件、服务和 COM 前置条件存在，但仍不能证明账号授权有效。

仍受外部环境阻塞：

- 第 7 步的 AutoCAD 2007 `NETLOAD`、`BZ/BZM/BZC/BZA/BZS`、文档切换和关闭重开回归无法在当前机器执行；2025 已有 AutoCAD 2026 Core Console 冒烟，但完整面板/实体回归仍待交互式主机；
- 2010/2013/2015 已增加命令级模拟动态契约，但真实旧版 AutoCAD 的加载器、事务和 WinForms 行为仍待对应主机确认；
- `DictPaletteControl` 的剩余 CAD/冲突工作流和 `ConfigLoader.Current` 兼容投影需要在主机验证后再决定是否继续抽离。

## 8. 本轮验证命令

- `dotnet sln PatentCAD.sln list`：测试项目和 2025 生产项目均已列入方案；
- `dotnet test .\cad-plugin\2025\PatentMarker.Tests\PatentMarker.Tests.csproj --configuration Release --no-restore`：106/106 通过；
- `powershell -File .\build.ps1 -Version all`：2007、2010、2013、2015、2025 全部通过；
- `powershell -File .\build.ps1 -Structure`：通过；
- `powershell -File .\build.ps1 -Static`：通过，13 条跨版本漂移警告仍为非阻断项；
- `powershell -File .\build.ps1 -Simulation`：2010/2013/2015 各 5/5，共 15/15 通过；
- `powershell -File .\check-api-contract.ps1 -Version all`：2010/2013/2015/2025 SDK API 表面约束全部通过；
- `powershell -File .\check-autocad-host.ps1 -RequireInstalled`：只读发现 AutoCAD 2026、Core Console、许可服务和 COM 注册；不对授权有效性作结论；
- `powershell -File .\check-version-sync.ps1`：关键共享契约通过；
- `node .\downlevel-port.js`：dry-run，0 个残留语法问题；
- `powershell -File .\package.ps1 -Version all`：暂存和 ILRepack 引用检查通过；
- AutoCAD 2026 Core Console 冒烟命令：`accoreconsole.exe /i <sample.dwg> /s <PatentCAD-autocad2026-smoke.scr> /l en-US`，退出码 0；仅作为部分主机证据，不替代交互式回归；
- `git diff --check`：通过。

## 9. 总体判断

当前项目仍属于“存在局部技术债，建议定点处理”。本轮已经解除测试接入、编译工具链、缓存/配置生命周期、主机边界模拟和发布暂存等可在本机完成的阻碍；AutoCAD 2026 已提供 2025 版的部分 Core Console 冒烟证据，但交互式主机回归、跨 Word COM 契约及面板剩余 CAD 工作流仍待确认。因此可以继续小步开发，不建议暂停所有功能进行全面重构；新增功能应继续先经过模拟契约和五版构建门禁。

## 10. 2026-08-05 执行补充与验证边界

本节用于覆盖前文在计划制定时对主机环境的保守描述：仓库版本矩阵明确 2025 版覆盖 AutoCAD 2025–2026+，因此 AutoCAD 2026 可以作为 2025 版验证主机；但本机的 COM 自动化服务器没有成功启动，不能把“安装了 2026”写成完整实机回归。

- 交互式 COM：尝试 `AutoCAD.Application.25.1`、`AutoCAD.Application.25` 和基础 ProgID，均返回 `80080005 (CO_E_SERVER_EXEC_FAILURE)`；未留下运行中的 AutoCAD 进程。
- Core Console：使用本地样例 DWG 和临时脚本加载 `cad-plugin/2025/PatentMarker/bin/Release/net8.0-windows/PatentMarker.dll`，脚本包含 `_.NETLOAD`、`_.PATCHECK`、`_.QUIT`，进程退出码为 0。输出能确认命令脚本被送入 Core Console，但没有可独立读取的面板/实体业务断言，所以仅标记为“部分冒烟”。
- 主机边界与动态模拟：五个版本的命令、面板、样式初始化、配置/字典读取统一通过 `IO/RuntimeHost.cs` 获取活动文档；严格模拟 Leader + MText 的创建前置条件、点拾取取消、自由模式末点、事务提交失败回滚和按文档隔离设置；这些测试直接使用对应版本的生产 `PatMarkCommand`，不是复制一份测试实现。
- API 约束：`check-api-contract.ps1 -Version all` 通过 2010/2013/2015/2025 Leader + MText 所需元数据检查，并确认新建命令不引用 MLeader，避免用错误的 API 名称或跨代标注类型编译。

后续真正解除剩余阻碍的顺序保持不变：先取得可交互的 AutoCAD 2025/2026 主机完成 `BZ/BZM/BZC/BZA/BZS` 和多文档回归，再在有对应旧版主机时执行 2010/2013/2015 的安装加载回归；在此之前，不把模拟测试或 Core Console 冒烟升级为完整运行时通过。

## 11. 2026-08-06 共享源码层执行记录

按本计划“先降低复制、再扩大拆分”的顺序，完成了第一批跨版本源码收敛：

- 新增 `cad-plugin/Shared/` 作为唯一共享源，收纳 `NumberIdentity`、`PatSettings`、`DictDiff`、`DictConflict`、`MarkingTextParser` 和 `Language` 六个不依赖 AutoCAD 实体的模块；五个 `PatentMarker.csproj` 通过链接编译，仍保留各自 .NET/AutoCAD SDK 边界，没有把五版合并成一个 DLL。
- 删除五个版本目录中的 30 份旧副本；删除前已备份到 `C:\Users\wjm\AppData\Local\Temp\PatentCAD-shared-source-backup-20260806-151835`。2025 单元测试和 2010/2013/2015 模拟测试项目同步改为链接共享源。
- `build.ps1 -Static` 新增共享层存在性、五版项目链接和“禁止本地重复副本”检查；`check-version-sync.ps1` 改为校验共享源，而不是要求同名文件复制到五个版本目录。构建脚本的版本信息也同步为五版 Leader + MText。
- 本轮实际验证：`build.ps1 -Structure` 通过；`build.ps1 -Static` 通过（7 条既有版本差异警告）；五版 `build.ps1 -Version all` 通过；2025 单元测试 106/106 通过；2010/2013/2015 模拟测试各 5/5，共 15/15；`check-version-sync.ps1` 共享层检查通过。

这一步只解决“同一纯逻辑多份维护”的结构性阻碍；`RuntimeHost`、`DictPaletteControl`、AutoCAD 事务和版本特有 JSON/UI 代码仍按计划保留在各版，待主机验证后再继续拆分。

## 12. 2026-08-06 面板职责拆分执行记录

在动态模拟基线通过并建立增量备份后，继续按“先非 UI、再宿主边界”的顺序拆分：

- 新增 `Shared/Palette/DictPaletteWorkflow.cs`，集中处理字典路径、缓存失效、当前字典加载、冲突判断和编号查找；`DictPaletteControl` 不再直接编排这些文件状态操作。
- 新增 `Shared/Palette/DictPaletteCadService.cs`，集中处理文档锁、事务、Leader/MText 编号同步和批量删除；面板只负责确认、状态提示和刷新。
- 新增 `Shared/Cad/PatEntityHelper.cs`，收敛五个版本重复的 Leader/MText/DBText 识别与文字更新辅助代码。
- 新增 `Shared/Palette/DictPaletteSession.cs`，收敛五个版本重复的当前字典、Diff 基线、筛选结果及统计逻辑；删除五个版本中的本地副本。
- 当前拆分的增量回滚备份为 `C:\Users\wjm\AppData\Local\Temp\PatentCAD-palette-split-backup-20260806-20260806-160731` 和 `C:\Users\wjm\AppData\Local\Temp\PatentCAD-session-split-backup-20260806-162727`；前一批共享层备份仍保留。
- 本轮最终复核：五版本 `build.ps1 -Version all` 通过；模拟宿主测试 2010/2013/2015 各 7/7，共 21/21；2025 单元测试 108/108；`build.ps1 -Structure`、`build.ps1 -Static`、`check-version-sync.ps1`、`check-api-contract.ps1 -Version all` 和 `git diff --check` 均通过。2025 构建仍有既有 WindowsBase/可空性警告，无错误。

至此，字典会话、字典工作流和 CAD 实体事务已经形成可独立复核的边界。剩余 `DictPaletteControl` 的纯 WinForms 视图布局与事件生命周期仍需要交互式 AutoCAD/WinForms 主机验证；在缺少该主机时不继续做高风险的视图对象拆分，也不把本轮结果表述为完整 UI 重构。

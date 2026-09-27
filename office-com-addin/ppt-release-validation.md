# PowerPoint 0.1.1.0 发布验收记录

记录时间：2026-09-25 至 2026-09-27。执行身份：本机当前用户；精确身份留在本机原始快照。环境：Windows 10 build 19045、Microsoft 365 PowerPoint 16.0.20430.20092 x64、.NET Framework 4.8.1；程序集目标为 .NET Framework 4.0。本记录分别保留前一候选和 2026-09-27 重建候选的证据；GitHub 发布另行执行。

## 当前发布候选（2026-09-27）

候选包：`PatentMarker-WordPowerPoint-0.1.1.0-20260927-095350.zip`，SHA-256 `9C437F4E2E7B991F472E3CC3476B18BC48886763B9F12E40CCEE9E79D536A477`。包内和实际安装的产品 DLL SHA-256 均为 `11991315EF0631F1893B113415D5D4B4E975A33A8CFCAC9C616E5306B10EEC79`；编译时 Git 源码提交为 `42f4c7f`。包内 README 已明确标记实验性、鼠标画线/视觉布局和 Office 2010 x86 待验。

| 场景 | 结果 | 证据层级 |
|---|---|---|
| 从当前 ZIP 构建与安装 | PASS，net40 Release 0 warning、0 error；隔离安装器回归和精确包内容/哈希校验通过；从该 ZIP 解压后覆盖实际产品安装。安装前后原 Word 字典、非产品文件及其他加载项注册项快照相等，安装后产品 DLL 哈希与包内一致 | L0 构建与包；实际产品安装。原始快照位于本机 `test-evidence/ppt-candidate-20260927-889ec8f5/` |
| 新包 DLL 与前一候选 | 文件 SHA-256 不同；程序集长度、版本和 100 个方法的逐方法 IL、栈信息及异常区一致。程序集信息的源码提交号从旧候选的 `e879d04d...` 变为 `42f4c7fd...`；不据此把两包视为逐字节相同 | 代码层比较 PASS；`method-il-comparison.json`，不代替新包宿主实测 |
| 新包冷启动标注 | run `2146f608ab744d1aa521624decf9d7d9`：加载安装 DLL、自动恢复真实 Word UI 字典副本并读 2 项；面板先检查无漏标，PowerPoint 键盘选中第一页原生未标注直线后，面板增加编号 1 的第二个标注组，日志 `annotation.create PASS;slide_id=256`；再次全稿检查 `missing=0;marked=2` | 安装版限定 L3；图片和原直线来自测试稿，选线、选编号、标注、检查通过界面键盘完成 |
| 新包保存与冷重开 | PASS，PowerPoint `Ctrl+S` 保存后 PPTX 有编号 1、1、2 的三个产品 Tags 部件；PPTX SHA-256 `6F6D5F57593D516F6BF724DDBAFB0D9593FCD86D61BA6DE347FAEB661E971BF5`。完全退出后 run `8499ed773a404b818a36d0d3fc7d7479` 自动恢复绑定、读 2 项并由面板检查 `missing=0;marked=2`，字典哈希仍为 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479` | 安装版限定 L3，实际产物及独立冷进程复核 |
| 本轮最终状态 | PowerPoint 进程 0；产品文件及注册项与安装后快照相等，原 Word 字典和其他产品外文件不变。严格的其他加载项注册项快照再次不等：`MSOfficePLUS.FeaturesGate.LastUpdateTime` 在首次新包宿主启动时从 `2026-09-26T16:45:44.633519Z` 变为 `2026-09-27T01:56:37.7243529Z`，其余值不变。第三方定时刷新是依据其 60 分钟配置和时间关系所作推断，尚无独立归因实验 | 产品安装隔离 PASS；全程其他注册项逐字节不变 FAIL |

## 前一候选与历史基线

### 场景明细

| 场景 | 结果 | 证据层级 |
|---|---|---|
| net40 Release 构建 | PASS，0 warning、0 error | L0 |
| 干净源码副本还原和构建 | PASS，仅复制 PowerPoint 项目顶层源码与共享编号比较文件；0 warning、0 error，未依赖工作树中的本地 CAD NuGet 目录 | L0 |
| 快速切换演示文稿后立即检查 | 旧已安装 0.1.0.0 DLL 稳定 FAIL：仍报告 A 文稿漏标 1、2；0.1.1.0 处理器在相同场景 PASS：识别 B 文稿并取消这次检查 | L1，模拟宿主对象和真实面板处理器 |
| 关联 XML 含未知路径类型 | 旧已安装 0.1.0.0 DLL FAIL：把未知类型当绝对路径接受；0.1.1.0 在相同输入上 PASS：要求重新绑定，正常绝对路径仍可解析 | L1，模拟 Custom XML 部件和真实解析器 |
| 同卷与异卷路径编码 | PASS，`verify-ppt-binding-kind.ps1` 对已安装 DLL 的写入路径计算作代码层断言：同卷得 `relative` 与 `near.dict.json`，模拟异卷 `Z:\PatentMarker\far.dict.json` 得 `absolute` 且路径不变；结果位于 `test-evidence/ppt-binding-kind-20260926-173941-ea6d302f/result.json` | L1；真实跨卷宿主场景见下文 |
| 安装器首次故障回滚、重复安装、升级故障回滚和卸载 | PASS，隔离 HKCU 树和临时目录 | 安装器脚本回归；非生产注册项 |
| 未登记文件与无关注册项保护 | PASS，目录存在未登记文件时安装和卸载均拒绝，文件和注册项哨兵不变 | 安装器脚本回归 |
| 发布 ZIP 文件清单和哈希 | PASS，精确 8 个资产加清单；逐文件 SHA-256 与源码、构建产物相同 | L0 |
| 从 ZIP 解压并执行包内安装、卸载脚本 | PASS，隔离目录及注册表树，安装 DLL 哈希与包内相同 | 安装器脚本回归 |
| 真实 PowerPoint 运行时阻止生产升级 | PASS，安装器拒绝；原 0.1.0.0 DLL SHA-256 与产品 `LoadBehavior=3` 不变 | 实际安装路径的负例 |
| 从前一候选 ZIP 覆盖实际产品安装 | PASS，当时安装版本 0.1.1.0，DLL SHA-256 与前一包内一致；真实 Word 字典、其他产品文件及 PowerPoint 其他加载项注册快照不变 | 前一候选的实际安装路径 |
| 第一次全新 PowerPoint 加载 | PASS，PID `11784`、run `cf9b5340f5104f6fb7c3166ab5158915`；`OnConnection`、`OnAddInsUpdate`、`OnStartupComplete` 均 PASS，日志的加载路径对应实际产品 DLL | 安装后宿主加载子链 |
| 真实 PowerPoint COM 对象标注与持久化 | PASS，绑定真实 Word UI 字典；图片误选被拒，编号 1 跨两页重复标注合法，编号 2 另标；保存关闭后重开，读取 1 个产品 Custom XML 部件和编号 1/1/2 的 3 个产品组、2 张原图片；字典 SHA-256 不变 | L2 宿主对象回归；直接调用方法，绕过面板 |
| 第二次全新 PowerPoint 重开 | PASS，PID `20948`、run `6ca260ba4806417bb5ecdd82a917a89e`；保存产物重开后加载项自身记录 `binding.resolve PASS`、`dict.read PASS`，2 页、2 张图片、3 个产品组和 1 个绑定部件仍在；真实面板执行全稿检查得到 `missing=0;marked=2` | 安装后加载、自动读取与面板检查 |
| 真实面板绑定与跨页标注 | PASS，同一 run 中切换到第二份不同主名的已保存 PPTX，面板先显示“未设置”；在路径栏绑定安装版 Word UI 导出的 2 项字典。面板分别在第 1、2 页标注编号 2、1，检查先报漏标 1，随后报无漏标；日志依次有 `annotation.create PASS`、`missing=1;marked=1`、`annotation.create PASS`、`missing=0;marked=2` | 限定 L3：图片、直线及选线由 COM 准备；绑定、选编号、标注与检查经面板键盘操作 |
| PowerPoint UI 保存、退出与冷重开 | PASS，面板标注后由 PowerPoint UI `Ctrl+S` 保存，文件包含两页各一个产品标注组；退出 PID `20948` 后，以文件路径全新启动 PID `20804`，run `659e3167c3ea4f708356e93662c58346` 自动读回同一字典，面板再次检查得到 `missing=0;marked=2` | 限定 L3 保存、持久化与冷重开 |
| 字典损坏、暂时丢失与恢复 | PASS，在第三份 PPTX 的测试字典副本上故障注入。损坏 JSON 时，面板保留 2 项列表、显示解析错误并禁用标注/检查，日志 `dict.read FAIL`；恢复后自动读回并启用。将副本暂时移开时，面板提示文件不存在、保留列表且禁用操作；移回后自动恢复。原 Word 字典全程未改 | 安装版面板与文件轮询实测，run `659e3167c3ea4f708356e93662c58346` |
| 图片误选 | PASS，第三份 PPTX 选中原图后，从面板执行标注，出现“选中的对象不是 PowerPoint 原生直线”提示，日志 `annotation.create FAIL`；两页形状数仍各为 2，字典哈希不变 | 安装版面板负例；图片选择由 COM 准备 |
| PowerPoint 界面插入原生直线 | PASS，在已保存的测试 PPTX 副本上，通过 PowerPoint“插入 → 形状 → 线条 → 直线”的键盘入口新增 `直接连接符 7`；面板标注编号 1 成功，run `e3b2e9e083e842cb9e46114bec4eecdb` 记 `annotation.create PASS;slide_id=256`。此前从同一形状库误选箭头连接符时面板拒绝，撤销后再选直线转为 PASS | 安装版宿主与真实界面操作；直线由 PowerPoint UI 创建并保持选择；未做鼠标拖拽和位置目检 |
| 界面新建直线保存并冷重开 | PASS，由 PowerPoint `Ctrl+S` 保存，PPTX 中第一页为含 `直接连接符 7` 和编号文字的组，`ppt/tags/tag1.xml` 为 `PATNUMBER=1`；完全退出后新 run `65cb01db81bb476c85d3c748473757e9` 自动恢复绑定，面板检查为 `missing=1;marked=1`，只漏编号 2 | 限定 L3，UI 创建、面板标注、UI 保存与新进程复核 |
| PowerPoint 键盘选线并跨页标注 | PASS，PowerPoint `Ctrl+PageDown` 到第二页，两次 `Tab` 依次选中原图（type 13）和 `PM_TEST_LINE_2`（type 9）；只读宿主对象确认选择归属第二页，再由面板标注编号 2。run `65cb01db81bb476c85d3c748473757e9` 记录 `annotation.create PASS;slide_id=257` 和 `missing=0;marked=2` | 限定 L3，第二页直线由 COM 预备，但选线、选编号和标注经界面键盘完成 |
| 两种界面路径最终持久化 | PASS，第二次 PowerPoint `Ctrl+S` 后，PPTX 两页各 1 个产品组，`ppt/tags/tag1.xml`、`tag2.xml` 分别记录编号 1、2；新 run `a2b5d87696b54d06bbd81e01779a3b8b` 冷重开并经面板检查 `missing=0;marked=2`。测试字典副本与原 Word 字典 SHA-256 相同 | 安装版、实际保存文件和独立冷进程复核 |
| PPT 与字典目录存在无关 DWG | PASS，在该测试目录新增独立 `.dwg` 文件名哨兵后，全新 run `682ba39ca4bd4283a019f0be1c4d6dd7` 仍从 PPT 绑定部件恢复 `recovery.dict.json`，面板读 2 项、全稿检查 `missing=0;marked=2`；没有按 DWG 或 PPT 主名猜测字典。哨兵仅验证文件存在，不用于 CAD 格式或 Word 导出验收 | 安装版 PPT 文件关联实测；Word 的 DWG 命名分支另验 |
| 真实跨卷绑定、异目录副本冷重开 | PASS，C: 测试 PPTX 经面板绑定 F: 字典副本，run `9dd3378adafa42d1b123f2065a13e6e9` 记录 `binding.save PASS` 与 `dict.read PASS`；PowerPoint UI 保存后，PPTX 的唯一产品 Custom XML 部件为 `kind="absolute"` 并保留 F: 路径。复制 PPTX 到 C: 另一目录后，全新 run `f6152041a75e45e5903584777ca97e85` 自动解析同一 F: 字典并由面板检查 `missing=0;marked=2`。异目录副本 PPTX SHA-256 `8B62E99686845EEE78EFF826D63707657045E8579780633E125658D2E80911AF`；字典副本与原 Word 字典 SHA-256 均为 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479` | 安装版真实 C:/F: 卷、面板绑定、保存、冷启动及异目录路径复核；原始断言在本机 `test-evidence/ppt-cross-volume-20260926-174212-0bd8b315/cross-volume-result.json` |
| 跨卷测试副本清理 | SKIP/BLOCKED，测试结束后 PowerPoint 已退出；当前执行环境的自动策略拒绝删除本轮 F: 临时目录。目录只含本轮创建的脱敏字典副本，留待用户或获授权的环境清理 | 本机资源清理缺口，不计入跨卷业务 PASS |
| 最终非产品注册项快照 | FAIL（严格字节相等）：`MSOfficePLUS` 的 `FeaturesGate.LastUpdateTime` 从 `2026-09-25T15:49:38.8357687Z` 变为 `2026-09-26T08:51:52.2414261Z`，恰在本轮首次 PowerPoint 冷启动后。该键声明 60 分钟刷新间隔；其 `LoadBehavior`、其他值、其他加载项、全部非产品文件不变。产品安装后与前一轮宿主测试后的快照相等；此差异发生于更晚的宿主运行期，来源于第三方加载项自动刷新是基于时间和键内容的推断，未做独立归因实验 | 全局“非产品注册项零变化”未满足；产品安装隔离回归仍 PASS |
| Computer Use 窗口连接 | 初次读取曾得到旧 Visio 面板树；重置 Computer Use 会话后重新枚举，PowerPoint 面板树与进程、路径一致，随后键盘路径通过。Windows 19045 截图接口失败，无坐标几何；未做画布拖拽与鼠标选线 | 键盘面板交互 PASS；画布视觉操作未覆盖 |
| 2026-09-27 截图组件重试 | SKIP/BLOCKED，更新后的 Computer Use 组件在新 PowerPoint 进程中首次截图返回 `FrameArrived timed out`；重新枚举同一目标窗口后仅重试一次，返回 `window capture timed out`。随后通过辅助功能状态和键盘关闭本轮进程；仍无截图或可用坐标 | 鼠标拖拽及视觉布局继续待验；这不是加载项业务失败 |

前一候选 ZIP：`PatentMarker-WordPowerPoint-0.1.1.0-20260925-235126.zip`，SHA-256 `F2E60DB3F1A64404D4ED333AD56593B7D1A6C0CD62587E689E4B05FFE216A8F8`。该包内与当时安装的产品 DLL SHA-256 均为 `3135C19BEFAD7A8831347E8876867E9E68A8CA87A21DF884F41C232FDC4A287B`。旧安装 0.1.0.0 DLL SHA-256 `817505CDCDE97D20DEA8C9A7BB70B790283F0304D15136B2A601E72750472B28`。测试输入为安装版 Word UI 导出的两项脱敏字典，验证前后 SHA-256 均为 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479`。直接方法调用的 PPTX 保存后 SHA-256 为 `9D5343F3EBC22715E888ECB9331B185744B089A9EF6407971DB0244927EBF257`；真实面板标注并经 PowerPoint UI 保存的第二份 PPTX SHA-256 为 `012DC673A7C461AC39F64353402C39C0E42FCFB4331248BE868DF6F57263FAC4`。本轮 UI 插线与键盘选线的最终 PPTX SHA-256 为 `B54C064DA51D94012BEA49DDD34917126C922F64E2826494FD59109D3AE544F6`，测试字典副本 SHA-256 仍为 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479`。安装前与前一轮全部宿主测试退出后的快照比较，原字典、其他产品文件和其他 PowerPoint 加载项注册项相等；本轮更晚的冷启动使 `MSOfficePLUS` 时间戳刷新，因此最终全局注册项快照不能写成零变化。最终产品文件及注册项仍与安装后相同，原字典哈希不变，PowerPoint 进程归零。原始快照与产物保存在本机忽略的 `test-evidence/`，公开记录只保留脱敏断言和哈希。

## 放行条件

PowerPoint 16 x64 的已保存 PPTX、字典关联自动恢复、面板标注、全稿检查、保存及全新进程重开已在当前 ZIP 上达到**限定 L3**。首次手动绑定、故障恢复、错误选择、界面键盘插入原生直线与真实跨卷绑定由前一候选完成；两版 DLL 的方法 IL 一致，当前包另完成界面键盘选线后的标注与冷重开。图片及测试直线来自预备的测试稿；此前界面插入的直线采用默认尺寸，未验证其指向图片目标的视觉位置。鼠标在画布上拖拽定向画线、鼠标选线和视觉布局未验，不能宣称完整手工标图路径通过。Windows 7 + Office 2010 x86 未验，不属于此本机原型放行范围。当前候选适合按这些限制发布为实验性 PowerPoint 原型；公开发布记录必须同时列出未覆盖项，不能写成广泛兼容的正式版。

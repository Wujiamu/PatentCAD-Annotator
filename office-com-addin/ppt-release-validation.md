# PowerPoint 0.1.1.0 发布验收记录

记录时间：2026-09-25 至 2026-09-26。执行身份：本机当前用户；精确身份留在本机原始快照。环境：Windows 10 build 19045、Microsoft 365 PowerPoint 16.0.20430.20092 x64、.NET Framework 4.8.1；程序集目标为 .NET Framework 4.0。本记录对应下述最终候选 ZIP 及同批源码；GitHub 发布另行执行。

## 当前结果

| 场景 | 结果 | 证据层级 |
|---|---|---|
| net40 Release 构建 | PASS，0 warning、0 error | L0 |
| 干净源码副本还原和构建 | PASS，仅复制 PowerPoint 项目顶层源码与共享编号比较文件；0 warning、0 error，未依赖工作树中的本地 CAD NuGet 目录 | L0 |
| 快速切换演示文稿后立即检查 | 旧已安装 0.1.0.0 DLL 稳定 FAIL：仍报告 A 文稿漏标 1、2；0.1.1.0 处理器在相同场景 PASS：识别 B 文稿并取消这次检查 | L1，模拟宿主对象和真实面板处理器 |
| 关联 XML 含未知路径类型 | 旧已安装 0.1.0.0 DLL FAIL：把未知类型当绝对路径接受；0.1.1.0 在相同输入上 PASS：要求重新绑定，正常绝对路径仍可解析 | L1，模拟 Custom XML 部件和真实解析器 |
| 安装器首次故障回滚、重复安装、升级故障回滚和卸载 | PASS，隔离 HKCU 树和临时目录 | 安装器脚本回归；非生产注册项 |
| 未登记文件与无关注册项保护 | PASS，目录存在未登记文件时安装和卸载均拒绝，文件和注册项哨兵不变 | 安装器脚本回归 |
| 发布 ZIP 文件清单和哈希 | PASS，精确 8 个资产加清单；逐文件 SHA-256 与源码、构建产物相同 | L0 |
| 从 ZIP 解压并执行包内安装、卸载脚本 | PASS，隔离目录及注册表树，安装 DLL 哈希与包内相同 | 安装器脚本回归 |
| 真实 PowerPoint 运行时阻止生产升级 | PASS，安装器拒绝；原 0.1.0.0 DLL SHA-256 与产品 `LoadBehavior=3` 不变 | 实际安装路径的负例 |
| 从最终 ZIP 覆盖实际产品安装 | PASS，已安装版本 0.1.1.0，DLL SHA-256 与包内一致；真实 Word 字典、其他产品文件及 PowerPoint 其他加载项注册快照不变 | 实际安装路径 |
| 第一次全新 PowerPoint 加载 | PASS，PID `11784`、run `cf9b5340f5104f6fb7c3166ab5158915`；`OnConnection`、`OnAddInsUpdate`、`OnStartupComplete` 均 PASS，日志的加载路径对应实际产品 DLL | 安装后宿主加载子链 |
| 真实 PowerPoint COM 对象标注与持久化 | PASS，绑定真实 Word UI 字典；图片误选被拒，编号 1 跨两页重复标注合法，编号 2 另标；保存关闭后重开，读取 1 个产品 Custom XML 部件和编号 1/1/2 的 3 个产品组、2 张原图片；字典 SHA-256 不变 | L2 宿主对象回归；直接调用方法，绕过面板 |
| 第二次全新 PowerPoint 重开 | PASS，PID `20948`、run `6ca260ba4806417bb5ecdd82a917a89e`；保存产物重开后加载项自身记录 `binding.resolve PASS`、`dict.read PASS`，2 页、2 张图片、3 个产品组和 1 个绑定部件仍在；真实面板执行全稿检查得到 `missing=0;marked=2` | 安装后加载、自动读取与面板检查 |
| 真实面板绑定与跨页标注 | PASS，同一 run 中切换到第二份不同主名的已保存 PPTX，面板先显示“未设置”；在路径栏绑定安装版 Word UI 导出的 2 项字典。面板分别在第 1、2 页标注编号 2、1，检查先报漏标 1，随后报无漏标；日志依次有 `annotation.create PASS`、`missing=1;marked=1`、`annotation.create PASS`、`missing=0;marked=2` | 限定 L3：图片、直线及选线由 COM 准备；绑定、选编号、标注与检查经面板键盘操作 |
| PowerPoint UI 保存、退出与冷重开 | PASS，面板标注后由 PowerPoint UI `Ctrl+S` 保存，文件包含两页各一个产品标注组；退出 PID `20948` 后，以文件路径全新启动 PID `20804`，run `659e3167c3ea4f708356e93662c58346` 自动读回同一字典，面板再次检查得到 `missing=0;marked=2` | 限定 L3 保存、持久化与冷重开 |
| 字典损坏、暂时丢失与恢复 | PASS，在第三份 PPTX 的测试字典副本上故障注入。损坏 JSON 时，面板保留 2 项列表、显示解析错误并禁用标注/检查，日志 `dict.read FAIL`；恢复后自动读回并启用。将副本暂时移开时，面板提示文件不存在、保留列表且禁用操作；移回后自动恢复。原 Word 字典全程未改 | 安装版面板与文件轮询实测，run `659e3167c3ea4f708356e93662c58346` |
| 图片误选 | PASS，第三份 PPTX 选中原图后，从面板执行标注，出现“选中的对象不是 PowerPoint 原生直线”提示，日志 `annotation.create FAIL`；两页形状数仍各为 2，字典哈希不变 | 安装版面板负例；图片选择由 COM 准备 |
| Computer Use 窗口连接 | 初次读取曾得到旧 Visio 面板树；重置 Computer Use 会话后重新枚举，PowerPoint 面板树与进程、路径一致，随后键盘路径通过。Windows 19045 截图接口失败，无坐标几何；未做画布拖拽与鼠标选线 | 键盘面板交互 PASS；画布视觉操作未覆盖 |

最终候选 ZIP：`PatentMarker-WordPowerPoint-0.1.1.0-20260925-235126.zip`，SHA-256 `F2E60DB3F1A64404D4ED333AD56593B7D1A6C0CD62587E689E4B05FFE216A8F8`。包内与实际安装产品 DLL SHA-256 均为 `3135C19BEFAD7A8831347E8876867E9E68A8CA87A21DF884F41C232FDC4A287B`。旧安装 0.1.0.0 DLL SHA-256 `817505CDCDE97D20DEA8C9A7BB70B790283F0304D15136B2A601E72750472B28`。测试输入为安装版 Word UI 导出的两项脱敏字典，验证前后 SHA-256 均为 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479`。直接方法调用的 PPTX 保存后 SHA-256 为 `9D5343F3EBC22715E888ECB9331B185744B089A9EF6407971DB0244927EBF257`；真实面板标注并经 PowerPoint UI 保存的第二份 PPTX SHA-256 为 `012DC673A7C461AC39F64353402C39C0E42FCFB4331248BE868DF6F57263FAC4`。安装前与全部宿主测试退出后的快照比较：原字典、其他产品文件、其他 PowerPoint 加载项注册项不变；最终产品文件及注册状态与安装后快照相同，PowerPoint 进程归零。原始快照与产物保存在本机忽略的 `test-evidence/`，公开记录只保留脱敏断言和哈希。

## 放行条件

PowerPoint 16 x64 的已保存 PPTX、字典绑定、面板标注、全稿检查、保存、全新进程重开、故障恢复和错误选择已达到**限定 L3**。直线与图片由 PowerPoint COM 预先创建，选线也通过 COM 完成；画布手绘、鼠标选择和视觉布局未验，不能宣称完整手工标图路径通过。Windows 7 + Office 2010 x86 未验，不属于此本机原型放行范围。当前候选适合按这些限制发布为实验性 PowerPoint 原型；公开发布记录必须同时列出未覆盖项，不能写成广泛兼容的正式版。

# Word → Office 只读标图加载项

本目录包含两个相互独立、面向 .NET Framework 4.0 的 Office COM 加载项。以下步骤描述 PowerPoint：它读取 Word 导出的 `.dict.json`，在已有图片上把用户绘制的 PowerPoint 直线与编号组成原生形状组，并检查整份演示文稿里的漏标编号。Visio 的安装、使用和证据范围见 [Visio 说明](visio-prototype.md)。

PowerPoint 和 Visio 各自仍是独立 DLL、COM 身份、面板、绑定和标注实现。它们通过链接源码共用 Office 字典模型/只读解析器、COM 扩展接口、诊断日志实现，以及 CAD 的 `NumberIdentity`；构建不会增加 Office 公共运行时 DLL。CAD 的字典读写器、面板和宿主代码没有被 Office 加载项复用。

PowerPoint 加载项不会修改 Word、CAD 生产代码或字典文件，也不包含图片导入、CAD 式点选和字典回写。Office 2010 x86 与 Windows 7 仍待目标环境验收。

## 使用方式

1. 先在 Word 中从 PatentMarker 面板导出字典。Word 输出规则以 DWG 为准：无 DWG 时用 Word 文件主名；仅有一个 DWG 时用 DWG 主名；有多个 DWG 时要明确选择目标，自动导出只复用该 Word 文档本次运行中记住的选择，不会猜测。
2. 打开 PowerPoint，先把演示文稿保存为 `.pptx`。加载项面板随 PowerPoint 启动显示。
3. 把已有图片放到幻灯片上；本加载项不导入或修改图片。
4. 在每条引线中，从编号文字应放置的位置向图片上的目标位置绘制一条 PowerPoint 原生直线。选中该线，在面板选择编号并点“标注所选直线”。加载项在直线起点放置字典原样编号，在终点加箭头，并将线与文字分组。绘制方向决定文字和箭头的位置。
5. 保存演示文稿。标注组的产品标识及编号写入组 Tags；重新打开时从幻灯片形状中扫描这些组。同号出现一次即算已标，重复标注允许，普通形状不会计入。
6. 点“检查整份演示文稿”查看字典里还没有对应标注组的编号。检查范围是当前整份演示文稿所有幻灯片，不依赖当前显示的那一页。

首次绑定时需要演示文稿已经保存。在“字典路径”框输入完整 `.dict.json` 路径并点击“绑定此路径”，或点击“选择 / 绑定字典”手动选择。路径保存在当前演示文稿的 Custom XML 部件里。同一卷保存相对路径，不同卷保存绝对路径。若文件丢失，面板会要求重新选择，不按 PPT 文件名猜测。绑定文件每两秒检查一次：解析出错时暂停新增标注和检查、保留上一次有效列表供查看，文件恢复后自动重新读取。

新增绑定、标注组都需要用户保存 `.pptx` 才会落盘；加载项不会自动保存演示文稿。

## 构建与安装

从仓库根目录运行：

```powershell
./office-com-addin/verify-code.ps1
```

`verify-code.ps1` 校验 PowerShell 语法、构建两个 net40 产品、执行两组代码测试，并验证隔离安装/回滚/卸载。它要求 TRX 中实际执行的测试全部通过；无测试或跳过不能通过。CI 的 Office 独立作业运行相同入口，不依赖 Office 或 AutoCAD SDK。隔离安装采用测试用 64 位注册表视图，不探测 Office；单独运行 PPT 安装回归时也可用 `-OfficeBitness 32` 选择测试视图。实际用户安装器仍自动判断宿主位数。宿主面板操作和冷重开另行验收。

需要分别执行时：

```powershell
./office-com-addin/build.ps1
dotnet test ./office-com-addin/tests/PatentOffice.PowerPoint.CodeTests/PatentOffice.PowerPoint.CodeTests.csproj --configuration Release --nologo -v minimal
./office-com-addin/build-visio.ps1
dotnet test ./office-com-addin/tests/PatentOffice.Visio.CodeTests/PatentOffice.Visio.CodeTests.csproj --configuration Release --nologo -v minimal
./office-com-addin/verify-installer.ps1
./office-com-addin/package-ppt.ps1
./office-com-addin/verify-visio-installer.ps1
./office-com-addin/package-visio.ps1
```

两组 Office 代码测试都验证链接到产品程序集的公共字典解析行为和 COM 扩展接口契约；Visio 另测其形状绑定/扫描行为。`NumberIdentity` 是 CAD 与 Office 共用的源码，修改后还应运行 CAD 编号测试及五版构建。

发布 ZIP 位于 `office-com-addin/release/`，由 `verify-ppt-package.ps1` 核对文件清单和哈希。解压整个 ZIP 后关闭 PowerPoint，在解压目录运行 `./install-office-addin.ps1`。安装器把加载项 DLL 与 Newtonsoft.Json DLL 安装到 `%LOCALAPPDATA%\PatentMarker\OfficeAddin\PowerPoint`，并只写当前用户的 COM 类及 PowerPoint AddIns 注册项。升级时先暂存新文件和注册状态；失败会恢复旧版。卸载：

```powershell
./office-com-addin/uninstall-office-addin.ps1
```

安装器要求产品所有权清单、目录文件清单和哈希匹配；目录混入未登记文件时拒绝覆盖或卸载。卸载只移除产品文件及 GUID、ProgID、AddIns 专属注册项。不要手动移动 DLL，否则 COM CodeBase 路径会失效。

## 诊断与已知边界

每次 PowerPoint 加载项连接生成唯一运行 ID，日志写入 `%LOCALAPPDATA%\PatentMarker\Logs\office-ppt-YYYYMMDD.tsv`。日志包含 PowerPoint 版本、进程位数、加载生命周期、绑定路径、字典哈希、标注/检查阶段及错误；不记录演示文稿正文。安装后的冷启动验收另记录实际 DLL 路径和 SHA-256。

项目不引用本机 Office PIA，Office 对象模型通过 COM 迟绑定调用，并以 `.NET Framework 4.0` 编译；本机当前 PowerPoint 为 64 位 Office 16。代码所用对象模型成员有官方文档依据，但仅 Office 主机实测能确认冷启动加载和具体交互行为。Win7 + Office 2010 x86 仍待验，不据本机通过外推支持结论。

`verify-visio-host-com.ps1` 是调用内部方法的真实 COM 白盒诊断，不覆盖面板用户路径。为保持与 net40 产品一致的运行时，使用 Windows PowerShell 5.1 的 STA 模式，先正常启动一份仅供测试的 Visio 实例：

```powershell
powershell.exe -NoProfile -STA -File ./office-com-addin/verify-visio-host-com.ps1 -DictionaryPath <脱敏字典完整路径>
```

该脚本自行创建并关闭临时图稿，不关闭宿主或其他图稿。PowerShell Core 调用在宿主变更前输出 SKIP 并非零退出；显式 `-SkipNegativeCases` 会把总体结果记为 SKIP。本机完整负例在 Windows PowerShell 5.1 STA 已通过，旧 Core 环境停顿的根因尚未完全定位。

## 当前本机验证状态

2026-10-01 干净源码副本独立构建通过；PPT 7/7、Visio 24/24、两端隔离安装回滚/卸载检查通过。最终包、实际加载 DLL、当时的面板场景及限定 L3 范围见[推送前验证](test-evidence/office-release-validation-20261001.md)。

2026-10-03 同一安装 DLL 在本机 Office 16 x64 补齐鼠标与视觉验收：PPT 四方向原生画线、取消后鼠标选线、重复编号及整稿检查；Visio 原生直线/直角连接线、鼠标选线、连接点粘合、目标跟随、一次撤销和重做；两端默认及最低尺寸面板目检、保存及从界面冷重开均通过，字典字节/属性保持不变。普通目标由公共 COM 准备，所有引线、粘合、选择和业务面板动作均经真实输入。完整证据、运行 ID 和边界见[鼠标与视觉验收](test-evidence/office-visual-validation-20261003.md)。本轮未修改产品代码或重新安装，沿用既有包身份；官方截图仍超时，使用的是用户授权的受限桌面备用工具。

历史 Visio 回归的三个必要负例曾跳过却输出总体 PASS，已将结论更正为 SKIP；当天代码测试也未覆盖这些标注入口负例，本次补齐并通过故障注入验证。详见[2026-09-28 历史记录](test-evidence/office-cold-start-20260928.md)。

2026-09-28 的 Office 公共源码抽取、PowerPoint 新代码测试和 `NumberIdentity.Comparer` 去空格比较已完成；PPT/Visio 代码测试、CAD 2025 单测和五版 CAD 编译记录见本报告历史条目。Windows 7 + Office 2010 x86 仍待目标环境验收。旧的 0.1.1.0 L3 结论仅适用于对应历史 ZIP/DLL，详见[PowerPoint 0.1.1 验收记录](ppt-release-validation.md)与[可行性报告](feasibility-report.md)。

准备发布时可使用 [PPT 发布说明](ppt-github-release-notes.md) 和 [Visio 发布说明](visio-github-release-notes.md)，上传推送前记录中校验过的最终 ZIP。

## Visio 独立原型

Visio 使用独立的 PatentOffice.Visio 程序集、COM 身份、安装目录和注册项。源码与操作限制、构建命令和当前证据见[Visio 原型说明](visio-prototype.md)；它不会加载 PowerPoint 程序集，也不改写 Word 字典。

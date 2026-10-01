# Word → Visio 原型

## 当前状态

这是与 PowerPoint 原型并列的独立 C# COM 加载项，目标 .NET Framework 4.0，当前版本 0.1.4.0。代码包含浮动 WinForms 面板、手动绑定只读 `.dict.json`、两秒刷新、Visio 一维线标注、整份文档漏标检查和独立安装器。

2026-10-01 的 0.1.4.0 已在 Visio 16.0.20430.20092 x64 的真实面板完成字典绑定、两页编号标注、同号多条线和整份漏标检查，并拒绝空选、普通矩形及已有标注线。对象及选择由公共 COM 准备；原生画布选线未通过，不能据面板通过宣布选线交互已验收。24/24 代码测试、干净源码构建与隔离安装回滚/卸载通过。最终 ZIP、保存产物冷重开及限定 L3 范围见[推送前验证](test-evidence/office-release-validation-20261001.md)。

历史 0.1.3.0 验收：本机 Windows 10 build 19045 x64、Microsoft Visio 16.0.20430.20092 x64 已从 0.1.3.0 ZIP 解压安装，冷启动加载成功。使用安装版 Word UI 实际导出的字典，经 Visio 面板的文本路径入口绑定，在真实面板上完成标注、全稿漏标检查、保存和完整关闭宿主后的重开复核。0.1.2.0 候选还覆盖了两页标注、多文档切换、丢失/损坏字典恢复和对象负例；0.1.3.0 修复文档切换后、定时刷新前的面板动作竞态，有同场景红绿证据。测试线由 COM 准备，旧版第二页通过 Visio 画布键盘选线。因此这是**限定 L3 面板与持久化链路通过**，鼠标手绘、粘合和旧版宿主仍待验。

0.1.3.0 首次冷启动 run ID：`4bb253aecae24fa7b036454dcb448ced`；安装 DLL SHA-256 `1A807EA80EC79C8DCF1F78816487B88DC2098F3EBC192F8AB8B2DAF9094F8AEC`。保存重开后的 run ID：`96121bcb91dc48b083fe4e00b1739dff`。原 Word 字典 SHA-256 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479`，测试前后相同。具体动作、限制与产物见 [0.1.3.0 验收记录](test-evidence/visio-release-validation-20260925-v013.md)；两页与恢复场景见 [0.1.2.0 记录](test-evidence/visio-release-validation-20260924-v012.md)，旧缺陷红绿证据见 [宿主失败契约](test-evidence/visio-host-failure-contract-20260924.md)与[发布前缺陷契约](test-evidence/visio-release-defects-20260924.md)。

## 首轮交互

1. 先保存 Visio 文档，再在面板手动选择 Word 导出的 `.dict.json`；也可在“字典路径”框粘贴完整路径，按 Enter 或点“绑定此路径”。
2. 在前景页使用 Visio 直线工具画一条一维线，方向从编号框位置指向目标。若希望目标移动时引线跟随，应先使用 Visio 自带的粘合功能，将线的终点粘到图片或原生图形。
3. 选中这条线，在面板选编号并点“标注所选线条”。
4. 加载项在起点创建白底编号框，在终点设置实心箭头。线和编号框是两个 Visio 形状，分别写入相同的产品标识、编号、版本和配对 ID；加载项不重新创建或重接原线。
5. 保存文档。重新打开后，加载项扫描所有前景页；同号有一处有效标注即视为已标，重复标注允许。

Visio 路线利用一维连接线和用户已有的粘合关系。它不复用 PowerPoint 的形状组、Tags、Custom XML 或字典绑定机制。

## 对象及行为边界

- 文档绑定信息存于文档 ShapeSheet 的产品专属 User 单元格，不写外部字典。同卷路径用相对路径，不同卷用绝对路径；移动或另存为后由用户核对绑定文件。
- 标注与检查按钮执行前同步核对当前文档；若发现刚切换图纸，就取消本次动作并要求在新图重新选择，防止沿用上一份图的编号列表。
- 编号比较沿用 `NumberIdentity` 的去首尾空格与大小写不敏感规则，显示文字保留字典原文。
- 普通形状不会算作产品标注。产品单元格缺失、编号不一致或引线/编号框配对不完整会报告损坏标注。
- 编号框置于引线起点。它与引线没有建立 Visio 粘合或物理分组；用户单独移动引线起点后，编号框不会跟随。
- 插件只给所选线条的终点加箭头，不会自动把线粘到图片或目标，也不识别图片中的具体部位。
- 实测修复：Visio 顶层形状的 `ContainingShape` 返回页面 PageSheet。旧代码把任何非空返回值视为组成员，错误拒绝独立直线。修复后读取容器 `Type`，允许 Page 类型、拒绝组类型；无法读取类型时安全失败。修复前后使用同一宿主对象回归，红绿证据见失败契约。
- 页面扫描只纳入前景页；背景页不参与漏标统计。本机已验证两页前景页的分布标注、整份检查和冷启动重开。
- 跨页同配对 ID 不会把第一页的引线和第二页的编号框误配；对应代码测试有修复前红灯、修复后绿灯。相同路径的 VSDX 关闭重开后会重读文档绑定；当前宿主日志记录旧对象 `UNBOUND` 后新对象 `PASS`。
- VSDX 保存关闭重开已覆盖；旧版 VSD、复杂组形状、动态连接线路由、粘合跟随及鼠标拖画仍待测。

## 构建和安装

从仓库根目录运行：

```powershell
./office-com-addin/build-visio.ps1
./office-com-addin/verify-visio-installer.ps1
./office-com-addin/package-visio.ps1
```

本机安装和卸载入口：

```powershell
./office-com-addin/install-visio-addin.ps1
./office-com-addin/uninstall-visio-addin.ps1
```

安装目录为 `%LOCALAPPDATA%\PatentMarker\OfficeAddin\Visio`。产品专属 HKCU 注册项和所有权清单用于保护升级回滚及卸载范围。隔离安装回归覆盖首装失败恢复、重复安装、升级失败恢复、卸载和旁边文件/无关注册项哨兵。

发布 ZIP 仅包含安装/卸载脚本、产品 DLL、依赖、许可证、使用说明及逐文件哈希清单。用 `verify-visio-package.ps1 -PackagePath <zip>` 可独立检查其内容；从解压目录运行安装器，无需源码树。

## 历史 0.1.2.0 / 0.1.3.0 验证结果与剩余范围

下表保留旧候选的证据。当前 0.1.4.0 的 24 项测试、最终安装及冷重开范围见[2026-10-01 验证](test-evidence/office-release-validation-20261001.md)。

| 检查 | 结果 | 证据 |
|---|---|---|
| net40 Release 构建 | PASS，0 warnings、0 errors | `build-visio.ps1`；版本 0.1.3.0 |
| 代码测试 | PASS，14/14 | `tests/PatentOffice.Visio.CodeTests`，net48；含跨页配对、同路径重开红绿回归 |
| 安装器隔离回归 | PASS | 首装/升级回滚、重复安装、卸载、非产品哨兵保护 |
| 冷启动加载 | PASS | Visio 16.0 x64；0.1.3.0 run `4bb253aecae24fa7b036454dcb448ced` 和 `96121bcb91dc48b083fe4e00b1739dff`；COM 生命周期事件均 PASS |
| 已安装 DLL | PASS | `%LOCALAPPDATA%\PatentMarker\OfficeAddin\Visio\PatentOffice.Visio.dll`；SHA-256 `1A807EA80EC79C8DCF1F78816487B88DC2098F3EBC192F8AB8B2DAF9094F8AEC` |
| 真实 Visio COM 对象负例回归 | PASS（0.1.2.0，面板绕过） | run `eeb36fffe289474daa2a0696bfad8031`（本地原始产物未公开）；空选择、普通矩形、重复绑定均拒绝，正常绑定/标注/扫描/重开通过 |
| Word UI 字典导出 | PASS | Word 16.0 x64 已安装模板的 run `20260924-151850-9FE7C2`；手动导出日志 `export.start/target/success` 均 PASS；2 个编号、0 警告 |
| Word 字典 → Visio 面板绑定、标注与漏标 | PASS，限定 L3 | 0.1.3.0 面板路径输入/Enter 绑定，标注编号 1，冷重开后检查仍报 `missing=1;marked=1`；字典前后 SHA-256 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479` |
| 两页 VSDX 冷启动重开 | PASS，0.1.2.0 限定 L3 | 两页 VSDX（本地原始产物未公开） SHA-256 `115AE23C0304F6A289644DF7E2646342453CAC564FE4711917EABB715734C72A`；重开面板仍报 `missing=0;marked=2` |
| 第二文档、字典丢失/损坏与恢复 | PASS，0.1.2.0 限定 L3 | 面板保持文档隔离；失效时留上次列表并禁用操作，恢复后重新读取；日志同 run ID 记录 `dict.read FAIL` 及 `PASS` |
| 文档快速切换竞态 | 0.1.2.0 FAIL → 0.1.3.0 PASS | [同场景红绿结果](test-evidence/visio-release-validation-20260925-v013.md)；按钮动作前刷新当前文档，切换时取消操作 |
| 历史发布 ZIP（0.1.3.0） | PASS | 0.1.3.0 发布包（本地原始产物未公开），SHA-256 `EF21913F5D35EB534B74916EA815D765BA67DA6021B0A1C1CB07CDF2405DF6D5`；Windows PowerShell 5.1 完整封包与隔离安装检查通过，从该 ZIP 实际安装、冷启动和面板重开检查通过 |
| 2026-09-28 的 0.1.4.0 COM 回归 | SKIP | 正向断言通过；必要负例跳过，整套不算 PASS；面板 UI 未操作。原记录更正见 [历史记录](test-evidence/office-cold-start-20260928.md) |
| Visio 鼠标手绘、粘合及 VSD | SKIP/BLOCKED | 线由 Visio COM 准备；第二页由画布键盘选线，未实测鼠标拖画、目标粘合和旧格式 |

历史结论（0.1.3.0）：本机 Visio 16.0 x64 的已安装候选，经真实面板完成字典绑定、标注与全稿检查，保存并冷重开后识别结果一致；文档快速切换的旧版失败已修复。两页、故障恢复及多文档场景为 0.1.2.0 的同一生产主链证据，0.1.3.0 仅调整按钮动作前状态核对。因画线依赖 COM 准备，该结论限定为面板及持久化 L3 子链。Office 2010 x86、Windows 7、其他 Visio 版本/位数和旧版 VSD 均待验。


## 2026-09-28 的 0.1.4.0 历史回归与更正

2026-09-28 的真实 COM 正向路径完成绑定、线条标注、扫描、同进程保存重开和新进程恢复；编号 1 可识别且损坏数为 0，字典逐字节不变。空选择、错误形状和重复线负例在外部反射调用中跳过，整套结果更正为 SKIP。当天 20 项代码测试也没有覆盖这些标注入口负例；2026-10-01 才补齐针对性测试。当天未操作面板控件，不作 L3 面板结论。详细更正见[2026-09-28 历史记录](test-evidence/office-cold-start-20260928.md)。

2026-10-01 的最新结果见[推送前验证](test-evidence/office-release-validation-20261001.md)：真实面板负例、同号另线、多页统计、字典恢复与多文档隔离均有独立断言；线条和选择的公共 COM 准备与原生选线未通过明确分开。外部反射诊断采用 Windows PowerShell 5.1 STA 后完整负例也通过，但它仍不属于面板用户路径证据。

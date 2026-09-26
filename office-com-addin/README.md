# Word → Office 只读标图加载项

本目录包含两个相互独立、面向 .NET Framework 4.0 的 Office COM 加载项。以下步骤描述 PowerPoint：它读取 Word 导出的 `.dict.json`，在已有图片上把用户绘制的 PowerPoint 直线与编号组成原生形状组，并检查整份演示文稿里的漏标编号。Visio 的安装、使用和证据范围见 [Visio 说明](visio-prototype.md)。

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
./office-com-addin/build.ps1
./office-com-addin/verify-installer.ps1
./office-com-addin/package-ppt.ps1
```

发布 ZIP 位于 `office-com-addin/release/`，由 `verify-ppt-package.ps1` 核对文件清单和哈希。解压整个 ZIP 后关闭 PowerPoint，在解压目录运行 `./install-office-addin.ps1`。安装器把加载项 DLL 与 Newtonsoft.Json DLL 安装到 `%LOCALAPPDATA%\PatentMarker\OfficeAddin\PowerPoint`，并只写当前用户的 COM 类及 PowerPoint AddIns 注册项。升级时先暂存新文件和注册状态；失败会恢复旧版。卸载：

```powershell
./office-com-addin/uninstall-office-addin.ps1
```

安装器要求产品所有权清单、目录文件清单和哈希匹配；目录混入未登记文件时拒绝覆盖或卸载。卸载只移除产品文件及 GUID、ProgID、AddIns 专属注册项。不要手动移动 DLL，否则 COM CodeBase 路径会失效。

## 诊断与已知边界

每次 PowerPoint 加载项连接生成唯一运行 ID，日志写入 `%LOCALAPPDATA%\PatentMarker\Logs\office-ppt-YYYYMMDD.tsv`。日志包含 PowerPoint 版本、进程位数、加载生命周期、绑定路径、字典哈希、标注/检查阶段及错误；不记录演示文稿正文。安装后的冷启动验收另记录实际 DLL 路径和 SHA-256。

项目不引用本机 Office PIA，Office 对象模型通过 COM 迟绑定调用，并以 `.NET Framework 4.0` 编译；本机当前 PowerPoint 为 64 位 Office 16。代码所用对象模型成员有官方文档依据，但仅 Office 主机实测能确认冷启动加载和具体交互行为。Win7 + Office 2010 x86 仍待验，不据本机通过外推支持结论。

## 当前本机验证状态

2026-09-26 在 Microsoft 365 PowerPoint 16.0.20430.20092 x64 上，0.1.1.0 已从最终 ZIP 覆盖安装，实际 DLL SHA-256 为 `3135C19BEFAD7A8831347E8876867E9E68A8CA87A21DF884F41C232FDC4A287B`。安装版 Word UI 导出的字典经真实 PowerPoint 面板绑定、跨两页标注、检查漏标、由 PowerPoint 保存、退出后全新进程重开并再次经面板检查通过；另实测多文稿切换、字典副本损坏或丢失后的恢复和图片误选拒绝。图片、直线和选线由 PowerPoint COM 预先准备，因此这是限定 L3 面板主链；用户在画布上手绘、鼠标选择直线及视觉布局待验。Windows 7 + Office 2010 x86 也待验。详见 [PowerPoint 0.1.1 验收记录](ppt-release-validation.md)与[可行性报告](feasibility-report.md)。

## Visio 独立原型

Visio 使用独立的 PatentOffice.Visio 程序集、COM 身份、安装目录和注册项。源码与操作限制、构建命令和当前证据见[Visio 原型说明](visio-prototype.md)；它不会加载 PowerPoint 程序集，也不改写 Word 字典。

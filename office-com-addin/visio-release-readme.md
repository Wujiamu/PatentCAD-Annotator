# PatentMarker Word → Visio 只读标注加载项 0.1.3

此包用于在已有 Visio 图中读取 Word 导出的 `.dict.json`，把编号绑定到用户选中的一维直线，并检查当前整份 Visio 文档的漏标。字典只读；加载项不会修改 Word、CAD 或外部 JSON。

## 已验证环境与范围

已在 Windows 10 22H2 x64、Microsoft Visio 16.0 x64 上验证：当前 0.1.3 从包安装后的冷启动、面板路径绑定、标注、全稿漏标检查，以及 VSDX 保存和冷启动重开。前一 0.1.2 候选另通过两页标注、多文档隔离、字典丢失/损坏后的恢复；安装器隔离回滚也已通过。测试中的原生直线由 Visio COM 预先建立，前一候选的第二页通过 Visio 画布键盘选线；鼠标画线、目标粘合和旧 `.vsd` 文件尚未验收。系统文件对话框在本机桌面工具下无法完成键盘输入，面板提供可粘贴完整文件路径的手动绑定入口。Windows 7、Office/Visio 2010 x86 和其他 Visio 版本尚未验收。

0.1.3 在执行标注或检查前重新核对当前文档。切换图纸时若面板尚未完成定时刷新，本次操作会提示重新选择，避免使用上一份图的列表；此修复有真实 Visio 宿主对象上的旧版失败与新版通过记录。

## 安装与卸载

1. 关闭 Visio，解压整个压缩包，保留 `dist/Visio` 与脚本的相对位置。
2. 在解压目录用 Windows PowerShell 执行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install-visio-addin.ps1`。此操作只在当前用户的 `%LOCALAPPDATA%\PatentMarker\OfficeAddin\Visio` 安装产品 DLL，并创建产品专属 HKCU COM/Visio 注册项。
3. 重新启动 Visio。出现“专利标注字典工具 · Visio”面板即表示加载项界面已打开。面板被关闭时会最小化，可从任务栏恢复。
4. 卸载前关闭 Visio，在同一目录执行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\uninstall-visio-addin.ps1`。卸载器只处理本产品的资产，且要求产品所有权清单完整。

安装器在升级前核对旧安装的所有权清单和文件哈希，故障时恢复旧产品文件与注册项。安装及卸载均要求 Visio 已关闭。不要手工移动已安装的 DLL，以免 COM 注册中的路径失效。

## 使用

1. 在 Word 的 PatentMarker 面板导出字典。Visio 文档必须先保存为 `.vsdx`。
2. 在 Visio 面板点“选择 / 绑定字典”手动选择对应的 `.dict.json`，也可将完整文件路径粘贴到“字典路径”并点“绑定此路径”或按 Enter。两个入口使用相同的校验和绑定逻辑。加载项不按 Visio 文件名猜字典。绑定保存在 Visio 文档的产品 ShapeSheet 单元格；绑定后保存 `.vsdx`。
3. 在**前景页**从编号应出现的位置向目标绘制一条 Visio 原生一维线。若希望目标移动时引线跟随，用 Visio 的粘合功能先把线端粘到目标。选中一条线，在面板选择编号，再点“标注所选线条”。加载项在起点创建白底编号框，在终点设置箭头；保存 `.vsdx`。
4. 点“检查整份文档”检查所有前景页。一个编号有一处完整的产品标注即视为已标；重复标注合法。普通形状不会计入。跨页面的残缺引线与编号框不会被配成有效标注。

编号框与引线不分组，移动引线起点后编号框不会自动跟随。加载项不导入图片，也不会自动粘合线端。绑定的字典丢失或损坏时暂停新增标注，保留上次有效列表供查看；恢复文件后会重新加载。日志位于 `%LOCALAPPDATA%\PatentMarker\Logs\office-visio-YYYYMMDD.tsv`，每次运行有独立 ID。

## 包完整性

`package-manifest.json` 列出本包每个文件的 SHA-256。源码仓库的 `verify-visio-package.ps1 -PackagePath <zip>` 可校验压缩包清单、文件哈希和包内文件范围。`third-party/Newtonsoft.Json-LICENSE.md` 为所带 Newtonsoft.Json 的许可证。

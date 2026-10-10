# 怎样打开 PPT / Visio 标注面板

当前加载项使用独立浮动窗口：安装后正常启动宿主时自动显示。没有功能区里的“打开标注面板”按钮，也没有供 `Alt+F8` 调用的宏。

| 宿主 | 面板窗口标题 | COM 加载项列表中的名称 |
|---|---|---|
| PowerPoint | 专利标注字典工具 · PowerPoint | PatentMarker Word to PowerPoint |
| Visio | 专利标注字典工具 · Visio | PatentMarker Word to Visio |

## 第一次打开

1. 保存并完全退出对应宿主。
2. 解压整个安装包，在解压目录运行 PPT 的 `install-office-addin.ps1` 或 Visio 的 `install-visio-addin.ps1`。详细安装步骤见 [PPT 说明](../ppt-release-readme.md)或 [Visio 说明](../visio-release-readme.md)。
3. 正常重新启动 PowerPoint / Visio，查找上表中的面板窗口。面板可能位于主窗口后面，可用任务栏或 `Alt+Tab` 切换到它。
4. 保存当前文稿，在面板点击“选择 / 绑定字典”，选择 Word 导出的 `.dict.json`；再保存文稿，绑定才会随文稿保存。

## 面板曾出现，后来不见了

面板右上角的关闭按钮会使它最小化；加载项仍然运行。点击 Windows 任务栏中对应的“专利标注字典工具”窗口恢复，或按 `Alt+Tab` 切换到该窗口。宿主退出时面板才随之真正关闭，下次正常启动宿主会重新显示。

## 启动后一直没有面板

1. 先通过任务栏和 `Alt+Tab` 排除面板被遮挡或最小化。
2. 在对应宿主打开“文件 → 选项 → 加载项”。底部“管理”选择“COM 加载项”，点“转到”，检查上表中的产品名称是否存在且已勾选。此处用于检查加载状态，日常恢复面板使用任务栏。
3. 未列出产品：保存并关闭宿主，在本机当前 Windows 用户下重新运行对应安装脚本，再正常启动。安装只写当前用户注册项；不要手工浏览并添加 DLL，也不要移动已安装目录。
4. 产品未勾选：勾选后确定。若产品出现在“管理 → 禁用项目”中，确认该产品后启用，再检查 COM 加载项。Office 加载项管理方式可参考 [Microsoft 官方说明](https://support.microsoft.com/zh-cn/office/add-ins/view-manage-and-install-add-ins-for-excel-powerpoint-and-word)。
5. 已勾选仍无面板，或重新启用失败：保留对话框中的加载错误和本次日志，进行诊断。PowerPoint 日志为 `%LOCALAPPDATA%\PatentMarker\Logs\office-ppt-YYYYMMDD.tsv`，Visio 日志为同目录的 `office-visio-YYYYMMDD.tsv`。日志里的 `OnConnection`、`lifecycle`、`FAIL` 可以帮助区分是否加载、是否打开界面以及错误阶段。没有本次日志时先检查宿主是否实际加载了产品。

面板已经打开但列表为空时，先检查文稿是否保存、字典是否绑定、路径是否存在以及面板状态提示。列表为空与面板没有打开是两种情况。

# PatentMarker Word → PowerPoint 只读标图加载项 0.1.2.0

这是实验性版本，面向 Windows 上的 Microsoft PowerPoint，程序集使用 .NET Framework 4.0。加载项读取现有 Word 导出的 `.dict.json`，只在 PowerPoint 演示文稿里创建标注；不会修改字典、Word 或 CAD 文件。

**版本与验收边界**：此包为 0.1.2.0。它将 Office 字典模型/读取器、COM 扩展接口与诊断逻辑改为共享源码，并调整编号比较器。0.1.1.0 的历史宿主验收只适用于其记录中的 DLL 和 ZIP；本版本的验收结果见随版本更新的项目记录。

## 怎样打开标注面板

1. 安装完成后，完全退出 PowerPoint，再正常重新启动。加载项自动打开“专利标注字典工具 · PowerPoint”浮动窗口。
2. 如果曾点击面板右上角的关闭按钮或最小化按钮，面板会最小化。点击 Windows 任务栏中的“专利标注字典工具 · PowerPoint”恢复；也可以按 `Alt+Tab` 切换到该窗口。
3. 如果从未出现面板，在 PowerPoint 的“文件 → 选项 → 加载项”中，底部“管理”选择“COM 加载项”并点“转到”，检查 `PatentMarker Word to PowerPoint` 是否存在并勾选。未列出时，关闭宿主后重新运行本包安装脚本；已勾选却没有面板时，检查下文日志。

当前版本没有功能区里的“打开标注面板”按钮，也没有供 `Alt+F8` 调用的宏。COM 加载项窗口用于检查是否启用；日常恢复面板使用任务栏或 `Alt+Tab`。字典列表为空时，先保存文稿并在面板选择 / 绑定 Word 导出的 `.dict.json`。

此插件属于 PatentMarker 的 Word → PowerPoint 路线；Word → AutoCAD 和另一 Office 路线的说明见[项目主页](https://github.com/Wujiamu/PatentCAD-Annotator)。


## 安装

1. 保存并关闭所有 PowerPoint 窗口。
2. 解压整个 ZIP，保留 `dist` 子目录。
3. 在该目录用 Windows PowerShell 执行 `./install-office-addin.ps1`。需要时可加 `-OfficeBitness 32` 或 `64`；默认自动探测。
4. 全新启动 PowerPoint。面板标题为“专利标注字典工具 · PowerPoint”。

安装范围仅为当前用户的产品专属文件和 COM 注册项。重复安装可升级；安装失败会尝试恢复旧版。卸载时先关闭 PowerPoint，再运行 `./uninstall-office-addin.ps1`。如 PowerShell 阻止脚本，可在当前 PowerShell 进程设置 `Set-ExecutionPolicy -Scope Process Bypass` 后重试。

## 使用

1. 在 Word 中导出 `.dict.json`。先把目标演示文稿保存为 `.pptx`。
2. 在面板中输入字典绝对路径并点击“绑定此路径”，或点击“选择 / 绑定字典”。绑定写入演示文稿；再保存 `.pptx` 才能在重开后恢复。
3. 把已有图片放在幻灯片上。从编号文字位置向图片目标画一条 PowerPoint 直线，选中这条线。在面板中选编号并点击“标注所选直线”。起点放编号，终点设箭头，线和文字组成产品标注组。
4. 保存 `.pptx`。点击“检查整份演示文稿”统计所有幻灯片漏标。同号标注多次是允许的；普通形状不计入。

同卷字典关联保存相对路径，跨卷保存绝对路径。文件丢失时需要重新选择，不按演示文稿名称猜测。加载项每两秒检查绑定文件；解析失败时保留上一次列表供查看、暂停标注与检查，文件恢复后重新读取。切换演示文稿时列表和关联随当前文稿刷新。

日志位于 `%LOCALAPPDATA%\PatentMarker\Logs\office-ppt-YYYYMMDD.tsv`。安装目录位于 `%LOCALAPPDATA%\PatentMarker\OfficeAddin\PowerPoint`。

## 验证边界

2026-10-01 的代码与隔离安装门禁通过：两个 net40 产品构建、PowerPoint 7 项代码测试及安装失败回滚/卸载哨兵检查。当前 0.1.2.0 DLL 在本机 PowerPoint 16 x64 的真实面板上完成绑定、两页编号标注、整稿漏标检查及字典损坏后的恢复，字典逐字节不变。线条由公共 COM 预先准备；第二页通过键盘选线。最终 ZIP、实际加载 DLL、保存产物冷重开和限定 L3 范围见本仓库的 [`test-evidence/office-release-validation-20261001.md`](test-evidence/office-release-validation-20261001.md)。

2026-10-03 同一安装版0.1.2.0在本机PowerPoint16 x64通过四方向鼠标拖画、取消后鼠标选线、编号起点/箭头终点目检、重复编号及整稿检查、默认/最低面板尺寸、保存后正常冷启动并从界面重开。字典字节及属性不变。官方截图仍超时，用户授权的备用工具取得真实像素并发送输入。证据见本仓库的 [`test-evidence/office-visual-validation-20261003.md`](test-evidence/office-visual-validation-20261003.md)。0.1.1.0 的键盘插线、跨卷绑定和多文稿历史结果只适用于其旧包。

同日补验安装版 Word 的单 DWG 导出、多个 DWG 取消与手选，再由 PPT 面板分别读取两份真实输出、检查漏标、保存关联及独立冷重开，均通过。Word / DWG / PPT 主名不同，关联按对应相对路径恢复；字典字节/属性、普通形状及 Word 非产品模板文件不变。该轮只验证手动导出和 DWG 文件名哨兵，不验证真实图纸内容或自动保存导出；见本仓库的 [`test-evidence/office-local-validation-20261003.md`](test-evidence/office-local-validation-20261003.md)。

Windows 7 + Office 2010 x86 仍待目标环境验证，.NET Framework 4.0 编译目标不代表这些旧环境已通过。加载项不导入图片，不提供 CAD 式幻灯片点击取点，不回写 `.dict.json`。

# PatentMarker Word → PowerPoint 只读标图加载项

这是实验性版本，面向 Windows 上的 Microsoft PowerPoint，程序集使用 .NET Framework 4.0。加载项读取现有 Word 导出的 `.dict.json`，只在 PowerPoint 演示文稿里创建标注；不会修改字典、Word 或 CAD 文件。

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

0.1.1.0 已在本机 PowerPoint 16 x64 验证：从安装包加载、面板绑定真实 Word 导出字典、在已有图片的测试稿中标注、全稿漏标检查、保存并在全新进程重开；同卷相对路径与跨卷绝对路径绑定、字典损坏或暂时丢失后的恢复也已验证。PowerPoint 界面键盘插入直线和键盘选线通过，但鼠标从编号位置向图片目标拖拽画线、鼠标选线及视觉位置尚未验证。本机截图工具无法取得可靠画布图像；请在实际图稿中目视检查线的起终点和箭头。

Windows 7 + Office 2010 x86 仍待目标环境验证，.NET Framework 4.0 编译目标不代表这些旧环境已通过。加载项不导入图片，不提供 CAD 式幻灯片点击取点，不回写 `.dict.json`。

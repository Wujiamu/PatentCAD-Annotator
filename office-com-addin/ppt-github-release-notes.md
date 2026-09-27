# Word → PowerPoint 只读标图原型 0.1.1.0（实验性）

**发布资产**：`PatentMarker-WordPowerPoint-0.1.1.0-20260927-095350.zip`
**SHA-256**：`9C437F4E2E7B991F472E3CC3476B18BC48886763B9F12E40CCEE9E79D536A477`

加载项读取 Word 已导出的 `.dict.json`，在已保存的 PowerPoint 演示文稿里把原生直线与编号组成标注组，并检查整份演示文稿的漏标。它不会回写字典，也不会修改 Word 或 CAD 文件。ZIP 内的 `README.md` 有安装、使用和卸载步骤。

本机 Windows 10、PowerPoint 16 x64 上，当前 ZIP 经实际安装及全新 PowerPoint 进程验证：恢复字典关联、通过面板标注、保存、退出并重开后识别产品组，整份检查无漏标。前一同版本候选另覆盖首次手动绑定、多文稿切换、字典损坏或丢失后的恢复、跨卷路径、图片误选和 PowerPoint 键盘插入直线；两版 DLL 的 100 个方法 IL 比较一致。详细证据见仓库的 `office-com-addin/ppt-release-validation.md`。

**尚未验证**：鼠标从编号位置向图片目标拖拽画线、鼠标选线和标注视觉布局；Windows 7 + Office 2010 x86。请在实际图稿中目视检查线的方向与箭头。本包不包含 Visio 功能、图片导入或 CAD 式幻灯片取点。

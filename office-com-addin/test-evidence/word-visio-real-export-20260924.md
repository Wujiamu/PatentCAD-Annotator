# Word UI 导出与 Visio 宿主对象联测

## 环境和部署

- 时间：2026-09-24 20:05–20:06（本地时间）
- 执行身份：`本机用户`
- Windows 10 build 19045 x64；Word 16.0 x64；Visio 16.0.20430.20092 x64
- Word 实际加载模板：`%APPDATA%\Microsoft\Word\STARTUP\PatentMarker.dotm`，SHA-256 `2F3E4C46DAAC61225648EA5905507EAAC6EBF602947F074C93CE2CEF91457BC8`
- Word VBA 版本：`1.0.3`；运行 ID `20260924-151850-9FE7C2`；日志 `%LOCALAPPDATA%\PatentMarker\Logs\word-vba-20260924.tsv`
- Word 测试文档：`word-installed-export-20260924-bb620f8f\visio-word-export-test.docm`；保存关闭后 SHA-256 `C5A821398CD1BAD270DCF678718C2737C934FBCE6BC2F6B811813A724F574EBB`
- Visio 实际加载 DLL：`%LOCALAPPDATA%\PatentMarker\OfficeAddin\Visio\PatentOffice.Visio.dll`，SHA-256 `96F782BE5C8A646EB1C5D947C226340F66F451095547344E0122F5201F1092A7`
- Visio 修复版冷启动 run ID：`82a3f666648a432081cede076facd1fb`

## Word 用户路径

- 在隔离目录创建并保存独立脱敏 `.docm` 测试文档；文档内容为本轮合成的两条附图标记，不含用户文档正文。Word 测试结束后只关闭此测试文档，原有 `文档1` 仍打开且未操作。
- Word 产品面板成功显示。Computer Use 对面板控件执行辅助功能点击返回“控件不可用”；刷新并重新获取窗口句柄后仍然如此。用同一窗口句柄发送 Enter 后，面板状态从“导出失败：没有可用输出路径”变为“已导出”，产品代码记录 `export.start`、`export.target`、`export.success` 均 PASS。
- 字典输出：`word-installed-export-20260924-bb620f8f\visio-word-export-test.dict.json`；403 字节，SHA-256 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479`。路径符合无 DWG 时采用 Word 文件主名规则；字典解析为 2 项，编号为 1、2，0 条警告。

## Visio 宿主对象回归

- 将上面的 Word UI 字典作为输入，由 `verify-visio-host-com.ps1 -DictionaryPath <实际输出字典>` 连接真实 Visio COM 宿主，操作实际加载的已安装程序集。
- Visio 结果目录：`visio-host-com-20260924-200638-11fcc1fd`。VSDX SHA-256 `A078D67F715C0D693E6CBA51020C916555BB546BBB02E141DE11E625D974F4D2`。
- Visio 运行 ID：`11fcc1fd80624e6fb6ac292764381fb1`；实际 DLL 路径和哈希与上列安装文件一致。
- 业务断言：字典读取 2 项；文档绑定 PASS；由字典第一项创建引线及标签 PASS；扫描识别编号且损坏数为 0 PASS；VSDX 保存、关闭、重新打开后绑定和标注仍可识别 PASS。
- 输入字典前后 SHA-256 都为 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479`，证明 Visio 测试没有改写 Word 字典。

## 判定和未覆盖项

- **PASS**：安装版 Word 产品面板手动导出测试文档；字典被实际已安装 Visio 代码成功读取、绑定、标注、扫描并在 VSDX 冷重开后识别。
- **限制**：Visio 部分通过测试脚本反射调用内部生产方法，未经过 Visio 加载项面板的绑定和标注按钮，也未由用户在画布上手绘并选择直线。因此不把 Word→Visio 整条用户路径记为 L3。
- 未覆盖：Visio 面板文件选择、UI 选线与取消/错选，图片目标粘合，复杂组、多页/多文档及 VSD。Office 2010 x86、Windows 7 未验。

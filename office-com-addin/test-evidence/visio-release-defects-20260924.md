# Visio 发布前缺陷契约

## 跨页面误配标注（L1 红灯）

- 执行日期：2026-09-24；工作树中的 Visio 0.1.0.0 源码，原已安装 DLL 位于 `%LOCALAPPDATA%\PatentMarker\OfficeAddin\Visio\PatentOffice.Visio.dll`，SHA-256 `96F782BE5C8A646EB1C5D947C226340F66F451095547344E0122F5201F1092A7`。
- 初始状态：两张前景页；第一页只有产品引线，第二页只有产品编号框，两者配对 ID 和编号相同。没有其他产品标注。
- 最短复现：运行 `AnnotationScanTests.ScanDocument_DoesNotPairPartsOnDifferentPages`，调用生产扫描方法。
- 预期：编号不计为已标，两页残件各报告一个损坏标注。
- 实际红灯：测试退出码 1；`Assert.Empty` 失败，`MarkedNumbers` 实际包含 `1`。生产扫描将所有前景页的形状放在同一个配对 ID 字典中。
- 修复与同场景绿灯：每张前景页分别配对，汇总全稿已标编号；同一测试转绿，整个 Visio 代码测试 13/13 PASS。
- 证据层：此项是函数层 L1 红绿，尚未证明安装版用户界面的跨页行为。

## 浮动面板难以找回（宿主 UI 待复核）

- 环境：Windows 10 build 19045 x64，Microsoft Visio 16.0.20430.20092 x64，当前进程 PID `12516`；安装版 0.1.0.0 冷启动 run ID `82a3f666648a432081cede076facd1fb`，安装 DLL SHA-256 同上。
- 初始状态：本轮创建并保存的脱敏测试图 `office-com-addin/test-evidence/visio-panel-probe-20260924.vsdx` 在 Visio 中打开。Visio 加载项在 `OnConnection` 中调用 WinForms `Show()`；面板代码设定 `ShowInTaskbar = false`，用户关闭面板后没有重新打开的命令。
- 最短复现：在测试图打开期间枚举 Computer Use 的 Visio 窗口，再读取主窗口辅助功能树。
- 预期：浮动面板有可直接找回的窗口入口，能够继续绑定字典和标注。
- 实际红灯：窗口列表只返回测试图的 Visio 主窗口，主窗口树没有产品浮动面板。此证据只证明当前 Computer Use 无法定位该面板，不能推断用户肉眼看不到；源码同时证明关闭后没有重新打开入口。
- 待修复验证：让浮动面板出现在任务栏，用户点关闭时最小化并可恢复；Visio 退出时正常关闭。需要在修复版全新 Visio 进程中检查窗口枚举及面板操作，不能以 WinForms 属性或启动日志替代。

## 同路径重开沿用旧面板状态（L1 红绿及宿主旁证）

- 执行日期：2026-09-24；工作树候选为 Visio 0.1.1.0，旧实现仅以 `FullName` 作面板文档键。已安装旧候选 DLL SHA-256 `3C6DFA2B00CCE729813A6DCA9C3CC1DAC33BEC8F71649F2E67B7943BD26CD499`。
- 初始状态：一份已保存的 VSDX；面板已缓存其字典/扫描状态。关闭此文档，随后在同一 Visio 进程按相同路径重开；新文档对象与旧对象不同，路径相同。
- 预期：重新解析该文档的字典关联并扫描其产品标注。
- 实际红灯：`PaletteDocumentIdentityTests.ReopenedDocumentAtSamePathHasNewPaletteIdentity` 退出码 1，两个不同文档对象得到相同的路径键；面板的变更分支不会执行。
- 修复：文档键加入对象身份，切换判定同时比较对象引用；同一对象的键保持稳定，不同对象即使路径相同也会重置缓存。相同测试转绿，Visio 代码测试 14/14 PASS。
- 安装包旁证：发布候选安装 DLL SHA-256 `6D211D3F8F8A3884F0F0823452E8A234749AA05E602BF2E57D5C0BF16DD2830B`；冷启动 run ID `2b3aa0e4ffbd44f192816db7392f93b9`。`verify-visio-host-com.ps1` 的 VSDX 关闭重开后，产品日志从同路径旧对象的 `binding.resolve UNBOUND` 转为新对象的 `binding.resolve PASS`，随后 `dict.read PASS`；结果目录 `visio-host-com-20260924-213353-cc7cbf5d`。此项仍未证明面板上的用户点击和完整 L3。

## 浮动面板修复后状态与系统文件对话框限制

- 发布候选冷启动 run ID `56b61f3b6da944bb804d55c6886e95e4`：Computer Use 枚举到独立窗口“专利标注字典工具 · Visio”，辅助功能树列出字典绑定、标注和漏标按钮；通过 `Alt+F4` 后窗口仍在，日志同 run ID 记录 `palette INFO minimized_by_user`，重新激活后可读取面板树。面板找回缺陷在本机可观察部分转绿。
- 从精确发布包解压安装后的冷启动 run ID `2b3aa0e4ffbd44f192816db7392f93b9`：准备保存的脱敏测试图，面板按钮由键盘 `Tab`/`Enter` 打开“选择 Word 导出的专利标注字典”系统文件对话框。该对话框出现在面板辅助功能树中，但不出现在 Computer Use 可选窗口列表；本机 Windows 10 19045 的截图请求有 `SetIsBorderRequired 0x80004002` 兼容问题，控件点击返回 `coordinate input geometry is unavailable`，对父窗口发送 `Alt+N`、`Ctrl+L`、`Tab`、`Escape` 不改变对话框焦点。文件选择与后续面板按钮尚未通过，不能记为 L3。

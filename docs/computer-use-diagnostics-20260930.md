# Computer Use 截图和双击故障诊断

日期：2026-09-30，约 21:03–21:15（Asia/Shanghai）。状态：**故障层已定位，官方原生截图链路仍 FAIL；尚未修复。**

2026-10-01 接续：用户明确批准独立 GDI/SendInput 工具，CAD 最终包的真实双击、按钮切换、绘制、夹点及多图纸验收已完成。备用路径使本轮工作可继续；下文官方链路故障未被修复，也未在技能包更新后重新宣称其状态。当前包及验收范围见 [最终记录](cad-acceptance-20261001.md)。

## 本机实测事实

| 项目 | 结果 |
|---|---|
| 操作系统 | Windows 10 Pro 22H2，build 19045.7417 |
| 应用更新器返回版本 | 26.928.21956，build 12404，prod；`up_to_date` |
| Windows Appx 包版本 | OpenAI.Codex 26.928.2636.0（与更新器使用不同版本标识） |
| Computer Use 技能包 | 26.928.21956 |
| 本机 `@oai/sky` 包 | 0.7.5 |
| 桌面会话 | CAD、记事本及 Computer Use 进程均为 Session 1 |
| 原生窗口枚举/启动/激活 | PASS |
| 空白记事本的辅助功能树 | PASS，能读到文本编辑器、系统菜单及窗口按钮 |
| 键盘派发 | PASS（记事本）；Alt+Space 显示系统菜单，X 最大化，窗口按钮由“最大化”变为“还原” |
| AutoCAD 2026 截图 | FAIL：`FrameArrived timed out: timed out waiting on channel` |
| 空白记事本截图 | FAIL：`window capture timed out: timed out waiting on channel`；重建会话后仍报 `FrameArrived timed out` |
| 记事本索引双击 | FAIL：`coordinate input geometry is unavailable` |
| 最大化记事本截图 | FAIL：`window capture timed out: timed out waiting on channel` |

只使用 Computer Use 技能规定的 `node_repl` + `@oai/sky` 官方 API。未修改安全/隐私设置、未读取认证凭据、未直接调用 helper 二进制、未用其他桌面输入或截图工具绕过。

## 超时发生在哪一层

本轮不是“等待应用授权超时”，也不是网络连接超时。`list_apps`、`launch_app`、`activate_window` 和辅助功能读取成功，且键盘实际改变了窗口状态；随后请求截图，原生链路返回等待图像帧超时。

应区分不同错误：

- `Computer Use app approval timed out`：应用授权阶段；不证明目标程序启动失败。本轮未出现此错误。
- `Computer Use native pipe connection timed out`：连接阶段；本轮未出现。
- `FrameArrived timed out` / `window capture timed out`：本轮实际错误，截图阶段没有在时限内收到图像帧。

JavaScript 客户端源码的只读核对显示，Windows 操作交给官方原生 transport/broker，失败消息由该链路返回。已公开的 `get_window_state` 参数仅提供是否截图、是否读文字及目标窗口，没有可选择另一种 Windows 截图后端的参数。当前没有经过文档确认的本地配置切换办法。

## 双击为什么不可用

即使先取得了新的辅助功能索引，`sky.click({element_index: 1, click_count: 2})` 在空白记事本仍返回几何信息不可用。同样错误也曾出现在 CAD 字典条目的双击上。因此不能把这个错误解释成 CAD 的 DoubleClick 事件未注册或插件按钮失效。

直接证据是原生输入层无法获得可用的坐标几何。**推断：**截图链路未建立有效的截图/窗口坐标映射，使索引点击和双击一起受阻。现有接口没有暴露更多内部错误细节，不能声称已证明原生实现中某一行代码就是唯一根因。

## 已确认的 Windows API 兼容性问题

通过只读 `ApiInformation` 查询：

- `GraphicsCaptureSession` 类型存在：True。
- `Direct3D11CaptureFramePool.CreateFreeThreaded` 存在：True。
- `GraphicsCaptureSession.IsSupported()`：True。
- `GraphicsCaptureSession.IsBorderRequired` 属性存在：**False**。

微软文档说明 `IsBorderRequired` 从 build 20348 / UniversalApiContract v12 才提供，本机 build 19045 不具备此属性。参见 [Microsoft Learn](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired?view=winrt-26100)。

此前项目协作记录中的 `SetIsBorderRequired / 0x80004002` 与这个 API 缺口相符。**本轮返回的是等待帧超时，而不是该 HRESULT，故不能将两者未经原生诊断就视为同一个断点。**已证明“本机缺少该 API”和“最新版原生截图在两个应用均失败”，尚未证明最新版等待帧超时的唯一内部原因。显示驱动问题也未排除；未擅自移除远程显示驱动或更改用户显示设置。

## 已完成的恢复措施

1. 重新枚举、选择唯一窗口，丢弃旧窗口/索引。
2. 在空白记事本对照，排除故障仅限于 CAD。
3. 重置 JavaScript 会话、重新初始化官方原生服务并选择返回的窗口。
4. 激活并最大化对照窗口，以可观察键盘变化确认输入链路有效。
5. 检查当前安装渠道更新：`up_to_date`，没有工具确认可安装的新版。

截图仍 FAIL，不能宣称恢复成功。延长外层工具超时、重复 NETLOAD 或修改 CAD 的鼠标事件不能修复已经定位到原生截图阶段的故障。

## 剩余恢复路径与 CAD 验收

本项目不包含 Computer Use 原生截图实现。可继续的路径是取得能在本机工作且由官方提供的 runtime 修复，或在已实际证明能截图/点击的 Windows 环境完成验收。Windows 11 是候选环境，尚未测试，不承诺迁移后必然解决。

Computer Use 技能规定不得自制 helper 协议、绕过官方桌面链路或猜坐标；本项目 AGENTS.md 又要求真实面板及安装后验收，不能用内部入口 PASS 替代。因此 CAD 代码、宿主命令和几何检查仍保持此前 PASS，真实面板 L3/L4 保持 SKIP/BLOCKED。详见 [CAD 候选验证](cad-acceptance-20260930.md)。

本记录可供官方维护者复核；未经用户单独授权，不向外部发送日志、上传机器数据或提交问题报告。

## 本轮可执行方案（原授权等待阶段，现已获准执行）

2026-10-01 更新：用户明确允许独立工具。真实 GDI 截图和记事本双击选词已通过，实际安装后 CAD 列表双击及面板切换也已通过；官方服务仍未修复。最新工具哈希为 `52C30D70B98A5600F6BE41B713DDB43B0AB21B235C4E92607505328A2E772782`。剩余 CAD 验收因关机要求暂停，状态见 [接续记录](cad-resume-20261001.md)；下文未授权状态及旧哈希仅记录当时经过。

继续核对了官方 Computer Use / 配置文档，未找到截图后端或帧池选择参数；公开仓库中可见的 Computer Use 文件是配置和协议定义，未找到本机原生截图实现。没有依据通过修改 CAD 代码、扩大应用权限或增加等待时间来修复等待帧超时。

因此准备了独立的 [CAD 桌面备用工具](../tools/CadDesktopFallback/README.md)：使用 Windows GDI 获取当前可见窗口像素，通过 Windows SendInput 发送真实鼠标双击和拖动，坐标直接来自新截图。它不替换或修改 Codex 原生服务，不接入私有 helper 协议。它解决的目标是恢复本轮 CAD 用户路径验收，**不是宣称官方 Computer Use 的原生缺陷已修复**。

- 源码：`tools/CadDesktopFallback/Program.cs`；目标 .NET 8 Windows x64。
- 构建：PASS，0 错误、0 警告。
- L1：7 项 PASS，包括 x64 INPUT 结构、负原点虚拟桌面坐标及边界、允许的进程/按键；此自检未执行任何窗口/截图/键鼠 API。
- 候选 DLL SHA-256：`02EF88A235EDBAD9F56E141FD49F715649ED015846C2C9BAC07188160136238E`。
- 运行约束：只接受本轮临时 CAD/记事本 PID；前台窗口和实际命中对象必须属于该目标窗口组；拒绝过期或复用的观察、进程重启、几何/DPI变化、图像变化和覆盖位置。输入派发不算业务 PASS。
- 桌面回归仍为 **SKIP/BLOCKED**。已向用户请求明确覆盖本机 AGENTS.md 和 Computer Use 技能的官方链路要求；未收到允许时不执行替代输入或截图。

获准后的顺序是：空白记事本真实截图/双击选词 → 最终候选包实际安装及 AutoCAD 冷启动 → 真实列表双击与按钮互相切换 → 左右文字视觉及夹点回归 → 检查加载文件路径/哈希与环境恢复。只有实际业务断言执行后才更新 CAD 放行结论。详细运行接口与验收边界见工具说明。

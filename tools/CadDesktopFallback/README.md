# CAD / Office 桌面验收备用工具

本工具供本项目的临时 AutoCAD 2026、Word、PowerPoint、Visio 和记事本验收使用，不属于插件或发行包。由于本机官方 Computer Use 的 Windows Graphics Capture 请求持续超时，工具使用微软 GDI `BitBlt` 获取**当前可见窗口像素**，使用 `SendInput` 发送真实输入。它不修复或替换 Codex 的原生服务，不调用 helper 协议，不修改系统安全、隐私、显示驱动或产品加载配置。

2026-10-03 为 PPT / Visio 鼠标验收增加受限扩展：`POWERPNT.EXE` / `VISIO.EXE` 进程白名单，以及固定的文稿编辑快捷键。用户已明确允许本轮使用该备用链路，仅操作临时 Office 文稿。新增编译与 10 项无桌面自检通过；Office 业务结论以各自实测记录为准，此前 CAD 授权和结果不能替代 Office 验收。

同日准备 Word 扩展：新增 `WINWORD.EXE` 白名单、`Alt+F8` 宏对话框与 `Ctrl+W` 文档关闭快捷键；编译零警告/错误、11 项无桌面自检通过。这只属于 L1；Word 实际输入必须先取得当前阶段的明确方法授权，既有 PPT / Visio 授权不自动覆盖 Word。测试范围、授权状态及结果见[本机补充验收](https://github.com/Wujiamu/PatentMarker-OfficeAddins/blob/main/test-evidence/office-local-validation-20261003.md)。

在 Visio 连接线工具中，可通过[悬停观察连接点](https://support.microsoft.com/en-us/visio/edit-connector-lines-arrows-or-points)。新增 `move` 操作：消费一次新观察记录，仅移动鼠标，不发送按钮或按键，沿用点击的前台、像素、坐标及窗口归属检查。悬停后必须重新截图确认实际连接点，才可选择拖线落点。

本轮Office实测已完成：PPT四方向鼠标画线和选线；Visio原生直线/直角连接线、鼠标选线、连接点粘合、目标跟随、一次撤销与重做；两端默认/最低面板目检及正常冷启动后的界面重开通过。原始像素用于操作与目检，只读公共COM用于审计，未以COM预建引线、粘合或选择。具体L3范围及未覆盖项见[2026-10-03验收](https://github.com/Wujiamu/PatentMarker-OfficeAddins/blob/main/test-evidence/office-visual-validation-20261003.md)。

## 当前状态与启用条件

- 编译：PASS，.NET 8 Windows x64，0 错误/警告。
- `self-test`：PASS；仅验证 x64 输入结构、负原点多屏坐标、边界拒绝及允许的目标/按键，属于 L1。
- 用户已明确允许本轮使用该独立工具，覆盖官方链路约定。空白记事本截图/双击选词、最终 CAD 包实际列表双击、按钮切换、左右绘制、原生夹点及多图纸验收通过。下午桌面复测按用户安排由 low 思考强度子代理执行、主代理审核，异常停下交回；同一时间只由一个代理控制桌面。见 [最终验收](../../docs/cad-acceptance-20261001.md)。
- 本机 AGENTS.md 原要求官方 `@oai/sky`。官方接口恢复仍失败，不能把备用工具的成功写成官方 Computer Use 已修复。

## 执行约束

1. 只在获准的本轮临时 `acad.exe`、`notepad.exe`、`WINWORD.EXE`、`POWERPNT.EXE` 或 `VISIO.EXE` 进程使用；工具拒绝其他进程名称及其他会话。测试者必须记录 PID、启动时间和测试资料路径；只能操作当前阶段已授权的文稿。
2. `windows <pid>` 枚举该进程的真实可见窗口。指定返回的唯一窗口句柄，激活后再截图。不得构造未知窗口句柄。
3. `capture` 要求窗口在前台且未最小化，裁剪到真实虚拟桌面；输出 PNG 和包含 PID、启动时间、句柄、物理像素范围、DPI、时间及图像 SHA-256 的 JSON。保存目录必须是新的。
4. 检视 PNG，再按其中的实际像素选择位置。一次操作消费一个观察记录；复用、超过两分钟、进程重启、窗口/DPI变化、图像被改写均拒绝。
5. 点击前比对位置附近的像素，并检查实际命中窗口属于同一目标窗口组；鼠标移动后确认物理位置。前台切换、其他窗口遮挡会拒绝输入。键盘输入前须从截图确认编辑焦点。
6. 每次操作后立即重新截图，并检查真实窗口/图纸/业务结果。输入返回成功只表示事件派发，不表示 CAD 行为通过。
7. 拖动检查路径上的目标窗口，Escape 可停止；失败时释放本次鼠标按下。部分输入失败按失败处理并尝试释放本次已派发的按键/鼠标，不盲目重试。

GDI 截图包含屏幕上覆盖目标矩形的实际像素，不能读取被遮挡内容。该工具没有后台截图、辅助功能读取、应用启动、权限修改、网络发送或安装操作。

## 用法

```powershell
dotnet build ./tools/CadDesktopFallback/CadDesktopFallback.csproj -c Release
$fallbackDll = './tools/CadDesktopFallback/bin/Release/net8.0-windows/CadDesktopFallback.dll'
dotnet $fallbackDll self-test
dotnet $fallbackDll windows <pid>
dotnet $fallbackDll activate <pid> <returned-hwnd>
dotnet $fallbackDll capture <pid> <returned-hwnd> <new-output-directory>
# 查看 window.png 后，进行恰好一次操作，并重新 capture：
dotnet $fallbackDll move <observation.json> <image-x> <image-y>
dotnet $fallbackDll click <observation.json> <image-x> <image-y> 2
dotnet $fallbackDll key <observation.json> Escape
dotnet $fallbackDll text <observation.json> '脱敏测试文字'
dotnet $fallbackDll command <observation.json> '_.ZOOM'
dotnet $fallbackDll drag <observation.json> <image-x1> <image-y1> <image-x2> <image-y2>
```

Windows 拒绝前台激活或 `SendInput` 时返回非零，不提高权限来绕过。所有截图和观察记录应置于本轮临时证据目录，不提交私人桌面图片。

`command` 仅在 AutoCAD 中可用，发送一段不含控制字符的文字和 Enter，等价于一次命令提交。必须先通过截图确认当前命令行输入状态，提交后重新观察实际提示，不批量执行脚本。

Office 可用固定快捷键 `Ctrl+1/3/6`（Visio 工具）、`Ctrl+Shift+W`（Visio 适应窗口）、`Ctrl+S/Z/Y/A/O/W`、`Alt+F8`（Word 宏对话框）和 `PageUp/PageDown`。不解析任意快捷键；系统和安全快捷键继续拒绝。快捷键进入工具后，实际绘制仍须用截图确定位置并执行鼠标拖动。Word 只能从已观察的宏对话框运行产品公开入口，不用此工具编辑 VBE、用户宏或安全选项。

## 放行场景

先在新空白记事本截图，输入脱敏单词，再真实双击并观察选择结果；通过后才进入临时 CAD。CAD 使用最终候选包经实际安装和新进程启动，不先调用内部初始化器、`Button.PerformClick` 或探针建立待测状态。通过面板列表真实双击启动 PATMARK，逐项点击大括号/对齐/检测，观察提示替换；绘制左右文字并检查没有底部延伸；再覆盖夹点同侧/跨侧拖动与多文档切换。各场景记录 PASS/FAIL/SKIP，恢复产品安装和本轮创建的临时环境。

API 依据：[BitBlt](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-bitblt)、[MOUSEINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput)。

# Word → Visio 0.1.3.0 当前发布包验收

## 候选和环境

- 时间：2026-09-25 00:56–01:25（北京时间）。执行身份：`本机用户`。Windows 10 22H2 build 19045 x64，Visio 16.0.20430.20092 x64，系统 .NET Framework 4.8.1，产品目标 .NET Framework 4.0。
- 源码：当前含未提交 `office-com-addin/` 的工作树；没有特定 Git 提交。Word/CAD 生产代码未修改。安装与实测仅涉及本轮临时 VSDX；测试结束无 Visio 进程遗留。
- 当前唯一发布包：PatentMarker-WordVisio-0.1.3.0-20260925-011901.zip（本地原始产物未公开），SHA-256 `EF21913F5D35EB534B74916EA815D765BA67DA6021B0A1C1CB07CDF2405DF6D5`。从包内脚本直接覆盖安装；包内、构建暂存和实际安装 DLL 的 SHA-256 均为 `1A807EA80EC79C8DCF1F78816487B88DC2098F3EBC192F8AB8B2DAF9094F8AEC`，程序集版本 `0.1.3.0`，安装路径 `%LOCALAPPDATA%\PatentMarker\OfficeAddin\Visio\PatentOffice.Visio.dll`。
- 输入为已安装 Word 16 x64 加载项通过真实面板导出的两项字典，SHA-256 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479`，本轮前后不变。Word UI 导出证据见 [Word→Visio 联测](word-visio-real-export-20260924.md)。

## 切换文档竞态：同一场景红绿

失败契约：加载项面板已绑定并显示 A 图的字典；在两秒定时刷新之前切到新保存且尚未绑定字典的 B 图，立刻按“检查整份文档”。预期不得把 A 图的漏标结果当作 B 图的结果；实际 0.1.2.0 仍显示“漏标 2 项：1、2”，`_activeDocumentKey` 停留在 A。此时操作没有修改字典或 B 图，但会误导用户；“标注”按钮也有同一入口竞态，存在把 A 字典编号用在 B 图的风险。

回归脚本 [verify-visio-palette-switch.ps1](../verify-visio-palette-switch.ps1) 在真实 Visio COM 中创建 A/B 两份临时 VSDX，加载指定产品 DLL，构造实际 WinForms 面板，绑定 A，然后在无计时器消息泵的窗口中切到 B 并直接调用面板检查处理器。它是**L2 宿主对象/面板处理器回归**，绕过了真实鼠标点击，不能单独替代 L3。

| 版本 | 程序集 SHA-256 | 同一回归结果 | 产物 |
|---|---|---|---|
| 0.1.2.0 旧版 | `C7470AD9E7D5B641816EBA679B30EDC35EB4E0AAD3C9427021E7C996D5AD4542` | `FAIL`，仍检查 A，显示“漏标 2 项：1、2” | 红灯 result.json（本地原始产物未公开） |
| 0.1.3.0 修复版 | `1A807EA80EC79C8DCF1F78816487B88DC2098F3EBC192F8AB8B2DAF9094F8AEC` | `PASS`，识别 B 已成为活动文档，停止本次操作并提示“当前文档已切换。请在新文档重新选择编号或执行检查。” | 绿灯 result.json（本地原始产物未公开） |

修复在“标注”和“检查”按钮处理器入口同步刷新活动文档；若本次发现切换，则清空旧列表、取消本次操作并要求重新选择。状态读取异常同时使字典失效，避免继续使用缓存。实际 L3 面板正常标注也在下节复测。

## 最终 ZIP 安装后用户动作与断言

| 层级 | 操作和业务断言 | 结果 |
|---|---|---|
| L0/L1 | `package-visio.ps1` 重新执行 net40 Release 构建，0 warnings、0 errors；14/14 代码测试；隔离安装矩阵含首装、重复安装、升级故障回滚、卸载及非产品哨兵保护；精确 ZIP 文件清单和逐文件 SHA-256 复核。 | PASS |
| 安装 | 关闭 Visio，从当前 ZIP 的解压目录（本地原始产物未公开）用 Windows PowerShell 5.1 直接运行包内安装脚本；实际安装 DLL 哈希与包内一致，HKCU 产品专属 Visio AddIns `LoadBehavior=3`。PowerPoint 加载项仍为 3，Visio 安装没有修改它。 | PASS |
| L3 限定主链 | 全新 Visio PID `18164`，run `4bb253aecae24fa7b036454dcb448ced` 的三个 COM 加载事件均 PASS。用 COM 在新保存的 VSDX 上准备一条原生一维线并选中；在加载项面板路径框输入实际 Word 字典，Enter 绑定，列表显示编号 1、2。 | PASS；画线和首次选线由 COM 准备 |
| L3 限定主链 | 面板选编号 1，键盘触发“标注所选线条”；`annotation.create PASS` 且面板显示已添加“1”；再按“检查整份文档”，面板显示漏标编号 2，日志 `marked=1;missing=1;malformed=0`。曾有一次未选中列表的操作被明确拒绝，重新选择后成功。 | PASS |
| 保存与冷重开 | Visio UI 保存并关闭图与宿主；测试 VSDX（本地原始产物未公开） SHA-256 `E0DC3BA348841A98491A42773D9832A953D291A1EED59D7F574EC9D1F8111393`。全新 PID `20948`，run `96121bcb91dc48b083fe4e00b1739dff`；面板自动恢复原字典和编号，真实面板检查仍为漏标编号 2，日志 `marked=1;missing=1;malformed=0`；图纸 `Saved=true`。 | PASS |
| 文件保护和退出 | 源字典最终 SHA-256 不变；测试 Visio 图和宿主均通过 UI 关闭，最终无 Visio 进程。 | PASS |

诊断原始日志：`%LOCALAPPDATA%\PatentMarker\Logs\office-visio-20260925.tsv`，按上述 run ID 读取；日志包含本机路径，外发时须先脱敏。0.1.2.0 在本机还通过了两页跨页扫描、多文档隔离和丢失/损坏字典恢复，详见 [0.1.2.0 历史验收](visio-release-validation-20260924-v012.md)；本轮只改面板动作前的活动文档保护，其他场景没有重复执行。

上述首次面板绑定、标注及重开实际先使用同 DLL 的 0.1.3.0 测试包（本地原始产物未公开），SHA-256 `A05B075A5ED3C7042DCFE2E3F6D941D437E29EF435094CCC93D6E938DA6849E1`。后续检查出 Windows PowerShell 5.1 脚本入口问题：旧 `verify-visio-installer.ps1` 的默认 `$PSScriptRoot` 在参数求值时为空，隔离回归直接 `FAIL`；移动默认路径计算到脚本体并正确捕获故障注入子进程的预期 stderr 后，同一隔离安装矩阵 `PASS`。旧封包脚本在 5.1 下也先有 UTF-8 无 BOM 解析失败，添加 BOM 后又暴露缺少 `System.IO.Compression` 引用；修复后 5.1 的完整构建、14/14 测试、隔离安装和封包全链 `PASS`。最终 ZIP 与首次面板测试包的产品 DLL 相同，但安装脚本和 README 已更新。

从最终 ZIP 解压目录（本地原始产物未公开）用 Windows PowerShell 5.1 再次覆盖安装后，全新 PID `32420`，run `f46443d3cc4f41539cb466d82e8e2190` 的 `OnConnection`、`OnAddInsUpdate`、`OnStartupComplete` 均 PASS；重开上述 VSDX，面板自动恢复两项字典，真实面板检查仍显示漏标编号 2，日志 `missing=1;marked=1;malformed=0`。测试图 `Saved=true`，经 UI 关闭图纸和宿主后无 Visio 进程。旧包已移到 `test-evidence/`，发布目录只保留此最终 ZIP。

Windows PowerShell 5.1 还再次执行了 切换文档回归（本地原始产物未公开），同一 0.1.3.0 DLL 得到 `PASS`。最终安装目录只含产品 DLL、Newtonsoft.Json 和所有权清单；清单版本与 DLL 哈希一致。PowerPoint 安装 DLL SHA-256 仍为 `817505CDCDE97D20DEA8C9A7BB70B790283F0304D15136B2A601E72750472B28`，其 `LoadBehavior=3`；Visio 的 `LoadBehavior=3`。

**范围**：在本机 Visio 16 x64，这个发布包通过已安装面板绑定、标注、检查和保存重开限定 L3 链路。鼠标手绘、目标粘合、撤销、复杂组与旧 `.vsd` 未验收；Windows 7、Visio/Office 2010 x86 及其他版本/位数均未验收。Windows 19045 的桌面截图接口故障使画布坐标拖画未覆盖；不能把它标记为产品行为 PASS 或 FAIL。

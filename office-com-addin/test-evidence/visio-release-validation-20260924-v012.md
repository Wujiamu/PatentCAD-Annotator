# Word → Visio 0.1.2.0 本机发布验收

## 候选、环境与前置状态

- 时间：2026-09-24 21:57–22:34（北京时间）；执行身份：`本机用户`。
- Windows 10 22H2 build 19045 x64；Visio 16.0.20430.20092 x64；运行 .NET Framework 4.8.1，程序集目标为 .NET Framework 4.0。Win7、Visio 2010 x86、其他 Visio 版本和旧 `.vsd` 均未验收。
- 源码为含未提交 `office-com-addin/` 的当前工作树，不对应 Git 提交。Word/CAD 生产代码未改。测试开始前 Visio 进程关闭；安装器从 0.1.2.0 测试 ZIP（本地原始产物未公开） 解压目录安装，ZIP SHA-256 `C4BDBBEF20EF83589457ECE3A134A7655775E5161121D9E23DEF653C8DD1E815`。产品 DLL 在 ZIP 与实际安装目录 `%LOCALAPPDATA%\PatentMarker\OfficeAddin\Visio\PatentOffice.Visio.dll` 的 SHA-256 均为 `C7470AD9E7D5B641816EBA679B30EDC35EB4E0AAD3C9427021E7C996D5AD4542`，程序集版本 `0.1.2.0`。
- Word 输入为已安装 Word 16.0 x64 加载项通过面板从临时文档实际导出的两项字典，详见 [Word UI 导出记录](word-visio-real-export-20260924.md)。字典 SHA-256 为 `74ADC14A23992785721CB366E13D313784D41CE2AFE26894C58F5019BF973479`，整个 Visio 测试前后不变。
- 测试前只有本轮创建的 Visio 测试图；未使用或修改用户图纸。PowerPoint 注册项 `LoadBehavior=3` 和此前安装 DLL SHA-256 `817505CDCDE97D20DEA8C9A7BB70B790283F0304D15136B2A601E72750472B28` 未受 Visio 安装影响。

## 用户界面及保存重开

| 层级 | 用户级动作与可检查结果 | 结果 |
|---|---|---|
| L3 限定主链 | 从上述 ZIP 安装后，全新 Visio 进程 run `66ff12cd06ac469cb49c9970939a6912` 的 `OnConnection`、`OnAddInsUpdate`、`OnStartupComplete` 均为 PASS。面板显示在独立窗口。 | PASS |
| L3 限定主链 | 对已保存的测试图，在面板“字典文件路径”输入框填入 Word 实际导出文件并按 Enter；面板显示绑定成功、两项编号、可用标注按钮。日志 `binding.save PASS`，含输入字典哈希。 | PASS |
| L3 限定主链 | 第 1 页先用 Visio COM 准备一条原生一维线并选中；在真实面板选择编号 1、键盘激活“标注所选线条”。面板显示已添加；日志 `annotation.create PASS`。面板“检查整份文档”显示漏标 1 项（编号 2），日志 `missing=1;marked=1;malformed=0`。 | PASS，画线和首次选线不属 UI 覆盖 |
| L3 限定主链 | 经 Visio UI 保存并关闭，关闭 Visio 冷启动 run `676878316b924164811834b8b5d67492`；重开同一 VSDX 时绑定自动恢复，面板再次显示漏标 2。 | PASS |
| L3 限定主链 | 在第二前景页先用 Visio COM 准备一条线，再用 Visio 画布 `Ctrl+A` 选中该页唯一线条；独立 COM 观察到 `SelectionCount=1`、`SelectionOneD=-1`。真实面板选编号 2 并激活标注按钮；整份检查显示均已标注，日志 `missing=0;marked=2;malformed=0;scope=foreground-pages`。 | PASS，第二页选线经过真实 UI |
| L3 限定主链 | Visio UI 保存，关闭宿主，冷启动 run `59660613f3b34a7bbd5999d23414c8da`，重开同一 VSDX；绑定与 2 项字典自动恢复，真实面板检查仍显示均已标注。最终 两页 VSDX（本地原始产物未公开） SHA-256 `115AE23C0304F6A289644DF7E2646342453CAC564FE4711917EABB715734C72A`；压缩包 XML 有一份文档绑定及两页各一对产品形状。 | PASS |
| L3 限定主链 | 切换到新保存的第二文档，面板清空绑定与编号、禁用标注；绑定该文档自己的字典副本后显示两项；切回首份文档时原关联自动恢复。 | PASS |
| L3 限定主链 | 第二文档的字典副本移走、恢复；另以真实一字节损坏文件 `{` 替换、再恢复原字节。面板在丢失与解析失败时保留上次有效列表、禁用标注与检查；文件恢复后自动重新读取并启用。日志出现 `dict.read FAIL` 与后续 `dict.read PASS`。原 Word 字典及副本恢复后哈希均为 `74ADC14A...`。 | PASS |

所有桌面操作使用 Computer Use 的 `@oai/sky` 辅助功能树和键盘，按读取状态、单次动作、复读状态执行。Windows 19045 上截图接口返回 `SetIsBorderRequired 0x80004002`，所以无法用坐标执行或观察拖画；没有以猜测坐标代替。系统文件选择框在当前桌面工具下不可操作，产品提供的文本路径绑定入口完成相同校验。整条“鼠标手绘、拖动粘合、鼠标选线”的用户动作仍为 `SKIP/BLOCKED`，以上是已安装版真实面板与持久化的**限定 L3**，不是全输入方式 L3 或目标旧版 L4。

## 宿主对象、安装与旁路断言

| 检查 | 结果与证据 |
|---|---|
| L0/L1 | net40 Release 构建 0 warning、0 error；代码测试 14/14 PASS。`package-visio.ps1` 的隔离安装器矩阵覆盖首装、重复安装、故障回滚、卸载和非产品哨兵不变。 |
| 宿主对象负例 | run `eeb36fffe289474daa2a0696bfad8031`（本地原始产物未公开） 在真实 Visio 16 x64 上断言空选择、普通矩形、同一产品引线重复绑定均被拒绝；正常绑定、标注、扫描、保存重开通过，字典前后哈希相同。此脚本反射调用已安装生产方法，属于宿主对象回归，不充当面板 L3。 |
| 产品形状范围 | 普通矩形不计入；VSDX 的产品身份仅存于本轮添加的线和编号框。页内标注配对和跨页残件的代码红绿记录见 [缺陷契约](visio-release-defects-20260924.md)。 |
| 多页与隔离 | 第一页编号 1、第二页编号 2，经 Visio UI 保存、完整关闭宿主并冷重开后，真实面板全稿扫描 `marked=2;missing=0`。第二文档绑定未覆盖第一文档。 |
| 文件保护 | Word 源字典测试前后 SHA-256 不变；测试 Visio 进程仅打开本轮两份图，最后保存并关闭；无 Visio 进程遗留。 |

## 最终封包与再次冷启动

- 更新包内使用说明后，生成当时的 0.1.2.0 包 PatentMarker-WordVisio-0.1.2.0-20260924-223915.zip（本地原始产物未公开），SHA-256 `1FA1959533FAC8D24D75824FFA14B778DBDEB65D7309DC0CCEB2B8CFBF884E99`。`verify-visio-package.ps1` 对精确文件清单、逐文件 SHA-256、版本和源文件一致性均 PASS。与首次实际安装的 0.1.2.0 测试 ZIP 逐项对比，两个安装脚本、共用模块、产品 DLL、Newtonsoft.Json DLL 和两份许可证均字节相同，只有包内 `README.md` 文字更新。此包现已归档，当前发布包为 0.1.3.0。
- 关闭 Visio 后，从最终 ZIP 的 实际解压目录（本地原始产物未公开）再次安装；已安装产品 DLL SHA-256 仍为 `C7470AD9E7D5B641816EBA679B30EDC35EB4E0AAD3C9427021E7C996D5AD4542`。全新进程 PID `15580`，run `b3e818b1b8254c3e96fbfbf508b7a751`，三个加载生命周期事件 PASS。重开两页 VSDX 后面板自动读到原字典两项，面板键盘触发“检查整份文档”显示均已标注；日志 `2026-09-25T00:52:01` 为 `missing=0;marked=2;malformed=0`。测试图 `Saved=true`，通过 UI 关闭图和宿主后无 Visio 进程遗留。
- 此 0.1.2.0 ZIP 已移至 `test-evidence/`；当前发布目录只保留 0.1.3.0，避免误选历史候选。

诊断原始文件为 `%LOCALAPPDATA%\PatentMarker\Logs\office-visio-20260924.tsv`，按上述 run ID 可找出各生命周期、绑定、标注、解析和检查阶段；运行跨过午夜，当前 run 仍写入其启动日的日志。记录中有本机绝对路径，分享时须先脱敏。旧 0.1.1.0 验证为历史候选，见 [先前记录](visio-release-validation-20260924.md)。

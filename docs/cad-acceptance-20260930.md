# CAD 修复候选验证（2026-09-30）

最新状态（2026-10-01 下午）：**本次 CAD 修复已完成最终 2025 包在 AutoCAD 2026 x64 的安装后鼠标、夹点、多图纸验收及 C1–C9 16 场景矩阵，达到可提交状态。** 最新隔离构建、包哈希及限制见 [最终验收](cad-acceptance-20261001.md)。下文 9 月 30 日的状态和哈希保留为历史记录。

## 环境和候选

- 时间：2026-09-30 20:31–20:55（Asia/Shanghai）；补验 2026-09-28 的候选。
- 执行身份：本机交互账号，脱敏为 `DESKTOP-…\<local-user>`；完整身份在本机证据清单中。
- 宿主：AutoCAD 2026 x64，托管版本 `25.1.0.0`，产品 R25.1.60.0.0；Windows 10 22H2。
- 源码：分支 `codex/office-addins-release`，基线 `a1e8886`，本轮未提交 CAD 工作树修改。已有 Office、NumberIdentity 及相关测试修改保留；编译使用当前工作树，不能宣称来自干净提交。
- 原 2025 部署 DLL：仓库 `PatentMarker-2025-deploy/PatentMarker.dll`，SHA-256 `CF634E2D05E78D1A4680D41EEF90DD85D4BFEF81EE12A89EC32300F02EF0F993`；原文件未覆盖。
- 最终暂存包：`%TEMP%/PatentMarker-CAD-release-20260930/{2007,2010,2013,2015,2025}`；2025 实际加载文件为其中 `2025/PatentMarker.dll`，SHA-256 `FFDB98F3CA9F6C43A234FC0A8637926BC45F9117A900542AE1B0223B7C5E54DB`。源码临时输出的 `49FBE5…` 另有 9 项通过记录，但交付结论使用最终暂存文件。
- 无测试日志的交付副本：`%TEMP%/PatentMarker-CAD-release-final-20260930/`。逐版 DLL 与上述暂存产物哈希完全相同。2013/2015 重新 ILRepack 会产生不同字节，因此交付副本沿用首次合并的同一 DLL，未以重新合并文件替换证据对应文件。
- 探针：`tools/CadPanelSwitchProbe`，最终 DLL SHA-256 `8ABDB2BDA701A02447A6FA144B586EF0DD594A43F06A8F6C1A4AED664BD5E509`。不安装、不打包。

## 失败契约与红绿证据

### 面板命令互相覆盖

初始状态：新 AutoCAD 进程、空白临时 DWG、同名字典仅包含 `901 / Switch Test`；本轮不操作用户图纸。开启 PATPALETTE，开始 PATMARK 等待附着点，调用真实大括号按钮处理逻辑。

预期：取消本面板的 PATMARK，PATBRACE 进入输入提示。原包实际：PATMARK 保持活动，10 秒超时，`PASS=0 FAIL=1 SKIP=8`。原用户报告的鼠标按钮路径仍未复现；该失败是宿主内部入口复现。

最终暂存包在同一探针下 `PASS=9 FAIL=0 SKIP=0`：

| 场景 | 业务断言 | 结果 |
|---|---|---|
| PATMARK → PATBRACE | 原命令结束，目标输入提示仍活动 | PASS |
| PATBRACE → PATMARK | 同上 | PASS |
| PATBRACE → PATALIGN | 同上 | PASS |
| PATALIGN → PATBRACE | 同上 | PASS |
| PATMARK / PATBRACE / PATALIGN → PATCHECK | 本图纸结果版本更新且 901 为漏标 | PASS（3 项） |
| PATMARK → PATBRACE → PATALIGN 快速切换 | 中间 PATBRACE 未启动，最终 PATALIGN 提示活动 | PASS |
| 原生 LINE 期间请求 PATBRACE | LINE 仍活动；探针主动取消 LINE 后才启动 PATBRACE | PASS |

所有命令失败事件会使探针 FAIL，未执行场景记 SKIP；退出码或日志存在不作业务断言。运行中只取消输入提示，结束后临时 DWG/JSON 字节不变。结果属 **L2_HOST_INTERNAL**：PATMARK 经面板公开 API 请求，其他按钮经 `Button.PerformClick`，绕过鼠标、列表双击和安装器。

证据目录：`%TEMP%/PatentMarker-CAD-panel-probe-20260930/`，最终红绿报告分别为 `red-final/panel-report.txt` 和 `green-final/panel-report.txt`。报告包含真实 Assembly、Drawing、宿主版本和命令事件时间。新进程显式 NETLOAD；候选启动时暂时关闭三个精确产品注册项的 LOADCTRLS，随后核对值及类型恢复原状；未修改 SECURELOAD、TRUSTEDPATHS 或非产品配置。

### 文字左侧的底部延伸线

初始状态：Autodesk IRD.dwt 的独立副本，模板 SHA-256 `6CE9ECEA91C943DBAA5A6A94264DBA3929C38699DB33E053404CB4C752C31CC5`。直线、无箭头、三点模式，使用生产 PATMARK 输入：左侧 `(0,0)→(30,20)→(10,20)`，右侧 `(100,0)→(130,20)→(150,20)`，编号 901。

原包的旧 PATMLVERIFY 曾报告 2/2 PASS；它缺少方向和文字底部几何断言，不能否定用户失败。原包生成的保留图纸 SHA-256 `5EFFBC1331338F92AB343B622286C6D37D969F51061A81ECC2C7637569853856`，由最终暂存 DLL 的增强验证器只读检查：两实体 C7/C8 均 FAIL，`total=2 passed=0 failed=2`，RightLeader 为 `AttachmentBottomOfTopLine`，底部线段各 1 段。

最终暂存 DLL 在新临时图纸重新执行同一 PATMARK 点链：`total=2 passed=2 failed=0`；两方向均为 `AttachmentMiddle`，C8 各 0 段，文字仍为 `(10,20)` 和 `(150,20)`；C1–C6 也通过。这是 **真实 Core Console 命令级几何证据**，覆盖新建实体，不代表面板 UI、安装或夹点拖拽。

补充组合矩阵：直线/样条 × 箭头开/关 × 三点/无限点 × 左/右文字，共 16 个生产 PATMARK 新建实体；无限点使用两个拐点。全部 C1–C8 通过，`total=16 passed=16 failed=0`。证据在 `%TEMP%/PatentMarker-CAD-geometry-matrix-20260930/`（`matrix.scr`、`matrix-report.txt`、`matrix-result.dwg`）。该矩阵同样是命令级证据，未覆盖夹点拖拽。

证据目录：`%TEMP%/PatentMarker-CAD-geometry-packaged-20260930/`，绿证据 `verify-report.txt`、`geometry-probe.txt`、`geometry-green-result.dwg`；红证据 `old-verifier-report.txt`、`old-probe.txt`、`verify-old.scr`。实际加载路径在 probe 和插件加载日志中相互核对，外部计算 DLL 哈希。旧实体不自动迁移。

## 最终检查

### 2026-10-01 实机复核发现的文字侧回折（修复前 FAIL）

用户已允许 `tools/CadDesktopFallback` 独立工具覆盖官方桌面链路约定。候选经 `install-2025.ps1` 实际安装，新进程正常打开临时 `ui-test.dwg`，日志确认加载 `installed-candidate/PatentMarker.dll`（`FFDB98F3…`），未显式 NETLOAD 或调用初始化器。测试目录为 `%TEMP%/PatentMarker-CAD-desktop-20260930-234032/`；PID、启动时间、窗口、截图、哈希和注册表原值均已保存。

失败契约：AutoCAD 2026 x64；新空白模型、901 字典、样条/三点/无箭头/无下划线、字高 3.50；真实双击 901，依次点击附着点、拐点及右侧文字点。预期引线从拐点直接结束于文字靠近拐点的一侧。实际新建引线越过文字并回折；`cad46`、`cad49`（REGEN 后）及 `cad50`（选中真实实体）保留该现象。拖动文字夹点并提交位置后回折消失（`cad52`）。此前 C1–C8 的 PASS 仅证明其已有断言，未捕获此文字侧错误，不能作为完整附着问题修复证据。

补充诊断 `tools/MLeaderRepro/MLeaderTailProbe.cs`（L2 内部入口，非用户路径）证明：创建时末顶点被宿主调整到文字的错误侧；只重赋文字位置或改 MText 对齐不能解决。显式设置 leader cluster 的 `SetDogleg` 方向虽关闭 dogleg 几何，仍能确定文字连接侧；左右分别设方向后，末端均落在面对最后拐点的一侧。报告在 `%TEMP%/PatentMarker-CAD-tail-diagnostic-20261001/tail-diagnostic.txt`。接下来加入对应生产修复及面对文字边缘的实际几何断言，再重跑同一鼠标场景。

| 检查 | 结果 | 限制 |
|---|---|---|
| 五版真实 SDK 编译 | PASS，0 错误 | 既有编译警告仍存在；编译不证明旧版宿主运行 |
| API、版本同步、MLeader 同步 | PASS | API all 不包含 2007；2007 实际编译通过 |
| Structure / Static | PASS | 文件、引用、Word 资产和安装器契约检查，非 UI |
| 2025 单测 | PASS，122/122，0 SKIP | L1 |
| 2007/2010/2013/2015 模拟宿主 | PASS，各 33/33，0 SKIP | L1 模拟，不是真实 CAD |
| 五版发行暂存 | PASS | 原部署文件未覆盖；暂存包未安装到用户环境 |
| GitHub 认证与远程只读访问 | PASS | 未推送，未据此宣称发布门通过 |

最终五版暂存 DLL SHA-256：

| 版本 | SHA-256 |
|---|---|
| 2007 | `8C0195E3AB6E58324FE9C4356C2E5BB05F41204961ABC705C6F027CF0AD39ECC` |
| 2010 | `A64AB0034E89D836254CDFD193ED3DBC8C9C5CC44B9E4F3A9F3FB8120DBD4A66` |
| 2013 | `A0C10603834F1F514601DFD67F95C63FD6D6728EE0289ACC3281A2A26E713AD5` |
| 2015 | `DB6CE7C95B09E6B7185C0FF988B98EDE49A870DC6C547218CD4A7817C78B21B9` |
| 2025 | `FFDB98F3CA9F6C43A234FC0A8637926BC45F9117A900542AE1B0223B7C5E54DB` |

可在仓库复核的脱敏业务报告见 [面板红证据](test-evidence/cad-20260930/panel-red.txt)、[面板绿证据](test-evidence/cad-20260930/panel-green.txt)、[几何红证据](test-evidence/cad-20260930/geometry-red.txt)、[几何绿证据](test-evidence/cad-20260930/geometry-green.txt) 和 [16 场景矩阵](test-evidence/cad-20260930/geometry-matrix.txt)。工作目录与临时路径分别替换为 `<WORKSPACE>` / `<TEMP>`。

## 尚未通过的放行项目

- **SKIP/BLOCKED：面板真实双击、按钮点击和视觉检查。** 使用 Computer Use 原生 `@oai/sky`，实际识别到新图纸窗口、901 字典条目及“字典已加载”、大括号/对齐/检测按钮。截图返回 `window capture timed out`，索引双击返回 `coordinate input geometry is unavailable`。键盘曾改变焦点，后续状态多次为空，无法形成可观察操作闭环。未猜坐标或改用自制桌面输入绕过。
- 后续诊断在空白记事本复现相同截图和双击失败；重建会话、最大化仍失败，官方更新器返回已是最新版。记事本键盘菜单和最大化动作可被辅助功能树验证，所以此前“键盘状态不稳定”不能扩展成“所有输入都坏了”。故障定位及 Windows API 兼容性证据见 [Computer Use 诊断](computer-use-diagnostics-20260930.md)。
- **SKIP/BLOCKED：候选包安装、关闭后正常冷启动的 L3，以及用户操作的 L4。** 本轮候选显式 NETLOAD、产品自动加载隔离，不代替安装后的用户路径。
- **SKIP/BLOCKED：夹点同侧/跨侧拖动、跨文档关闭时的实机输入切换，以及其他支持年份实机。** 历史测试或模拟 PASS 不能外推。

只关闭本轮拥有的 AutoCAD 临时进程。完整本机身份、路径、哈希、日志和源码状态保存在上述临时证据目录，提交文档使用脱敏样本。Computer Use 能提供可靠截图/输入后，需用同一最终候选完成真实安装和面板操作，再更新放行结论。

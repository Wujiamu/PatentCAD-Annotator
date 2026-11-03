# 修复任务接续记录（第二轮）

时间：2026-11-01T16:20+08:00（Asia/Shanghai）

基线 HEAD：`b945b1a68fec26ada268b5a4ee3ac066386e96ab`，含用户原有未提交与未跟踪工作树内容；本轮未创建任何提交，未推送。

## 本轮完成的任务包

| 任务包 | 状态 | 一句话结论 |
|---|---|---|
| G01 | **R-030 关闭** | 补修候选由协调者在集成后的根工作树重新独立构建，33 个冻结程序集 `passed=33 failed=0` |
| G02 | **已集成** | 一次命中链修 R-016／017／018／021／032／033；C# 与真实 VBScript 双引擎 25 + 48 例全绿，真实 Word L2 5 项全绿 |
| G05 | **已集成** | 19 生产源 + 公共测试迁移写入根树；2025 全套 160→196 PASS、Word L2 0 FAIL |
| G06 | **已集成** | 改号结果对象化 + 单事务回滚 + 显式重试；6 个专用用例绿，红灯 1 FAIL 成立 |
| G07 | **已集成** | `Release` 只重置当前所属图纸；原生同步失败进 recent errors；两组红灯各 2 FAIL 成立 |
| G08 | **已集成** | 清单暂存后激活 + 改名前的参照副本 + 每步核对；15 例隔离矩阵全 PASS |
| G10 | **已集成** | 两面板各去两段式读取、绑定每次重解析并清陈旧状态；Visio 16 + PPT 15 全绿 |
| G11 | **R-014／R-031 关闭；R-012 结论修正** | 真实 PowerPoint 16.0 验证；R-012 的“组合后仍保留 Tags”前提被宿主推翻 |
| G14 | **已集成** | 卸载前建立独立恢复副本；PPT／Visio 隔离矩阵 + 单元自检全绿，红灯两宿主各 exit 1 |

上一轮已关闭或已集成：G03、G04、G09、G12、G13（G13 的 R-028 仍待真实宿主核验）。

## 最终门禁（全部在本轮实际执行）

- 2025 全套 196 PASS／0 FAIL；四组模拟 83／96／96／96 PASS，0 FAIL
- Office：PPT 34 PASS；Visio 39 PASS + 3 SKIP（`Theory` 不受 FactRunner 支持，非通过）
- `build.ps1 -Version all` 五版编译通过；`build.ps1 -Static` 退出 0
- 四个同步检查（版本、MLeader、VBA、Word 资产）全部退出 0
- 五套部署 `PatentMarker-release.txt` 与 DLL／DOTM／VBA 资产全部匹配
- VBA L2：`verify-vba-export.vbs`、`test-vba-panel.vbs`、`test-g05-word.vbs` 全部 PASS／0 FAIL
- 隔离安装矩阵：`verify-vba-installer.ps1`、`verify-installer.ps1`、`verify-visio-installer.ps1` 全部退出 0
- **真实 Startup L4**：`verify-vba-installed-e2e.ps1 -ExtendedMatrix` 退出 0，`auto_export=true`、`normal_unchanged=true`、`save_as_immediate_export=false`，实际加载 dotm `43D18D2E…`

## 本轮的环境发现（非产品缺陷，但会误导后来者）

1. **`%TEMP%` 写入被拒**：`tools/ReleaseManifest` 与 ILRepack 的 `Path.GetTempFileName` 在系统 `%TEMP%` 下报 `Access denied`，同路径 PowerShell 写入正常。打包必须先把 `TEMP`／`TMP` 指向工作目录。
2. **项目根 `dotnet test` 假绿灯**：xunit 适配器在该路径下加载失败，发现 0 个测试但退出码 0。`verify-code.ps1` 靠核对 TRX 的 `total/executed` 拦截了它；本轮 Office 证据统一用 `tools/run-fact-tests.ps1`。
3. **VSTest 父进程句柄**：本机 `dotnet test` 在断言前中止，按计划记环境 SKIP。
4. **PowerShell 5.1 需要 UTF-8 BOM**：编辑 Office 模块／脚本时若丢掉 BOM，中文错误信息会变乱码并解析失败。本轮已因此返工一次，所有模块文件已补回 BOM。
5. **真实 PowerPoint 组合语义**：把产品标注组与其它图形组合，PowerPoint 会**展平内层组并丢弃 Tags**。这是 R-012 结论修正的直接依据。

## 仍未关闭

- **GitHub 外部宿主验收**：2007／2010／2013／2015 四组目标环境未回传；本机未安装或探测旧宿主。
- **真实 AutoCAD 2025 用户路径**：G03／G06／G13 的宿主部分未做真实命令／UI／保存重开。
- **真实 PPT／Visio 面板交互**：G10 的 Undo/Redo、交互式 Save As；G11／G12 只做了属性与渲染探针。
- **R-028**：大括号 Xrecord 组码契约仍待真实宿主核验。
- **独立验收**：本轮 G02／G06／G07／G08／G10／G11／G14 均为协调者或实现者自测；G01／G03／G04／G09／G12／G13 有上一轮的独立重跑记录。

## 交付物位置

- 逐包交接：`docs/repair-work/Gxx.md`
- 证据：`docs/test-evidence/repair-20261031/Gxx/`
- 执行登记：`docs/repair-plan.md` 第 9、10 节

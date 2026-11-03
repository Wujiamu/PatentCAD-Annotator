# 修复任务接续记录（第三轮，2026-11-02）

基线 HEAD：`b945b1a68fec26ada268b5a4ee3ac066386e96ab`，叠加前两轮已集成内容与用户原有未提交改动。本轮未创建任何提交，未推送。

## 本轮范围

按 `docs/repair-plan.md` 第 11 节，只处理复核报告列出的 **4 项**：R-016 补修（G15）、R-034（G16）、R-004 补修（G17）、R-029 补修（G18）。未重新分派已完成的 G01–G14，未因文档旧状态再动 R-012／R-028。

| 包 | 条目 | 结果 |
|---|---|---|
| G15 | R-016 | 已集成。name-first 编号左边界两侧同修 |
| G16 | R-034 | 已集成。恢复选择上下文与已解析模型分开 |
| G17 | R-004 | 已集成。待同步记录按图纸隔离、写入前拦截 |
| G18 | R-029 | 已集成。文件完整才恢复注册项 |

## 最终门禁（全部在本轮实际执行）

- 2025 全套 **252 PASS / 0 FAIL**（G16 前基线 214 → +19 G16 → +19 G17）
- 四组模拟 83 / 96 / 96 / 96 PASS，0 FAIL
- Office 经 VSTest：Visio **48/48**、PPT **34/34**
- 五版编译通过；`build.ps1 -Static` 退出 0
- 四个同步检查与五套 release manifest 全部通过
- VBA L2：`verify-vba-export`、`test-vba-panel`、`test-g05-word`（FAILURES=0）、`test-g02-l2`（7 PASS）
- 隔离安装矩阵：`verify-vba-installer`、`verify-installer`、`verify-visio-installer` 全部退出 0
- **真实 Word 16.0 64 位 L4**：`verify-vba-installed-e2e.ps1 -ExtendedMatrix` 退出 0，`auto_export=true`、`normal_unchanged=true`，加载 dotm `ED38D9E7…`

## 本轮的环境发现

1. **VSTest 假绿灯根因已定位**：工作区根目录带一条 `Everyone : Deny DeleteSubdirectoriesAndFiles` ACL（沙箱遗留）。在该路径下 `dotnet vstest` 报「没有可用测试」却**退出码 0**。同一程序集复制到 `%TEMP%` 或 `C:\pmvt` 即正常。已新增 `tools/run-office-tests-vstest.ps1`：复制到中性目录运行并强制核对 TRX 的 `total/executed/passed`，发现 0 个测试即判失败。此前「Visio 39 PASS + 3 SKIP」的缺口因此关闭。ACL 本身未改（环境状态，非产品缺陷）。
2. **`%TEMP%` 写入被拒**：`tools/ReleaseManifest` 与 ILRepack 在系统 `%TEMP%` 下报 `Access denied`，打包需先把 `TEMP`／`TMP` 指向工作目录。
3. **PowerShell 5.1 需 UTF-8 BOM**：G18 与我在编辑 Office 脚本时多次剥掉 BOM，会导致中文错误信息变乱码并解析失败。已逐文件补回并纳入检查。
4. **.NET 2.0 无 `System.Func`**：G17 用了 `Func<string, DictEntry>`，2007 组编译 `CS0246`。已改为具名委托 `NumberLookup`。这条回归由协调者在集成后发现并修复——说明五版真实编译门禁确实有必要。

## 仍未关闭

- 真实 CAD 面板操作：G16／G17 的按钮点击、对话框弹出与视觉、跨图纸切换、真实 `DocumentLock`／`Transaction`。
- 四组旧宿主（2007/2010/2013/2015）GitHub 外部验收未回传；本机按约定未安装或探测。
- AutoCAD 2025 本体（R-028 仅在 2026 内核往返核验）。
- 32 位 Office、其他 Word 版本、ACL／目录权限拒绝、宏策略阻止、VBA Reset。
- G15–G18 缺独立验收角色，本轮为自测。

## 交付物

- 逐包交接：`docs/repair-work/G15.md`、`G16.md`、`G17.md`、`G18.md`
- 证据：`docs/test-evidence/repair-review-20261101/{G15 分散于根目录同名文件,G16,G17,G18}/`
- 执行登记：`docs/repair-plan.md` 第 11.3、11.4 节
- 新增工具：`tools/run-office-tests-vstest.ps1`、`tools/verify-r028-xrecord-roundtrip.ps1`（上轮）

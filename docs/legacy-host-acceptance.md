# 四个旧宿主的候选验收

这是待验证的候选包交接，不代表旧宿主已通过。分别在真实 AutoCAD 2007–2009、2010–2012、2013–2014、2015–2024 的许可环境执行；记录实际年份、完整版本和位数，不跨组混装。2007 使用 Leader + MText，其他组使用 MLeader。

候选位于仓库根目录 `PatentMarker-<edition>-deploy`。同目录的 `PatentMarker-release.txt` 记录 DLL SHA-256/MVID。候选由有修改的工作树生成；manifest 中的原始 commit 和 dirty 状态保留，不能把上传后的新 commit 当作原构建 commit。执行前把 DLL 哈希与 manifest 比较；不重新打包或替换候选后沿用旧证据。

只使用本轮新建的可丢弃图纸与脱敏字典。先保存并正常退出既有 CAD，备份已有本产品加载路径、DLL、配置及本产品安装注册项。安装/恢复按对应包说明执行；不更改图形、宏、安全策略或其他插件。安装后使用全新进程，不以再次 NETLOAD 冒充冷启动切换。若已有产品无法安全备份或恢复，先记录 BLOCKED，不覆盖它。

## 每个宿主必须记录

| 字段 | 内容 |
|---|---|
| 宿主 | 年份、完整版本、32/64 位、Windows 版本 |
| 候选 | edition、实际 DLL 完整路径、外部 SHA-256、manifest MVID |
| 初始状态 | 新进程、空白测试图、字典路径、测试前实体数量 |
| 身份 | PATDOCTOR 的实际程序集路径/SHA/MVID，PASS/FAIL/SKIP 明细 |
| 几何 | PATGEOMVERIFY 原报告、RUN_ID、SUMMARY、RESULT |
| GUI | 以下每项的预期/实际、截图、所用输入动作 |
| 持久化 | 保存前后及冷重开的图纸哈希、内容和加载身份 |
| 退出/恢复 | 正常退出或异常、崩溃事件、仅本产品恢复的前后核对 |

无业务断言、无报告或仅退出码 0 都不是 PASS。崩溃、无响应、身份不符时停止本轮，保留错误和最短步骤，不重复启动撞相同故障。

## 当前端点规格（R8确认、R9沿用）

末端位于朝向前一拐点的实际文字边框中点、上角或下角外侧，保留0.4×字高间隙，文字拾取点保持不动。T3/C9用同一边框间隙判定。无限点模式末次拾取作文字锚点时，不得保留贴在文字上的重复末顶点。不得继续使用旧插入点回缩或零间隙目标。

## 固定测试矩阵

1. **身份与冷启动**：安装候选后正常启动新 CAD，不先 NETLOAD，执行 PATDOCTOR。核对实际路径/SHA/MVID；空图缺文字样式导致 SKIP 应保留，不能写七项全通过。
2. **四方向大括号**：PATBRACE 三点分别为 `(0,40),(0,0),(8,0)`；`(30,40),(30,0),(22,0)`；`(50,20),(90,20),(90,28)`；`(50,0),(90,0),(90,-8)`。检查中尖点、四个转角、方向和无多余回折；近景如实记录采样折线。
3. **正规编辑**：PATBRACEEDIT 选择第一个对象，按实际关键字选择尺寸，高 60、宽 12；确认同一对象更新。保存 edited → Ctrl+Z 后 undone → 立即 Ctrl+Y 后 redone 截图，中间不执行任何 CAD 取证命令。
4. **文字/引线**：在面板设置引线开、箭头关、三点、文字高 3.5、下划线关。通过真实按钮切换直线/样条，各建左右方向。点为 `(0,100),(20,110),(40,110)`、`(60,100),(80,110),(65,110)`，样条对应 Y 加 40。确认文字水平朝 +X、无长尾/回环/额外 dogleg。2007 检查独立 MText 与 Leader 关联，MLeader 专有断言记不适用。
5. **原生夹点**：左右样条文字分别做同侧和跨侧移动，以及开始后 Esc 取消。大括号做一个原生顶点移动及取消。每次保留 before/moved/undone/redone 四状态；Undo 与 Redo 之间不插入 LIST、保存、报告或其他命令。最后撤销破坏性变形。原生编辑可能令保存参数链过期，必须单列几何结果，不把视觉可操作等同参数验证通过。
6. **面板与取消**：单击选行、双击开始标注、F2/右键编辑；产品命令之间切换与 Esc；原生 LINE 等点时点击产品按钮，不应抢断原生命令。记录实际提示和是否重复创建。
7. **多图隔离**：A/B 两张新图的同名字典分别为 901/Manual-A、902/Manual-B。切换、关闭 A 后在 B 标注，确认不串字典或向已关闭图提交。
8. **保存与再启动**：保存两图，正常完全退出 CAD，新进程重开；不先 NETLOAD，复核身份、实体数量、文字/方向/位置、A/B 字典及 PATGEOMVERIFY。

字典最小内容（A 图同名 `.dict.json`，UTF-8；B 改为 902 和 Manual-B）：

```json
{"metadata":{"version":"legacy-acceptance"},"entries":[{"number":"901","name":"Manual-A","occurrences":1,"conflicts":[]}],"warnings":[]}
```

## 独立报告门禁

在启动本轮 CAD 的父进程中设置唯一 `PATGEOM_RUN_ID`、独立 `PATGEOM_REPORT_DIR`、`PATGEOM_REQUIRE_BRACES=1`、`PATGEOM_REQUIRE_TEXTS=1`、`PATGEOM_STRICT=1`；只影响该测试子进程。记录这些值，启动后恢复父进程环境。不设置用户/机器级环境变量，不启用后台监控。对只含一种实体的专门对照，把另一类 required 明确设为 0 并分别记录。

将原报告复制到复核机器，使用当前仓库的报告门禁；无需在那里安装旧 CAD。示例中的值须替换为本次预先约定的 run ID、实际路径和候选 manifest 值：

```powershell
pwsh -NoProfile -File ./tools/verify-geometry.ps1 `
  -ReportPath '<fresh-report.txt>' -RunId '<expected-run-id>' `
  -ExpectedAssemblyPath '<actual-loaded-PatentMarker.dll>' `
  -ExpectedDllSha256 '<manifest-sha256>' -ExpectedMvid '<manifest-mvid>' `
  -RequireBraces 1 -RequireTexts 1
```

门禁退出码 0 只证明这份报告满足给定身份与几何契约，不能替代 GUI 和安装证明。旧 run ID、错误身份、缺必需断言、重复断言、计数矛盾都应拒绝。提交结果时附原报告、截图、脱敏图纸、哈希索引和逐项 PASS/FAIL/SKIP/BLOCKED 表；分享前移除账号、私有路径和正文，不发送 Autodesk SDK、凭据或本机协作说明。

## R9 固定交接范围

四旧组本机没有，只允许GitHub外部真实宿主执行；不要回到本机重复探测或安装。当前候选及SHA/MVID见[cad-acceptance-20261022.md](cad-acceptance-20261022.md)。新增必测：只读/锁定字典失败保护与解除后恢复、隐藏/系统/其他属性保留、外来`.tmp`不变、备份缺失/损坏/占用与重试；闭合/带bulge的PAT大括号必须FAIL；空图无业务目标不能作为PASS。仍需真实Leader与MLeader差异、正常GUI/夹点/Undo/Redo与安装后冷启动证据。

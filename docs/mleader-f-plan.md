# MLeader 复线方案（F 方案）/ MLeader Fork Plan (Plan F)

> 状态 / Status: MVP 开发中（2025/2026 版先行）/ MVP in progress (2025/2026 edition first)
> 分支 / Branch: `mleader`　基线 / Baseline: `5545e89` (v4.6)

---

## 1. 背景与目标 / Background & Goal

**中文：**
主线项目（PatentCAD-Annotator）的标注引擎采用 `Leader + MText` 两个独立实体拼合，通过扩展字典维护关联。2026-08-06 曾因 MLeader"鱼钩形态"问题回退该方案；2026-08-15 的形态探针（`tools/MLeaderRepro`）重新定位了根因：

- 根因不是"宿主硬编码行为"，而是**顶点链不完整**：旧实现只提供 attach→dogleg 两点，MLeader 自行计算文字着陆点，产生回折线段（鱼钩）。
- 把**文字点作为最后一个顶点**加入顶点链后（F 方案），引线路径与用户点击点完全一致。

本复线（fork）以 F 方案为标注引擎，独立于主线演进；先在 AutoCAD 2025/2026 版（`cad-plugin/2025`，.NET 8）跑通 MVP，再评估向 2013/2015 版移植。

**English:**
The mainline annotation engine composes `Leader + MText` as two separate entities linked via extension dictionaries. MLeader was rolled back on 2026-08-06 due to the "fishhook" distortion; the 2026-08-15 form probe (`tools/MLeaderRepro`) re-identified the root cause:

- The cause was NOT hardcoded host behavior but an **incomplete vertex chain**: the old implementation supplied only attach→dogleg, so MLeader computed its own text landing point and produced a hooked-back segment.
- When the **text point is appended as the final vertex** (Plan F), the drawn leader path matches the user-picked points exactly.

This fork adopts Plan F as its annotation engine and evolves independently. The MVP targets the AutoCAD 2025/2026 edition (`cad-plugin/2025`, .NET 8) first; porting to 2013/2015 is evaluated afterwards.

---

## 2. F 方案定义 / Plan F Definition

**创建序列（Create sequence）：**

```csharp
// 1) 全禁用自动几何（样式级 + 实体级双保险）
EnableDogleg = false;  EnableLanding = false;  ExtendLeaderToText = false;
DoglegLength = 0;      LandingGap = 0;
TextAttachmentDirection = AttachmentHorizontal;
TextAttachmentType = AttachmentMiddle;
TextAngleType = HorizontalAngle;
LeaderLineType = StraightLeader (或 SplineLeader，随面板开关);

// 2) 三点顶点链：attach → dogleg → text（关键差异）
int line = ml.AddLeaderLine(attachPt);
ml.AddLastVertex(line, doglegPt);
ml.AddLastVertex(line, textPt);      // ← 文字点进顶点链

// 3) 文字挂接（先顶点后文字，顺序即探针验证顺序）
ml.MText = mt;  ml.TextLocation = textPt;  ml.TextHeight = h;
```

**无限点模式（Unlimited mode）：** `attach → dogleg₁ → … → doglegₙ → text`，文字点始终是最后顶点（与主线 `AppendTextEndpoint` 同义；text 与最后拐点重合时跳过重复顶点）。

**English summary:** all auto-geometry off (style + entity level), then the vertex chain is `attach → dogleg(s) → text` where the text point is always the LAST vertex; text is attached via `ml.MText` + `TextLocation` after the vertices.

---

## 3. 实证依据 / Empirical Evidence

探针 `MLFORM`（AutoCAD 2026, v25.1）用 `Explode()` 提取实际绘制几何，`MoveGripPointsAt` 模拟 UI 拖拽：

| 场景 / Case | 结果 / Result |
|---|---|
| F 创建后不动 | 3 段：attach→dogleg ✅、dogleg→文字方向 ✅、~4.08 着陆小尾巴；**无多点附着**（距文字 1×字高内线段数=0） |
| F 同侧拖文字 (+15,+5) | 2 段，尾巴消失，单端点距文字 1.75（=字高一半，贴文字左中）✅ |
| F 跨侧拖文字 (-45,+15) | 3 段，路径仍经过 dogleg ✅，尾巴重现（~4.08） |
| B0 旧两点链（对照） | 鱼钩形态：S0 直插文字、S1 回折 -173.8° ❌（2026-08-06 回退依据，已失效） |
| Z Leader+MText（对照） | 顶点连线即所见 ✅ |

**已知残留（Known residual）：** 创建后（及跨侧拖拽后）存在一条 ~4.08 单位的水平着陆小尾巴（约为文字宽度的下缘线段）。属性重赋 `TextLocation` 无法消除（F2/F3 验证）；用户拖动文字一次即消失（T3a 验证）。MVP 接受该残留并如实记录；程序化消除留作后续课题（候选：创建后 `MoveGripPointsAt` 零位移重算）。

---

## 4. MVP 范围 / MVP Scope

**In（本 fork 首个可用版本）：**
1. `PATMARK`/`BZM` 以 MLeader（F 方案）创建标注——交互流与主线完全一致（附着点 → 循环拐点 → 文字点，Enter=最后拐点）。
2. 面板开关全部生效：`Arrow Off/On`（Off 用空箭头块 `_PAT_NO_ARROW` 实现）、`Line Type Spline/Straight`、箭头大小、字高、下划线、无引线纯文字模式（纯文字仍为独立 MText，不变）。
3. `PATSELECTALL`/`BZS` 识别 PAT MLeader（扩展字典标记 `PATENTMARKER_MLEADER`，含用户点链记录），同时兼容旧图纸的 Leader 标注与独立文字。
4. `PATMLVERIFY` 诊断命令：Explode 全部 PAT MLeader，对照记录的用户点链输出形态报告（本 fork 的回归测试工具）。
5. 文字样式沿用 `PatentTimesNewRoman`；MLeader 样式新建 `PAT_MLEADER`（全禁用）。

**Out（后续版本）：**
- `PATCHECK`/`BZC`、`PATALIGN`/`BZA` 的 MLeader 适配（首版沿用现有逻辑，对 MLeader 不生效）。
- 花括号标注（PatBrace）、字典面板双击创建等不受影响的命令——天然兼容，无需改动。
- 2013/2015 版移植（`acdbmgd` API 同构，.NET 4.x）；2007/2010 版**不在计划内**（2007 无 MLeader 实体）。
- 着陆尾巴的程序化消除。

---

## 5. 架构策略 / Architecture

```
主线 mainline:  Shared/Commands/PatMarkCommand.cs ──(链接)──> 5 个版本
复线 fork:      2025 版改用版本本地文件（csproj 移除 Shared 链接、本地文件自动包含）:
                cad-plugin/2025/PatentMarker/Commands/
                  ├─ PatMarkCommand.cs          ← 本地替换版（交互流同源，创建走 MLeader）
                  ├─ PatMLeaderCreator.cs       ← F 方案核心：样式/顶点链/箭头块/标记
                  ├─ PatSelectAllCommand.cs     ← 本地替换版（+MLeader 识别）
                  └─ PatMLeaderVerifyCommand.cs ← PATMLVERIFY 形态诊断
```

**为什么不用 Shared 层 / Why not the Shared layer:** MLeader 实体与托管 API 在 AutoCAD 2007 中不存在，2007/2010 版（.NET 2.0/3.5）无法编译 MLeader 代码；Shared 单源层必须保持 5 版本可编译。因此复线在 2025 版使用"csproj 级替换"（移除 Shared 链接 + 版本本地实现），其余 4 版本在 fork 中继续使用主线 Leader+MText 代码，保持可编译、可对照。

**English:** MLeader does not exist in AutoCAD 2007, so the Shared single-source layer (buildable by all 5 editions) cannot reference it. The fork swaps the 2025 edition to version-local command files via csproj changes; the other four editions keep compiling the mainline Leader+MText sources inside this fork.

---

## 6. 验收标准 / Acceptance Criteria

MVP 视为跑通，当且仅当在真实 AutoCAD 2026 宿主中：

1. `NETLOAD` 后 `PATMARK` 脚本化创建 MLeader 标注成功（三点模式 + 无限点模式各至少 1 例）。
2. `PATMLVERIFY` 报告确认：绘制几何起点=附着点（<0.5）、每个拐点在绘制路径上（<0.5）、文字位置=请求位置、单实体（无独立 MText）。
3. 无"多点附着"：距文字 1×字高内的线段端点数 ≤1。
4. 箭头 Off 时 Explode 无箭头 Solid；On 时有。
5. 样条开关切换后 `LeaderType` 相应变化。
6. `PATSELECTALL` 能选中新建 MLeader。

---

## 7. 风险与对策 / Risks

| 风险 | 对策 |
|---|---|
| 着陆尾巴影响观感 | 文档明示；拖动即消；后续探索零位移重算 |
| 旧图纸 MLeader（默认样式）形态异常 | 不迁移旧实体；`PATSELECTALL` 只认带 PAT 标记的 MLeader |
| 跨版本 API 差异（2013/2015 移植） | 移植前用探针复跑 MLFORM 矩阵；`docs/mleader-attachment-grip-incident.md` 的 API 对照表仍适用 |
| 与主线共享代码漂移 | fork 内 2007–2015 版仍链接 Shared 层；2025 本地文件头部注明来源与差异点；`check-version-sync.ps1` 需按复线差异放行 2025 版两条链接缺失（TODO） |

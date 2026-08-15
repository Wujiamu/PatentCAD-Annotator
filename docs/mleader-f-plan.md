# MLeader 标注方案（F 方案）/ MLeader Annotation Plan (Plan F)

> 状态 / Status: **已合并主线**（2010/2013/2015/2025 四版本，AutoCAD 2026 实测 4/4 PASS，2026-08-16）
> Merged into mainline (editions 2010/2013/2015/2025; verified 4/4 PASS on AutoCAD 2026, 2026-08-16)

---

## 1. 背景与目标 / Background & Goal

**中文：**
主线项目（PatentCAD-Annotator）的标注引擎原采用 `Leader + MText` 两个独立实体拼合，通过扩展字典维护关联。2026-08-06 曾因 MLeader"鱼钩形态"问题回退该方案（见 [mleader-attachment-grip-incident.md](mleader-attachment-grip-incident.md)）；2026-08-15 的形态探针（`tools/MLeaderRepro`）重新定位了根因：

- 根因不是"宿主硬编码行为"，而是**顶点链不完整**：旧实现只提供 attach→dogleg 两点，MLeader 自行计算文字着陆点，产生回折线段（鱼钩）。
- 把**文字点作为最后一个顶点**加入顶点链后（F 方案），引线路径与用户点击点完全一致。
- 无箭头时 `ArrowSize` 必须为 0：非零 ArrowSize 即使配空箭头块（`_PAT_NO_ARROW`），也会把引线起点修剪掉 ArrowSize，导致引线不触及零件。

2026-08-16 F 方案合并进主线：2010/2013/2015/2025 四个版本的新建标注统一改为 MLeader（F 方案）；2007 无 MLeader API，保持 `Leader + MText`。

**English:**
The mainline annotation engine used to compose `Leader + MText` as two separate entities linked via extension dictionaries. MLeader was rolled back on 2026-08-06 due to the "fishhook" distortion; the 2026-08-15 form probe (`tools/MLeaderRepro`) re-identified the root cause:

- The cause was NOT hardcoded host behavior but an **incomplete vertex chain**: the old implementation supplied only attach→dogleg, so MLeader computed its own text landing point and produced a hooked-back segment.
- When the **text point is appended as the final vertex** (Plan F), the drawn leader path matches the user-picked points exactly.
- `ArrowSize` must be 0 when the arrow is off: a non-zero ArrowSize trims the leader start by ArrowSize even with the empty arrow block (`_PAT_NO_ARROW`), leaving the leader detached from the part.

On 2026-08-16 Plan F was merged into the mainline: editions 2010/2013/2015/2025 create annotations as MLeader (Plan F); 2007 has no MLeader API and keeps `Leader + MText`.

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
ArrowSize = HasArrowHead ? settings.ArrowSize : 0.0;   // 无箭头必须为 0
ArrowSymbolId = HasArrowHead ? ObjectId.Null : NoArrowBlock;

// 2) 顶点链：attach → dogleg → text（关键差异）
int line = ml.AddLeaderLine(attachPt);
ml.AddLastVertex(line, doglegPt);
ml.AddLastVertex(line, textPt);      // ← 文字点进顶点链

// 3) 文字挂接（先顶点后文字，顺序即探针验证顺序）
ml.MText = mt;  ml.TextLocation = textPt;  ml.TextHeight = h;
```

**无限点模式（Unlimited mode）：** `attach → dogleg₁ → … → doglegₙ → text`，文字点始终是最后顶点（与主线 `AppendTextEndpoint` 同义；text 与最后拐点重合时跳过重复顶点）。

**跨版本 API 适配（Cross-version adaptation）：** `ExtendLeaderToText` 为 2014+ SDK 属性（2010-2012 无此成员），统一经 `PatMLeaderCreator.SetExtendLeaderToText / GetExtendLeaderToText` 反射访问，保持四版本单一代码；不支持时静默跳过（其默认行为即不延伸）。命令文件整体保持 .NET 3.5 兼容语法（不使用 `string.IsNullOrWhiteSpace` 等 4.0+ API）。

**English summary:** all auto-geometry off (style + entity level), then the vertex chain is `attach → dogleg(s) → text` where the text point is always the LAST vertex; text is attached via `ml.MText` + `TextLocation` after the vertices. `ExtendLeaderToText` is a 2014+ SDK property and is accessed via reflection so one source file serves all four editions.

---

## 3. 实证依据 / Empirical Evidence

探针 `MLFORM`（AutoCAD 2026, v25.1）用 `Explode()` 提取实际绘制几何，`MoveGripPointsAt` 模拟 UI 拖拽：

| 场景 / Case | 结果 / Result |
|---|---|
| F 创建后不动 | 路径与点击点一致；**无多点附着**（距文字 1×字高内线段数=0） |
| F 同侧拖文字 (+15,+5) | 单端点距文字 1.75（=字高一半，贴文字左中）✅ |
| F 跨侧拖文字 (-45,+15) | 路径仍经过 dogleg ✅ |
| B0 旧两点链（对照） | 鱼钩形态：S0 直插文字、S1 回折 -173.8° ❌（2026-08-06 回退依据，已失效） |
| Z Leader+MText（对照） | 顶点连线即所见 ✅ |

**生产版回归（PATMLVERIFY，AutoCAD 2026 批处理实测 2026-08-16）：** 三点直线（无箭头）、三点样条+箭头、无限模式 1 拐点、无限模式 2 拐点，4/4 PASS（C1 附着点、C2 拐点在路径、C3 文字位置、C4 单附着、C5 箭头一致性、C6 直线模式几何载体）。

**已知残留（Known residual）：** 创建后（及跨侧拖拽后）可能存在一条 ~4.08 单位的水平着陆小尾巴；用户拖动文字一次即消失。MVP 接受该残留并如实记录；程序化消除留作后续课题。

---

## 4. 范围 / Scope

**已实现（In mainline since 2026-08-16）：**
1. `PATMARK`/`BZM` 以 MLeader（F 方案）创建标注——交互流与原 Leader+MText 版完全一致（附着点 → 循环拐点 → 文字点，Enter=最后拐点）。
2. 面板开关全部生效：`Arrow Off/On`（Off 用空箭头块 `_PAT_NO_ARROW` + `ArrowSize=0`）、`Line Type Spline/Straight`、箭头大小、字高、下划线、无引线纯文字模式（纯文字仍为独立 MText）。
3. `PATSELECTALL`/`BZS` 识别 PAT MLeader（扩展字典标记 `PATENTMARKER_MLEADER`，含用户点链记录），同时兼容旧图纸的 Leader 标注与独立文字。
4. `PATMLSET` 调试命令（ThreePoint/Spline/Arrow 开关的脚本化入口）与 `PATMLVERIFY` 形态诊断命令：Explode 全部 PAT MLeader，对照记录的用户点链输出报告（回归测试工具）。
5. 覆盖 2010/2013/2015/2025 四版本（.NET 3.5/4.0/4.5/8.0），四版本命令文件字节级相同。

**后续（Backlog）：**
- `PATCHECK`/`BZC`、`PATALIGN`/`BZA` 的 MLeader 适配（当前沿用现有逻辑，对 MLeader 不生效）。
- 着陆尾巴的程序化消除（候选：创建后 `MoveGripPointsAt` 零位移重算）。
- 2007 版不在计划内（无 MLeader 实体，保持 Leader + MText）。

---

## 5. 架构 / Architecture

```
Shared 层（5 版本链接）:  Shared/Commands/PatMarkCommand.cs、PatSelectAllCommand.cs
                          └─ 仅 2007 版编译（Leader + MText 基线）
MLeader 版本组（4 版本本地，字节级相同）:
  cad-plugin/{2010,2013,2015,2025}/PatentMarker/Commands/
    ├─ PatMarkCommand.cs          ← 版本本地替换版（交互流同源，创建走 MLeader）
    ├─ PatMLeaderCreator.cs       ← F 方案核心：样式/顶点链/箭头块/标记/反射适配
    ├─ PatMLeaderSetCommand.cs    ← PATMLSET 开关脚本化入口
    ├─ PatMLeaderVerifyCommand.cs ← PATMLVERIFY 形态诊断
    └─ PatSelectAllCommand.cs     ← 版本本地替换版（+MLeader 识别）
```

**为什么不用 Shared 层 / Why not the Shared layer:** MLeader 实体与托管 API 在 AutoCAD 2007 中不存在，Shared 单源层必须保持 5 版本可编译，因此 MLeader 实现放在 2010/2013/2015/2025 的版本本地 `Commands/` 目录，由 `check-version-sync.ps1` 的 **MLeader 组校验**强制四版本字节级一致（2007 不得携带这些文件，且必须继续链接 Shared 的 Leader+MText 版本）。

**English:** MLeader does not exist in AutoCAD 2007, so the Shared single-source layer (buildable by all 5 editions) cannot reference it. The MLeader implementation lives in version-local `Commands/` folders of the four editions; `check-version-sync.ps1` enforces byte-identical files across them (2007 must not carry them and must keep linking the Shared Leader+MText versions).

---

## 6. 验收标准 / Acceptance Criteria

MVP 视为跑通，当且仅当在真实 AutoCAD 2026 宿主中：

1. `NETLOAD` 后 `PATMARK` 脚本化创建 MLeader 标注成功（三点模式 + 无限点模式各至少 1 例）。
2. `PATMLVERIFY` 报告确认：绘制几何起点=附着点（<0.5）、每个拐点在绘制路径上（<0.5）、文字位置=请求位置、单实体（无独立 MText）。
3. 无"多点附着"：距文字 1×字高内的曲线端点数 ≤1。
4. 箭头 Off 时 Explode 无箭头 Solid（且引线触及零件）；On 时有 Solid。
5. 样条开关切换后 `LeaderType` 相应变化。
6. `PATSELECTALL` 能选中新建 MLeader。

**2026-08-16 结果：4/4 场景 PASS（SUMMARY: total=4 passed=4 failed=0）。**

---

## 7. 风险与对策 / Risks

| 风险 | 对策 |
|---|---|
| 着陆尾巴影响观感 | 文档明示；拖动即消；后续探索零位移重算 |
| 旧图纸 MLeader（默认样式）形态异常 | 不迁移旧实体；`PATSELECTALL` 只认带 PAT 标记的 MLeader |
| 四版本命令文件漂移 | `check-version-sync.ps1` MLeader 组校验强制字节级一致（CI `-Static` 层执行） |
| 2010-2012 无 `ExtendLeaderToText` | 反射访问，不支持时静默跳过（默认行为即不延伸） |
| 直线模式 Explode 产物因版本而异（Line vs Polyline） | `PATMLVERIFY` C6 断言两者均为合法直线几何载体 |

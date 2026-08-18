# Changelog

本项目的对外版本管理自 **1.0.0** 开始。早于 1.0.0 的 v2.0–v5.3 均为发布前开发里程碑，已并入 1.0.0 的功能总览（见根 `README.md` 的"版本历史"），不再作为独立对外版本号呈现。

This project's public versioning starts at **1.0.0**. Internal iterations v2.0–v5.3 are pre-release milestones merged into the feature overview of 1.0.0 (see the "Version history" section of the root `README.md`) and are no longer exposed as standalone public releases.

Adopts [Semantic Versioning](https://semver.org/).

---

## [1.0.0] - 2026-08-18

**首个正式版本。** 首个对外稳定发布，覆盖 AutoCAD 2007—2026+，Word 说明书 → 附图标记字典 → CAD 一键标注的全流程闭环。

**First official release.** Initial stable public release covering AutoCAD 2007–2026+, with a full closed loop from Word specs → reference-numeral dictionary → one-click CAD annotation.

### 功能总览 / Feature overview

- **Word 端自动导出** / **Word-side auto export**：Word 保存时自动提取附图标记，生成 `.dict.json`（1.0.0 起为隐藏+系统属性文件，资源管理器默认不可见；文件夹拷贝/共享不受影响）。
- **CAD 引线标注** / **CAD leader annotation**：BZM 一键创建标准引线标注，支持样条/直线、箭头开关与尺寸调节、三点/无限点模式。
- **字典双向同步与比对** / **Dictionary two-way sync & diff**：字典变更自动高亮新增/删除/编号变/名称变；CAD 端支持直接编辑、粘贴识别、冲突裁决。
- **漏标检测与对齐** / **Unmarked check & align**：BZC 报告"字典有·图纸未标注"清单；BZA 基于选择集的线/框对齐，空间不足时自动延伸。
- **参数化矢量大括号** / **Parameterized vector brace**：Brace 三点创建、控制点或尺寸调整。
- **自检诊断** / **Self-check diagnostics**：BZD 一键生成插件状态与最近错误报告。
- **多版本适配** / **Multi-edition support**：5 个版本覆盖 AutoCAD 2007—2026+（版本矩阵见根 `README.md`）。

### 里程碑时间线 / Milestone timeline (pre-1.0.0, for reference)

| 里程碑 / Milestone | 日期 / Date | 关键内容 / Key content |
|----------------------|-------------|-------------------------|
| M5 (v5.2–5.3) | 2026-08-18 | 引线末端与文字间距、字典文件隐藏化+孤儿自动清理 |
| M4 (v4.9–5.1) | 2026-08-15/16 | 面板单一入口、PATCHECK 漏标检测、PATALIGN v2、MLeader F 方案、2026 实机测试 |
| M3 (v4.5–4.8) | 2026-08-15 | PATDOCTOR 诊断、共享层收敛、五套部署包重打包 |
| M2 (v4.0–4.1) | 2026-08-06/11 | CAD 端字典编辑闭环、矢量大括号 |
| M1 (v2.0–3.2) | 2026-07 至 08-04 | 2007 版完成、多版本适配、多格式识别 |

<!-- Compare / 比较： `1.0.0` 是首个已发布版本，无早期可比版本。 -->
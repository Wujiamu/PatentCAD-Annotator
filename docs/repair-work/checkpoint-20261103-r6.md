# 修复任务接续记录（第六轮，2026-11-03）

基线 HEAD：`b945b1a68fec26ada268b5a4ee3ac066386e96ab`，叠加前五轮已集成内容与用户原有未提交改动。本轮未创建任何提交，未推送。

## 本轮范围

按 `docs/repair-plan.md` 第 14 节，只有 **1 项**：R-004／G28。前 114 项面板断言、G24/G25 的 160/100 项交付矩阵、2025 全套 302 项、两套完整隔离安装矩阵、默认/显式 TRX 留存均被复核者独立重跑通过。

## 缺陷与修复

`ReadSnapshot` 分两次从磁盘取同一份内容：先 `File.ReadAllBytes` 算身份，再 `DictLoader.Load(path)` 按路径重读一遍解析。两次读取之间文件被替换，就得到「A 的身份 + B 的模型」；后续以 A 的身份做 CAS，却把 B 的条目写进 A。方法自己的 XML 注释当时已写着「一次 `ReadAllBytes` 同时产生身份和模型」，实现没做到。

修复是一行：把已读到的字节传下去。

```csharp
DictModel model = DictLoader.LoadBytes(dictPath, bytes);
```

`LoadBytes` 是五版 `DictEntry.cs` 已有的 `internal static`，`Load(path)` 本身就是 `LoadBytes(path, null)` 的薄包装。

## 证据

用复核者自己的快照探针（`-p:WorkflowSource=` 指向被测文件），同一断言、同一判据：

| 运行 | 源码 | 结果 |
|---|---|---|
| 红 | 修复前快照（第 98 行 `DictLoader.Load`） | **25 FAIL / 60 PASS，exit 1** |
| 绿 | 当前生产 | **85 PASS / 0 FAIL，exit 0** |

红灯关键项：`A-to-B-to-A-model-A`（`expected=999 actual=302`）、`A-to-B-to-A-no-phantom-overwrite`（`writer-succeeded=True original-bytes-preserved=False`）、`bad-json/A-to-B-parsed`（`expected=False actual=True`）。

## 一处必须说清楚的局限

**A→B 交错无法在 L1 用真实加载器确定性复现**——它要求文件在「第一次读」与「第二次读」之间改变，单线程做不到；`ReadSnapshot` 自己会先 `ReadAllBytes`，在外面加独占锁只会让两次读都失败，不构成判据。

我一度写了两条试图确定性复现的单元测试，实测**修复前后都通过**：它们断言的是底层 `LoadBytes` 与 `Load` 的差异，而不是 `ReadSnapshot` 的行为。这类测试留着就是假绿灯，已删除。缺陷捕获证据因此来自复核者的钩子探针（在证据副本内注入时序），生产侧只保留能真正验证契约的等价断言。

## 最终门禁

- 2025 全套 **312 PASS / 0 FAIL**（302 → +10）
- 四组模拟 83 / 96 / 96 / 96 PASS
- Office VSTest：Visio **48/48**、PPT **34/34**
- 五版编译（含 .NET 2.0）通过；`build.ps1 -Static` 退出 0
- 四项同步检查全部通过
- VBA L2 export PASS；安装矩阵 PPT **127/0**、Visio **126/0**
- **真实 Word 16.0 L4** 退出 0，`auto_export=true`、`normal_unchanged=true`，dotm `21DAC67F…`

## 仍未关闭

- 真实 CAD 面板操作、`DocumentLock`／`Transaction`、重启持久化。
- 读取与随后的 CAD 事务之间仍有窗口（既有「原子替换 ≠ 双写锁」边界，本轮不声称解决并发丢更新）。
- 四组旧宿主 GitHub 外部验收未回传；AutoCAD 2025 本体未验；32 位 Office、其他 Word 版本。
- G28 为自测，缺独立验收角色。

## 交付物

- 交接：`docs/repair-work/G28.md`
- 证据：`docs/test-evidence/repair-review-20261102-r3/G28/`（`red-snapshot-probe.txt`、`green-snapshot-probe.txt`）
- 执行登记：`docs/repair-plan.md` §14.2

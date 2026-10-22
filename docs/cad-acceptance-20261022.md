# R9 修复后验收报告（2026-10-22）

整体 **FAIL**：23项 findings中22项限定范围PASS，新增R9-ACL-01未关闭。Win10本机自动校验PASS；GitHub独立Windows环境135/136、1项FAIL；当前本机交互验收SKIP/BLOCKED。

本机Win10自动边界、真实命令与冷启动校验通过；GitHub独立Windows环境发现未关闭的ACL继承反例R9-ACL-01，因此整体FAIL。当前视觉复核另受官方截图超时阻断。2007/2010/2013/2015四组本机不存在，仅交GitHub外部验收，绝不列为本机待测。历史GUI证据按原候选身份保留，不冒充新候选GUI通过。

- Win10本机377/377唯一测试、零跳过：136项2025；58/61/61/61项模拟宿主。此数字不外推GitHub环境；重复次数不累计。
- 五版SDK编译、API门禁和Static通过。SDK编译与模拟运行不算四旧版真实宿主通过。
- 最终候选真实CAD2026两个全新进程：A103/103、B24/24，PATDOCTOR各7/0/0；实载SHA/MVID匹配。
- 本机实际候选NTFS14场景×3通过；保护DACL的ACL证据不能关闭GitHub未保护继承ACL失败，见R9-ACL-01。
- 真实CoreConsole24次规模/负例与24个外部门禁一致；6次顶层常规异常注入安全返回，错误/缺失报告均拒绝。
- 当前官方截图两次超时，未输入画布、未打开GUI测试图；本轮A/B目检与有位移热夹点预览取消仍BLOCKED。历史R8 GUI不冒充新候选通过。
- 所有临时安装恢复155/155，无后台监看，不改图形/安全设置。

## 本轮红绿闭环与开放反例

|ID|缺陷与修复|同一反例结果|
|---|---|---|
|R9-IO-01|DictWriter / shared atomic writer|红3次→绿3次；PASS|
|R9-BACKUP-01|DictConflict cleanup/recovery|红3次→绿3次；PASS|
|R9-GEOM-01|PatBraceAssertions|红3次→绿3次；PASS|
|R9-GEOM-02|PatBraceGeometry finite inputs / bounded sampling|红3次→绿3次；PASS|
|R9-GATE-01|verify-geometry empty report acceptance|红3次→绿3次；PASS|
|R9-ACL-01|DictWriter / Windows2025 inherited ACL preservation|GitHub相同判定重复FAIL；未关闭|

完整精确选择器、预期/实际、角色和证据双哈希见[机器可读汇总](acceptance-summary-20261022.json)。原始失败日志保留；预期被拒绝的负例报告继续为FAIL，不修改预期来取得PASS。

## 候选身份

|组|DLL SHA-256 / MVID|执行位置与结论|
|---|---|---|
|AutoCAD 2007|`0f57c0d2309bf00d8d3179453836358fda37177f3c0bc4547f93fbf3618a38c8 / 10da5c59-4fc3-4f85-b819-5b7e8d950241`|github_external_only / SKIP/BLOCKED|
|AutoCAD 2010|`c709eaf1e13a979fb1b264b96dfc774f6add5850a110421b2e86a22477117a50 / 36686f29-3165-43cd-a919-93583227bed9`|github_external_only / SKIP/BLOCKED|
|AutoCAD 2013|`730eb00153ca234157a8e25f5f139634e9b6d636b9505b1cc24e10eda7028840 / 095c098d-f237-4470-92ca-f35f5ab0d16f`|github_external_only / SKIP/BLOCKED|
|AutoCAD 2015|`33de9e57b677cd59a39d3dde744de110b2bf7a13e973615c8c55b64ef2527e3d / 2f7c67a8-d342-4ca6-bbec-dc8854900672`|github_external_only / SKIP/BLOCKED|
|AutoCAD 2026 x64 R25.1.60.0.0|`7aa122c8551c8ce0ae4b640e6f9e1a198fb971840ebc2294ababdd98de242f21 / ce03cc0c-4f1b-4d9e-a243-b877cf29b2e3`|local / SKIP/BLOCKED|

构建来自归档dirty源码，原commit01ed4ca；上传后commit不是原构建commit。源码snapshot `70f3af5acd363637b6084c05a890ceed919c88171a6c1e05b654cdf78a189cb7`，构建源码snapshot `300c8fdd1cf7fb2b2af23d37a503535d27877c32f05549035b6f499a2a9e0678`。

## 规模和性能

|场景|实体数|每实体顶点|三次 min/median/max ms|报告bytes|
|---|---|---|---|---|
|vertices-100|1|100|3.688/4.03/40.878|1885|
|vertices-1000|1|1000|11.481/11.59/13.981|1886|
|vertices-5000|1|5000|109.326/114.179/124.939|1889|
|entities-100|100|35|22.909/33.332/38.858|87233|
|entities-1000|1000|35|258.786/271.213/333.638|863039|
|entities-5000|5000|35|1156.062/1178.914/1577.946|4311039|

Actual AutoCAD2026 CoreConsole production PATGEOMVERIFY including report generation, three repetitions. Exact even vertex cases include one coincident straight-stem junction. Largest measured5000 default35-vertex entities; process peak402.1MiB, maximum report4,311,039bytes. Stopwatch includes production verification/report write but excludes process startup. Process peak is cumulative, not per-case allocation. Generation cap4096samples/16387vertices; reference257points per corner. No hard entity cap or guarantee beyond measured sizes, and no5000x16387 cross-product claim.

## 剩余风险与外部交接

- **R9-ACL-01**：Known unprotected ACL inheritance counterexample in independent GitHub Windows environment; local NTFS protected-DACL tests do not close it.
- **sampled-polyline-boundary**：Closed/bulged/nonfinite input rejected. Samples2 remain allowed by the current polyline approximation; no continuous-curvature proof between sampled vertices.
- **GUI-final-capture**：Current final-candidate interactive A/B reopen, visual layout and displaced-hover cancel unavailable because official sky capture failed twice; no guessed coordinate input. Native cold-read passes do not replace it.
- **native-grip-recorded-anchor**：R8 native grip movement may leave creation Xrecord anchors stale; T3a/C3 correctly reject them. No automatic Xrecord synchronization is claimed.
- **text-crossing-user-path**：User-picked incoming path may cross text despite valid terminal gap. C8 detects retained negative input; no automatic rerouting is implemented.
- **filesystem-scope**：NTFS ACL/ADS/creation/compression/fullDOSattributes tested. EFS not provisioned/tested; no certificate/security changes. Replace fails closed, and no double-writer lock or JSON-DWG transaction is claimed.
- **backup-post-commit-cleanup**：Verified partial-success boundary: target may already be restored when backup cleanup fails; backup bytes/attributes retained and retry works. Not a rollback guarantee.
- **scale-beyond-measurement**：Native measured5000vertices or5000defaultentities. No combined worst-case or unlimited-size/time/memory guarantee; verifier has quadratic geometry checks.
- **external-host-editions**：Four legacy groups are GitHub external execution only. Not local unfinished work; no claim that build/simulation/CI is actual legacy runtime PASS.
- **independence-limit**：Separate non-author reviewer and operator, same account/machine/model family. No unaffiliated human auditor. Native top-level seam faults are L2, not ordinary user-path evidence.
- **absolute-process-survival**：Ordinary injected managed faults contained. Native AV/stack overflow/process termination not guaranteed; hostile custom Exception.Message getter is source-derived theoretical edge, not observed/injected user failure.

四个旧宿主只在GitHub交接后由对应真实环境执行：[外部验收步骤](legacy-host-acceptance.md)。本机不再探测、安装或启动它们。

角色：Same machine/account; independently frozen pre-R9 published source plus identical new tests, separately built final source archive. Repair author performed fixes and primary acceptance; a separate operator reviewed crash/gate evidence and attempted GUI. This is not an unaffiliated external audit.

公开材料是脱敏子集，不假称完整本地6256+文件均已上传。原始/公开SHA映射、完整测试选择器、未执行项见JSON；完整本地报告由canonical findings生成并另行封存。

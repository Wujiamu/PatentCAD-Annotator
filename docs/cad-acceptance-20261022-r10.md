# R10 ACL修复后验收（2026-10-22）

**R9-ACL-01已修复；23项缺陷在各自证据范围内PASS，无开放FAIL。** 全支持范围仍为SKIP/BLOCKED：当前官方截图故障使GUI三项未执行；四个旧宿主只交GitHub外部真实环境。

- 原GitHub Windows2025精确SDDL标准保留；修复前135/136失败，修复后push/PR各139/139通过，两轮共10检查成功。
- 本机380项唯一测试零跳过，五集合各连续三轮通过；58/61/61/61为模拟宿主测试，不代表旧CAD实机。
- 同一实际DLL探针16项NTFS矩阵：旧候选新增两项失败，最终候选16/16连续三轮通过。
- 最终安装候选的全新AutoCAD2026进程A103/103、B24/24；PATDOCTOR均7PASS/0FAIL/0SKIP，实载路径/SHA/MVID正确。
- Core显式NETLOAD正例通过；原始错误图被拒绝。两个原生进程均正常退出。
- 官方截图初次及一次恢复均超时；未开GUI图，不把命令测试当GUI通过。正常退出，安装恢复155/155；无监看、无图形/安全/服务更改。

## 红绿闭环

|场景|修复前|修复后|
|---|---|---|
|原精确ACL/ADS/创建时间测试|GitHub135PASS/1FAIL|两轮各139PASS/0FAIL/0SKIP|
|旧式继承精确SDDL与修复被拒绝|旧源码两项FAIL，连续3轮|新源码139/139连续3轮|
|实际DLL同16项NTFS矩阵|旧DLL两项FAIL，连续3轮|最终DLL16/16连续3轮|
|父目录撤权传播|新增邻界检查，无既有红证据|继承权限随父目录撤销，不固化为显式ACE|

修复在PatSettings共享原子写入：Replace保留原文件备份，校验Owner/Group/DACL完全一致；仅原描述无AUTO_INHERITED且被转换时，以DACL-only API恢复旧式状态。恢复被拒绝则将原文件对象移回，核对旧字节、属性、ADS、创建时间和SDDL；不以覆盖新数据的Move兜底。

完整选择器、命令/退出码、原始与脱敏双哈希见[机器汇总](acceptance-summary-20261022-r10.json)。原R9失败证据仍见[历史报告](cad-acceptance-20261022.md)。

## 最终候选

构建源码为clean commit `1d56652ef54ad3772ee2e16d26b51849843ae82f`。上传文档提交不冒充构建提交。

|组|DLL SHA-256 / MVID|真实宿主位置/状态|
|---|---|---|
|AutoCAD 2007|`26825d9dff2e5aea7c5629acbbd0bc20938552a5f576e5356192c433c5a20a61 / 3acd2494-908b-4d81-9858-2f8ff0a92e74`|github_external_only / SKIP/BLOCKED|
|AutoCAD 2010|`6cc639f1c6838b71462afe2b8f6244c617ab540b3903c159a7bdc4e4315613f1 / 89c9caa1-5050-4c30-baf7-01e988d4e40e`|github_external_only / SKIP/BLOCKED|
|AutoCAD 2013|`8104016abcfa40387bdf87a4f52c76bbd5061241ea41dc80c45057bf082d263b / 19666f5f-758b-4cee-863d-99b1b80f0051`|github_external_only / SKIP/BLOCKED|
|AutoCAD 2015|`7f622ccbdb9bb4a70888e5dd963151d36e82d812beed2eefec05203abcd6df44 / 0a0b071b-a778-471d-baad-b22765010860`|github_external_only / SKIP/BLOCKED|
|AutoCAD 2026 x64 R25.1.60.0.0|`5b65191eb6ab9b8666df0c9a173dac83047d2169fe6e962e8f3c6598327f4460 / e8eb2160-24dd-4f27-84c9-1aa6035d9187`|local / SKIP/BLOCKED|

## 剩余范围

- GUI：G10A/B交互保存关闭重开与布局、G06热夹点位移预览后Esc取消，均未执行；历史R8 GUI保持其旧候选身份。
- 原生夹点可能使创建参数记录过期；验证器拒绝，不声称已自动同步。用户点链穿过文字时不自动改道。
- NTFS已测试；EFS、网络文件系统、双写锁、进程崩溃事务恢复及回滚本身再次拒绝未证明。
- 历史R9性能/故障注入保留原身份；本轮仅IO修复，不冒充最终DLL的新规模测试。
- [四旧组验收步骤](legacy-host-acceptance.md)只能在GitHub交接后的真实目标环境执行，本机不探测、不安装。

角色：Same machine/account. Original R9 source/DLL and original GitHub counterexample retained. Identical new local legacy-DACL fixtures run against old/new production, actual old/new DLLs tested separately, final source built from clean commit1d56652 and independently rerun on GitHub Windows2025 for both events. A separate non-author operator attempted GUI; screenshot failed before drawing opened. This is repair-author acceptance with independent CI, not an unaffiliated audit.

公开材料为脱敏子集；完整本地findings由统一工具生成报告并单独封存，不能用公开子集冒充完整机器放行。

# R9 独立崩溃边界复核（更新）

**PASS：本项独立复核执行完成。** 结论限于常规托管异常边界、空业务报告门禁与给定冻结证据；本机 GUI 阻断和 GitHub 外部旧宿主验收属于其他事项。

复核者是同机器、同账号 desktop-3vj2a3b\wjm 的独立操作子代理，未编写修复，不是外部人工审计。仅阅读源码与已生成证据，未启动宿主、输入桌面、修改生产代码。首轮 review 已另存时间戳历史副本。

- 空 PASS 门禁已闭合：第106行新增零业务目标拒绝，新增 empty-PASS 自测。同一 forged report 的三次红证据为旧脚本 exit0；三次绿证据为新脚本 exit1，明确输出 `report has no applicable business targets`。三个自测均 exit0。绿证据所用 worktree 脚本与当前项目脚本 SHA256 相同。
- 顶层异常证据已补齐：真实 AutoCAD2026、真实候选 DLL，通过现有 RuntimeHost._override seam 注入 InvalidOperationException。三次写出包含相同错误与正确 RUN_ID 的 FAIL 报告；三次报告路径也是文件、写入拒绝，均保留 FAULT-SENTINEL、无报告。6/6 escaped_exception 为 null，override/env 恢复，native command exit0、未超时。PATDOCTOR 实际记录候选 SHA/MVID 和六次故障日志。此为 L2 定位性故障注入，不冒充安装/GUI 用户路径；未逐份新执行外部门禁，缺报告/简略 FAIL 报告由源码规则拒绝。
- 普通报告写失败另行解释：native-report-write-fault-netload 的 stdout 确有三次 `PATGEOMVERIFY report write failed`，命中 RunVerification 内层报告写失败 catch。原 results.json 因 harness 错等顶层 IOException 文本而标 FAIL，原记录保留；不能将这三次算成顶层 catch 证据。native exit0、sentinel 未变与内层安全返回一致。

R9 scale 的24份报告仍为18 PASS、6预期 FAIL，SHA/RUN_ID/候选身份/实际断言计数均一致。results.json 的24个 PASS是预期结果匹配，不是24份几何都通过。R8 正例 native0/门禁0/报告PASS、负例 native0/门禁1/报告FAIL仍匹配，历史崩溃证据不改写。

`ex.Message` 恶意虚拟 getter 可能再抛的静态边界，以及 native AV、栈溢出等不可恢复异常仍仅列理论风险；没有实证常规异常逃逸，不据此否定已完成的常规失败路径复核，也不声称获得普遍崩溃免疫。原独立复核条目只记录作者角色缺口，没有另列具体未验证反例。

详细核验、原报告 SHA256、门禁命令与范围见 `independent-crash-review.json`；24份 scale 核验见 `independent-native-report-audit.json`。

# CAD 修复候选验收（2026-10-20，草稿）

**命令层 PASS；GUI 进行中；四个旧宿主 SKIP/BLOCKED。整体尚未放行。**

这是候选交接草稿。GUI完成后更新同一分支，不以原生进程退出或模拟测试外推GUI/旧宿主通过。修复作者负责源码和命令复验，另一个低思考操作代理通过官方sky API及用户安装的本机兼容候选进行GUI；不声明外部独立审计。

9次真实AutoCAD 2026宿主进程正常退出：3次新建及对应Core复核、冷重开和brace-only均通过；冻结的原错误图保持FAIL并被门禁拒绝。所有报告实际DLL路径/SHA/MVID一致；4份PATDOCTOR为7 PASS/0 FAIL/0 SKIP；命令阶段安装恢复155项通过。

独立发布工作区Structure、Static和353项测试通过（127+55+57+57+57，零跳过）。最初Static发现Git换行转换令VBA资产manifest失配，增加字节保留规则后通过；从Git暂存区导出的54份VBA资产与候选逐字节一致。候选DLL未因此改变。

当前2025 DLL SHA-256：`a78b0dbdbd938ff14f804f7514b8fec2ae5751f35a119ee14c7f570bb8d92cdd`；MVID：`05dc0b57-02e0-45a0-9c2e-c0a3dfe9d3cd`。五版DLL和manifest位于各部署包，保留原dirty工作树构建身份；本次上传提交不是原构建提交。

见[机器可读摘要](acceptance-summary-20261020.json)、[脱敏报告索引](test-evidence/cad-20261020/index.json)和[四个旧宿主交接矩阵](legacy-host-acceptance.md)。公开报告是脱敏衍生文本，与原始字节流的SHA不同，摘要同时保留原始本地哈希；完整原始证据、截图、DWG、SDK和本机协作说明均未上传。

剩余范围包括GUI/夹点/面板/多图/冷重开矩阵、四个旧宿主、采样点之间连续曲率与部分尖角位置变体、强制File.Replace拒绝与File.Move的ACL/ADS/元数据保留、规模上限及修复作者之外的审计。未执行项不计PASS。
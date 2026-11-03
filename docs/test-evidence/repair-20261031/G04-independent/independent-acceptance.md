# G04 独立验收

结论：生产源码隔离回归 PASS；实际 CAD 宿主和部署链路 SKIP/BLOCKED。本验收者 /root/g01 未参与 G04 实现。

旧候选五版对含 FF 的 UTF-8 损坏备份均错误接受，覆盖当前文件并删除备份，4 项断言 FAIL、退出 1；固定后的五版均拒绝读取，返回错误，保留当前/备份全部字节与属性，退出 0。当前文件 SHA-256 d3440e9b69609a924e429b959fbf1daffb975293fb050b0efca8817b4ec568b7，属性 32 (Archive)；备份 SHA-256 db6b201e5b9107e5689ae11bfcf715db775144f5acee2127118dbde2a84cecef，属性 38 (Hidden|System|Archive)。两文件修复后前后完全相等。

同一 IndependentEdgeProbe-v2 探针逐版重新编译旧候选和修复候选，验证 12 项相邻语法边界、嵌套未知字段、BOM/无 BOM 的英文中文和 emoji、坏备份恢复及文件保护。命令、精确参数和退出码见 independent-edge-runs-v2.json，详细输出见 edge-v2-*.log。

固定候选五版 G04JsonRegression 的全部 selector 均通过（原始候选/基线已留下红绿输出）；旧版生产 SimpleJson 和加载器还通过真正 .NET Framework 4.0 进程的 2007/2010 红绿测试，2013 net40 与 2015 net45 的 Newtonsoft 语法兼容探针通过。2025 VSTest 完整 142/142。程序没有调用 G01 FactRunner；所有 CAD 类型均为测试桩。

Framework 层限制：2007/2010 测试运行于 .NET Framework 4.x，并不宣称旧 CLR 2.0/3.5 或真实 CAD 成功。2013/2015 在 Framework 上的 Newtonsoft 对 1e400 这类极大未知数原本就拒绝，此行为固定候选保持不变；普通未知字段正常。四组旧版 CAD 的用户环境需通过 GitHub 外部宿主验收；本机没有探测或安装旧版 CAD，2025 真实宿主及部署也未执行。

G05 可用契约：已测坏 JSON、非对象根和错误 UTF-8 读取返回 null；合法旧可选字段与普通未知字段仍可读取；已测损坏备份恢复返回错误，当前和备份字节及属性保持不变。2007/2010 写出合法 JSON、结构行尾 CRLF、UTF-8 无 BOM 并保留精确 CAD 标记；字符串内容中的单独 CR 和 CRLF 会规范化为 LF，不能作为原样往返契约。R002_Utf8 还额外复跑 FF、不完整 C3、过长 C0 80，各含/不含 BOM 六种损坏样本。

source-inventory.json 是两份候选及实际集成候选的 SHA-256 清单，source-diff.json 列出修复差异，tool-hashes.json 为工具和探针；frozen candidate 与集成候选完整源码树经逐文件哈希匹配。运行命令、选择器、退出码、状态矩阵见 independent-findings.json 和对应 runs.json；evidence-sha256.json 固化本目录的重要日志、探针源码、测试程序集和样本。

import json
from pathlib import Path

base = Path(__file__).resolve().parent
findings = json.loads((base / "checkpoint-findings.json").read_text(encoding="utf-8"))
rows = findings["checks"]
allowed = {"PASS", "FAIL", "SKIP/BLOCKED"}
if any(row["status"] not in allowed for row in rows):
    raise ValueError("Unknown status")
for row in rows:
    for evidence in row.get("evidence", []):
        if not (base / evidence).exists():
            raise FileNotFoundError(evidence)
lines = [
    "# 修复任务暂停记录",
    "",
    "时间：" + findings["time"],
    "",
    "基线 HEAD：`" + findings["head"] + "`；基线包含用户原有工作树改动，未创建提交。",
    "",
    "| 任务包 / 层级 | 结果 | 说明 |",
    "|---|---|---|",
]
for row in rows:
    lines.append("| " + row["scope"] + " | " + row["status"] + " | " + row["detail"].replace("|", "\\|") + " |")
lines += ["", "## 证据和边界", "", findings["limitations"], "", "逐包交接见本目录的 G01、G03、G04、G05、G09、G12、G13 记录；机器可读状态为 `../test-evidence/repair-20261031/checkpoint-findings.json`。", ""]
output = base.parents[1] / "repair-work" / "checkpoint-20261101.md"
output.write_text("\n".join(lines), encoding="utf-8")
print(output)

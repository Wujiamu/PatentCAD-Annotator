import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

root = Path(__file__).resolve().parent
cases = {
    "beforeafter": ("blocked", "FixtureTests.Valid"),
    "after-method": ("blocked", "FixtureTests.Valid"),
    "before-class": ("blocked", "FixtureTests.Valid"),
    "after-class": ("blocked", "FixtureTests.Valid"),
    "before-assembly": ("blocked", "FixtureTests.Valid"),
    "before-inherited-class": ("blocked", "FixtureTests.Valid"),
    "before-inherited-method": ("blocked", "FixtureTests.Valid"),
    "before-derived-attribute": ("blocked", "FixtureTests.Valid"),
    "before-mixed": ("blocked", "FixtureTests.Unsupported"),
    "custom-orderer": ("blocked-all", None),
    "assembly-orderer": ("blocked-all", None),
    "collection-orderer": ("blocked-all", None),
    "collection-type-orderer": ("blocked-all", None),
    "framework-initialization": ("blocked-all", None),
    "benign-attribute": ("pass", None),
    "dispose-failure": ("dispose-failure", "FixtureTests.Valid"),
    "assertion-and-dispose": ("assertion-dispose", "FixtureTests.Fails"),
    "abstract-static": ("not-demonstrated", None),
    "custom-framework": ("not-demonstrated", None),
}

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def parse_summary(output):
    match = re.search(r"^SUMMARY: passed=(\d+) failed=(\d+) skipped=(\d+)$", output, re.MULTILINE)
    return dict(zip(("passed", "failed", "skipped"), map(int, match.groups()))) if match else None

def assess(rule, test, code, output):
    if rule == "not-demonstrated":
        return "SKIP/BLOCKED", "xUnit and old candidate agreed; not established as a false-green case"
    blocked = "SKIP/BLOCKED" in output and code != 0
    if rule == "blocked":
        ok = blocked and not re.search(r"^PASS " + re.escape(test) + r"$", output, re.MULTILINE)
        return ("PASS" if ok else "FAIL"), "nonzero, explicit SKIP/BLOCKED, no PASS for " + test
    if rule == "blocked-all":
        ok = blocked and not re.search(r"^PASS ", output, re.MULTILINE)
        return ("PASS" if ok else "FAIL"), "nonzero, explicit SKIP/BLOCKED, no tests reported PASS"
    if rule == "pass":
        ok = code == 0 and parse_summary(output) == {"passed": 1, "failed": 0, "skipped": 0}
        return ("PASS" if ok else "FAIL"), "exit 0 and one passing Fact"
    if rule in ("dispose-failure", "assertion-dispose"):
        ok = code != 0 and parse_summary(output) == {"passed": 0, "failed": 1, "skipped": 0} and not re.search(r"^PASS " + re.escape(test) + r"$", output, re.MULTILINE)
        return ("PASS" if ok else "FAIL"), "exit nonzero and exactly one failed test, no PASS for it"
    raise ValueError(rule)

parser = argparse.ArgumentParser()
parser.add_argument("--runner", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--source", type=Path)
args = parser.parse_args()
runner = args.runner.resolve(strict=True)
outdir = args.output.resolve()
outdir.mkdir(parents=True, exist_ok=False)
env = os.environ.copy()
env["DOTNET_CLI_UI_LANGUAGE"] = "en-US"
env["VSLANG"] = "1033"
env.pop("PAT_G01_CUSTOM_FRAMEWORK", None)
facts = {
    "time": datetime.datetime.now().astimezone().isoformat(),
    "identity": subprocess.run(["whoami"], capture_output=True, text=True).stdout.strip(),
    "role": "independent reviewer; did not author candidate",
    "runner": str(runner),
    "runner_sha256": sha(runner),
    "source": str(args.source.resolve()) if args.source else None,
    "source_sha256": sha(args.source.resolve()) if args.source else None,
    "audit_tool_sha256": sha(Path(__file__)),
    "cases": [],
}
for name, (rule, test) in cases.items():
    d = root / "extra" / name
    dll = d / "bin" / "Release" / "net8.0" / "Fixture.dll"
    cmd = ["dotnet", "exec", str(runner), str(dll)]
    try:
        result = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", env=env, timeout=30)
        output, code = result.stdout, result.returncode
        status, expected = assess(rule, test, code, output)
    except subprocess.TimeoutExpired:
        output, code, status, expected = "runner timed out after 30 seconds", None, "SKIP/BLOCKED", "runner must finish within 30 seconds"
    (outdir / (name + ".txt")).write_text("command: " + " ".join(cmd) + "\nexit: " + str(code) + "\n" + output, encoding="utf-8")
    facts["cases"].append({
        "name": name, "status": status, "expected": expected,
        "exit": code, "summary": parse_summary(output),
        "fixture_sha256": sha(dll),
        "fixture_source_sha256": sha(d / "Fixture.cs"),
        "xunit_observation": str(d / "xunit.txt"),
        "output": str(outdir / (name + ".txt")),
    })
    print(status, name, "exit=" + str(code), flush=True)
facts["summary"] = {k: sum(c["status"] == k for c in facts["cases"]) for k in ("PASS", "FAIL", "SKIP/BLOCKED")}
(outdir / "findings.json").write_text(json.dumps(facts, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print("SUMMARY", facts["summary"])
sys.exit(1 if facts["summary"]["FAIL"] else 0)

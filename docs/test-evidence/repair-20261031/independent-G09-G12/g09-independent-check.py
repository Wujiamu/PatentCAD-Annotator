from pathlib import Path
from collections import Counter
import hashlib
import json
import re
import subprocess
import sys

root = Path(__file__).resolve().parent
work = root / "G09-independent"
work.mkdir(exist_ok=False)
scripts = work / "scripts"
support = work / "support"
appdata = work / "appdata"
scripts.mkdir()
support.mkdir()
appdata.mkdir()
original = Path(r"<USERPROFILE>\AppData\Local\Temp\PatentMarker-G09-4b7a104dc2f74b96b15afadde384d08d\mixed-and-duplicates")

for operation in ("install", "uninstall"):
    text = (original / (operation + ".vbs")).read_text(encoding="cp936")
    for token in ("Set reg = New FakeReg", "Set shell = New FakeShell", "Set r = New FakeShell"):
        if text.count(token) != 1:
            raise RuntimeError("isolation anchor missing: " + token)
    for kind in ("String", "DWORD"):
        marker = "    Sub Set" + kind + "Value(ByVal hive, ByVal key, ByVal name, ByVal value)\n"
        if text.count(marker) != 1:
            raise RuntimeError("trace injection anchor missing: " + kind)
        text = text.replace(marker, marker + '        Trace "SET' + kind.upper() + ':" & LCase(name) & "=" & CStr(value), hive, key\n')
    (scripts / (operation + ".vbs")).write_text(text, encoding="cp936")
(scripts / "PatentMarker.dll").write_text("test only", encoding="ascii")

targets = [("R17.0", "ACAD-A"), ("R17.1", "ACAD-A"), ("R17.1", "ACAD-B"),
           ("R17.1", "ACAD-C"), ("R17.2", "ACAD-A")]
paths = {}
before = {}
for index, target in enumerate(targets):
    path = support / ("target-" + str(index))
    path.mkdir()
    (path / "acad.lsp").write_bytes(b"; user sentinel\r\n")
    (path / "user-other.lsp").write_bytes(b"; unrelated user object\r\n")
    paths[target] = path
    before[str(path / "acad.lsp")] = (path / "acad.lsp").read_bytes()
    before[str(path / "user-other.lsp")] = (path / "user-other.lsp").read_bytes()
outside = support / "outside"
outside.mkdir()
(outside / "acad.lsp").write_bytes(b"; outside sentinel\r\n")
before[str(outside / "acad.lsp")] = (outside / "acad.lsp").read_bytes()
profiles = [
    ("HKCU", "R17.1", "ACAD-A"), ("HKCU", "R17.1", "ACAD-A"),
    ("HKLM", "R17.1", "ACAD-A"), ("HKCU", "R17.1", "ACAD-B"),
    ("HKLM", "R17.1", "ACAD-C"), ("HKCU", "R17.0", "ACAD-A"),
    ("HKLM", "R17.2", "ACAD-A"), ("HKCU", "R18.0", "ACAD-OUT")
]
profile_file = work / "profiles.txt"
profile_file.write_bytes(("\r\n".join("|".join((h, v, c, "S=" + str(paths.get((v, c), outside))))
                                      for h, v, c in profiles) + "\r\n").encode("cp936"))
checks = []

def check(label, condition, details=""):
    checks.append({"check": label, "result": "PASS" if condition else "FAIL", "details": str(details)})

def expected_traces(operation):
    expected = Counter()
    for version, code in targets:
        key = ("software\\autodesk\\autocad\\" + version + "\\" + code + "\\applications\\patentmarker").lower()
        for hive in ("HKCU", "HKLM"):
            if operation == "install":
                for verb in ("CREATE", "SETSTRING:description=PatentMarker - Patent Drawing Annotation Plugin",
                             "SETDWORD:loadctrls=14", "SETDWORD:managed=1",
                             "SETSTRING:loader=" + str(scripts / "PatentMarker.dll")):
                    expected[(verb, hive, key)] += 1
            else:
                expected[("DELETE", hive, key)] += 1
    return expected

def run(operation, label):
    trace = work / (label + ".trace")
    result = subprocess.run(["cscript.exe", "//nologo", str(scripts / (operation + ".vbs")),
                             str(profile_file), str(appdata), str(trace)],
                            capture_output=True, text=True, encoding="cp936", errors="replace")
    (work / (label + ".output.txt")).write_text(
        "exit=" + str(result.returncode) + "\n" + result.stdout + "\n" + result.stderr, encoding="utf-8")
    check(label + " exit", result.returncode == 0, result.returncode)
    actual = Counter()
    admin = Counter()
    for line in trace.read_text(encoding="cp936").splitlines():
        parts = line.split("|", 2)
        if len(parts) != 3:
            check(label + " malformed trace", False, line)
            continue
        verb, hive, key = parts
        if re.fullmatch(r"software\\pmtest\d+", key):
            admin[(verb, hive, key)] += 1
        else:
            actual[(verb, hive, key)] += 1
    expected = expected_traces(operation)
    check(label + " exact registry operation set", actual == expected,
          {"missing": list((expected - actual).elements()), "unexpected": list((actual - expected).elements())})
    if operation == "install":
        admin_keys = {key for verb, hive, key in admin if hive == "HKLM" and verb in ("CREATE", "DELETE")}
        admin_ok = len(admin_keys) == 1 and sum(admin.values()) == 2
        if admin_keys:
            key = next(iter(admin_keys))
            admin_ok = admin_ok and admin[("CREATE", "HKLM", key)] == 1 and admin[("DELETE", "HKLM", key)] == 1
        check(label + " isolated admin probe", admin_ok, dict((str(k), v) for k, v in admin.items()))
    else:
        check(label + " no extra admin probe", not admin, str(admin))
    for target, path in paths.items():
        data = (path / "acad.lsp").read_bytes()
        if operation == "install":
            ok = data.startswith(b"; user sentinel\r\n") and data.count(b"; --- PatentMarker autoload ---") == 1
        else:
            ok = data == before[str(path / "acad.lsp")]
        check(label + " support " + "/".join(target), ok, hashlib.sha256(data).hexdigest())
        check(label + " user file " + "/".join(target),
              (path / "user-other.lsp").read_bytes() == before[str(path / "user-other.lsp")])
    check(label + " R18 outside sentinel",
          (outside / "acad.lsp").read_bytes() == before[str(outside / "acad.lsp")])

run("install", "first-install")
run("install", "repeat-install")
run("uninstall", "uninstall")
(root / "g09-independent-results.json").write_text(json.dumps(checks, indent=2) + "\n", encoding="utf-8")
failed = [c for c in checks if c["result"] == "FAIL"]
print("G09 INDEPENDENT: passed=" + str(len(checks) - len(failed)) + " failed=" + str(len(failed)))
for item in failed:
    print("FAIL", item)
sys.exit(1 if failed else 0)

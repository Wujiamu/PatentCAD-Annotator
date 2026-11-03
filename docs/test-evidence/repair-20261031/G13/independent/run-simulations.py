from pathlib import Path
import datetime
import json
import os
import subprocess
import sys

root = Path(__file__).resolve().parent
cases = [("baseline-run", "2007")] + [("candidate", v) for v in ("2007", "2010", "2013", "2015")]
results = []
env = dict(os.environ)
env["DOTNET_CLI_UI_LANGUAGE"] = "en-US"
for snapshot, version in cases:
    project = "cad-plugin/RuntimeContract.Tests/PatentMarker.RuntimeContract." + version + ".Tests.csproj"
    command = ["dotnet", "test", project, "--configuration", "Release", "--nologo",
               "--filter", "FullyQualifiedName~G13BracePlanarityTests",
               "--logger", "console;verbosity=normal"]
    run = subprocess.run(command, cwd=root / snapshot, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    log = snapshot + "-" + version + ".log"
    (root / log).write_bytes(run.stdout)
    entry = {"snapshot": snapshot, "version": version, "command": command,
             "exit_code": run.returncode, "log": log,
             "time_local": datetime.datetime.now().astimezone().isoformat()}
    results.append(entry)
    (root / "simulation-runs.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    print(snapshot, version, "exit=" + str(run.returncode), flush=True)
    if run.returncode != (1 if snapshot == "baseline-run" else 0):
        sys.exit(1)
sys.exit(0)

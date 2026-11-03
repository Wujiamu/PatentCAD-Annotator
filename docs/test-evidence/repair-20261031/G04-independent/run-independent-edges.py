from pathlib import Path
import datetime
import json
import os
import shutil
import subprocess
import sys

root = Path(__file__).resolve().parent
project = root / "edge-probe"
project.mkdir()
for name in ("G04JsonRegression.csproj", "HostStubs.cs"):
    shutil.copy2(root / "candidate/tools/G04JsonRegression" / name, project / name)
shutil.copy2(root / "IndependentEdgeProbe.cs", project / "Program.cs")
env = dict(os.environ)
env["DOTNET_CLI_UI_LANGUAGE"] = "en-US"
cases = [("baseline-run", "2007")] + [("candidate", v) for v in ("2007", "2010", "2013", "2015", "2025")]
results = []
for snapshot, year in cases:
    source = root / snapshot
    tag = snapshot + "-" + year
    parser = (source / "cad-plugin/Shared/IO/SimpleJson.cs" if snapshot == "candidate"
              else source / "cad-plugin/2007/PatentMarker/IO/SimpleJson.cs")
    obj = project / ("obj/" + tag)
    out = project / ("bin/" + tag)
    command = ["dotnet", "build", str(project / "G04JsonRegression.csproj"), "-c", "Release", "--nologo", "-v", "quiet",
               "-p:CadYear=" + year, "-p:SourceRoot=" + str(source), "-p:ParserFile=" + str(parser),
               "-p:BaseIntermediateOutputPath=" + str(obj) + os.sep,
               "-p:MSBuildProjectExtensionsPath=" + str(obj) + os.sep,
               "-p:BaseOutputPath=" + str(out) + os.sep]
    build = subprocess.run(command, cwd=root, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (root / ("edge-" + tag + "-build.log")).write_bytes(build.stdout)
    entry = {"snapshot": snapshot, "year": year, "build_command": command, "build_exit": build.returncode}
    if build.returncode == 0:
        dll = out / "Release/net8.0/G04JsonRegression.dll"
        command = ["dotnet", str(dll), str(root / ("edge-data-" + tag))]
        run = subprocess.run(command, cwd=root, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (root / ("edge-" + tag + ".log")).write_bytes(run.stdout)
        entry.update({"command": command, "exit": run.returncode,
                      "time_local": datetime.datetime.now().astimezone().isoformat()})
    results.append(entry)
    (root / "independent-edge-runs.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    print(tag, "build=" + str(build.returncode), "run=" + str(entry.get("exit")), flush=True)
    if build.returncode != 0 or entry.get("exit") not in (0, 1):
        sys.exit(2)
sys.exit(0)
